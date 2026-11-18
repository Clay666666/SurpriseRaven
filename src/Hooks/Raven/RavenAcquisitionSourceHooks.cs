using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Rooms;

namespace SurpriseRaven.Hooks.Raven;

internal static class RavenAcquisitionSourceHooks
{
    internal static bool CaptureDirectNeowAncient(Player player, RelicModel relic) =>
        RavenServiceHooks.IsSingleplayer && !RavenServiceHooks.SuppressAcquisitions
        && relic.Rarity == RelicRarity.Ancient
        && player.RunState.BaseRoom is EventRoom { CanonicalEvent: Neow } room
        && IsDirectNeowAncient(relic, room.LocalMutableEvent);

    // Snapshot before Obtain: AfterObtained can award other relics or change event state.
    internal static bool IsDirectNeowAncient(RelicModel relic, EventModel? currentEvent) =>
        relic.Rarity == RelicRarity.Ancient && currentEvent is Neow neow
        && neow.CurrentOptions.Any(option => option.WasChosen && ReferenceEquals(option.Relic, relic));
}
