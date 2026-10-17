using BeatInsight.Overlay;

namespace BeatInsight.Tests.Overlay;

public sealed class OverlayForegroundGateTests
{
    [Fact]
    public void MatchingForegroundPidIsAccepted()
    {
        Assert.True(Evaluate(osuProcessId: 42, foregroundProcessId: 42));
    }

    [Fact]
    public void DifferentForegroundPidFailsClosed()
    {
        Assert.False(Evaluate(osuProcessId: 42, foregroundProcessId: 99));
    }

    [Fact]
    public void NullForegroundWindowFailsClosed()
    {
        Assert.False(Evaluate(
            osuProcessId: 42,
            foregroundWindow: IntPtr.Zero,
            foregroundProcessId: null));
    }

    [Fact]
    public void MissingOsuWindowFailsClosed()
    {
        Assert.False(Evaluate(osuProcessId: 42, osuWindowFound: false));
    }

    [Fact]
    public void MissingOrStoppedOsuProcessFailsClosed()
    {
        Assert.False(Evaluate(osuProcessId: null));
        Assert.False(Evaluate(osuProcessId: 42, osuProcessExists: false));
    }

    private static bool Evaluate(
        int? osuProcessId,
        bool osuProcessExists = true,
        bool osuWindowFound = true,
        IntPtr? foregroundWindow = null,
        int? foregroundProcessId = 42) =>
        OverlayForegroundEvaluator.IsOsuForeground(
            osuProcessId,
            osuProcessExists,
            osuWindowFound,
            foregroundWindow ?? new IntPtr(123),
            foregroundProcessId);
}
