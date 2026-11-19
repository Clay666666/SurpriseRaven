using System.Text.RegularExpressions;
using Godot;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;

namespace SurpriseRaven.Hooks.Raven;

// Text-only fallback used when the 2D stage cannot be built (and for headless node checks).
internal sealed partial class RavenLampPanelHooks
{
    private void BuildFallbackFrame()
    {
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Stop };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _pageRoot.AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(900, 600) };
        panel.AddThemeStyleboxOverride("panel", RavenStyleHooks.Panel());
        center.AddChild(panel);
        _content.AddThemeConstantOverride("separation", 10);
        panel.AddChild(_content);
    }

    private void BuildContent()
    {
        var state = RavenServiceHooks.State;
        _revision = state.Revision;
        _content.AddChild(Text("渡鸦路灯", RavenStyleHooks.LampTitleSize(30)));
        if (IsTerminal)
        {
            _content.AddChild(Text(_terminalMessage ?? state.LastMessage, 24));
            _exit = CreateActionButton("离开（Esc）", RequestExit);
            _content.AddChild(_exit);
            return;
        }
        _content.AddChild(Text($"第 {state.Round + 1} 轮", 22));
        var pendingPlan = RavenServiceHooks.GetPendingPlan(_player);
        string message = pendingPlan is null ? state.LastMessage : RavenStageHooks.ResultMessage(pendingPlan);
        if (!string.IsNullOrWhiteSpace(message)) _content.AddChild(Text(message, 20));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(850, 220), SizeFlagsVertical = SizeFlags.ExpandFill };
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        var entries = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(entries);
        _content.AddChild(scroll);
        var revision = _revision;
        if (pendingPlan is { } plan)
        {
            if (plan.OriginalReturn is { } original)
                AddRelic(entries, original, "取回原遗物", () => RunOperation(() => RavenServiceHooks.CollectAsync(_player,
                    new RavenPickupRequest(RavenPickupKind.Original), revision)));
            if (plan.Outcome == RavenOutcome.Bonus && RavenRewardsHooks.ChoiceText(plan.Rewards.Count) is { Length: > 0 } title)
            {
                var label = Text(title, 24);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                entries.AddChild(label);
            }
            for (int i = 0; i < plan.Rewards.Count; i++)
            {
                int index = i;
                AddRelic(entries, plan.Rewards[i], "领取",
                    () => RunOperation(() => RavenServiceHooks.CollectAsync(_player,
                        new RavenPickupRequest(RavenPickupKind.Candidate, index), revision)));
            }
        }
        else
        {
            entries.AddChild(Text("将上方可献祭遗物拖到下方圆环，松手献祭。", 22));
            entries.AddChild(Text("蜡质、初始遗物及升级品、涅奥遗物、事件与任务遗物不可投入。", 17));
        }
        var targetHost = new Control { CustomMinimumSize = new Vector2(850, 100), MouseFilter = MouseFilterEnum.Ignore };
        _fallbackTarget ??= new RavenSacrificeTargetHooks();
        _fallbackTarget.GetParent()?.RemoveChild(_fallbackTarget);
        _fallbackTarget.Position = new Vector2(297, 20);
        targetHost.AddChild(_fallbackTarget);
        _content.AddChild(targetHost);
        _exit = CreateActionButton("离开（Esc）", RequestExit);
        _content.AddChild(_exit);
    }

    private void AddRelic(VBoxContainer parent, RelicModel relic, string action, Action pressed, bool eligible = true)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var details = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        details.AddChild(Text($"{relic.Title.GetFormattedText()}  [{RarityName(relic.Rarity)}]", 21));
        details.AddChild(Text(Regex.Replace(relic.DynamicDescription.GetFormattedText(), @"\[[^\]]*\]", ""), 17));
        row.AddChild(details);
        var button = CreateActionButton(action, pressed, !eligible || RavenServiceHooks.Busy || RavenServiceHooks.State.Faulted);
        button.MouseEntered += () => { if (!button.Disabled) _fallbackPickup = pressed; };
        button.FocusEntered += () => { if (!button.Disabled) _fallbackPickup = pressed; };
        void Unfocus() { if (ReferenceEquals(_fallbackPickup, pressed)) _fallbackPickup = null; }
        button.MouseExited += Unfocus;
        button.FocusExited += Unfocus;
        button.CustomMinimumSize = new Vector2(110, 45);
        row.AddChild(button);
        parent.AddChild(row);
    }

    private static Label Text(string text, int size)
    {
        var label = RavenStyleHooks.Text(text, size, outline: 4);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return label;
    }

    internal static Button CreateActionButton(string text, Action pressed, bool disabled = false)
    {
        var button = RavenStyleHooks.Button(text, pressed, 20);
        button.Disabled = disabled;
        return button;
    }

    private static string RarityName(RelicRarity rarity) => rarity switch
    {
        RelicRarity.Common => "普通", RelicRarity.Uncommon => "罕见", RelicRarity.Rare => "稀有",
        RelicRarity.Ancient => "先古", RelicRarity.Shop => "商店", RelicRarity.Event => "事件",
        RelicRarity.Starter => "初始", _ => "无档位"
    };
}
