using Godot;

namespace SurpriseRaven.Hooks.Raven;

public static partial class RavenHeadlessTestsHooks
{
    // Proves the deployed PCK carries every staged asset intact and decodable by the engine.
    private static void TestFrontendAssets()
    {
        Check(RavenAssetsHooks.Available, "Raven asset manifest missing from the mounted PCK.");
        foreach (var entry in RavenAssetsHooks.Manifest)
        {
            Check(RavenAssetsHooks.Verify(entry), $"Asset {entry.Id} hash/size differs from staging.");
            if (entry.Kind == "wav")
            {
                var stream = RavenAssetsHooks.Audio(entry.Id) as AudioStreamWav;
                Check(stream is not null && Math.Abs(stream.GetLength() - entry.DurationSeconds) < 0.05,
                    $"Audio {entry.Id} failed to decode with its staged duration.");
                continue;
            }
            if (entry.Kind == "mp3")
            {
                Check(RavenAssetsHooks.Audio(entry.Id) is AudioStreamMP3 stream && stream.GetLength() > 0,
                    $"Audio {entry.Id} failed to decode as MP3.");
                continue;
            }
            var texture = RavenAssetsHooks.Texture(entry.Id);
            Check(texture is not null && texture.GetWidth() == entry.Width && texture.GetHeight() == entry.Height,
                $"Texture {entry.Id} failed to decode at {entry.Width}x{entry.Height}.");
            if (entry.Grid is { Length: 2 } grid)
                Check(entry.Width % grid[0] == 0 && entry.Height % grid[1] == 0, $"Flipbook {entry.Id} grid mismatch.");
        }
        foreach (var id in new[] { "tex.flame", "tex.smoke", "tex.paper_gold", "tex.paper_color", "tex.glow", "tex.sky" })
            Check(RavenAssetsHooks.Find(id) is not null, $"Required texture {id} missing.");
        var errors = RavenFrontendConfigHooks.Current.Validate(id => RavenAssetsHooks.Find(id) is { Kind: "wav" or "mp3" });
        Check(errors.Count == 0, "Frontend config invalid: " + string.Join(", ", errors));
        var lead = RavenAssetsHooks.Find("sfx.swap_reward");
        Check(lead is { Kind: "wav", Path: "audio/sfx/swap_reward.wav", Size: 128816 }
            && lead.Sha256.Equals("ca3933d7a396921945ea2ca4a7e1272d09878f8be2049d2f8e838c7fe4d48f06", StringComparison.OrdinalIgnoreCase),
            "The mounted swap lead must match the clipped and faded reference WAV.");
        Check(RavenAssetsHooks.Audio("sfx.swap_reward") is AudioStreamWav { MixRate: 44100, Stereo: true } wav
            && Math.Round(wav.GetLength() * wav.MixRate) == 32193, "The 0.73-second swap lead decodes into 32,193 stereo frames.");
        Check(RavenAssetsHooks.Find("sfx.swap")?.Sha256 == "3b79d253419194c71a09b227e594e608db09622ba9ae2147884fbfcae3e15042",
            "The three caws retain their spacing and start at the trimmed onset.");
    }

