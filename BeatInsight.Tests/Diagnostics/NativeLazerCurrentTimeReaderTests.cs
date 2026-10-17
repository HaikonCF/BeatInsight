using BeatInsight.Diagnostics.NativeLazer;

namespace BeatInsight.Tests.Diagnostics;

public sealed class NativeLazerCurrentTimeReaderTests
{
    [Fact]
    public void ValidChainReturnsCurrentTimeWithoutKnownDuration()
    {
        var fixture = new NativeLazerGameplayClockDiscoveryTests.Fixture();

        NativeLazerGameplayClockResult result = Read(fixture);

        Assert.Equal(NativeLazerGameplayClockOutcome.Unique, result.Outcome);
        Assert.Equal(5_000, result.CurrentTimeMilliseconds);
    }

    [Fact]
    public void NonPlayerReturnsNull()
    {
        var fixture = new NativeLazerGameplayClockDiscoveryTests.Fixture();
        fixture.Memory.SetPointer(
            NativeLazerGameplayClockDiscoveryTests.Fixture.Player + fixture.Layout.PlayerGame,
            0x99999);

        Assert.Null(Read(fixture).CurrentTimeMilliseconds);
    }

    [Fact]
    public void GenerationMismatchReturnsNullWithoutMemoryReads()
    {
        var fixture = new NativeLazerGameplayClockDiscoveryTests.Fixture();
        NativeLazerGenerationIdentity other = new(
            fixture.Generation.ProcessId,
            fixture.Generation.ProcessStartIdentity.AddSeconds(1));

        NativeLazerGameplayClockResult result = new NativeLazerCurrentTimeReader().TryReadCurrentTime(
            fixture.Generation,
            other,
            NativeLazerGameplayClockDiscoveryTests.Fixture.Root,
            fixture.Layout,
            fixture.Memory,
            _ => true);

        Assert.Null(result.CurrentTimeMilliseconds);
        Assert.Equal(0, result.Metrics.ReadOperations);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("player")]
    [InlineData("container")]
    [InlineData("clock")]
    [InlineData("final-null")]
    [InlineData("final-wrong")]
    public void InvalidClockChainReturnsNull(string brokenLink)
    {
        var fixture = new NativeLazerGameplayClockDiscoveryTests.Fixture();
        Break(fixture, brokenLink);

        Assert.Null(Read(fixture).CurrentTimeMilliseconds);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1d)]
    public void InvalidCurrentTimeReturnsNull(double value)
    {
        var fixture = new NativeLazerGameplayClockDiscoveryTests.Fixture();
        fixture.Memory.SetDouble(
            NativeLazerGameplayClockDiscoveryTests.Fixture.Interpolated +
            fixture.Layout.InterpolatingClockCurrentTime,
            value);

        Assert.Null(Read(fixture).CurrentTimeMilliseconds);
    }

    [Fact]
    public void PlayerChangedDuringReadRejectsOldValue()
    {
        var fixture = new NativeLazerGameplayClockDiscoveryTests.Fixture();
        ulong activeScreenSlot = NativeLazerGameplayClockDiscoveryTests.Fixture.Array + fixture.Layout.ArrayData;
        fixture.Memory.BeforeRead = (address, occurrence) =>
        {
            if (address == activeScreenSlot && occurrence == 2)
                fixture.Memory.SetPointer(activeScreenSlot, 0x90000);
        };

        Assert.Null(Read(fixture).CurrentTimeMilliseconds);
    }

    [Fact]
    public void NewValidatedChainReturnsOnlyNewValue()
    {
        var fixture = new NativeLazerGameplayClockDiscoveryTests.Fixture();
        fixture.Memory.SetDouble(
            NativeLazerGameplayClockDiscoveryTests.Fixture.Interpolated +
            fixture.Layout.InterpolatingClockCurrentTime,
            42_000);
        Assert.Equal(42_000, Read(fixture).CurrentTimeMilliseconds);

        fixture.Memory.SetPointer(
            NativeLazerGameplayClockDiscoveryTests.Fixture.Player + fixture.Layout.PlayerGame,
            0x99999);
        Assert.Null(Read(fixture).CurrentTimeMilliseconds);

        fixture.Memory.SetPointer(
            NativeLazerGameplayClockDiscoveryTests.Fixture.Player + fixture.Layout.PlayerGame,
            NativeLazerGameplayClockDiscoveryTests.Fixture.Root);
        fixture.Memory.SetDouble(
            NativeLazerGameplayClockDiscoveryTests.Fixture.Interpolated +
            fixture.Layout.InterpolatingClockCurrentTime,
            3_000);

        Assert.Equal(3_000, Read(fixture).CurrentTimeMilliseconds);
    }

    [Fact]
    public void ReaderHasNoAnalysisOrRootDiscoveryCallback()
    {
        var fixture = new NativeLazerGameplayClockDiscoveryTests.Fixture();
        int externalWorkCalls = 0;

        NativeLazerGameplayClockResult result = Read(fixture);

        Assert.Equal(NativeLazerGameplayClockOutcome.Unique, result.Outcome);
        Assert.Equal(0, externalWorkCalls);
        Assert.InRange(result.Metrics.ReadOperations, 1, 100);
    }

    [Fact]
    public void KnownDurationRejectsValueBeyondBeatmap()
    {
        var fixture = new NativeLazerGameplayClockDiscoveryTests.Fixture();
        fixture.Memory.SetDouble(
            NativeLazerGameplayClockDiscoveryTests.Fixture.Interpolated +
            fixture.Layout.InterpolatingClockCurrentTime,
            5_000);

        NativeLazerGameplayClockResult result = Read(fixture, knownDuration: 4_999);

        Assert.Null(result.CurrentTimeMilliseconds);
    }

    private static NativeLazerGameplayClockResult Read(
        NativeLazerGameplayClockDiscoveryTests.Fixture fixture,
        double? knownDuration = null) =>
        new NativeLazerCurrentTimeReader().TryReadCurrentTime(
            fixture.Generation,
            fixture.Generation,
            NativeLazerGameplayClockDiscoveryTests.Fixture.Root,
            fixture.Layout,
            fixture.Memory,
            identity => identity == fixture.Generation,
            knownDuration);

    private static void Break(
        NativeLazerGameplayClockDiscoveryTests.Fixture fixture,
        string brokenLink)
    {
        ulong player = NativeLazerGameplayClockDiscoveryTests.Fixture.Player;
        ulong container = NativeLazerGameplayClockDiscoveryTests.Fixture.Container;
        ulong clock = NativeLazerGameplayClockDiscoveryTests.Fixture.Clock;
        switch (brokenLink)
        {
            case "root":
                fixture.Memory.SetPointer(NativeLazerGameplayClockDiscoveryTests.Fixture.Root, 0);
                break;
            case "player":
                fixture.Memory.SetPointer(
                    NativeLazerGameplayClockDiscoveryTests.Fixture.Array + fixture.Layout.ArrayData,
                    0);
                break;
            case "container":
                fixture.Memory.SetPointer(player + fixture.Layout.PlayerGameplayClockContainer, 0);
                break;
            case "clock":
                fixture.Memory.SetPointer(container + fixture.Layout.GameplayClockContainerClock, 0);
                break;
            case "final-null":
                fixture.Memory.SetPointer(clock + fixture.Layout.FramedBeatmapClockFinalClockSource, 0);
                break;
            case "final-wrong":
                fixture.Memory.SetPointer(
                    clock + fixture.Layout.FramedBeatmapClockFinalClockSource,
                    NativeLazerGameplayClockDiscoveryTests.Fixture.Decoupled);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(brokenLink));
        }
    }
}
