using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using BeatInsight.Integration.Game;
using BeatInsight.Integration.SongSelect;
using BeatInsight.Services;

namespace BeatInsight.Tests.Integration.SongSelect;

/// <summary>V3.14-COM-PP-03 : service SS de sélection (calcul en arrière-plan, cache, debounce, annulation, péremption).</summary>
public sealed class SongSelectPerformanceServiceTests : IDisposable
{
    private const string Process = "4242:2026-10-15T16:00:00.0000000Z";
    private readonly string directory = Path.Combine(Path.GetTempPath(), "bi-pp03-" + Guid.NewGuid().ToString("N"));
    private readonly ConcurrentQueue<string> computed = new();

    public SongSelectPerformanceServiceTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
    }

    private string Map(string name, string content = "osu file format v14")
    {
        string path = Path.Combine(directory, name + ".osu");
        if (!File.Exists(path)) File.WriteAllText(path, content);
        return path;
    }

    private static SongSelectSelection Selection(string path, string mods = "NM") =>
        new(Process, path, mods == "NM" ? [] : mods.Split('+'), mods);

    /// <summary>Calcul simulé : SS déterministe selon le fichier et les mods, durée réglable.</summary>
    private SongSelectPerformanceService.Compute Fake(int delayMs = 0, bool honourCancellation = true, string? fail = null) =>
        (key, mods, token) =>
        {
            computed.Enqueue($"{Path.GetFileNameWithoutExtension(key.BeatmapPath)}|{key.ModsKey}");
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < delayMs)
            {
                if (honourCancellation) token.ThrowIfCancellationRequested();
                Thread.Sleep(2);
            }
            if (fail is not null && key.BeatmapPath.Contains(fail, StringComparison.Ordinal))
                return new(null, LivePerformanceFailureReason.UnsupportedMods);
            double ss = 100 + Path.GetFileNameWithoutExtension(key.BeatmapPath).Length + (key.ModsKey == "NM" ? 0 : 50);
            return new(new LiveStaticPerformance(ss, ss * 0.7, key.ModsKey), LivePerformanceFailureReason.None);
        };

    private static SongSelectPerformanceState WaitFor(
        SongSelectPerformanceService service, SongSelectSelection selection, Func<SongSelectPerformanceState, bool> until, int timeoutMs = 5000)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            SongSelectPerformanceState state = service.Update(selection);
            if (until(state)) return state;
            if (watch.ElapsedMilliseconds > timeoutMs) throw new TimeoutException($"état {state.Status} ({state.Reason})");
            Thread.Sleep(5);
        }
    }

    [Fact]
    public void Pending_then_ready_and_never_computed_on_the_calling_thread()
    {
        using var service = new SongSelectPerformanceService(Fake(delayMs: 50), "v1", TimeSpan.FromMilliseconds(10));
        var selection = Selection(Map("alpha"), "DT+HD");
        var watch = Stopwatch.StartNew();
        SongSelectPerformanceState first = service.Update(selection);
        Assert.True(watch.ElapsedMilliseconds < 40); // Update ne calcule jamais
        Assert.Equal(SongSelectPerformanceStatus.Pending, first.Status);
        Assert.Null(first.Value);

        SongSelectPerformanceState ready = WaitFor(service, selection, s => s.Status == SongSelectPerformanceStatus.Ready);
        Assert.Equal(155, ready.Value!.SsPp);
        Assert.Equal(first.Generation, ready.Generation);
        Assert.Equal("DT+HD", ready.Key!.ModsKey);
        Assert.Equal("v1", ready.Key.CalculatorVersion);
    }

    [Fact]
    public void Repeated_ticks_and_reselection_use_the_cache()
    {
        using var service = new SongSelectPerformanceService(Fake(), "v1", TimeSpan.FromMilliseconds(5));
        var a = Selection(Map("alpha"));
        var b = Selection(Map("beta"));
        WaitFor(service, a, s => s.Status == SongSelectPerformanceStatus.Ready);
        for (int i = 0; i < 20; i++) service.Update(a);
        WaitFor(service, b, s => s.Status == SongSelectPerformanceStatus.Ready);
        SongSelectPerformanceState again = service.Update(a);

        Assert.Equal(SongSelectPerformanceStatus.Ready, again.Status); // immédiat, sans nouveau calcul
        Assert.Equal(2, computed.Count);
        Assert.True(service.Statistics.CacheHits >= 1);
    }

    [Fact]
    public void Fast_selection_changes_are_debounced_to_the_last_one()
    {
        using var service = new SongSelectPerformanceService(Fake(), "v1", TimeSpan.FromMilliseconds(150));
        string[] maps = Enumerable.Range(0, 8).Select(i => Map("fast" + i)).ToArray();
        foreach (string map in maps)
        {
            service.Update(Selection(map));
            Thread.Sleep(10);
        }
        var last = Selection(maps[^1]);
        SongSelectPerformanceState ready = WaitFor(service, last, s => s.Status == SongSelectPerformanceStatus.Ready);

        Assert.Equal(new[] { "fast7|NM" }, computed.ToArray());
        Assert.Equal(maps[^1], ready.Key!.BeatmapPath);
    }

    [Fact]
    public void Obsolete_calculation_is_cancelled_cooperatively()
    {
        using var service = new SongSelectPerformanceService(Fake(delayMs: 2000), "v1", TimeSpan.Zero);
        var slow = Selection(Map("slow"));
        service.Update(slow);
        SpinWait.SpinUntil(() => !computed.IsEmpty, 2000);
        var watch = Stopwatch.StartNew();
        var next = Selection(Map("next"), "HR");
        service.Update(next);
        SpinWait.SpinUntil(() => service.Statistics.Cancelled == 1, 2000);

        Assert.True(watch.ElapsedMilliseconds < 1000);
        Assert.Equal(1, service.Statistics.Cancelled);
    }

    [Fact]
    public void Late_result_of_a_previous_selection_is_never_presented()
    {
        // Calcul qui ignore l'annulation : son résultat arrive après le changement de sélection.
        using var service = new SongSelectPerformanceService(Fake(delayMs: 300, honourCancellation: false), "v1", TimeSpan.Zero);
        var old = Selection(Map("old"));
        long oldGeneration = service.Update(old).Generation;
        SpinWait.SpinUntil(() => !computed.IsEmpty, 2000);
        var current = Selection(Map("current"), "DT");
        SongSelectPerformanceState state = service.Update(current);
        Assert.True(state.Generation > oldGeneration);

        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 2000)
        {
            state = service.Update(current);
            if (state.Status == SongSelectPerformanceStatus.Ready) break;
            Assert.Null(state.Value); // jamais la valeur de « old » pour « current »
            Thread.Sleep(5);
        }
        Assert.Equal(SongSelectPerformanceStatus.Ready, state.Status);
        Assert.Equal("DT", state.Key!.ModsKey);
        Assert.EndsWith("current.osu", state.Key.BeatmapPath);
        Assert.Equal(1, service.Statistics.StaleRejected);
    }

    [Fact]
    public void Cache_is_bounded_lru()
    {
        using var service = new SongSelectPerformanceService(Fake(), "v1", TimeSpan.Zero, capacity: 2);
        var a = Selection(Map("a"));
        var b = Selection(Map("b"));
        var c = Selection(Map("c"));
        WaitFor(service, a, s => s.Status == SongSelectPerformanceStatus.Ready);
        WaitFor(service, b, s => s.Status == SongSelectPerformanceStatus.Ready);
        WaitFor(service, a, s => s.Status == SongSelectPerformanceStatus.Ready); // a redevient récent
        WaitFor(service, c, s => s.Status == SongSelectPerformanceStatus.Ready); // évince b
        Assert.Equal(2, service.Statistics.CacheEntries);
        Assert.Equal(SongSelectPerformanceStatus.Ready, service.Update(a).Status);
        Assert.Equal(SongSelectPerformanceStatus.Pending, service.Update(b).Status);
        WaitFor(service, b, s => s.Status == SongSelectPerformanceStatus.Ready);
        Assert.Equal(4, computed.Count);
    }

    [Fact]
    public void Failure_is_cached_and_not_recomputed_every_tick()
    {
        using var service = new SongSelectPerformanceService(Fake(fail: "mania"), "v1", TimeSpan.Zero);
        var selection = Selection(Map("mania"));
        SongSelectPerformanceState failed = WaitFor(service, selection, s => s.Status == SongSelectPerformanceStatus.Failed);
        for (int i = 0; i < 20; i++) service.Update(selection);
        Thread.Sleep(50);
        Assert.Null(failed.Value);
        Assert.Single(computed);
    }

    [Fact]
    public void Key_includes_file_identity_mods_process_and_calculator_version()
    {
        using var service = new SongSelectPerformanceService(Fake(), "v1", TimeSpan.Zero);
        string path = Map("edited");
        SongSelectPerformanceKey before = WaitFor(service, Selection(path), s => s.Status == SongSelectPerformanceStatus.Ready).Key!;
        File.AppendAllText(path, "\n// édité");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        SongSelectPerformanceKey after = WaitFor(service, Selection(path), s => s.Status == SongSelectPerformanceStatus.Ready).Key!;

        Assert.NotEqual(before, after);
        Assert.NotEqual(before.FileLength, after.FileLength);
        Assert.Equal(2, computed.Count);
        Assert.NotEqual(before, before with { ModsKey = "HD" });
        Assert.NotEqual(before, before with { ProcessIdentity = "other" });
        Assert.NotEqual(before, before with { CalculatorVersion = "v2" });
    }

    [Fact]
    public void No_selection_or_missing_file_is_unknown_and_advances_the_generation()
    {
        using var service = new SongSelectPerformanceService(Fake(), "v1", TimeSpan.Zero);
        SongSelectPerformanceState a = WaitFor(service, Selection(Map("a")), s => s.Status == SongSelectPerformanceStatus.Ready);
        SongSelectPerformanceState none = service.Update(null);
        Assert.Equal(SongSelectPerformanceStatus.Unknown, none.Status);
        Assert.True(none.Generation > a.Generation);
        SongSelectPerformanceState missing = service.Update(Selection(Path.Combine(directory, "missing.osu")));
        Assert.Equal(SongSelectPerformanceStatus.Unknown, missing.Status);
        Assert.Equal("beatmap absente", missing.Reason);
    }

    [Fact]
    public void Dispose_stops_the_worker_and_cancels_the_running_calculation()
    {
        var service = new SongSelectPerformanceService(Fake(delayMs: 5000), "v1", TimeSpan.Zero);
        service.Update(Selection(Map("long")));
        SpinWait.SpinUntil(() => !computed.IsEmpty, 2000);
        var watch = Stopwatch.StartNew();
        service.Dispose();
        Assert.True(watch.ElapsedMilliseconds < 2000);
        Assert.Equal(SongSelectPerformanceStatus.Unknown, service.Update(Selection(Map("long"))).Status);
    }

    [Fact]
    public void Real_calculator_is_used_by_default_and_matches_gameplay()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Maps", "Tower Of Heaven [Extra].osu");
        using var service = new SongSelectPerformanceService(debounce: TimeSpan.Zero);
        var selection = new SongSelectSelection(Process, path, ["HD", "DT"], "DT+HD");
        SongSelectPerformanceState ready = WaitFor(service, selection, s => s.Status == SongSelectPerformanceStatus.Ready, 30_000);

        var mods = System.Collections.Immutable.ImmutableArray.Create(new LiveGameplayMod("HD"), new LiveGameplayMod("DT"));
        LivePerformanceSnapshot gameplay = new OsuLivePerformanceCalculator()
            .Calculate(new LivePerformanceInput(path, mods, 1, 1, 1, 0, 0, 0, 1, TimeSpan.FromDays(1))).Snapshot!;
        Assert.Equal(gameplay.SsPp, ready.Value!.SsPp);
        Assert.Equal(gameplay.Accuracy95FcPp, ready.Value.Accuracy95FcPp);
        Assert.Equal(OsuLivePerformanceCalculator.StaticPerformanceVersion, ready.Key!.CalculatorVersion);
    }
}
