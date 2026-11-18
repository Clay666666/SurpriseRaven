using Godot;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Localization.Fonts;

namespace SurpriseRaven.Hooks.Raven;

// Slay the Spire 2 palette and helpers: cream text with dark brown outline, iron/teal panels,
// gold trims and thick dark outlines on painted shapes.
internal static partial class RavenStyleHooks
{
    private static GradientTexture2D? _glow;
    internal static Color Rarity(RelicRarity rarity) => RavenRulesHooks.Tier(rarity) switch
    {
        RelicRarity.Ancient => new Color(0.83f, 0.5f, 0.98f),
        RelicRarity.Rare => new Color(0.98f, 0.8f, 0.3f),
        RelicRarity.Uncommon => new Color(0.38f, 0.72f, 0.96f),
        _ => new Color(0.82f, 0.82f, 0.8f)
    };

    internal static Font? Font(bool bold = false)
    {
        try { return FontManager.GetSubstituteFont("zhs", bold ? FontType.Bold : FontType.Regular); }
        catch { return null; }
    }

    internal static Label Text(string text, int size, bool bold = false, Color? color = null, int outline = 8)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        if (Font(bold) is { } font) label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color ?? Cream);
        label.AddThemeColorOverride("font_outline_color", TextOutline);
        label.AddThemeConstantOverride("outline_size", outline);
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.25f));
        label.AddThemeConstantOverride("shadow_offset_x", 3);
        label.AddThemeConstantOverride("shadow_offset_y", 3);
        return label;
    }

    internal static StyleBoxFlat Panel(Color? border = null, int radius = 10)
    {
        var box = new StyleBoxFlat { BgColor = PanelFill, BorderColor = border ?? new Color(Gold, 0.75f) };
        box.SetBorderWidthAll(3);
        box.SetCornerRadiusAll(radius);
        box.SetContentMarginAll(16);
        box.ShadowColor = new Color(0, 0, 0, 0.45f);
        box.ShadowSize = 12;
        return box;
    }

    internal static Button Button(string text, Action pressed, int size = 24)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 56), FocusMode = Control.FocusModeEnum.All };
        if (Font(true) is { } font) button.AddThemeFontOverride("font", font);
        button.AddThemeFontSizeOverride("font_size", size);
        foreach (var color in new[] { "font_color", "font_hover_color", "font_focus_color", "font_pressed_color" })
            button.AddThemeColorOverride(color, Cream);
        button.AddThemeColorOverride("font_outline_color", TextOutline);
        button.AddThemeConstantOverride("outline_size", 6);
        var normal = Panel(new Color(Gold, 0.6f), 8);
        normal.BgColor = new Color(0.12f, 0.17f, 0.19f, 0.95f);
        var hover = Panel(Gold, 8);
        hover.BgColor = new Color(0.2f, 0.25f, 0.24f, 0.98f);
        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("focus", hover);
        button.AddThemeStyleboxOverride("pressed", normal);
        button.AddThemeStyleboxOverride("disabled", normal);
        button.Pressed += pressed;
        return button;
    }

    // Soft additive halo shared by candles, lanterns and reward rings.
    internal static GradientTexture2D Glow => _glow ??= new GradientTexture2D
    {
        Width = 128, Height = 128, Fill = GradientTexture2D.FillEnum.Radial,
        FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f),
        Gradient = new Gradient { Offsets = [0f, 0.35f, 1f], Colors = [new Color(1, 1, 1, 0.9f), new Color(1, 1, 1, 0.3f), new Color(1, 1, 1, 0)] }
    };

    internal static CanvasItemMaterial Additive() => new() { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };

    // Native game scenes keep the exact Slay the Spire 2 look; failures fall back to Mod widgets.
    internal static T? Native<T>(string path) where T : Node
    {
        try { return ResourceLoader.Load<PackedScene>(path)?.Instantiate<T>(); }
        catch (Exception error)
        {
            Logger.Log($"[Raven] Native scene {path} unavailable: {error.Message}");
            return null;
        }
    }

    internal static void FillOutlined(CanvasItem canvas, Vector2[] points, Color fill, float outline = 4f)
    {
        canvas.DrawColoredPolygon(points, fill);
        var closed = new Vector2[points.Length + 1];
        points.CopyTo(closed, 0);
        closed[^1] = points[0];
        canvas.DrawPolyline(closed, Ink, outline, true);
    }
}
