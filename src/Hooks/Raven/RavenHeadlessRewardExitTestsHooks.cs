using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace SurpriseRaven.Hooks.Raven;

internal static partial class RavenOverlayHooks
{
    internal static async Task<int> RunRewardExitChecksAsync()
    {
        Require(DisplayServer.GetName() == "headless"
            && System.Environment.GetEnvironmentVariable("STS2_SURPRISE_RAVEN_HEADLESS_TEST") == "1"
            && !RunManager.Instance.IsInProgress, "Reward exit fixture requires isolated headless with no run.");

        var player = Player.CreateForNewRun<Ironclad>(UnlockState.none, 32035UL);
        CreateRewardFixtureRun(player);
        var runState = DispatchProxy.Create<IRunState, RewardExitRunStateFixture>();
        var hooks = (RewardExitRunStateFixture)(object)runState;
        int checks = 0;
        void Check(bool condition, string message) { Require(condition, message); checks++; }
        using var temporary = new RewardExitScreenFixture(runState, terminal: false);
        using var terminal = new RewardExitScreenFixture(runState, terminal: true);

        var state = RavenStorageHooks.Get(player);
        RavenSessionHooks.Enqueue(state, 2, "reward exit fixture");
        var pending = new RavenPendingRound
        {
            Outcome = RavenOutcome.Upgrade,
            SettlementVersion = RavenTransactionHooks.CurrentVersion,
            StakeConsumed = true,
            NextLights = 3,
            StakeSnapshot = ModelDb.Relic<Strawberry>().ToMutable().ToSerializable(),
            Candidates = [ModelDb.Relic<GlassEye>().ToMutable().ToSerializable()]
        };
        state.Pending = pending;
        var dismissal = new TaskCompletionSource();
        int awards = 0;
        RavenClaimResult? result = null;
        Task Obtain(int _) { awards++; return dismissal.Task; }
        Task<RavenClaimResult> Collect() =>
            RavenTransactionHooks.CollectAsync(state, pending, 0, _ => true, Obtain, static () => { });
        var settlement = RavenServiceHooks.RunRewardExitSettlementFixtureAsync(player, state,
            async () => { result = await Collect(); });
        try
        {
            Check(RavenServiceHooks.Busy && state.InFlight && !settlement.IsCompleted && awards == 1,
                "The mod's acquisition operation waits on the native reward completion once.");
            temporary.CheckProceed();
            Check(temporary.Enabled, "The native temporary reward leave button enables during acquisition.");
            terminal.CheckProceed();
            Check(!terminal.Enabled && !Hook.ShouldProceedToNextMapPoint(runState),
                "Terminal rewards and normal movement retain the Busy restriction.");

            temporary.DisallowSkipping = true;
            temporary.CheckProceed();
            Check(!temporary.Enabled, "Native DisallowSkipping still prevents leaving.");
            temporary.DisallowSkipping = false;
            temporary.DisableForever = true;
            temporary.CheckProceed();
            Check(!temporary.Enabled, "A permanently disabled native button stays disabled.");
            temporary.DisableForever = false;
            var veto = CreateRewardExitVeto();
            hooks.Listeners = () => [veto];
            temporary.CheckProceed();
            Check(!temporary.Enabled, "A native model's veto is preserved.");

            hooks.Listeners = () =>
            {
                hooks.Listeners = static () => [];
                terminal.CheckProceed();
                Check(!terminal.Enabled, "A nested terminal check does not inherit the temporary exemption.");
                return [];
            };
            temporary.CheckProceed();
            Check(temporary.Enabled && !Hook.ShouldProceedToNextMapPoint(runState),
                "The outer temporary check resumes, then restores the ordinary movement restriction.");
            var expected = new InvalidOperationException("Reward listener exception fixture.");
            hooks.Listeners = () => throw expected;
            Exception? actual = null;
            try { temporary.CheckProceed(); }
            catch (TargetInvocationException error) { actual = error.InnerException; }
            hooks.Listeners = static () => [];
            Check(ReferenceEquals(actual, expected) && !Hook.ShouldProceedToNextMapPoint(runState),
                "A failed native check propagates its exception and restores the movement restriction.");
            Check(!settlement.IsCompleted && await Collect() == RavenClaimResult.Rejected && awards == 1,
                "Repeated pickup cannot reenter while the temporary reward is waiting.");
            temporary.CheckProceed();
            Check(temporary.Enabled, "The temporary reward leave button stays usable after a failed check.");
        }
        finally
        {
            // Model the native completion signal without pushing a live overlay or skipping live rewards.
            dismissal.TrySetResult();
            await settlement;
        }
        Check(result == RavenClaimResult.Claimed && !RavenServiceHooks.Busy && !state.InFlight
            && state.Pending is null && state.Round == 1 && awards == 1 && !state.Faulted,
            "Dismissing the native reward completes the settlement once and releases Busy.");
        Check(await Collect() == RavenClaimResult.Rejected && awards == 1
            && Hook.ShouldProceedToNextMapPoint(runState) && !RunManager.Instance.IsInProgress,
            "A settled award cannot be replayed and normal movement is enabled without opening a run.");
        // NClickableControl.Enable defers SetProcessInput until the next tree iteration.
        var tree = (SceneTree)Engine.GetMainLoop();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        return checks;
    }

