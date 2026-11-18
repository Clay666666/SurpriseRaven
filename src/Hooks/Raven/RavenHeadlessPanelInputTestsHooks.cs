using Godot;

namespace SurpriseRaven.Hooks.Raven;

internal static partial class RavenOverlayHooks
{
    internal static async Task<int> RunPanelInputChecksAsync()
    {
        if (DisplayServer.GetName() != "headless") throw new InvalidOperationException("Headless only.");
        var tree = (SceneTree)Engine.GetMainLoop();
        var viewport = new SubViewport
        {
            Size = new Vector2I(1920, 1080), HandleInputLocally = true,
            GuiDisableInput = false, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
        };
        var panel = new Control { Size = new Vector2(1920, 1080) };
        var child = new Button { Text = "focus", Size = new Vector2(120, 50) };
        var text = new LineEdit { Position = new Vector2(200, 100), Size = new Vector2(200, 50) };
        panel.AddChild(child);
        panel.AddChild(text);
        viewport.AddChild(panel);
        tree.Root.AddChild(viewport);
        int pickups = 0, exits = 0, checks = 0;
        bool allowed = true, dragging = false;
        void Check(bool result, string message) { Require(result, message); checks++; }
        async Task Frame() => await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        async Task KeyEvent(Key physical, bool pressed = true, bool echo = false, bool ctrl = false)
        {
            using var key = new InputEventKey { PhysicalKeycode = physical, Pressed = pressed, Echo = echo, CtrlPressed = ctrl };
            viewport.PushInput(key, true);
            await Frame();
        }
        var input = new RavenPanelInputHooks(panel, () => allowed, () => pickups++, () =>
        {
            if (dragging) dragging = false;
            else exits++;
        }, nativeBack: false);
        try
        {
            child.GrabFocus();
            await Frame();
            await KeyEvent(Key.F);
            Check(pickups == 1, "F reaches the panel with focused child");
            await KeyEvent(Key.F, false);
            await KeyEvent(Key.F, echo: true);
            await KeyEvent(Key.F, ctrl: true);
            Check(pickups == 1, "Release, repeat and Ctrl+F do not claim");
            await KeyEvent(Key.B);
            Check(pickups == 1 && exits == 0, "B no longer opens a bag");
            dragging = true;
            await KeyEvent(Key.Escape);
            Check(!dragging && exits == 0, "Escape cancels a drag without exiting");
            await KeyEvent(Key.Escape, false);
            await KeyEvent(Key.Escape);
            Check(exits == 1, "A later Escape may exit");
            text.GrabFocus();
            await Frame();
            await KeyEvent(Key.F);
            Check(pickups == 1, "Text entry blocks pickup shortcut");
            child.GrabFocus();
            allowed = false;
            input.Refresh();
            await KeyEvent(Key.F);
            await KeyEvent(Key.Escape);
            Check(pickups == 1 && exits == 1, "Covered/transition gate blocks shortcuts");
            allowed = true;
            panel.Hide();
            input.Refresh();
            await KeyEvent(Key.F);
            Check(pickups == 1, "Hidden panel cannot claim");
            panel.Show();
            child.GrabFocus();
            input.Refresh();
            await KeyEvent(Key.F);
            Check(pickups == 2, "Shown panel restores shortcuts");
            input.Dispose();
            await KeyEvent(Key.F);
            Check(pickups == 2, "Disposed shortcuts do not fire");
            input.Dispose();
        }
        finally
        {
            input.Dispose();
            tree.Root.RemoveChild(viewport);
            viewport.QueueFree();
            await Frame();
        }
        return checks;
    }
}
