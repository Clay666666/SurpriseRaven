using System.Collections;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Runs;

namespace SurpriseRaven.Hooks.Raven;

// Input stays inside this viewport; tooltip ownership is inspected without clearing other owners.
internal sealed class RavenHoverFixtureHooks : IDisposable
{
    private readonly SceneTree _tree = (SceneTree)Engine.GetMainLoop();
    private readonly NControllerManager _controller;
    private readonly InputType _previousMode;
    private readonly IDictionary _tips;
    internal SubViewport Viewport { get; } = new()
    {
        Size = new Vector2I(1920, 1080), HandleInputLocally = true, GuiDisableInput = false,
        RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
    };
    internal Control Host { get; } = new() { Size = RavenStyleHooks.Design, MouseFilter = Control.MouseFilterEnum.Ignore };
    internal int Checks { get; private set; }

    internal RavenHoverFixtureHooks()
    {
        if (DisplayServer.GetName() != "headless" || RunManager.Instance.IsInProgress
            || System.Environment.GetEnvironmentVariable("STS2_SURPRISE_RAVEN_HEADLESS_TEST") != "1")
            throw new InvalidOperationException("Hover fixture requires isolated headless without a run.");
        _controller = NControllerManager.Instance ?? throw new InvalidOperationException("Input manager unavailable.");
        _previousMode = _controller.InputType;
        _tips = (IDictionary)AccessTools.Field(typeof(NHoverTipSet), "_activeHoverTips").GetValue(null)!;
        Viewport.AddChild(Host);
        _tree.Root.AddChild(Viewport);
        SetMode(InputType.MouseAndKeyboard);
    }

    internal void SetMode(InputType mode) =>
        typeof(NControllerManager).GetProperty(nameof(NControllerManager.InputType))!.SetValue(_controller, mode);

    internal bool HasTip(Control owner) => _tips.Contains(owner);

    internal void Check(bool condition, string message)
    {
        Checks++;
        if (!condition) throw new InvalidOperationException("Raven hover check failed: " + message);
    }

    internal async Task Frame() => await _tree.ToSignal(_tree, SceneTree.SignalName.ProcessFrame);

    internal async Task Move(Vector2 point)
    {
        using var input = new InputEventMouseMotion { Position = point, GlobalPosition = point };
        Viewport.PushInput(input, true);
        await Frame();
    }

    public void Dispose()
    {
        Viewport.GuiReleaseFocus();
        Viewport.Free();
        SetMode(_previousMode);
    }
}
