using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Vfx.Cards;

namespace SurpriseRaven.Hooks.Raven;

internal static partial class RavenOverlayHooks
{
    internal static async Task<int> RunRelicGlowChecksAsync()
    {
        Require(DisplayServer.GetName() == "headless", "Relic glow checks require headless.");
        var tree = (SceneTree)Engine.GetMainLoop();
        var viewport = new SubViewport { Size = new Vector2I(1920, 1080), HandleInputLocally = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled };
        var panel = new Control { Size = RavenStyleHooks.Design };
        viewport.AddChild(panel);
        tree.Root.AddChild(viewport);
        var relic = ModelDb.Relic<Strawberry>().ToMutable();
        var source = NRelicInventoryHolder.Create(relic) ?? throw new InvalidOperationException("Native inventory holder unavailable.");
        source.Position = new Vector2(60, 50);
        viewport.AddChild(source);
        int checks = 0;
        void CheckGlow(bool condition, string message) { Require(condition, message); checks++; }
        async Task Frame() => await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        RavenNativeDragBindingHooks? binding = null;
        RavenRetainedIconHooks? retained = null;
        try
        {
            await Frame();
            var size = source.Size;
            var nativeFilter = source.MouseFilter;
            binding = new RavenNativeDragBindingHooks(source, relic, _ => { }, () => { });
            binding.SetHint(true);
            var glows = source.Relic.GetChildren().OfType<NCardUncommonGlow>().ToArray();
            CheckGlow(glows.Length == 1 && glows[0].Visible && glows[0].Emitting,
                "An eligible top relic uses one emitting native teal glow.");
            CheckGlow(glows[0].Position.IsEqualApprox(source.Relic.Size / 2) && glows[0].Scale.IsEqualApprox(Vector2.One * 0.3f),
                "Top halo is centered and scaled in native relic coordinates.");
            binding.SetHint(false);
            CheckGlow(!glows[0].Visible && !glows[0].Emitting, "Busy or ineligible relic stops and hides its halo.");
            binding.SetHint(true);
            binding.SetHint(true);
            CheckGlow(source.Relic.GetChildren().OfType<NCardUncommonGlow>().Count() == 1,
                "Repeated eligibility refresh does not stack native particles.");
            CheckGlow(source.IsEnabled && source.MouseFilter == nativeFilter && source.Size.IsEqualApprox(size),
                "Halo attachment preserves original holder input and layout.");

            RelicModel? pendingOriginal = relic;
            retained = new RavenRetainedIconHooks(panel, () => pendingOriginal,
                model => GodotObject.IsInstanceValid(source) && ReferenceEquals(model, relic) ? source.Relic : null);
            CheckGlow(!source.Relic.Visible && source.Visible && source.Size.IsEqualApprox(size),
                "Pending original hides only its visual while retaining the inventory holder slot.");
            panel.Hide();
            retained.Refresh();
            CheckGlow(!source.Relic.Visible, "Covering the lamp during a native reward choice does not reveal the unclaimed original.");
            panel.Show();
            pendingOriginal = null;
            retained.Refresh();
            CheckGlow(source.Relic.Visible, "Claiming the original restores its existing native visual.");
            pendingOriginal = ModelDb.Relic<Strawberry>().ToMutable();
            retained.Refresh();
            CheckGlow(source.Relic.Visible, "A same-ID different instance cannot hide an unrelated original.");
            pendingOriginal = relic;
            retained.Refresh();
            retained.Dispose();
            retained.Dispose();
            CheckGlow(source.Relic.Visible, "Teardown restores the exact retained visual without deleting its holder.");

            source.Relic.Hide();
            retained = new RavenRetainedIconHooks(panel, () => relic, _ => source.Relic);
            retained.Dispose();
            CheckGlow(!source.Relic.Visible, "Cleanup preserves an icon that was already hidden by its owner.");
            source.Relic.Show();
            binding.Dispose();
            binding.Dispose();
            CheckGlow(glows[0].IsQueuedForDeletion() && !glows[0].Visible && !glows[0].Emitting,
                "Disposal removes only the binding's halo and stops its emission immediately.");
            await Frame();
            CheckGlow(source.Relic.GetChildren().OfType<NCardUncommonGlow>().Count() == 0,
                "Native halo is freed after the binding ends.");
        }
        finally
        {
            retained?.Dispose();
            binding?.Dispose();
            viewport.Free();
        }
        return checks;
    }
}