    private sealed class RewardExitScreenFixture : IDisposable
    {
        private readonly NRewardsScreen _screen = new();
        private readonly RewardExitProceedFixture _button = new();
        private static readonly MethodInfo CheckProceedMethod =
            AccessTools.DeclaredMethod(typeof(NRewardsScreen), "TryEnableProceedButton", Type.EmptyTypes);

        public RewardExitScreenFixture(IRunState runState, bool terminal)
        {
            SetField("_runState", runState);
            SetField("_isTerminal", terminal);
            SetField("_proceedButton", _button);
            AccessTools.PropertySetter(typeof(NProceedButton), nameof(NProceedButton.IsSkip)).Invoke(_button, [true]);
        }

        public bool Enabled => _button.IsEnabled;
        public bool DisallowSkipping { set => SetField("_skipDisallowed", value); }
        public bool DisableForever { set => SetField("_disableProceedForever", value); }

        public void CheckProceed()
        {
            _button.Disable();
            CheckProceedMethod.Invoke(_screen, null);
        }

        private void SetField(string name, object value) => AccessTools.DeclaredField(typeof(NRewardsScreen), name)
            .SetValue(_screen, value);

        public void Dispose()
        {
            _screen.Free();
            _button.QueueFree();
        }
    }

    // Keep the real Enable/IsEnabled behavior without starting tweens.
    private sealed partial class RewardExitProceedFixture : NProceedButton
    {
        protected override void OnEnable() { }
        protected override void OnDisable() { }
    }

    private static AbstractModel CreateRewardExitVeto()
    {
        // A concrete model in the mod assembly would be discovered during normal gameplay.
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("RavenRewardExitFixture"), AssemblyBuilderAccess.RunAndCollect);
        var type = assembly.DefineDynamicModule("Main").DefineType("RewardExitVeto", TypeAttributes.Public, typeof(AbstractModel));
        foreach (var method in new[] { typeof(AbstractModel).GetProperty(nameof(AbstractModel.ShouldReceiveCombatHooks))!.GetMethod!,
            typeof(AbstractModel).GetMethod(nameof(AbstractModel.ShouldProceedToNextMapPoint))! })
        {
            var replacement = type.DefineMethod(method.Name, MethodAttributes.Public | MethodAttributes.Virtual, typeof(bool), Type.EmptyTypes);
            var il = replacement.GetILGenerator();
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            type.DefineMethodOverride(replacement, method);
        }
        return (AbstractModel)RuntimeHelpers.GetUninitializedObject(type.CreateType()!);
    }
}

internal class RewardExitRunStateFixture : DispatchProxy
{
    public Func<IEnumerable<AbstractModel>> Listeners { get; set; } = static () => [];

    protected override object Invoke(MethodInfo? targetMethod, object?[]? args) =>
        targetMethod?.Name == nameof(IRunState.IterateHookListeners)
            ? Listeners()
            : throw new NotSupportedException($"Unexpected reward fixture member: {targetMethod?.Name}");
}

public static partial class RavenServiceHooks
{
    internal static Task RunRewardExitSettlementFixtureAsync(Player player, RavenState state, Func<Task> operation)
    {
        if (DisplayServer.GetName() != "headless"
            || System.Environment.GetEnvironmentVariable("STS2_SURPRISE_RAVEN_HEADLESS_TEST") != "1"
            || RunManager.Instance.IsInProgress || Busy)
            throw new InvalidOperationException("Reward settlement fixture requires isolated headless with no run.");
        return RunOperationAsync(player, state, operation);
    }
}
