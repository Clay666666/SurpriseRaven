using Godot;
using static SurpriseRaven.Hooks.Raven.RavenStyleHooks;

namespace SurpriseRaven.Hooks.Raven;

// One candle: painted body (RavenCandleArtHooks), flickering additive halo, flame and an 8x8 extinguish-smoke
// flipbook (30 fps). The flame shader draws a stable upright teardrop core over the 8x8 flame flipbook (24 fps,
// per-candle offset, frame blending, trimmed to an upright envelope); without the atlas it draws the core alone.
// The offline preview's lib/flame.mjs emulates FlameShader line for line; keep them in sync.
internal sealed partial class RavenCandleHooks : Control
{
    private const string FlameShader = """
        shader_type canvas_item;
        uniform float frame = 0.0;
        uniform float strength = 1.0;
        uniform float flicker = 0.94;
        uniform float flipbook = 1.0;
        vec2 cell_uv(float f, vec2 uv) {
            f = mod(f, 64.0);
            return (vec2(mod(f, 8.0), floor(f / 8.0)) + clamp(uv, 0.002, 0.998)) / 8.0;
        }
        float teardrop(float y, float tip) {
            float c = clamp(y, 0.0, 1.0);
            return pow(c, 0.45) * pow(1.0 - c, tip) * 2.55;
        }
        void fragment() {
            float f = floor(frame);
            vec4 t = mix(texture(TEXTURE, cell_uv(f, UV)), texture(TEXTURE, cell_uv(f + 1.0, UV)), fract(frame)) * flipbook;
            float lift = 1.0 + 0.8 * (flicker - 0.94);
            float y = (0.875 - UV.y) / (0.4 * lift);
            float dx = UV.x - 0.5 - 0.006 * sin(frame * 0.21) * y * y;
            float inside = smoothstep(0.0, 0.04, y) * step(y, 1.0);
            float q = abs(dx) / max(0.082 * teardrop(y, 1.1), 0.0001);
            float blue = 1.0 - smoothstep(0.0, 0.2, y);
            float core = (1.0 - smoothstep(0.6, 1.0, q)) * (1.0 - 0.6 * blue) * inside;
            vec3 col = mix(vec3(1.0, 0.5, 0.14), vec3(1.0, 0.8, 0.38), 1.0 - smoothstep(0.35, 0.95, q));
            float white = (1.0 - smoothstep(0.0, 0.55, q)) * smoothstep(0.08, 0.3, y) * (1.0 - smoothstep(0.45, 0.85, y));
            col = mix(mix(col, vec3(1.0, 0.97, 0.82), white), vec3(0.22, 0.38, 1.0), 0.8 * blue);
            float ye = (0.875 - UV.y) / (0.47 * lift);
            float e = abs(UV.x - 0.5) / max(0.115 * teardrop(ye, 0.9), 0.0001);
            float envelope = (1.0 - smoothstep(0.55, 1.0, e)) * smoothstep(0.0, 0.04, ye) * step(ye, 1.0);
            vec3 outer = mix(vec3(1.0, 0.45, 0.12), vec3(1.0) - exp(-pow(t.rgb, vec3(2.2)) * vec3(9.0, 1.15, 0.8)), flipbook);
            float oa = mix(0.3 * (1.0 - smoothstep(0.9, 2.4, q)) * inside, t.a * 0.7 * envelope, flipbook);
            float a = core + oa * (1.0 - core);
            COLOR = vec4((col * core + outer * oa * (1.0 - core)) / max(a, 0.0001), a * strength);
        }
        """;
    private const string SmokeShader = """
        shader_type canvas_item;
        uniform float frame = 0.0;
        uniform float strength = 0.0;
        void fragment() {
            float f = mod(floor(frame), 64.0);
            vec4 t = texture(TEXTURE, (vec2(mod(f, 8.0), floor(f / 8.0)) + clamp(UV, 0.002, 0.998)) / 8.0);
            COLOR = vec4(0.42, 0.44, 0.43, t.r * strength * 0.825);
        }
        """;

    private readonly RavenCandleSpot _spot;
    private readonly TextureRect _glow = new(), _flame;
    private readonly TextureRect? _smoke;
    private readonly ShaderMaterial _flameMaterial;
    private readonly ShaderMaterial? _smokeMaterial;
    private float _strength, _target, _painted = -1;
    private double _smokeAge = -1, _smokeLife;

