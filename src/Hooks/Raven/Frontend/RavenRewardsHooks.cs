using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Runs;
using static SurpriseRaven.Hooks.Raven.RavenStyleHooks;

namespace SurpriseRaven.Hooks.Raven;

// Original returns and candidate rewards are independent groups of native chest holders.
internal sealed partial class RavenRewardsHooks : Control
{
    private readonly List<RavenRewardEntryHooks> _entries = [];
    private readonly IRunState? _runState;
    private readonly Func<NTreasureRoomRelicHolder?>? _nativeFactory;
    private readonly Label _choiceTitle = Text("", 24, bold: true, outline: 5);
    private TextureRect? _stake;
    private RavenRewardEntryHooks? _hovered;
    private bool _interactive;

    internal RavenRewardsHooks(IRunState? runState = null, Func<NTreasureRoomRelicHolder?>? nativeFactory = null)
    {
        _runState = runState;
        _nativeFactory = nativeFactory;
        Name = "Rewards";
        Size = Design;
        MouseFilter = MouseFilterEnum.Ignore;
        _choiceTitle.Name = "ChoiceTitle";
        _choiceTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _choiceTitle.Hide();
        AddChild(_choiceTitle);
        TreeExiting += Clear;
    }

    internal event Action<RavenPickupRequest>? PickupRequested;
    internal Func<bool> CanInteract { get; set; } = () => true;
    internal bool Interactive
    {
        get => _interactive;
        set
        {
            _interactive = value;
            foreach (var entry in _entries) entry.Slot.Interactive = value;
        }
    }
    internal int Count => _entries.Count;
    internal int HoveredIndex => _hovered is null ? -1 : _entries.IndexOf(_hovered);
    internal RavenPickupRequest? HoveredRequest => _hovered?.Request;
    internal Control? FirstSlot => _entries.FirstOrDefault()?.Slot.Button;

    internal void ShowRewards(IReadOnlyList<RelicModel> relics, Vector2? dropFrom,
        RelicModel? originalReturn = null, bool bonus = false, bool reducedMotion = false)
    {
        // A synchronous refresh keeps existing controls enabled and preserves valid focus.
        HideStake();
        List<RavenRewardEntryHooks> next = [];
        if (originalReturn is not null)
            Add(originalReturn, new RavenPickupRequest(RavenPickupKind.Original), 0);
        for (int i = 0; i < relics.Count && i < 3; i++)
            Add(relics[i], new RavenPickupRequest(RavenPickupKind.Candidate, i), i);
        foreach (var old in _entries.Except(next)) old.Slot.Dispose();
        _entries.Clear();
        _entries.AddRange(next);
        if (_hovered is not null && !_entries.Contains(_hovered)) _hovered = null;
        RefreshChoiceTitle(Math.Min(relics.Count, 3), bonus);
        RefreshNavigation();

        void Add(RelicModel relic, RavenPickupRequest request, int index)
        {
            var entry = _entries.FirstOrDefault(e => e.Request.Kind == request.Kind && e.RelicId == relic.Id);
            bool created = entry is null;
            entry ??= new RavenRewardEntryHooks(relic, request, _runState, RequestPickup, Hover, _nativeFactory);
            entry.Request = request;
            var home = RavenRewardEntryHooks.Home(relics.Count, index, bonus, request.Kind == RavenPickupKind.Original);
            if (created)
            {
                entry.Slot.Root.Name = request.Kind == RavenPickupKind.Original ? "OriginalReturn" : $"Reward{index}";
                entry.Slot.Root.Position = (reducedMotion ? home : dropFrom ?? home) - entry.Slot.Root.Size / 2;
                AddChild(entry.Slot.Root);
                if (dropFrom is not null && IsInsideTree())
                {
                    var tween = entry.Slot.Root.CreateTween();
                    if (reducedMotion)
                    {
                        entry.Slot.Root.Modulate = new Color(Colors.White, 0);
                        tween.TweenProperty(entry.Slot.Root, "modulate:a", 1f, 0.3);
                    }
                    else
                    {
                        tween.TweenInterval(next.Count * 0.08);
                        tween.TweenProperty(entry.Slot.Root, "position", home - entry.Slot.Root.Size / 2, 0.45)
                            .SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
                    }
                }
            }
            else entry.Slot.Root.Position = home - entry.Slot.Root.Size / 2;
            next.Add(entry);
        }
    }

    internal static string ChoiceText(int count) => count switch { 3 => "三选一！！！", 2 => "二选一！！！", _ => "" };

    private void RefreshChoiceTitle(int count, bool bonus)
    {
        _choiceTitle.Text = bonus ? ChoiceText(count) : "";
        _choiceTitle.Visible = _choiceTitle.Text.Length > 0;
        if (!_choiceTitle.Visible) return;
        var first = RavenRewardEntryHooks.Home(count, 0, true, false);
        var last = RavenRewardEntryHooks.Home(count, count - 1, true, false);
        _choiceTitle.Size = new Vector2(last.X - first.X + 136, 36);
        _choiceTitle.Position = new Vector2(first.X - 68, first.Y - 112);
    }

    private void RefreshNavigation()
    {
        if (!IsInsideTree() || _entries.Count == 0) return;
        for (int i = 0; i < _entries.Count; i++)
        {
            var button = _entries[i].Slot.Button;
            button.FocusNeighborLeft = _entries[(i + _entries.Count - 1) % _entries.Count].Slot.Button.GetPath();
            button.FocusNeighborRight = _entries[(i + 1) % _entries.Count].Slot.Button.GetPath();
        }
    }

    private void Hover(RavenRewardEntryHooks entry, bool on)
    {
        if (on) _hovered = entry;
        else if (ReferenceEquals(_hovered, entry)) _hovered = null;
    }

    internal void PickupHovered() { if (_hovered is not null) RequestPickup(_hovered); }

    internal void ClearHoverTips()
    {
        foreach (var entry in _entries) entry.Slot.ClearHover();
        _hovered = null;
    }

    private void RequestPickup(RavenRewardEntryHooks entry)
    {
        if (!Interactive || !IsVisibleInTree() || !CanInteract() || !_entries.Contains(entry)) return;
        Interactive = false;
        PickupRequested?.Invoke(entry.Request);
    }

    internal void ShowStake(Texture2D? icon, Vector2 from, double duration)
    {
        HideStake();
        _choiceTitle.Hide();
        _stake = new TextureRect
        {
            Texture = icon, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Size = new Vector2(96, 96), Position = from - new Vector2(48, 48), MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_stake);
        if (IsInsideTree()) _stake.CreateTween().TweenProperty(_stake, "position", RavenLayout.Altar - new Vector2(48, 70), duration)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
    }

    internal void HideStake()
    {
        _stake?.QueueFree();
        _stake = null;
    }

    internal void Clear()
    {
        Interactive = false;
        foreach (var entry in _entries) entry.Slot.Dispose();
        _entries.Clear();
        _hovered = null;
        _choiceTitle.Text = "";
        _choiceTitle.Hide();
        HideStake();
    }

    internal void Update(double time) { }
}
