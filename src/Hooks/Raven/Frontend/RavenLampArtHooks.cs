using Godot;
using static SurpriseRaven.Hooks.Raven.RavenStyleHooks;

namespace SurpriseRaven.Hooks.Raven;

// Lamp, pedestal and stone platform layer. The art is the engine-free RavenLampPaintHooks (also drawn by
// the offline preview); the lantern glass light belongs to the lantern candles so it dims with them.
internal sealed partial class RavenLampArtHooks : Control
{
    internal RavenLampArtHooks()
    {
        Name = "LampArt";
        Size = Design;
        MouseFilter = MouseFilterEnum.Ignore;
        Draw += () => RavenLampPaintHooks.Paint(new RavenPen(new RavenCanvasPainter(this)));
    }
}
