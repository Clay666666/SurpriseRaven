using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace SurpriseRaven.Hooks.Raven;

public sealed class RavenState
{
    public List<int> PendingLights { get; set; } = [];
    public List<RavenDeferredOpportunity> DeferredOpportunities { get; set; } = [];
    public int Lights { get; set; }
    public int Round { get; set; }
    public long Revision { get; set; }
    public bool IsActive { get; set; }
    public bool Faulted { get; set; }
    public bool InFlight { get; set; }
    public string RoomKey { get; set; } = "";
    public string LastMessage { get; set; } = "选择一个遗物投入，或放弃本次机会。";
    // Legacy 0.2.x bonus choice; migrated into Pending on restore.
    public List<SerializableRelic> BonusCandidates { get; set; } = [];
    // Locked result: the stake is committed on drop and the output is granted on pickup.
    public RavenPendingRound? Pending { get; set; }
    public long OpportunityId { get; set; }
    public HashSet<string> BlockedStakeIds { get; set; } = new(StringComparer.Ordinal);
    public bool HasOpportunity => PendingLights.Count > 0;
}

public sealed class RavenPendingRound
{
    // Missing fields identify rounds saved before immediate sacrifice was introduced.
    public int SettlementVersion { get; set; }
    public SerializableRelic? StakeSnapshot { get; set; }
    public bool StakeConsumed { get; set; }
    public RavenOriginalState OriginalState { get; set; }
    public bool BonusResolved { get; set; }
    public string? AncientId { get; set; }
    public string? Notice { get; set; }
    public RavenOutcome? Shortage { get; set; }
    internal int ExcludedCandidates { get; set; }
    public RavenOutcome Outcome { get; set; }
    public string StakeId { get; set; } = "";
    public int StakeIndex { get; set; } = -1;
    public string StakeTitle { get; set; } = "";
    public RelicRarity Target { get; set; }
    // -1 identifies legacy bonus choices whose round and lights had already advanced.
    public int NextLights { get; set; } = -1;
    public bool RoundAlreadyApplied { get; set; }
    public List<SerializableRelic> Candidates { get; set; } = [];
}

public enum RavenOutcome { Swallow, Upgrade, Swap, Downgrade, Bonus, Keep }
public enum RavenOriginalState { None, Pending, Returned, Missing, ForfeitStarted, Forfeited }
public enum RavenPickupKind { Candidate, Original }
public readonly record struct RavenPickupRequest(RavenPickupKind Kind, int CandidateIndex = 0);
public sealed record RavenDeferredOpportunity(int Lights, string RoomKey);
