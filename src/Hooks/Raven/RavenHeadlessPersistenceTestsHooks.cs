using System.Text.Json;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    // Matches the verified RitsuLib RunSavedDataJson options, including saved public fields.
    private static readonly JsonSerializerOptions SaveOptions = new() { IncludeFields = true };

    private static void TestRngRestore()
    {
        var original = new Rng(918273UL, "raven.headless.fixture");
        for (int i = 0; i < 23; i++) original.NextInt(0, 100);
        var json = JsonSerializer.Serialize(original.ToSerializable(), SaveOptions);
        var restoredSave = JsonSerializer.Deserialize<SerializableRng>(json, SaveOptions)
            ?? throw new InvalidOperationException("RNG snapshot did not restore.");
        var restored = new Rng(restoredSave);
        for (int i = 0; i < 100; i++)
            Check(original.NextUnsignedLong() == restored.NextUnsignedLong(), "RNG state continuation diverged.");
        var initial = new Rng(1234UL, "raven.initial_lights.fixture");
        for (int i = 0; i < 100; i++)
        {
            int lights = initial.NextInt(0, 6);
            Check(lights is >= 0 and <= 5, "Initial light value outside 0–5.");
        }
    }

    private static void TestRelicRestore()
    {
        // Does not Obtain, call AfterObtained, alter a player, or start a run.
        var glass = (SeaGlass)ModelDb.Relic<SeaGlass>().ToMutable();
        glass.CharacterId = ModelDb.Character<Ironclad>().Id;
        glass.FloorAddedToDeck = 7;
        var state = new RavenState
        {
            PendingLights = [2, 5], Lights = 4, Round = 3, Revision = 9,
            RoomKey = "headless.fixture", IsActive = true,
            LastMessage = "保存候选测试", BonusCandidates = [glass.ToSerializable()]
        };
        var json = JsonSerializer.Serialize(state, SaveOptions);
        var copy = JsonSerializer.Deserialize<RavenState>(json, SaveOptions)
            ?? throw new InvalidOperationException("Raven state did not restore.");
        Check(copy.PendingLights.SequenceEqual([2, 5]) && copy.Lights == 4 && copy.Round == 3,
            "Light/queue state lost during save.");
        Check(copy.Revision == 9 && copy.RoomKey == state.RoomKey && copy.IsActive,
            "Session identity lost during save.");
        Check(copy.LastMessage == state.LastMessage && copy.BonusCandidates.Count == 1,
            "Pending choice state lost during save.");
        Check(copy.BonusCandidates[0].Props is not null, "Complete candidate properties were discarded.");
        var relic = RelicModel.FromSerializable(copy.BonusCandidates[0]);
        Check(relic is SeaGlass restored && restored.CharacterId == glass.CharacterId,
            "SeaGlass saved character initialization was discarded.");
        Check(relic.FloorAddedToDeck == 7, "Candidate floor property was discarded.");
        Check(!ReferenceEquals(relic, glass), "Restore reused the original relic instance.");
    }

    private static void TestSessions()
    {
        var state = new RavenState();
        RavenSessionHooks.Enqueue(state, 2, "room-a");
        RavenSessionHooks.Enqueue(state, 5, "room-a");
        Check(state.PendingLights.SequenceEqual([2, 5]), "Batch acquisition merged independent opportunities.");
        Check(state.Lights == 2 && state.Round == 0 && !state.IsActive, "First offer initial state differs.");
        RavenSessionHooks.ApplyRound(state, 3);
        Check(state.Lights == 3 && state.Round == 1 && state.IsActive, "Round did not light and activate.");
        Check(state.PendingLights[1] == 5, "Current round changed the next offer's initial lights.");
        state.BonusCandidates.Add(new SerializableRelic());
        RavenSessionHooks.EndOpportunity(state, "退出");
        Check(state.PendingLights.SequenceEqual([5]) && state.Lights == 5 && state.Round == 0,
            "Exit failed to advance to the next independent offer.");
        Check(!state.IsActive && state.BonusCandidates.Count == 0, "Exit retained active reward choice.");
        RavenSessionHooks.ApplyRound(state, 6);
        RavenSessionHooks.ApplyRound(state, 6);
        Check(state.Lights == 6 && state.Round == 2 && state.HasOpportunity,
            "Six lights terminated a surviving opportunity.");
        RavenSessionHooks.Expire(state, "离开房间");
        Check(!state.HasOpportunity && state.BonusCandidates.Count == 0 && !state.IsActive,
            "Room exit retained opportunities or reward choices.");
        RavenSessionHooks.Enqueue(state, 1, "room-b");
        RavenSessionHooks.Enqueue(state, 4, "room-c");
        Check(state.PendingLights.SequenceEqual([4]) && state.RoomKey == "room-c",
            "An opportunity was banked across rooms.");
        Reject(() => RavenSessionHooks.Enqueue(state, 6, "room-c"), "Six-light initial opportunity accepted.");
    }
}
