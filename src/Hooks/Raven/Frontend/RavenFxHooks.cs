using Godot;
using MegaCrit.Sts2.Core.Entities.Relics;
using static SurpriseRaven.Hooks.Raven.RavenStyleHooks;

namespace SurpriseRaven.Hooks.Raven;

// One-shot CPU particle bursts (no _Process overrides needed) plus native rarity glow VFX.
internal static class RavenFxHooks
{
    // Reduced motion keeps only the stationary flare, without flying paper.
    internal static void Confetti(Node parent, Vector2 at, bool particles = true)
    {
        if (particles && RavenAssetsHooks.Texture("tex.paper_gold") is { } gold)
            parent.AddChild(Burst(at, gold, 4, 4, 46, 0.11f, 0.17f, new Color(1, 1, 1)));
        if (particles && RavenAssetsHooks.Texture("tex.paper_color") is { } color)
            parent.AddChild(Burst(at, color, 2, 2, 34, 0.04f, 0.07f, new Color(1, 1, 1)));
        var flare = new CpuParticles2D
        {
            Position = at, Amount = 3, Lifetime = 0.7, OneShot = true, Explosiveness = 1, Texture = RavenAssetsHooks.Texture("tex.glow") ?? (Texture2D)Glow,
            Gravity = Vector2.Zero, ScaleAmountMin = 2.5f, ScaleAmountMax = 3.5f, Material = Additive(), Color = new Color(1f, 0.85f, 0.5f),
            ColorRamp = Fade(), AngleMin = -180, AngleMax = 180
        };
        AutoFree(flare);
        parent.AddChild(flare);
    }

    internal static void Feathers(Node parent, Vector2 at, int amount = 10)
    {
        var feather = new GradientTexture2D
        {
            Width = 22, Height = 8,
            Gradient = new Gradient { Offsets = [0f, 1f], Colors = [new Color(0.18f, 0.2f, 0.27f), new Color(0.06f, 0.07f, 0.1f)] }
        };
        var particles = new CpuParticles2D
        {
            Position = at, Amount = amount, Lifetime = 1.6, OneShot = true, Explosiveness = 0.9f, Texture = feather,
            Direction = Vector2.Up, Spread = 120, InitialVelocityMin = 60, InitialVelocityMax = 160, Gravity = new Vector2(0, 90),
            AngularVelocityMin = -240, AngularVelocityMax = 240, DampingMin = 30, DampingMax = 60, ColorRamp = Fade(),
            AngleMin = -180, AngleMax = 180
        };
        AutoFree(particles);
        parent.AddChild(particles);
    }

    internal static Node? RarityGlow(RelicRarity rarity)
    {
        var path = RavenRulesHooks.Tier(rarity) switch
        {
            RelicRarity.Rare or RelicRarity.Ancient => "res://scenes/vfx/relic_rare_glow_vfx.tscn",
            RelicRarity.Uncommon => "res://scenes/vfx/uncommon_glow_vfx.tscn",
            _ => null
        };
        return path is null || DisplayServer.GetName() == "headless" ? null : Native<Node>(path);
    }

    private static CpuParticles2D Burst(Vector2 at, Texture2D texture, int columns, int rows, int amount,
        float minScale, float maxScale, Color color)
    {
        var particles = new CpuParticles2D
        {
            Position = at, Amount = amount, Lifetime = 4.5, LifetimeRandomness = 0.33, OneShot = true, Explosiveness = 1,
            Texture = texture, Direction = Vector2.Up, Spread = 42, InitialVelocityMin = 650, InitialVelocityMax = 1050,
            Gravity = new Vector2(0, 980), DampingMin = 140, DampingMax = 230, AngularVelocityMin = -420, AngularVelocityMax = 420,
            AngleMin = -180, AngleMax = 180, ScaleAmountMin = minScale, ScaleAmountMax = maxScale, Color = color,
            ColorRamp = Fade(), AnimOffsetMax = 1, EmissionShape = CpuParticles2D.EmissionShapeEnum.Sphere, EmissionSphereRadius = 18,
            Material = new CanvasItemMaterial
            {
                ParticlesAnimation = true, ParticlesAnimHFrames = columns, ParticlesAnimVFrames = rows, ParticlesAnimLoop = false
            }
        };
        AutoFree(particles);
        return particles;
    }

    private static Gradient Fade() => new()
    {
        Offsets = [0f, 0.75f, 1f], Colors = [new Color(1, 1, 1, 1), new Color(1, 1, 1, 0.9f), new Color(1, 1, 1, 0)]
    };

    private static void AutoFree(CpuParticles2D particles)
    {
        particles.Emitting = true;
        particles.Finished += () => { if (GodotObject.IsInstanceValid(particles)) particles.QueueFree(); };
    }
}
