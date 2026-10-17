using System.Diagnostics;

namespace BeatInsight.Services.Lifecycle;

internal enum ShutdownState
{
    Running,
    ShuttingDown,
    Disposed
}

internal sealed record ShutdownStep(string Name, Action Execute);

internal sealed record ShutdownResult(
    bool Started,
    ShutdownState State,
    TimeSpan Duration,
    IReadOnlyList<string> FailedSteps);

internal sealed class ShutdownCoordinator
{
    private readonly IReadOnlyList<ShutdownStep> steps;
    private readonly Action<string> info;
    private readonly Action<string> warning;
    private int state;

    internal ShutdownCoordinator(
        IEnumerable<ShutdownStep> steps,
        Action<string>? info = null,
        Action<string>? warning = null)
    {
        ArgumentNullException.ThrowIfNull(steps);
        this.steps = steps.ToArray();
        if (this.steps.Any(step => step is null || string.IsNullOrWhiteSpace(step.Name)))
            throw new ArgumentException("Shutdown steps must have names.", nameof(steps));

        this.info = info ?? (_ => { });
        this.warning = warning ?? (_ => { });
    }

    internal ShutdownState State => (ShutdownState)Volatile.Read(ref state);

    internal ShutdownResult BeginShutdown()
    {
        if (Interlocked.CompareExchange(
                ref state,
                (int)ShutdownState.ShuttingDown,
                (int)ShutdownState.Running) != (int)ShutdownState.Running)
        {
            return new(false, State, TimeSpan.Zero, []);
        }

        long started = Stopwatch.GetTimestamp();
        List<string> failed = [];
        info("SHUTDOWN BEGIN");
        try
        {
            foreach (ShutdownStep step in steps)
            {
                try
                {
                    step.Execute();
                    info($"SHUTDOWN | {step.Name}");
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    failed.Add(step.Name);
                    warning($"SHUTDOWN | {step.Name} failed with {exception.GetType().Name}");
                }
            }
        }
        finally
        {
            Volatile.Write(ref state, (int)ShutdownState.Disposed);
        }

        TimeSpan duration = Stopwatch.GetElapsedTime(started);
        info($"SHUTDOWN END | DurationMs={duration.TotalMilliseconds:F1} | FailedSteps={failed.Count}");
        return new(true, ShutdownState.Disposed, duration, failed);
    }
}
