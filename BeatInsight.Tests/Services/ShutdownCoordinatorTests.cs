using BeatInsight.Services.Lifecycle;

namespace BeatInsight.Tests.Services;

public sealed class ShutdownCoordinatorTests
{
    [Fact]
    public void BeginShutdown_TransitionsRunningThroughDisposed()
    {
        ShutdownState observed = ShutdownState.Running;
        ShutdownCoordinator? coordinator = null;
        coordinator = new ShutdownCoordinator(
            [new("observe", () => observed = coordinator!.State)]);

        ShutdownResult result = coordinator.BeginShutdown();

        Assert.True(result.Started);
        Assert.Equal(ShutdownState.ShuttingDown, observed);
        Assert.Equal(ShutdownState.Disposed, coordinator.State);
        Assert.Equal(ShutdownState.Disposed, result.State);
    }

    [Fact]
    public void SecondShutdownCall_IsNoOp()
    {
        int count = 0;
        var coordinator = new ShutdownCoordinator([new("once", () => count++)]);

        ShutdownResult first = coordinator.BeginShutdown();
        ShutdownResult second = coordinator.BeginShutdown();

        Assert.True(first.Started);
        Assert.False(second.Started);
        Assert.Equal(1, count);
    }

    [Fact]
    public void Steps_RunInDeclaredSafetyOrder()
    {
        List<string> order = [];
        var coordinator = new ShutdownCoordinator(
        [
            new("timers", () => order.Add("timers")),
            new("edit", () => order.Add("edit")),
            new("overlays", () => order.Add("overlays")),
            new("tray", () => order.Add("tray")),
            new("providers", () => order.Add("providers"))
        ]);

        coordinator.BeginShutdown();

        Assert.Equal(["timers", "edit", "overlays", "tray", "providers"], order);
    }

    [Fact]
    public void FailedStep_DoesNotPreventRemainingCleanup()
    {
        List<string> order = [];
        var coordinator = new ShutdownCoordinator(
        [
            new("timers", () => throw new InvalidOperationException()),
            new("overlays", () => order.Add("overlays")),
            new("providers", () => order.Add("providers"))
        ]);

        ShutdownResult result = coordinator.BeginShutdown();

        Assert.Equal(["timers"], result.FailedSteps);
        Assert.Equal(["overlays", "providers"], order);
        Assert.Equal(ShutdownState.Disposed, coordinator.State);
    }

    [Fact]
    public void Shutdown_EmitsConciseBeginStepsAndEndTrace()
    {
        List<string> messages = [];
        var coordinator = new ShutdownCoordinator(
            [new("timers stopped", () => { })],
            messages.Add,
            messages.Add);

        coordinator.BeginShutdown();

        Assert.Equal("SHUTDOWN BEGIN", messages[0]);
        Assert.Equal("SHUTDOWN | timers stopped", messages[1]);
        Assert.StartsWith("SHUTDOWN END |", messages[2], StringComparison.Ordinal);
    }
}
