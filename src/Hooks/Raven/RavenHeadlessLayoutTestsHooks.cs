using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace SurpriseRaven.Hooks.Raven;

internal static partial class RavenOverlayHooks
{
    // Isolated node fixtures only: no run, menu navigation or real-game input.
    internal static int RunLayoutChecks()
    {
        if (DisplayServer.GetName() != "headless")
            throw new InvalidOperationException("Raven layout checks require headless.");
        var tree = (SceneTree)Engine.GetMainLoop();
        int checks = 0;
        void CheckLayout(bool condition, string message) { Require(condition, message); checks++; }
        foreach (var size in new[] { new Vector2(1920, 1080), new Vector2(1680, 1260), new Vector2(2580, 1080), new Vector2(1280, 720) })
        {
            var fixture = new Control { Size = size, Position = new Vector2(19, 23), Scale = new Vector2(1.2f, 1.2f) };
            tree.Root.AddChild(fixture);
            try
            {
                var panel = new RavenLampPanelHooks(null!, "layout fixture", allowStage: false);
                fixture.AddChild(panel);
                var background = new Control { Position = new Vector2(0, -1), Size = new Vector2(size.X, 100), Scale = new Vector2(1, 1.01f) };
                var map = new Control { Position = new Vector2(size.X - 240, 0), Size = new Vector2(80, 80) };
                fixture.AddChild(background);
                fixture.AddChild(map);
                float top = RavenPageLayoutHooks.TopBoundary(panel, [background, map]);
                CheckLayout(top is >= 112 and <= 113, "Native bounds are measured in panel coordinates, allowing one pixel of outward rounding.");
                CheckLayout(RavenPageLayoutHooks.TopBoundary(panel, []) == 112, "Unready native UI uses the 112 logical-pixel fallback.");
                RavenPageLayoutHooks.Apply(panel.ContentRoot, size, top);
                var content = panel.ContentRoot;
                CheckLayout(panel.Size.IsEqualApprox(size) && content.Size.IsEqualApprox(size), "Outer modal and internal logical dimensions remain unchanged.");
                CheckLayout(content.Scale.X == 1 && content.Position.X == 0, "Page width and horizontal origin are unchanged.");
                CheckLayout(Mathf.IsEqualApprox(content.Position.Y + content.Size.Y * content.Scale.Y, size.Y), "Compressed page ends at the original bottom edge.");
                CheckLayout(content.Position.Y >= 112 && content.Scale.Y > 0 && content.Scale.Y < 1, "Compressed page clears the native top region.");
                CheckLayout(content.GetChildCount() == 1 && content.GetChild(0) is CenterContainer, "Text fallback shares the transformed content root.");
                CheckLayout(panel.UseSharedBackstop && panel.MouseFilter == Control.MouseFilterEnum.Stop, "Full-screen modal behaviour is retained.");
                var target = new Control { Position = new Vector2(400, 300), Size = new Vector2(96, 96) };
                content.AddChild(target);
                var center = target.GetGlobalTransform() * (target.Size / 2);
                CheckLayout(target.GetGlobalRect().HasPoint(center), "Scaled control hit rectangle contains its displayed center.");
                CheckLayout(!target.GetGlobalRect().HasPoint(target.GetGlobalRect().Position - Vector2.One), "Scaled control does not accept points outside its displayed bounds.");
                var ghost = new Control { Size = new Vector2(96, 96) };
                content.AddChild(ghost);
                var mouse = content.GetGlobalTransform().AffineInverse() * center;
                ghost.Position = RavenPageLayoutHooks.CenterPreview(mouse, ghost.Size);
                CheckLayout((ghost.GetGlobalTransform() * (ghost.Size / 2)).IsEqualApprox(center), "Drag preview stays centered on the mouse after Y compression.");
                CheckLayout(Mathf.IsEqualApprox(target.Position.X, 400) && Mathf.IsEqualApprox(target.Size.X, 96), "Child horizontal geometry is untouched.");
                panel.AfterOverlayClosed();
            }
            finally { fixture.Free(); }
        }
        checks += CheckNativeLayoutTweens(tree);
        return checks;
    }

    private static int CheckNativeLayoutTweens(SceneTree tree)
    {
        int checks = 0;
        void CheckNative(bool condition, string message) { Require(condition, message); checks++; }
        var parent = new Control { Size = new Vector2(1920, 1080), Position = new Vector2(0, 112), Scale = new Vector2(1, 0.75f) };
        tree.Root.AddChild(parent);
        try
        {
            var banner = RavenStyleHooks.Native<NCommonBanner>("res://scenes/ui/common_banner.tscn")!;
            var original = RavenStyleHooks.Native<NCommonBanner>("res://scenes/ui/common_banner.tscn")!;
            var back = RavenStyleHooks.Native<NBackButton>("res://scenes/ui/back_button.tscn")!;
            RavenNativeLayoutHooks.Register(banner);
            RavenNativeLayoutHooks.Register(back);
            parent.AddChild(banner);
            parent.AddChild(original);
            parent.AddChild(back);
            Vector2 Point(Control widget, string field) => (Vector2)AccessTools.Field(widget.GetType(), field).GetValue(widget)!;
            void Finish(Control widget, string field) => ((Tween)AccessTools.Field(widget.GetType(), field).GetValue(widget)!).CustomStep(1);
            var show = Point(banner, "_showPos");
            var hide = Point(banner, "_hidePos");
            CheckNative(banner.Position.IsEqualApprox(show), "Owned banner initial layout uses local coordinates exactly once.");
            banner.AnimateIn();
            ((Tween)AccessTools.Field(typeof(NCommonBanner), "_tween").GetValue(banner)!).CustomStep(0.2);
            CheckNative(!banner.Position.IsEqualApprox(hide) && !banner.Position.IsEqualApprox(show), "Native banner animation retains an intermediate position.");
            Finish(banner, "_tween");
            CheckNative(banner.GlobalPosition.IsEqualApprox(parent.GetGlobalTransform() * show), "Owned banner tween ends inside the transformed page.");
            original.AnimateIn();
            Finish(original, "_tween");
            CheckNative(original.GlobalPosition.IsEqualApprox(Point(original, "_showPos")), "Unregistered native banner retains the game's global-coordinate behaviour.");
            CheckNative(RavenNativeLayoutHooks.PositionPath(original) == "global_position", "Native widgets outside the Mod are not opted into local animation.");
            back.MoveToHidePosition();
            CheckNative(back.Position.IsEqualApprox(Point(back, "_hidePos")), "Owned back button's direct hide positioning follows the content transform.");
            back.Enable();
            Finish(back, "_moveTween");
            CheckNative(back.GlobalPosition.IsEqualApprox(parent.GetGlobalTransform() * Point(back, "_showPos")), "Owned back button enable tween respects Y compression.");
            back.Disable();
            Finish(back, "_moveTween");
            CheckNative(back.Position.IsEqualApprox(Point(back, "_hidePos")), "Owned back button disable tween retains its local target.");
        }
        finally { parent.Free(); }
        return checks;
    }
}
