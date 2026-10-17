using System.Diagnostics;
using System.Windows.Threading;

namespace BeatInsight.CommunityOverlays.Poc.Adapter;

public enum PublishTimerMode
{
    /// <summary>DispatcherTimer périodique (COM-01 à COM-06), priorité configurable.</summary>
    Dispatcher,
    /// <summary>Échéance absolue compensée (V3.14-COM-07), sur le thread UI.</summary>
    Compensated,
}

/// <summary>
/// Déclenche la publication du mock à cadence fixe, toujours sur le thread UI
/// (les appels WebView2 y restent). Arrêt définitif après <see cref="Dispose"/> :
/// aucune publication après fermeture.
/// </summary>
public sealed class PublishTicker : IDisposable
{
    private readonly Action _publish;
    private readonly DispatcherTimer _timer;
    private readonly PublishTimerMode _mode;
    private readonly TimeSpan _period;
    private readonly Stopwatch _clock = new();
    private CompensatedSchedule? _schedule;
    private bool _stopped;
    private bool _inTick;

    public PublishTicker(PublishTimerMode mode, TimeSpan period, DispatcherPriority priority, Action publish)
    {
        _mode = mode;
        _period = period;
        _publish = publish;
        _timer = new DispatcherTimer(mode == PublishTimerMode.Compensated ? DispatcherPriority.Normal : priority)
        {
            Interval = period,
        };
        _timer.Tick += OnTick;
    }

    public long EarlyWakeups { get; private set; }

    public long SkippedDeadlines => _schedule?.SkippedTotal ?? 0;

    public void Start()
    {
        _clock.Start();
        if (_mode == PublishTimerMode.Compensated)
            _schedule = new CompensatedSchedule(_clock.Elapsed, _period);
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_stopped || _inTick) return; // pas de tick concurrent ni après arrêt
        _inTick = true;
        try
        {
            if (_schedule is null)
            {
                _publish();
                return;
            }

            var decision = _schedule.OnWake(_clock.Elapsed);
            if (decision.Publish) _publish();
            else EarlyWakeups++;

            // Réarmement sur l'échéance absolue suivante (le minuteur arrondit au pas système).
            _timer.Interval = decision.NextDelay > TimeSpan.FromMilliseconds(1) ? decision.NextDelay : TimeSpan.FromMilliseconds(1);
        }
        finally
        {
            _inTick = false;
        }
    }

    public void Dispose()
    {
        _stopped = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
