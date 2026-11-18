using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace SurpriseRaven.Hooks.Raven;

// Native buttons in a separate viewport exercise real _Input/_GuiInput without a live run.
internal sealed class RavenHeadlessDragFixtureHooks : IDisposable
{
    internal readonly SubViewport Viewport = new()
    {
        Size = new Vector2I(1920, 1080), HandleInputLocally = true, GuiDisableInput = false,
        RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
    };
    internal readonly Control Panel = new() { Size = RavenStyleHooks.Design };
    internal readonly Control Page = new() { Size = RavenStyleHooks.Design, MouseFilter = Control.MouseFilterEnum.Ignore };
    internal readonly RavenSacrificeTargetHooks Target = new();
    internal readonly NButton Source = new()
    {
        Position = new Vector2(60, 40), Size = new Vector2(68, 68), FocusMode = Control.FocusModeEnum.All,
        MouseFilter = Control.MouseFilterEnum.Stop
    };
    internal readonly RelicModel Relic = ModelDb.Relic<Strawberry>().ToMutable();
    internal readonly RavenNativeDragHooks Drag;
    internal bool Allowed = true, Eligible = true;
    internal long Revision = 7, Opportunity = 3;
    internal int Drops, Inspections;
    internal long SubmittedRevision = -1;
    private readonly SceneTree _tree;
    private bool _pressed;
    private Vector2 _last;
    internal float OriginalThreshold { get; }
    internal Vector2 SourcePoint => Source.GetGlobalTransformWithCanvas() * (Source.Size / 2);
    internal Vector2 TargetPoint => Target.GetGlobalTransformWithCanvas() * (Target.Size / 2);

    internal RavenHeadlessDragFixtureHooks(SceneTree tree)
    {
        _tree = tree;
        Page.AddChild(Target);
        Panel.AddChild(Page);
        Viewport.AddChild(Panel);
        Viewport.AddChild(Source); // Mirrors the game's inventory above its overlay sibling.
        tree.Root.AddChild(Viewport);
        OriginalThreshold = RavenNativeDragInputHooks.GetThreshold(Source);
        Drag = new RavenNativeDragHooks(Panel, Target, () => Allowed,
            relic => Eligible && ReferenceEquals(relic, Relic), () => (Revision, Opportunity),
            (_, _, revision) => { Drops++; SubmittedRevision = revision; });
        Drag.SetSources([(Source, Relic)]);
        Source.Released += _ => { if (!RavenNativeDragInputHooks.SuppressesInspect(Relic)) Inspections++; };
    }

    internal async Task Frame() => await _tree.ToSignal(_tree, SceneTree.SignalName.ProcessFrame);

    internal async Task Move(Vector2 position)
    {
        using var input = new InputEventMouseMotion
        {
            Position = position, GlobalPosition = position, Relative = position - _last,
            ButtonMask = _pressed ? MouseButtonMask.Left : 0
        };
        _last = position;
        Viewport.PushInput(input, true);
        await Frame();
    }

    internal async Task Press()
    {
        await Move(SourcePoint);
        _pressed = true;
        using var input = new InputEventMouseButton
        {
            Position = SourcePoint, GlobalPosition = SourcePoint, ButtonIndex = MouseButton.Left,
            ButtonMask = MouseButtonMask.Left, Pressed = true
        };
        Viewport.PushInput(input, true);
        await Frame();
    }

    internal async Task Release(Vector2? point = null)
    {
        _pressed = false;
        var position = point ?? _last;
        using var input = new InputEventMouseButton
        {
            Position = position, GlobalPosition = position, ButtonIndex = MouseButton.Left, Pressed = false
        };
        Viewport.PushInput(input, true);
        await Frame();
    }

    public void Dispose()
    {
        Drag.Dispose();
        Viewport.Free();
        RavenNativeDragInputHooks.ClearSuppression();
    }
}