    private static void TestTimeline()
    {
        var timings = RavenFrontendConfigHooks.Current.Timings;
        RavenCue[] returning = [RavenCue.Offer, RavenCue.Takeoff, RavenCue.Grab, RavenCue.Gone,
            RavenCue.ReturnStart, RavenCue.Drop, RavenCue.Reveal, RavenCue.Landed, RavenCue.Finish];
        RavenCue[] swallow = [RavenCue.Offer, RavenCue.Takeoff, RavenCue.Grab, RavenCue.Extinguish,
            RavenCue.SwallowVoice, RavenCue.Gone, RavenCue.Finish];
        foreach (bool eats in new[] { false, true })
        foreach (bool gains in new[] { false, true })
        {
            var schedule = RavenTimeline.Schedule(timings, eats, gains);
            var cues = schedule.Select(c => c.Cue).ToArray();
            var expected = eats ? swallow : returning;
            Check(cues.Where(c => c != RavenCue.Ignite).SequenceEqual(expected), "Round cues differ from the presentation order.");
            Check(cues.Count(c => c == RavenCue.Ignite) == (gains && !eats ? 1 : 0), "Exactly one surviving light gain can ignite.");
            if (!eats)
            {
                var swap = new RavenTimeline(timings, false, gains, true);
                Check(Math.Abs(swap.TimeOf(RavenCue.Drop) - swap.TimeOf(RavenCue.SwapSound) - 0.20) < 1e-6 &&
                    swap.Advance(30).Count(c => c == RavenCue.SwapSound) == 1 && swap.Advance(30).Count == 0,
                    "Swap audio starts 200 ms before the drop and also fires exactly once across a long frame.");
                double At(RavenCue cue) => schedule.Single(c => c.Cue == cue).At;
                double gone = At(RavenCue.Gone), back = At(RavenCue.ReturnStart);
                Check(Math.Abs(back - gone - (gains ? 0.8 : 0.0)) < 1e-6, "Only a gained light holds the raven offscreen for 0.8 seconds.");
                if (gains)
                    Check(At(RavenCue.Ignite) >= gone && At(RavenCue.Ignite) < back && Array.IndexOf(cues, RavenCue.Gone) < Array.IndexOf(cues, RavenCue.Ignite),
                        "Ignition follows disappearance and precedes the return.");
                Check(Math.Abs(At(RavenCue.Drop) - back - timings.DropAt) < 1e-6
                    && Math.Abs(At(RavenCue.Reveal) - At(RavenCue.Drop) - timings.DropFall) < 1e-6, "Relative drop and reveal times follow the return.");
            }
            var stepped = new RavenTimeline(timings, eats, gains);
            List<RavenCue> fine = [];
            for (int i = 0; i < 60 * 30 && !stepped.Finished; i++) fine.AddRange(stepped.Advance(1 / 60.0));
            var jumped = new RavenTimeline(timings, eats, gains);
            Check(jumped.Advance(0).SequenceEqual([RavenCue.Offer]), "Offer is immediate.");
            Check(jumped.Advance(30).SequenceEqual(fine.Skip(1)) && fine.SequenceEqual(cues)
                && stepped.Finished && jumped.Finished && jumped.Advance(30).Count == 0, "Coarse and fine steps deliver each cue once in the same order.");
        }
    }

    private static void TestAudioEvents()
    {
        var config = RavenFrontendConfigHooks.Current;
        Check(config.Events["candle.light"].GainDb == 6 && config.Events.Where(e => e.Key != "candle.light").All(e => e.Value.GainDb == 0),
            "The six ignition variants receive the same 6 dB boost.");
        Check(config.Events.Keys.Order().SequenceEqual(RavenFrontendConfig.RequiredEvents.Order()),
            "Audio events differ from the approved set.");
        string[] entries = ["voice.entry.11", "voice.entry.15", "voice.entry.16"];
        Check(RavenAudioHooks.PickClip("reward.swap") == "sfx.swap_reward" && RavenAudioHooks.PickClip("result.swap") == "sfx.swap",
            "Swap uses the new reward WAV followed by the original three-caw recording.");
        Check(RavenAudioHooks.PickClip("entry", new AudioRandom(0.5, 0, 0)) is null, "Entry at 0.5 must be silent.");
        for (int i = 0; i < entries.Length; i++)
            Check(RavenAudioHooks.PickClip("entry", new AudioRandom(0.499999, i, 3)) == entries[i],
                $"Entry clip {entries[i]} cannot be selected.");

        foreach (var (eventName, prefix) in new[] { ("candle.light", "sfx.candle_light"), ("candle.out", "sfx.candle_out") })
        {
            for (int i = 0; i < 6; i++)
            {
                var random = new AudioRandom(double.NaN, i, 6);
                string wanted = $"{prefix}.{i + 1:00}";
                Check(RavenAudioHooks.PickClip(eventName, random) == wanted, $"{wanted} cannot be selected.");
                Check(RavenAudioHooks.PickClip(eventName, random) == wanted, $"{wanted} cannot repeat immediately.");
            }
        }
    }

    private sealed class AudioRandom(double roll, int clip, int count) : Random
    {
        public override double NextDouble()
        {
            Check(!double.IsNaN(roll), "Guaranteed clips must skip the chance roll.");
            return roll;
        }

        public override int Next(int maxValue)
        {
            Check(maxValue == count, $"Clip draw offered {maxValue} variants instead of {count}.");
            return clip;
        }
    }
}