    internal RavenCandleHooks(RavenCandleSpot spot, int index)
    {
        _spot = spot;
        Index = index;
        Name = "Candle_" + spot.Name;
        MouseFilter = MouseFilterEnum.Ignore;
        var frame = RavenCandleArtHooks.Frame(spot);
        Size = frame.Size;
        Position = frame.Position;
        _glow.Texture = Glow;
        _glow.Material = Additive();
        _glow.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        _glow.Size = frame.Glow.Size;
        _glow.Position = frame.Glow.Position;
        _glow.MouseFilter = MouseFilterEnum.Ignore;
        _glow.Modulate = Colors.Transparent;
        AddChild(_glow);
        // Without the atlas the shader still draws the procedural core; any texture keeps the rect drawing.
        var atlas = RavenAssetsHooks.Texture("tex.flame");
        _flameMaterial = new ShaderMaterial { Shader = new Shader { Code = FlameShader } };
        _flameMaterial.SetShaderParameter("flipbook", atlas is null ? 0f : 1f);
        _flame = Sheet(atlas ?? Glow, _flameMaterial, frame.Flame, frame.FlamePivot);
        _flame.Visible = false;
        if (RavenAssetsHooks.Texture("tex.smoke") is { } smoke)
        {
            _smokeMaterial = new ShaderMaterial { Shader = new Shader { Code = SmokeShader } };
            _smoke = Sheet(smoke, _smokeMaterial, frame.Smoke, frame.SmokePivot);
            _smoke.Visible = false;
        }
        Draw += Paint;
    }

    internal int Index { get; }
    internal bool Lit { get; private set; }
    internal bool Smoking => _smokeAge >= 0;

    internal void SetLit(bool lit, bool animate)
    {
        if (Lit == lit && (animate || (_strength == _target && !Smoking))) return;
        Lit = lit;
        _target = lit ? 1 : 0;
        if (!animate)
        {
            _strength = _target;
            _smokeAge = -1;
            if (_smoke is not null) _smoke.Visible = false;
            ApplyFlame(0);
        }
        else if (lit)
        {
            _smokeAge = -1;
            if (_smoke is not null) _smoke.Visible = false;
            _strength = Math.Max(_strength, 0.12f);
            ApplyFlame(0);
        }
        else if (_smoke is not null)
        {
            _smokeAge = 0;
            _smokeLife = 2.5 + 0.5 * (Index * 7 % 11) / 10.0;
        }
        QueueRedraw();
    }

    internal void Update(double time, double delta)
    {
        _strength = Mathf.MoveToward(_strength, _target, (float)delta * (_target > _strength ? 4f : 9f));
        ApplyFlame(time);
        if (_smoke is null || _smokeAge < 0) return;
        _smokeAge += delta;
        double life = _smokeAge / _smokeLife;
        _smoke.Visible = life < 1;
        if (life >= 1) { _smokeAge = -1; return; }
        float grow = (float)Math.Min(1, life / 0.45);
        _smoke.Scale = new Vector2(0.35f + 0.65f * grow, 0.1f + 0.9f * grow);
        _smokeMaterial!.SetShaderParameter("frame", _smokeAge * 30);
        _smokeMaterial.SetShaderParameter("strength", Math.Min(1, _smokeAge / 0.095) * (life < 0.55 ? 1 : Math.Max(0, (1 - life) / 0.45)));
    }

    private void ApplyFlame(double time)
    {
        float flicker = RavenCandleArtHooks.Flicker(time, Index);
        _glow.Modulate = RavenCandleArtHooks.GlowColor(_strength, flicker);
        _flame.Visible = _strength > 0.01f;
        _flame.Scale = Vector2.One * (0.4f + 0.6f * _strength);
        _flameMaterial.SetShaderParameter("frame", RavenCandleArtHooks.FlameFrame(time, Index));
        _flameMaterial.SetShaderParameter("strength", _strength);
        _flameMaterial.SetShaderParameter("flicker", flicker);
        // The painted body (wax warmth, local light, ember) only changes with strength.
        if (_strength != _painted) QueueRedraw();
    }

    private TextureRect Sheet(Texture2D texture, ShaderMaterial material, Rect2 rect, Vector2 pivot)
    {
        var sheet = new TextureRect
        {
            Texture = texture, Material = material, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale, Size = rect.Size, Position = rect.Position,
            PivotOffset = pivot, MouseFilter = MouseFilterEnum.Ignore, TextureFilter = TextureFilterEnum.LinearWithMipmaps
        };
        AddChild(sheet);
        return sheet;
    }

    private void Paint()
    {
        _painted = _strength;
        RavenCandleArtHooks.Paint(new RavenPen(new RavenCanvasPainter(this)), _spot, Size, Index, _strength);
    }
}
