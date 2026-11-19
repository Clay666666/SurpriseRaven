using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Unlocks;

namespace SurpriseRaven.Hooks.Raven;

internal static partial class RavenOverlayHooks
{
    // Desktop, 16:10, 4:3 (both expand directions) and ultrawide pages, Y-compressed below the top bar.
    private static readonly Vector2[] StageSizes = [new(1920, 1080), new(1680, 1050), new(1920, 1200), new(1920, 1440), new(1440, 1080), new(2560, 1080)];

    internal static async Task<int> RunStageLayoutChecksAsync()
    {
        if (DisplayServer.GetName() != "headless") throw new InvalidOperationException("Stage layout checks require headless.");
        var tree = (SceneTree)Engine.GetMainLoop();
        var player = Player.CreateForNewRun<Ironclad>(UnlockState.none, 32353UL);
        CreateRewardFixtureRun(player);
        var pickup = new RavenRoundPlan(RavenOutcome.Bonus, null, [ModelDb.Relic<Strawberry>()], default, 0, 1,
            ModelDb.Relic<JuzuBracelet>());
        var session = (RavenStageSession.IntroOpportunityId, RavenStageSession.TimelineActive);
        int checks = 0;
        void Check(bool condition, string message) { Require(condition, message); checks++; }
        foreach (var size in StageSizes)
        {
            const float top = 112;
            var page = new Control { Size = size, Position = new Vector2(0, top), Scale = new Vector2(1, (size.Y - top) / size.Y) };
            tree.Root.AddChild(page);
            var stage = new RavenStageHooks(player, "layout fixture", playAudio: _ => { });
            try
            {
                page.AddChild(stage);
                stage.OnShown();
                stage.Hud.SetMessage("渡鸦带走了一件遗物，下一轮机会仍然保留。");
                stage.Hud.SetCaption(RavenStageHooks.PickupCaption(pickup));
                stage.Hud.Pop("升值成功", RavenStyleHooks.Gold);
                // Native back/banner entrance animations take 0.35/0.4s; measure their settled, visible positions.
                var shown = tree.CreateTimer(0.5);
                await tree.ToSignal(shown, SceneTreeTimer.SignalName.Timeout);
                string at = $"{size.X}x{size.Y}";
                var visible = new Rect2(0, top, size.X, size.Y - top);
                var canvas = stage.Canvas.GetGlobalTransform();
                var scale = canvas.Scale;
                Check(Mathf.Abs(scale.X - scale.Y) < 1e-3f * scale.X, $"{at}: lamp art is scaled uniformly ({scale}).");
                var art = Bounds(canvas, new Rect2(Vector2.Zero, RavenStyleHooks.Design));
                Check(visible.Grow(1).Encloses(art), $"{at}: lamp canvas fits the visible page ({art}).");
                var panel = stage.Hud.InfoPanel.GetGlobalRect();
                var exit = stage.Hud.ExitControl.GetGlobalRect();
                var flight = Bounds(canvas, RavenLayout.FlightArea);
                List<(string Name, Rect2 Rect)> hud = [("round panel", panel), ("exit", exit)];
                Check(stage.Hud.HasNativeBack && stage.Hud.Banner is { Modulate.A: >= 0.99f },
                    $"{at}: native back button and fully revealed native banner are present.");
                var banner = (NCommonBanner)stage.Hud.Banner!;
                var title = banner.label;
                var titleFont = title.GetThemeFont("font");
                int titleSize = title.GetThemeFontSize("font_size");
                Check(titleFont.GetStringSize(title.Text, fontSize: titleSize).X <= title.Size.X
                    && titleFont.GetHeight(titleSize) <= title.Size.Y,
                    $"{at}: the enlarged title fits on one line without clipping.");
                hud.Add(("banner", banner.GetGlobalRect()));
                hud.Add(("caption", TextRect(stage.Hud.CaptionLabel)));
                hud.Add(("pop", TextRect(stage.Hud.PopLabel)));
                Logger.Log($"[Raven] Stage layout {at}: canvas {art}, flight {flight}, " + string.Join(", ", hud.Select(h => $"{h.Name} {h.Rect}")));
                for (int i = 0; i < hud.Count; i++)
                {
                    Check(visible.Grow(1).Encloses(hud[i].Rect), $"{at}: {hud[i].Name} {hud[i].Rect} fits the visible page {visible}.");
                    for (int j = i + 1; j < hud.Count; j++)
                        Check(!hud[i].Rect.Intersects(hud[j].Rect), $"{at}: {hud[i].Name} {hud[i].Rect} overlaps {hud[j].Name} {hud[j].Rect}.");
                }
                foreach (var (name, rect) in hud.Where(h => h.Name is "round panel" or "exit" or "banner"))
                    Check(!flight.Intersects(rect), $"{at}: raven flight area {flight} stays clear of the {name} {rect}.");
            }
            finally
            {
                stage.Shutdown();
                page.Free();
            }
        }
        (RavenStageSession.IntroOpportunityId, RavenStageSession.TimelineActive) = session;
        return checks;
    }

    private static Rect2 Bounds(Transform2D transform, Rect2 rect)
    {
        var bounds = new Rect2(transform * rect.Position, Vector2.Zero);
        foreach (var corner in new[] { new Vector2(rect.End.X, rect.Position.Y), rect.End, new Vector2(rect.Position.X, rect.End.Y) })
            bounds = bounds.Expand(transform * corner);
        return bounds;
    }

    // The painted text of a centred label, not its full-width box.
    private static Rect2 TextRect(Label label)
    {
        var rect = label.GetGlobalRect();
        var font = label.GetThemeFont("font");
        float width = font.GetStringSize(label.Text, HorizontalAlignment.Left, -1, label.GetThemeFontSize("font_size")).X
            * label.GetGlobalTransform().Scale.X;
        return new Rect2(rect.Position.X + (rect.Size.X - width) / 2, rect.Position.Y, width, rect.Size.Y);
    }
}
