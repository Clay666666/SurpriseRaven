using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Vfx.Cards;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace SurpriseRaven.Hooks.Raven;

internal static partial class RavenOverlayHooks
{
    internal static async Task<int> RunNativeDragChecksAsync()
    {
        if (DisplayServer.GetName() != "headless") throw new InvalidOperationException("Native drag checks require headless.");
        using var f = new RavenHeadlessDragFixtureHooks((SceneTree)Engine.GetMainLoop());
        int checks = 0;
        void CheckDrag(bool condition, string description) { Require(condition, description); checks++; }
        await f.Frame();
        var fill = f.Target.FillPolygon();
        CheckDrag(Geometry2D.TriangulatePolygon(fill).Length == (fill.Length - 2) * 3,
            "The highlighted altar fill forms a valid polygon without a duplicate closing vertex.");
        CheckDrag(f.Drag.SourceCount == 1 && RavenNativeDragInputHooks.GetThreshold(f.Source) == 10,
            "Native binding owns one source and temporarily uses the ten pixel threshold.");
        await f.Press();
        CheckDrag(f.Drag.Active && !f.Drag.Dragging, "Native MousePressed starts a potential drag.");
        await f.Release();
        CheckDrag(f.Inspections == 1 && f.Drops == 0, "An ordinary click preserves native inspection.");
        await f.Press();
        await f.Move(f.SourcePoint + new Vector2(9, 0));
        CheckDrag(!f.Drag.Dragging, "Movement below threshold does not start dragging.");
        await f.Move(f.SourcePoint + new Vector2(11, 0));
        CheckDrag(f.Drag.Dragging && !RavenNativeDragInputHooks.NativePressed(f.Source),
            "Crossing threshold starts a drag and clears native pressed state.");
        await f.Move(f.TargetPoint);
        CheckDrag(f.Target.Hot && f.Drag.PreviewCenter.IsEqualApprox(f.TargetPoint), "Target highlights and preview remains on the pointer.");
        await f.Release();
        CheckDrag(f.Drops == 1 && f.SubmittedRevision == 7 && !f.Drag.Active, "Valid release submits once with captured revision.");
        CheckDrag(RavenNativeDragInputHooks.SuppressesInspect(f.Relic), "Drag release retains inspection suppression through GUI dispatch.");
        f.Source.ForceClick();
        CheckDrag(f.Inspections == 1, "Delayed native release cannot open inspection after a drag.");
        await f.Release();
        CheckDrag(f.Drops == 1, "A duplicate mouse release cannot submit again.");
        await f.Press();
        await f.Release();
        CheckDrag(f.Inspections == 2, "The next genuine click clears drag-only suppression.");

        await f.Press();
        await f.Move(f.TargetPoint + new Vector2(300, 0));
        await f.Release();
        CheckDrag(f.Drops == 1 && !f.Target.Hot, "An outside release cancels without a sacrifice.");
        f.Eligible = false;
        await f.Press();
        CheckDrag(!f.Drag.Active, "Ineligible relic cannot begin a drag.");
        await f.Release();
        f.Eligible = true;
        var toyBox = ModelDb.Relic<ToyBox>().ToMutable();
        foreach (var blocked in new[] { f.Relic, toyBox })
        {
            f.Relic.IsWax = ReferenceEquals(blocked, f.Relic);
            f.Drag.SetSources([(f.Source, blocked)]);
            await f.Frame();
            var glow = f.Source.GetChildren().OfType<NCardUncommonGlow>().Single(node => !node.IsQueuedForDeletion());
            CheckDrag(!glow.Visible && !glow.Emitting, "A wax or blocked Ancient relic displays the sacrifice halo.");
            await f.Press();
            await f.Move(f.TargetPoint);
            await f.Release();
            CheckDrag(!f.Drag.Active && f.Drops == 1, "A wax or blocked Ancient relic was submitted through drag.");
        }
        f.Relic.IsWax = false;
        f.Drag.SetSources([(f.Source, f.Relic)]);
        await f.Frame();
        CheckDrag(f.Source.GetChildren().OfType<NCardUncommonGlow>().Single(node => !node.IsQueuedForDeletion()).Emitting,
            "The normal Strike Dummy cannot be offered.");
        await f.Press();
        await f.Move(f.TargetPoint);
        CheckDrag(f.Drag.Cancel() && !f.Drag.Cancel(), "Escape route cancels the active drag once.");
        await f.Release();
        CheckDrag(f.Drops == 1, "Release after escape cancellation has no effect.");

        foreach (string reason in new[] { "covered or unfocused", "revision", "opportunity", "eligibility", "hidden", "source removed" })
        {
            await f.Press();
            await f.Move(f.TargetPoint);
            switch (reason)
            {
                case "covered or unfocused": f.Allowed = false; break;
                case "revision": f.Revision++; break;
                case "opportunity": f.Opportunity++; break;
                case "eligibility": f.Eligible = false; break;
                case "hidden": f.Panel.Hide(); break;
                case "source removed": f.Drag.SetSources([]); break;
            }
            await f.Release();
            CheckDrag(f.Drops == 1 && !f.Drag.Active, $"Dispatch rejects a drag invalidated by {reason}.");
            f.Allowed = f.Eligible = true;
            f.Panel.Show();
            f.Drag.SetSources([(f.Source, f.Relic)]);
            await f.Frame();
        }

        f.Page.Position = new Vector2(90, 160);
        f.Page.Scale = new Vector2(0.7f, 0.55f);
        await f.Frame();
        CheckDrag(f.Target.HitGlobal(f.TargetPoint), "Nonuniformly scaled altar still hits its center.");
        var outsideCorner = f.Target.GetGlobalTransformWithCanvas() * new Vector2(1, 1);
        CheckDrag(!f.Target.HitGlobal(outsideCorner), "Ellipse does not accept the rectangle's corners.");
        await f.Press();
        await f.Move(f.TargetPoint);
        CheckDrag(f.Drag.PreviewCenter.IsEqualApprox(f.TargetPoint), "Unclipped preview stays centered after page compression.");
        await f.Release();
        CheckDrag(f.Drops == 2, "Transformed target submits through native input dispatch.");

        var second = new NButton { Position = new Vector2(150, 40), Size = new Vector2(68, 68) };
        f.Viewport.AddChild(second);
        f.Drag.SetSources([(f.Source, f.Relic), (second, f.Relic)]);
        f.Drag.SetSources([(f.Source, f.Relic), (second, f.Relic)]);
        CheckDrag(f.Drag.SourceCount == 2, "Repeated dynamic inventory binding is idempotent.");
        f.Drag.SetSources([(f.Source, f.Relic)]);
        second.Free();
        await f.Press();
        f.Source.Hide();
        await f.Release();
        CheckDrag(!f.Drag.Active && f.Drops == 2, "Hidden native source cancels without changing inventory.");
        f.Source.Show();
        f.Drag.Dispose();
        f.Drag.Dispose();
        CheckDrag(RavenNativeDragInputHooks.GetThreshold(f.Source) == f.OriginalThreshold,
            "Disposal restores the original native threshold.");
        await f.Press();
        await f.Move(f.TargetPoint);
        await f.Release();
        CheckDrag(f.Drops == 2 && RavenNativeDragInputHooks.Owner is null, "Disposed binding leaves no input owner or callbacks.");
        return checks;
    }
}
