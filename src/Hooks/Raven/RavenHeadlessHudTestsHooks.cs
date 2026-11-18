using Godot;
using MegaCrit.Sts2.Core.Entities.Relics;

namespace SurpriseRaven.Hooks.Raven;

internal static partial class RavenOverlayHooks
{
    internal static int RunHudChecks()
    {
        Require(DisplayServer.GetName() == "headless", "HUD checks require headless.");
        var hud = new RavenHudHooks();
        int checks = 0;
        void CheckHud(bool condition, string message) { Require(condition, message); checks++; }
        try
        {
            hud.SetMessage("HUD message fixture");
            hud.SetCaption("HUD caption fixture");
            var message = HudNodes(hud).OfType<Label>().Single(label => label.Text == "HUD message fixture");
            var caption = HudNodes(hud).OfType<Label>().Single(label => label.Text == "HUD caption fixture");
            foreach (int lights in new[] { 0, 3, 6 })
            {
                hud.SetInfo(lights, lights, false);
                var nodes = HudNodes(hud).ToList();
                var labels = nodes.OfType<Label>().Select(label => label.Text).ToList();
                CheckHud(labels.Contains($"第 {lights + 1} 轮"), "HUD retains the current round.");
                CheckHud(!labels.Any(text => text.Contains("烛火") || text.Contains("/6")), "HUD does not expose numeric candle counts.");
                CheckHud(labels.Contains("HUD message fixture") && labels.Contains("HUD caption fixture"),
                    "HUD retains the current message and input caption.");
                CheckHud(!nodes.OfType<Control>().Any(control => HasPercentage(control.TooltipText))
                    && !labels.Any(HasPercentage), "HUD exposes no odds percentage in labels or tooltips.");
            }
            hud.SetMessage("");
            hud.SetCaption("");
            CheckHud(!message.Visible && !caption.Visible, "Empty reward descriptions collapse instead of leaving visible blank labels.");
            foreach (var outcome in Enum.GetValues<RavenOutcome>())
            {
                var plan = new RavenRoundPlan(outcome, null, [], RelicRarity.Common, 0, 1);
                CheckHud(RavenStageHooks.ResultMessage(plan) == "", "Routine reward results do not repeat their descriptions.");
                CheckHud(RavenStageHooks.ResultMessage(plan with { Notice = "候选已失效" }) == "候选已失效",
                    "Exceptional reward notices remain readable.");
            }
            var missingOriginal = new RavenRoundPlan(RavenOutcome.Bonus, null, [], RelicRarity.Common, 0, 1)
                { OriginalState = RavenOriginalState.Missing };
            hud.SetMessage(RavenStageHooks.ResultMessage(missingOriginal));
            CheckHud(message.Visible && message.Text.Contains("移除"), "A missing original retains its exception message.");
            hud.SetInfo(0, 0, true);
            var endedNodes = HudNodes(hud).ToList();
            CheckHud(endedNodes.OfType<Label>().Any(label => label.Text == "渡鸦路灯 · 机会已结束"),
                "Ended HUD retains its status message.");
            CheckHud(!endedNodes.OfType<Control>().Any(control => HasPercentage(control.TooltipText))
                && !endedNodes.OfType<Label>().Any(label => HasPercentage(label.Text)), "Ended HUD exposes no odds percentage.");
            CheckHud(hud.ExitControl is not null && HudNodes(hud).Contains(hud.ExitControl), "HUD retains its exit control.");
            CheckHud(!endedNodes.OfType<Button>().Any(button => button.Text.Contains("背包")), "HUD does not restore a bag button.");
        }
        finally { hud.Free(); }
        return checks;
    }

    private static bool HasPercentage(string text) => text.Contains('%') || text.Contains('％');

    private static IEnumerable<Node> HudNodes(Node root)
    {
        yield return root;
        foreach (var child in root.GetChildren())
            foreach (var node in HudNodes(child)) yield return node;
    }
}
