using Godot;

namespace SurpriseRaven.Hooks.Raven;

internal static partial class RavenOverlayHooks
{
    // Real engine flow layout, with detached UI fixtures only. No run or real-menu input.
    internal static async Task<int> RunRelicLayoutChecksAsync()
    {
        if (DisplayServer.GetName() != "headless")
            throw new InvalidOperationException("Relic layout checks require headless.");
        var tree = (SceneTree)Engine.GetMainLoop();
        int checks = 0;
        void Check(bool condition, string message) { Require(condition, message); checks++; }
        foreach (var size in new[] { new Vector2(1920, 1080), new Vector2(1680, 1260), new Vector2(2580, 1080) })
        {
            var fixture = new Control { Size = size, Position = new Vector2(19, 23), Scale = new Vector2(1.2f, 0.9f) };
            tree.Root.AddChild(fixture);
            try
            {
                var panel = new Control { Size = size };
                var content = new Control();
                fixture.AddChild(panel);
                panel.AddChild(content);
                var background = new Control { Size = new Vector2(size.X, 100) };
                var map = new Control { Position = new Vector2(size.X - 200, 0), Size = new Vector2(80, 90) };
                fixture.AddChild(background);
                fixture.AddChild(map);
                var inventoryParent = new Control();
                fixture.AddChild(inventoryParent);
                var flow = new HFlowContainer { Position = new Vector2(24, 104), Size = new Vector2(480, 64) };
                flow.AddThemeConstantOverride("h_separation", 8);
                flow.AddThemeConstantOverride("v_separation", 6);
                inventoryParent.AddChild(flow);
                var rows = new List<Control>();
                IEnumerable<Control?> Bounds() => new Control?[] { background, map }.Concat(rows);
                float Boundary() => RavenPageLayoutHooks.TopBoundary(panel, Bounds());
                void Verify(float expectedBottom, string label)
                {
                    float top = Boundary();
                    Check(top >= expectedBottom + 12 - 0.01f && top <= expectedBottom + 13.01f,
                        $"{label}: header boundary does not follow the visible relic rows.");
                    RavenPageLayoutHooks.Apply(content, size, top);
                    Check(content.Position.X == 0 && content.Scale.X == 1 && content.Size.IsEqualApprox(size),
                        $"{label}: horizontal or logical dimensions changed.");
                    Check(Mathf.IsEqualApprox(content.Position.Y + content.Size.Y * content.Scale.Y, size.Y),
                        $"{label}: page no longer ends at the original bottom.");
                }
                void AddTo(int count)
                {
                    while (rows.Count < count)
                    {
                        var row = new Control { CustomMinimumSize = new Vector2(64, 64), MouseFilter = Control.MouseFilterEnum.Ignore };
                        rows.Add(row);
                        flow.AddChild(row);
                    }
                }

                await WaitForRelicLayout(tree);
                Verify(100, "Empty inventory");
                foreach (var sample in new[] { (Count: 1, Lines: 1), (Count: 6, Lines: 1), (Count: 7, Lines: 2), (Count: 13, Lines: 3) })
                {
                    AddTo(sample.Count);
                    await WaitForRelicLayout(tree);
                    Check(flow.GetLineCount() == sample.Lines, "Actual FlowContainer did not wrap into the expected rows.");
                    Verify(104 + sample.Lines * 64 + (sample.Lines - 1) * 6, $"{sample.Lines} relic rows");
                }

                flow.Size = new Vector2(224, flow.Size.Y);
                await WaitForRelicLayout(tree);
                Check(flow.GetLineCount() == 5, "Narrowing the inventory did not produce five rows.");
                Verify(104 + 5 * 64 + 4 * 6, "Narrow inventory");
                flow.Position = new Vector2(24, -1000);
                Verify(100, "Inventory moved above the screen while still Visible");
                flow.Position = new Vector2(24, -20);
                Verify(-20 + 5 * 64 + 4 * 6, "Partially visible inventory");
                flow.Position = new Vector2(24, 104);
                inventoryParent.Hide();
                Verify(100, "Inventory hidden through its ancestor");
                inventoryParent.Show();
                while (rows.Count > 6)
                {
                    var removed = rows[^1];
                    rows.RemoveAt(rows.Count - 1);
                    flow.RemoveChild(removed);
                    removed.Free();
                }
                await WaitForRelicLayout(tree);
                Check(flow.GetLineCount() == 2, "Removing relics did not reduce the line count.");
                Verify(104 + 2 * 64 + 6, "Relics removed");
                flow.Size = new Vector2(480, flow.Size.Y);
                await WaitForRelicLayout(tree);
                Check(flow.GetLineCount() == 1, "Widening the inventory did not restore one row.");
                Verify(168, "Inventory widened");
                inventoryParent.Position = new Vector2(17, 30);
                inventoryParent.Scale = new Vector2(1.1f, 0.8f);
                Verify(30 + 168 * 0.8f, "Inventory ancestor transformed");

                float stableTop = Boundary();
                var excluded = new Control { Position = new Vector2(0, 700), Size = new Vector2(64, 64) };
                fixture.AddChild(excluded);
                excluded.Hide();
                Check(Mathf.IsEqualApprox(RavenPageLayoutHooks.TopBoundary(panel, Bounds().Append(excluded)), stableTop),
                    "Hidden nodes affect the header boundary.");
                excluded.Show();
                excluded.Position = new Vector2(size.X + 10, 700);
                Check(Mathf.IsEqualApprox(RavenPageLayoutHooks.TopBoundary(panel, Bounds().Append(excluded)), stableTop),
                    "Nodes outside the horizontal visible area affect the header boundary.");
                excluded.Position = new Vector2(0, 700);
                excluded.QueueFree();
                Check(Mathf.IsEqualApprox(RavenPageLayoutHooks.TopBoundary(panel, Bounds().Append(excluded)), stableTop),
                    "Queued nodes affect the header boundary before deletion.");

                var target = new Control { Position = new Vector2(400, 300), Size = new Vector2(96, 96) };
                content.AddChild(target);
                var displayedCenter = target.GetGlobalTransform() * (target.Size / 2);
                Check(target.GetGlobalRect().HasPoint(displayedCenter), "Compressed target hit area misses its displayed center.");
                var preview = new Control { Size = target.Size };
                content.AddChild(preview);
                preview.Position = RavenPageLayoutHooks.CenterPreview(content.GetGlobalTransform().AffineInverse() * displayedCenter, preview.Size);
                Check((preview.GetGlobalTransform() * (preview.Size / 2)).IsEqualApprox(displayedCenter),
                    "Drag preview shifts after the relic boundary changes.");
            }
            finally { fixture.Free(); }
        }
        return checks;
    }

    private static async Task WaitForRelicLayout(SceneTree tree)
    {
        // FlowContainer sorting is deferred; allow both the layout request and its redraw pass.
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }
}
