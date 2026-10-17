using BeatInsight.CommunityOverlays.Poc.Adapter;
using Xunit;

namespace BeatInsight.CommunityOverlays.Poc.Tests;

/// <summary>V3.14-COM-07 : ordonnancement à échéance absolue.</summary>
public class CompensatedScheduleTests
{
    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(100);
    private const double WindowsTimerStepMs = 15.625;

    /// <summary>Simule un minuteur Windows : le réveil arrive au multiple suivant de 15,625 ms.</summary>
    private static TimeSpan Quantize(TimeSpan now, TimeSpan delay)
    {
        double target = now.TotalMilliseconds + delay.TotalMilliseconds;
        double steps = Math.Ceiling(target / WindowsTimerStepMs);
        return TimeSpan.FromMilliseconds(steps * WindowsTimerStepMs);
    }

    private static List<double> Run(CompensatedSchedule schedule, int publications, Func<TimeSpan, TimeSpan, TimeSpan> wake, out int earlyWakes)
    {
        var published = new List<double>();
        earlyWakes = 0;
        TimeSpan now = TimeSpan.Zero;
        TimeSpan delay = Period;
        while (published.Count < publications)
        {
            now = wake(now, delay);
            var decision = schedule.OnWake(now);
            if (decision.Publish) published.Add(now.TotalMilliseconds);
            else earlyWakes++;
            delay = decision.NextDelay;
        }
        return published;
    }

    [Fact]
    public void Average_period_is_100ms_despite_timer_granularity()
    {
        var schedule = new CompensatedSchedule(TimeSpan.Zero, Period);

        var ticks = Run(schedule, 1200, Quantize, out _);

        double average = (ticks[^1] - ticks[0]) / (ticks.Count - 1);
        Assert.InRange(average, 99.0, 101.0);
    }

    [Fact]
    public void No_cumulative_drift_over_two_minutes()
    {
        var schedule = new CompensatedSchedule(TimeSpan.Zero, Period);

        var ticks = Run(schedule, 1200, Quantize, out _);

        // Le n-ième tick reste à moins d'un pas de minuteur de son échéance n × 100 ms.
        for (int i = 0; i < ticks.Count; i++)
            Assert.InRange(ticks[i] - (i + 1) * 100.0, -1.0, WindowsTimerStepMs + 0.001);
    }

    [Fact]
    public void Plain_periodic_timer_would_drift_for_reference()
    {
        // Référence : un minuteur périodique de 100 ms arrondi au pas Windows fait 109,375 ms.
        double drifted = Math.Ceiling(100 / WindowsTimerStepMs) * WindowsTimerStepMs;
        Assert.Equal(109.375, drifted, 3);
    }

    [Fact]
    public void Early_wakeup_does_not_publish()
    {
        var schedule = new CompensatedSchedule(TimeSpan.Zero, Period);

        var decision = schedule.OnWake(TimeSpan.FromMilliseconds(93.75));

        Assert.False(decision.Publish);
        Assert.Equal(6.25, decision.NextDelay.TotalMilliseconds, 3);
        Assert.Equal(0, schedule.Published);
    }

    [Fact]
    public void Overload_publishes_once_then_skips_missed_deadlines_without_burst()
    {
        var schedule = new CompensatedSchedule(TimeSpan.Zero, Period);
        schedule.OnWake(TimeSpan.FromMilliseconds(100));

        // Thread UI bloqué 750 ms : réveil à 870 ms au lieu de 200 ms.
        var late = schedule.OnWake(TimeSpan.FromMilliseconds(870));
        Assert.True(late.Publish);
        // 200…700 abandonnées (800 publiée en retard) + 900, trop proche (30 ms) de la publication tardive.
        Assert.Equal(7, late.SkippedDeadlines);
        Assert.Equal(1000, schedule.NextDeadline.TotalMilliseconds, 3);

        // Un réveil immédiat ne republie pas (pas de rafale).
        var immediate = schedule.OnWake(TimeSpan.FromMilliseconds(871));
        Assert.False(immediate.Publish);
        Assert.Equal(2, schedule.Published);
    }

    [Fact]
    public void Late_publication_close_to_next_deadline_skips_it_to_avoid_a_pair_burst()
    {
        var schedule = new CompensatedSchedule(TimeSpan.Zero, Period);
        schedule.OnWake(TimeSpan.FromMilliseconds(100));

        // Réveil tardif à 280 ms : l'échéance alignée suivante (300 ms) serait 20 ms plus tard.
        var late = schedule.OnWake(TimeSpan.FromMilliseconds(280));

        Assert.True(late.Publish);
        Assert.Equal(400, schedule.NextDeadline.TotalMilliseconds, 3);
        Assert.True(late.NextDelay >= Period / 2);
    }

    [Fact]
    public void Random_stalls_never_produce_intervals_below_half_a_period()
    {
        var random = new Random(42);
        var schedule = new CompensatedSchedule(TimeSpan.Zero, Period);

        // Minuteur Windows + blocages aléatoires (0 à 1,2 s) une fois sur 20.
        var ticks = Run(schedule, 1500, (now, delay) =>
        {
            var wake = Quantize(now, delay);
            return random.Next(20) == 0 ? wake + TimeSpan.FromMilliseconds(random.Next(1200)) : wake;
        }, out _);

        for (int i = 1; i < ticks.Count; i++)
            Assert.True(ticks[i] - ticks[i - 1] >= 50 - 0.001, $"intervalle {ticks[i] - ticks[i - 1]} ms à {ticks[i]} ms");
    }

    [Fact]
    public void Never_two_publications_closer_than_a_timer_step_in_normal_operation()
    {
        var schedule = new CompensatedSchedule(TimeSpan.Zero, Period);

        var ticks = Run(schedule, 600, Quantize, out int early);

        for (int i = 1; i < ticks.Count; i++)
            Assert.True(ticks[i] - ticks[i - 1] >= 100 - WindowsTimerStepMs - 0.001, $"rafale à {ticks[i]} ms");
        Assert.True(early < ticks.Count, "les réveils précoces restent minoritaires");
    }

    [Fact]
    public void Exact_timer_gives_exact_period()
    {
        var schedule = new CompensatedSchedule(TimeSpan.Zero, Period);

        var ticks = Run(schedule, 50, (now, delay) => now + delay, out int early);

        Assert.Equal(0, early);
        for (int i = 0; i < ticks.Count; i++)
            Assert.Equal((i + 1) * 100.0, ticks[i], 3);
    }

    [Fact]
    public void Default_timer_mode_is_compensated()
    {
        Assert.Equal(PublishTimerMode.Compensated, PocOptions.Parse([]).PublishTimer);
        Assert.Equal(PublishTimerMode.Dispatcher, PocOptions.Parse(["--publish-timer", "dispatcher"]).PublishTimer);
        Assert.Equal((15000, 500), PocOptions.Parse(["--ui-busy-at", "15000", "--ui-busy-ms", "500"]).UiBusy);
    }
}
