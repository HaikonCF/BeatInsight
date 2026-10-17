using System.Diagnostics;
using BeatInsight.Integration.KeyOverlay;
using Xunit;

namespace StableKeyOverlayReader.Tests;

public class KeyOverlayPollingLoopTests
{
    private static KeyOverlaySample Unknown() => new(
        KeyOverlayReadStatus.Unknown, KeyOverlayReadFailureReason.RouteNotGameplay,
        null, null, null, null, null, Stopwatch.GetTimestamp(), 0, 0);

    [Fact]
    public void Ticks_never_overlap()
    {
        int inside = 0, overlaps = 0, ticks = 0;
        using var loop = new KeyOverlayPollingLoop(() =>
        {
            if (Interlocked.Increment(ref inside) > 1) Interlocked.Increment(ref overlaps);
            Thread.SpinWait(2000);
            Interlocked.Decrement(ref inside);
            Interlocked.Increment(ref ticks);
            return Unknown();
        }, _ => { }, TimeSpan.FromMilliseconds(5));
        loop.Start();
        Thread.Sleep(300);
        Assert.True(loop.Stop(TimeSpan.FromSeconds(2)));
        Assert.Equal(0, overlaps);
        Assert.True(ticks > 10);
    }

    [Fact]
    public void Slow_tick_is_skipped_not_caught_up_in_a_burst()
    {
        var starts = new List<long>();
        int n = 0;
        using var loop = new KeyOverlayPollingLoop(() =>
        {
            lock (starts) starts.Add(Stopwatch.GetTimestamp());
            if (Interlocked.Increment(ref n) == 3) Thread.Sleep(120); // ≈ 12 périodes de retard
            return Unknown();
        }, _ => { }, TimeSpan.FromMilliseconds(10));
        loop.Start();
        Thread.Sleep(400);
        loop.Stop(TimeSpan.FromSeconds(2));

        Assert.True(loop.Stats.SkippedTicks >= 10, $"skipped={loop.Stats.SkippedTicks}");
        // Après le tick lent, aucun intervalle « en rafale » (< 2 ms) n'apparaît.
        double[] intervals;
        lock (starts)
            intervals = starts.Zip(starts.Skip(1), (a, b) => (b - a) * 1000.0 / Stopwatch.Frequency).ToArray();
        Assert.DoesNotContain(intervals.Skip(3), i => i < 2.0);
    }

    [Fact]
    public void Stop_is_immediate_even_with_a_long_period()
    {
        using var loop = new KeyOverlayPollingLoop(Unknown, _ => { }, TimeSpan.FromSeconds(10));
        loop.Start();
        Thread.Sleep(50);
        var sw = Stopwatch.StartNew();
        Assert.True(loop.Stop(TimeSpan.FromSeconds(2)));
        Assert.True(sw.ElapsedMilliseconds < 500, $"stop took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void No_read_after_dispose()
    {
        int reads = 0;
        var loop = new KeyOverlayPollingLoop(() => { Interlocked.Increment(ref reads); return Unknown(); }, _ => { },
            TimeSpan.FromMilliseconds(2));
        loop.Start();
        Thread.Sleep(50);
        loop.Dispose();
        int after = Volatile.Read(ref reads);
        Thread.Sleep(50);
        Assert.Equal(after, Volatile.Read(ref reads));
    }

    [Fact]
    public void Sink_exception_does_not_stop_the_loop()
    {
        int ticks = 0;
        using var loop = new KeyOverlayPollingLoop(() => { Interlocked.Increment(ref ticks); return Unknown(); },
            _ => throw new InvalidOperationException("sink"), TimeSpan.FromMilliseconds(5));
        loop.Start();
        Thread.Sleep(100);
        loop.Stop(TimeSpan.FromSeconds(2));
        Assert.True(ticks > 5);
        Assert.Equal(ticks, loop.Stats.SinkFailures);
    }

    [Fact]
    public void Uses_a_dedicated_background_thread()
    {
        (bool Background, bool Pool, string? Name)? seen = null;
        using var loop = new KeyOverlayPollingLoop(() =>
        {
            Thread t = Thread.CurrentThread;
            seen ??= (t.IsBackground, t.IsThreadPoolThread, t.Name);
            return Unknown();
        }, _ => { }, TimeSpan.FromMilliseconds(5));
        loop.Start();
        Thread.Sleep(50);
        loop.Stop(TimeSpan.FromSeconds(2));
        Assert.NotNull(seen);
        Assert.True(seen!.Value.Background);
        Assert.False(seen.Value.Pool);
        Assert.Equal("KeyOverlayPollingLoop", seen.Value.Name);
    }

    [Fact]
    public void Real_reader_at_100_hz_over_a_simulated_process()
    {
        var osu = new FakeOsu();
        using var reader = osu.CreateReader();
        int valid = 0;
        using var loop = new KeyOverlayPollingLoop(reader.Read, s => { if (s.IsValid) Interlocked.Increment(ref valid); },
            TimeSpan.FromMilliseconds(10));
        loop.Start();
        Thread.Sleep(500);
        loop.Stop(TimeSpan.FromSeconds(2));
        Assert.True(loop.Stats.Ticks >= 30, $"ticks={loop.Stats.Ticks}");
        Assert.Equal(loop.Stats.Ticks, valid);
    }
}
