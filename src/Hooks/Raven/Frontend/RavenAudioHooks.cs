using Godot;

namespace SurpriseRaven.Hooks.Raven;

// One persistent node so clips survive the panel stepping aside. Output follows the
// game's Godot "SFX" bus, which NGame and the SFX/master sliders keep in sync with settings.
internal static class RavenAudioHooks
{
    private static Node? _host;
    private static StringName _bus = "Master";
    private static readonly List<AudioStreamPlayer> Players = [];
    private static readonly Random Rng = new();

    internal static string? PickClip(string eventName, Random? random = null)
    {
        if (!RavenFrontendConfigHooks.Current.Events.TryGetValue(eventName, out var evt) || evt.Clips.Count == 0) return null;
        random ??= Rng;
        if (evt.Chance < 1.0 && random.NextDouble() >= evt.Chance) return null;
        return evt.Clips[random.Next(evt.Clips.Count)];
    }

    internal static void Play(string eventName)
    {
        if (DisplayServer.GetName() == "headless")
        {
            Trace(eventName, "skipped headless");
            return;
        }
        if (eventName == "reward.swap") PlaySwapReward(TryPlay);
        else TryPlay(eventName, null);
    }

    internal static void PlaySwapReward(Func<string, Action?, bool> tryPlay)
    {
        void PlayCaws() => tryPlay("result.swap", null);
        if (!tryPlay("reward.swap", PlayCaws)) PlayCaws();
    }

    internal static Error OnFinishedOnce(AudioStreamPlayer player, Action finished) =>
        player.Connect(AudioStreamPlayer.SignalName.Finished, Callable.From(finished), (uint)GodotObject.ConnectFlags.OneShot);

    private static bool TryPlay(string eventName, Action? finished)
    {
        if (PickClip(eventName) is not { } clip)
        {
            Trace(eventName, "skipped no clip selected");
            return false;
        }
        Trace(eventName, $"requested clip={clip}");
        if (RavenAssetsHooks.Audio(clip) is not { } stream)
        {
            Trace(eventName, $"failed clip={clip} stream unavailable");
            return false;
        }
        if (!EnsureHost())
        {
            Trace(eventName, $"failed clip={clip} scene unavailable");
            return false;
        }
        // The swap lead owns its player until Finished, even if the next round already started.
        var player = finished is null ? GetAvailablePlayer() : CreatePlayer("SwapReward");
        if (finished is not null && OnFinishedOnce(player, () =>
            {
                player.QueueFree();
                finished();
            }) != Error.Ok)
        {
            Trace(eventName, $"failed clip={clip} could not connect Finished");
            player.QueueFree();
            return false;
        }
        var config = RavenFrontendConfigHooks.Current;
        player.Stream = stream;
        player.VolumeDb = Mathf.LinearToDb(config.Volume * config.Events[eventName].Volume);
        bool Start()
        {
            if (GodotObject.IsInstanceValid(player) && player.IsInsideTree())
            {
                player.Play();
                if (player.Playing)
                {
                    Trace(eventName, $"started clip={clip} bus={player.Bus} gainDb={player.VolumeDb:F1}");
                    return true;
                }
            }
            Trace(eventName, $"failed clip={clip} player did not start");
            if (finished is not null && GodotObject.IsInstanceValid(player)) player.QueueFree();
            return false;
        }
        if (player.IsInsideTree()) return Start();
        Callable.From(() =>
        {
            if (!Start()) finished?.Invoke();
        }).CallDeferred();
        return true;
    }

    private static void Trace(string eventName, string message)
    {
        if (eventName is "candle.light" or "reward.swap" or "result.swap")
            Logger.Log($"[Raven] Audio event={eventName} {message}");
    }

    private static bool EnsureHost()
    {
        if (_host is not null && GodotObject.IsInstanceValid(_host)) return true;
        if (Engine.GetMainLoop() is not SceneTree tree) return false;
        _host = new Node { Name = "RavenLampAudio" };
        _bus = AudioServer.GetBusIndex("SFX") >= 0 ? "SFX" : "Master";
        Players.Clear();
        tree.Root.CallDeferred(Node.MethodName.AddChild, _host);
        return true;
    }

    private static AudioStreamPlayer GetAvailablePlayer()
    {
        // A deferred first play also reserves its player until the host enters the tree.
        if (_host!.IsInsideTree() && Players.FirstOrDefault(player => !player.Playing) is { } available)
            return available;
        var player = CreatePlayer($"Sound{Players.Count}");
        Players.Add(player);
        return player;
    }

    private static AudioStreamPlayer CreatePlayer(string name)
    {
        var player = new AudioStreamPlayer { Name = name, Bus = _bus };
        _host!.AddChild(player);
        return player;
    }
}
