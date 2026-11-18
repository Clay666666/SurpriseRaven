using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Runs;

namespace SurpriseRaven.Hooks.Raven;

// Stable view identity keeps the other pickup group alive when one group is collected.
internal sealed class RavenRewardEntryHooks
{
    internal ModelId RelicId { get; }
    internal RavenPickupRequest Request { get; set; }
    internal RavenRewardSlotHooks Slot { get; }

    internal RavenRewardEntryHooks(RelicModel relic, RavenPickupRequest request, IRunState? runState,
        Action<RavenRewardEntryHooks> pickup, Action<RavenRewardEntryHooks, bool> hover,
        Func<NTreasureRoomRelicHolder?>? nativeFactory)
    {
        RelicId = relic.Id;
        Request = request;
        Slot = new RavenRewardSlotHooks(relic, runState, () => pickup(this), on => hover(this, on), nativeFactory);
    }

    internal static Vector2 Home(int count, int index, bool bonus, bool original)
    {
        if (bonus) return RavenLayout.Slots[original ? 0 : index + 1];
        return count switch
        {
            1 => new Vector2(960, 932),
            2 => new Vector2(index == 0 ? 850 : 1070, 932),
            _ => new Vector2(740 + index * 220, 932)
        };
    }
}
