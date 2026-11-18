using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace SurpriseRaven.Hooks.Raven;

internal static partial class RavenNativeAncientOptionsHooks
{
    private static Player? FixturePlayer;
    private static ISet<ModelId>? FixtureObtained;
    private static bool FixtureExhausted;
    private static bool FixtureApplies(Player player) => ReferenceEquals(FixturePlayer, player);

    // Permits testing the actual patched generator/wrapper with detached models, without BeginEvent.
    internal static IDisposable Fixture(Player player, ISet<ModelId> obtained, bool exhausted = false)
    {
        if (DisplayServer.GetName() != "headless" || System.Environment.GetEnvironmentVariable("STS2_SURPRISE_RAVEN_HEADLESS_TEST") != "1")
            throw new InvalidOperationException("Native Ancient fixture requires isolated headless testing.");
        if (FixturePlayer is not null) throw new InvalidOperationException("Nested native Ancient fixture.");
        FixturePlayer = player; FixtureObtained = obtained; FixtureExhausted = exhausted;
        return new FixtureScope();
    }

    private sealed class FixtureScope : IDisposable
    {
        public void Dispose() { FixturePlayer = null; FixtureObtained = null; FixtureExhausted = false; }
    }
}
