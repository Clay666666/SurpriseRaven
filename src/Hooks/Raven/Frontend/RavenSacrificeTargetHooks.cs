using Godot;

namespace SurpriseRaven.Hooks.Raven;

// Local ellipse geometry is shared by drawing and hit testing, including nonuniform page scaling.
internal sealed partial class RavenSacrificeTargetHooks : Control
{
    internal static readonly Vector2 Radius = new(128, 30);
    private bool _available, _hot;

    internal RavenSacrificeTargetHooks()
    {
        Name = "SacrificeTarget";
        Position = RavenLayout.Altar - Radius;
        Size = Radius * 2;
        MouseFilter = MouseFilterEnum.Ignore;
        Draw += Paint;
    }

    internal bool Hot => _hot;
    internal bool Available => _available;
    internal static string Caption(bool hot) => hot ? "松手献祭" : "将发光的遗物拖到这里";
    internal Vector2[] FillPolygon() => RavenStyleHooks.Ellipse(Size / 2, Size.X / 2, Size.Y / 2)[..^1];

    internal void SetState(bool available, bool hot = false)
    {
        hot &= available;
        if (_available == available && _hot == hot) return;
        _available = available;
        _hot = hot;
        QueueRedraw();
    }

    internal bool HitGlobal(Vector2 point)
    {
        if (!IsInsideTree() || !IsVisibleInTree() || Size.X <= 0 || Size.Y <= 0) return false;
        Vector2 local = GetGlobalTransformWithCanvas().AffineInverse() * point;
        Vector2 normalized = (local - Size / 2) / (Size / 2);
        return normalized.LengthSquared() <= 1f;
    }

    private void Paint()
    {
        if (!_available) return;
        var color = new Color(RavenStyleHooks.Gold, _hot ? 1f : 0.6f);
        var ellipse = RavenStyleHooks.Ellipse(Size / 2, Size.X / 2, Size.Y / 2);
        if (_hot) DrawColoredPolygon(FillPolygon(), new Color(color, 0.28f));
        DrawPolyline(ellipse, color, _hot ? 5 : 2, true);
        // The primary action of the Ready state, written where the drop happens.
        var font = RavenStyleHooks.Font(true) ?? ThemeDB.FallbackFont;
        string caption = Caption(_hot);
        var textSize = font.GetStringSize(caption, HorizontalAlignment.Left, -1, 22);
        var at = new Vector2((Size.X - textSize.X) / 2, -14);
        DrawStringOutline(font, at, caption, HorizontalAlignment.Left, -1, 22, 7, RavenStyleHooks.TextOutline);
        DrawString(font, at, caption, HorizontalAlignment.Left, -1, 22, _hot ? RavenStyleHooks.Gold : RavenStyleHooks.Cream);
    }
}
