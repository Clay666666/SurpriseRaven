using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace SurpriseRaven.Hooks.Raven;

internal static partial class RavenOverlayHooks
{
    private static async Task CheckFallbackHoverAsync(RavenHoverFixtureHooks f, IRunState run, RelicModel relic)
    {
        var rewards = new RavenRewardsHooks(run, () => null);
        f.Host.AddChild(rewards);
        rewards.ShowRewards([relic], null);
        rewards.Interactive = true;
        await f.Frame();
        var button = (TextureButton)rewards.FirstSlot!;
        await f.Move(button.GetGlobalRect().GetCenter());
        f.Check(f.HasTip(button), "Fallback pointer hover creates an owned tooltip.");
        f.SetMode(InputType.KeyboardOnlyMode);
        button.GrabFocus();
        f.Check(f.HasTip(button), "Pointer plus focus shares one fallback tooltip, including the 111 API.");
        // Isolate mouse exit from the input manager's switch back to mouse mode.
        button.EmitSignal(Control.SignalName.MouseExited);
        f.Check(f.HasTip(button), "Directional focus keeps the fallback tooltip after its pointer leaves.");
        button.ReleaseFocus();
        f.Check(!f.HasTip(button), "Releasing the final fallback hover/focus source removes the tooltip.");
        f.SetMode(InputType.MouseAndKeyboard);
        await f.Move(button.GetGlobalRect().GetCenter());
        button.GrabFocus();
        await f.Move(new Vector2(30, 300));
        f.Check(!f.HasTip(button) && !button.HasFocus(), "Mouse exit clears fallback tooltip and stale focus together.");
        await f.Move(button.GetGlobalRect().GetCenter());
        rewards.Interactive = false;
        f.Check(!f.HasTip(button) && rewards.HoveredRequest is null, "Disabled fallback clears the tooltip and pickup target.");
        rewards.Interactive = true;
        await f.Move(new Vector2(30, 300));
        await f.Move(button.GetGlobalRect().GetCenter());
        rewards.Hide();
        f.Check(!f.HasTip(button), "Hidden fallback removes its tooltip without relying on a pointer event.");
        rewards.Show();
        await f.Move(new Vector2(30, 300));
        await f.Move(button.GetGlobalRect().GetCenter());
        rewards.Clear();
        rewards.Clear();
        f.Check(!f.HasTip(button), "Repeated fallback teardown removes its exact owner safely.");
        rewards.QueueFree();
        await f.Frame();
    }
}
