using System.Diagnostics;
using System.IO;
using BeatInsight.Integration.CommunityOverlays;
using BeatInsight.Integration.CommunityOverlays.Pipe;
using BeatInsight.Integration.Game;
using BeatInsight.Integration.SongSelect;
using BeatInsight.Services;

namespace BeatInsight.Tests.Integration.CommunityOverlays;

/// <summary>V3.14-COM-PP-03 : publication v3 « selection » (résolution de la sélection, contrat, opt-in).</summary>
public sealed class CommunityOverlaySelectionPublisherTests
{
    private const string Process = "4242:2026-10-15T16:00:00.0000000Z";
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 16, 0, 0, TimeSpan.Zero);
    private static readonly string MapPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps", "Tower Of Heaven [Extra].osu");

    private static SongSelectModsSnapshot Mods(SongSelectModsValidity validity = SongSelectModsValidity.Valid, string process = Process,
        int mask = 72) =>
        new(process, validity == SongSelectModsValidity.NotSongSelect ? 2 : 5, mask, StableModsMask.Normalize(mask),
            validity == SongSelectModsValidity.Valid ? StableModsMask.Key(StableModsMask.Normalize(mask)) : null,
            validity, Now, SongSelectModsFailure.None);

    private static GameStateSnapshot Game(string? path = null, string process = Process) =>
        new("NativeStableGameProvider", "osu!stable", 4242, process, path ?? MapPath, "123", "456", Now,
            GameStateAvailability.Available, null);

    [Fact]
    public void Resolve_requires_valid_mods_same_process_and_a_known_beatmap()
    {
        var (selection, validity, _) = CommunityOverlaySelectionPublisher.Resolve(Mods(), Game());
        Assert.Equal(SelectionValidity.Valid, validity);
        Assert.Equal("DT+HD", selection!.ModsKey);

        Assert.Equal(SelectionValidity.NotSongSelect, CommunityOverlaySelectionPublisher.Resolve(Mods(SongSelectModsValidity.NotSongSelect), Game()).Validity);
        Assert.Equal(SelectionValidity.Unknown, CommunityOverlaySelectionPublisher.Resolve(Mods(SongSelectModsValidity.UnknownBits), Game()).Validity);
        Assert.Equal(SelectionValidity.Unknown, CommunityOverlaySelectionPublisher.Resolve(Mods(SongSelectModsValidity.Unknown), Game()).Validity);
        Assert.Equal("processus différent", CommunityOverlaySelectionPublisher.Resolve(Mods(process: "other"), Game()).Reason);
        Assert.Null(CommunityOverlaySelectionPublisher.Resolve(Mods(), new GameStateSnapshot("NativeStableGameProvider", "osu!stable", 4242, Process, null, null, null, Now, GameStateAvailability.Available, null)).Selection);
    }

    [Fact]
    public void Publishes_pending_then_valid_with_the_gameplay_ss_and_a_strict_message()
    {
        var messages = new List<byte[]>();
        using var service = new SongSelectPerformanceService(debounce: TimeSpan.Zero);
        using var publisher = new CommunityOverlaySelectionPublisher(() => Mods(), () => Game(), service, messages.Add);

        publisher.Tick();
        SelectionPipeEnvelope first = CommunityOverlaySelectionPipeProtocol.TryDeserialize(messages[0], out _)!;
        Assert.Equal(SelectionValidity.Pending, first.Validity);
        Assert.Null(first.Performance);

        var watch = Stopwatch.StartNew();
        SelectionPipeEnvelope? last = null;
        while (watch.ElapsedMilliseconds < 30_000)
        {
            publisher.Tick();
            last = CommunityOverlaySelectionPipeProtocol.TryDeserialize(messages[^1], out string? rejection);
            Assert.Null(rejection);
            if (last!.Validity == SelectionValidity.Valid) break;
            Thread.Sleep(10);
        }
        Assert.Equal(SelectionValidity.Valid, last!.Validity);
        Assert.Equal(first.SelectionGeneration, last.SelectionGeneration);
        Assert.Equal("DT+HD", last.ModsKey);
        Assert.Equal(Path.GetFullPath(MapPath), last.Beatmap!.Path);
        Assert.True(last.Sequence > first.Sequence);

        var mods = System.Collections.Immutable.ImmutableArray.Create(new LiveGameplayMod("HD"), new LiveGameplayMod("DT"));
        LivePerformanceSnapshot gameplay = new OsuLivePerformanceCalculator()
            .Calculate(new LivePerformanceInput(MapPath, mods, 1, 1, 1, 0, 0, 0, 1, TimeSpan.FromDays(1))).Snapshot!;
        Assert.Equal(gameplay.SsPp, last.Performance!.SsPp);
        Assert.Equal(gameplay.Accuracy95FcPp, last.Performance.Accuracy95Pp);
    }

    [Fact]
    public void Selection_change_invalidates_immediately_with_a_new_generation()
    {
        var messages = new List<byte[]>();
        int mask = 72;
        using var service = new SongSelectPerformanceService((_, _, _) => new(new LiveStaticPerformance(400, 300, "x"), LivePerformanceFailureReason.None),
            "v1", TimeSpan.Zero);
        using var publisher = new CommunityOverlaySelectionPublisher(() => Mods(mask: mask), () => Game(), service, messages.Add);
        SpinWait.SpinUntil(() =>
        {
            publisher.Tick();
            return publisher.LastEnvelope?.Validity == SelectionValidity.Valid;
        }, 5000);
        SelectionPipeEnvelope valid = publisher.LastEnvelope!;

        mask = 16; // HR
        publisher.Tick();
        SelectionPipeEnvelope changed = publisher.LastEnvelope!;
        Assert.True(changed.SelectionGeneration > valid.SelectionGeneration);
        Assert.Equal("HR", changed.ModsKey);
        Assert.NotEqual(SelectionValidity.Valid, changed.Validity); // jamais l'ancien SS pour les nouveaux mods
        Assert.Null(changed.Performance);
    }

    [Fact]
    public void Not_in_song_select_publishes_no_performance()
    {
        var messages = new List<byte[]>();
        using var service = new SongSelectPerformanceService((_, _, _) => new(new LiveStaticPerformance(400, 300, "x"), LivePerformanceFailureReason.None),
            "v1", TimeSpan.Zero);
        using var publisher = new CommunityOverlaySelectionPublisher(() => Mods(SongSelectModsValidity.NotSongSelect), () => Game(), service, messages.Add);
        publisher.Tick();
        SelectionPipeEnvelope e = CommunityOverlaySelectionPipeProtocol.TryDeserialize(messages[0], out _)!;
        Assert.Equal(SelectionValidity.NotSongSelect, e.Validity);
        Assert.Null(e.Performance);
        Assert.Null(e.Beatmap);
    }

    [Fact]
    public void Feature_is_opt_in()
    {
        Assert.False(MainWindow.IsCommunityOverlaysSelectionEnabled([], null));
        Assert.True(MainWindow.IsCommunityOverlaysSelectionEnabled(["--community-overlays-selection"], null));
        Assert.True(MainWindow.IsCommunityOverlaysSelectionEnabled([], "1"));
        Assert.False(MainWindow.IsCommunityOverlaysSelectionEnabled([], "true"));
    }

    [Fact]
    public void Selection_pipe_does_not_replace_existing_pipes()
    {
        Assert.Equal("BeatInsight.CommunityOverlays.selection.v3", CommunityOverlaySelectionPipeProtocol.PipeName);
        Assert.NotEqual(CommunityOverlayPipeProtocol.PipeName, CommunityOverlaySelectionPipeProtocol.PipeName);
        Assert.NotEqual(CommunityOverlayPipeProtocol.LegacyPipeNameV1, CommunityOverlaySelectionPipeProtocol.PipeName);
        Assert.NotEqual(CommunityOverlayPrecisePipeProtocol.PipeName, CommunityOverlaySelectionPipeProtocol.PipeName);
        Assert.Equal(2, CommunityOverlayPipeProtocol.Version);
    }
}
