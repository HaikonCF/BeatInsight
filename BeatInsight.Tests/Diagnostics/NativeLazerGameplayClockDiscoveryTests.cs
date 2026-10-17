using BeatInsight.Diagnostics.NativeLazer;
using System.Buffers.Binary;
using System.IO;

namespace BeatInsight.Tests.Diagnostics;

public sealed class NativeLazerGameplayClockDiscoveryTests
{
    private static readonly DateTime start = new(2026, 10, 4, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ValidActivePlayerAndInterpolatedFinalSource_AreUnique()
    {
        Fixture fixture = new();

        NativeLazerGameplayClockResult result = fixture.Discover();

        Assert.Equal(NativeLazerGameplayClockOutcome.Unique, result.Outcome);
        Assert.Equal(5_000, result.CurrentTimeMilliseconds);
        Assert.False(result.IsPaused);
        Assert.Equal(Fixture.Player, result.PlayerAddress);
        Assert.Contains("interpolatedTrack", result.AuthoritativeSource);
        Assert.InRange(result.Metrics.BytesRead, 1, 2_000);
        Assert.True(result.Metrics.ReadOperations > 0);
        Assert.Equal(17, result.Metrics.ChainLength);
    }

    [Fact]
    public void FinalOffsetClock_UsesFramedTimePlusOffset()
    {
        Fixture fixture = new();
        fixture.Memory.SetPointer(Fixture.Clock + fixture.Layout.FramedBeatmapClockFinalClockSource,
            Fixture.OffsetClock);
        fixture.Memory.SetDouble(Fixture.OffsetClock + fixture.Layout.FramedClockCurrentTime, 4_975);
        fixture.Memory.SetDouble(Fixture.OffsetClock + fixture.Layout.FramedOffsetClockOffset, 25);

        NativeLazerGameplayClockResult result = fixture.Discover();

        Assert.Equal(NativeLazerGameplayClockOutcome.Unique, result.Outcome);
        Assert.Equal(5_000, result.CurrentTimeMilliseconds);
        Assert.Contains("userBeatmapOffsetClock", result.AuthoritativeSource);
    }

    [Fact]
    public void NonPlayerTopScreen_IsNone()
    {
        Fixture fixture = new();
        fixture.Memory.SetPointer(Fixture.Player + fixture.Layout.PlayerGame, 0xDEADBEEF);

        NativeLazerGameplayClockResult result = fixture.Discover();

        Assert.Equal(NativeLazerGameplayClockOutcome.None, result.Outcome);
        Assert.Null(result.CurrentTimeMilliseconds);
    }

    [Fact]
    public void MultipleMatchingFinalSources_AreAmbiguous()
    {
        Fixture fixture = new();
        fixture.Memory.SetPointer(Fixture.Clock + fixture.Layout.FramedBeatmapClockUserBeatmapOffsetClock,
            Fixture.Interpolated);
        fixture.Memory.SetDouble(Fixture.Interpolated + fixture.Layout.FramedClockCurrentTime, 4_990);
        fixture.Memory.SetDouble(Fixture.Interpolated + fixture.Layout.FramedOffsetClockOffset, 10);

        NativeLazerGameplayClockResult result = fixture.Discover();

        Assert.Equal(NativeLazerGameplayClockOutcome.Ambiguous, result.Outcome);
        Assert.Null(result.CurrentTimeMilliseconds);
    }

    [Fact]
    public void DifferentGeneration_IsUnknownWithoutReadingMemory()
    {
        Fixture fixture = new();
        NativeLazerGenerationIdentity other = new(fixture.Generation.ProcessId, start.AddSeconds(1));

        NativeLazerGameplayClockResult result = fixture.Discovery.Discover(
            fixture.Generation, other, Fixture.Root, fixture.Layout, fixture.Memory,
            fixture.Duration, _ => true);

        Assert.Equal(NativeLazerGameplayClockOutcome.Unknown, result.Outcome);
        Assert.Null(result.CurrentTimeMilliseconds);
        Assert.Equal(0, result.Metrics.BytesRead);
        Assert.Equal(0, result.Metrics.ReadOperations);
    }

    [Fact]
    public void OutOfRangeValue_IsUnknownAndDoesNotReturnStaleTime()
    {
        Fixture fixture = new();
        Assert.Equal(NativeLazerGameplayClockOutcome.Unique, fixture.Discover().Outcome);
        fixture.Memory.SetDouble(Fixture.Interpolated + fixture.Layout.InterpolatingClockCurrentTime,
            fixture.Duration + 1);

        NativeLazerGameplayClockResult result = fixture.Discover();

        Assert.Equal(NativeLazerGameplayClockOutcome.Unknown, result.Outcome);
        Assert.Null(result.CurrentTimeMilliseconds);
        Assert.Contains("outside", result.FailureReason);
    }

    [Fact]
    public void TransientInitialSeek_IsUnknown()
    {
        Fixture fixture = new();
        fixture.Memory.SetByte(Fixture.Clock + fixture.Layout.FramedBeatmapClockInitialSeek, 1);
        fixture.Memory.SetDouble(
            Fixture.Clock + fixture.Layout.FramedBeatmapClockInitialSeek +
            NativeLazerGameplayClockLayout.NullableDoubleValueOffset,
            1_000);

        NativeLazerGameplayClockResult result = fixture.Discover();

        Assert.Equal(NativeLazerGameplayClockOutcome.Unknown, result.Outcome);
        Assert.Null(result.CurrentTimeMilliseconds);
        Assert.Contains("initial-seek", result.FailureReason);
    }

    [Fact]
    public void OwnerChangeDuringRead_IsUnknownAndClearsTime()
    {
        Fixture fixture = new();
        int playerReads = 0;
        ulong playerSlot = Fixture.Array + fixture.Layout.ArrayData;
        fixture.Memory.BeforeRead = (address, _) =>
        {
            if (address == playerSlot && ++playerReads == 2)
                fixture.Memory.SetPointer(playerSlot, 0xB0000);
        };

        NativeLazerGameplayClockResult result = fixture.Discover();

        Assert.Equal(NativeLazerGameplayClockOutcome.Unknown, result.Outcome);
        Assert.Null(result.CurrentTimeMilliseconds);
        Assert.Contains("identity changed", result.FailureReason);
    }

    [Fact]
    public void PauseState_IsReportedWithoutChangingClockAuthority()
    {
        Fixture fixture = new();
        fixture.Memory.SetByte(Fixture.PauseBindable + fixture.Layout.BindableBooleanValue, 1);

        NativeLazerGameplayClockResult result = fixture.Discover();

        Assert.Equal(NativeLazerGameplayClockOutcome.Unique, result.Outcome);
        Assert.True(result.IsPaused);
        Assert.Equal(5_000, result.CurrentTimeMilliseconds);
    }

    [Fact]
    public void ConservativeBeatmapDuration_HandlesCircleSliderSpinnerAndHold()
    {
        string path = Path.Combine(Path.GetTempPath(), $"beatinsight-clock-{Guid.NewGuid():N}.osu");
        try
        {
            File.WriteAllText(path, """
                osu file format v14
                [Difficulty]
                SliderMultiplier:1.4
                [TimingPoints]
                0,500,4,2,1,50,1,0
                [HitObjects]
                64,64,1000,1,0,0:0:0:0:
                64,64,2000,2,0,B|128:64,2,280
                64,64,5000,8,0,7000,0:0:0:0:
                64,64,8000,128,0,9000:0:0:0:0:
                """);

            double? duration = NativeLazerGameplayClockBeatmapDuration.TryReadMilliseconds(path);

            Assert.Equal(9_000, duration);
        }
        finally
        {
            File.Delete(path);
        }
    }

    internal sealed class Fixture
    {
        internal const ulong Root = 0x10000;
        internal const ulong ScreenStack = 0x20000;
        internal const ulong Stack = 0x30000;
        internal const ulong Array = 0x40000;
        internal const ulong Player = 0x50000;
        internal const ulong GameplayState = 0x60000;
        internal const ulong Container = 0x70000;
        internal const ulong PlayingStateBindable = 0x71000;
        internal const ulong PauseBindable = 0x72000;
        internal const ulong Clock = 0x80000;
        internal const ulong Decoupled = 0x81000;
        internal const ulong Interpolated = 0x82000;
        internal const ulong OffsetClock = 0x83000;

        internal Fixture()
        {
            Layout = new(
                OsuGameScreenStack: 0x10,
                ScreenStackStack: 0x18,
                StackArray: 0x08,
                StackSize: 0x10,
                ArrayLength: 0x08,
                ArrayData: 0x10,
                PlayerGame: 0x20,
                PlayerGameplayState: 0x28,
                PlayerGameplayClockContainer: 0x30,
                PlayerPlayingState: 0x38,
                GameplayClockContainerClock: 0x20,
                GameplayClockContainerPaused: 0x28,
                GameplayClockContainerStartTime: 0x30,
                GameplayClockContainerGameplayStartTime: 0x38,
                FramedBeatmapClockInitialSeek: 0x20,
                FramedBeatmapClockDecoupledTrack: 0x30,
                FramedBeatmapClockInterpolatedTrack: 0x38,
                FramedBeatmapClockUserBeatmapOffsetClock: 0x40,
                FramedBeatmapClockFinalClockSource: 0x48,
                InterpolatingClockCurrentTime: 0x20,
                DecouplingClockCurrentTime: 0x20,
                FramedClockCurrentTime: 0x28,
                FramedOffsetClockOffset: 0x30,
                BindableBooleanValue: 0x20,
                BindablePlayingStateValue: 0x20);
            Generation = new(1234, start);
            foreach (ulong address in new[]
                     {
                         Root, ScreenStack, Stack, Array, Player, GameplayState, Container,
                         PlayingStateBindable, PauseBindable, Clock, Decoupled, Interpolated, OffsetClock
                     })
                Memory.SetObject(address, address + 0x100000);

            Memory.SetPointer(Root + Layout.OsuGameScreenStack, ScreenStack);
            Memory.SetPointer(ScreenStack + Layout.ScreenStackStack, Stack);
            Memory.SetPointer(Stack + Layout.StackArray, Array);
            Memory.SetInt32(Stack + Layout.StackSize, 1);
            Memory.SetInt32(Array + Layout.ArrayLength, 4);
            Memory.SetPointer(Array + Layout.ArrayData, Player);
            Memory.SetPointer(Player + Layout.PlayerGame, Root);
            Memory.SetPointer(Player + Layout.PlayerGameplayState, GameplayState);
            Memory.SetPointer(Player + Layout.PlayerGameplayClockContainer, Container);
            Memory.SetPointer(Player + Layout.PlayerPlayingState, PlayingStateBindable);
            Memory.SetInt32(PlayingStateBindable + Layout.BindablePlayingStateValue, 2);
            Memory.SetPointer(Container + Layout.GameplayClockContainerPaused, PauseBindable);
            Memory.SetByte(PauseBindable + Layout.BindableBooleanValue, 0);
            Memory.SetPointer(Container + Layout.GameplayClockContainerClock, Clock);
            Memory.SetByte(Clock + Layout.FramedBeatmapClockInitialSeek, 0);
            Memory.SetPointer(Clock + Layout.FramedBeatmapClockDecoupledTrack, Decoupled);
            Memory.SetPointer(Clock + Layout.FramedBeatmapClockInterpolatedTrack, Interpolated);
            Memory.SetPointer(Clock + Layout.FramedBeatmapClockUserBeatmapOffsetClock, OffsetClock);
            Memory.SetPointer(Clock + Layout.FramedBeatmapClockFinalClockSource, Interpolated);
            Memory.SetDouble(Decoupled + Layout.DecouplingClockCurrentTime, 5_000);
            Memory.SetDouble(Interpolated + Layout.InterpolatingClockCurrentTime, 5_000);
            Memory.SetDouble(OffsetClock + Layout.FramedClockCurrentTime, 5_000);
            Memory.SetDouble(OffsetClock + Layout.FramedOffsetClockOffset, 0);
        }

        internal NativeLazerGameplayClockDiscovery Discovery { get; } = new();
        internal NativeLazerGameplayClockLayout Layout { get; }
        internal FakeMemory Memory { get; } = new();
        internal NativeLazerGenerationIdentity Generation { get; }
        internal double Duration => 120_000;

        internal NativeLazerGameplayClockResult Discover() => Discovery.Discover(
            Generation, Generation, Root, Layout, Memory, Duration, identity => identity == Generation);
    }

    internal sealed class FakeMemory : INativeLazerGameplayClockMemory
    {
        private readonly Dictionary<ulong, byte> bytes = [];
        private readonly Dictionary<ulong, int> reads = [];

        internal Action<ulong, int>? BeforeRead { get; set; }
        public long BytesRead { get; private set; }
        public int ReadOperationCount { get; private set; }

        public byte? ReadByte(ulong address) => Read(address, 1) is { } data ? data[0] : null;
        public int? ReadInt32(ulong address) => Read(address, 4) is { } data
            ? BinaryPrimitives.ReadInt32LittleEndian(data)
            : null;
        public double? ReadDouble(ulong address) => Read(address, 8) is { } data
            ? BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(data))
            : null;
        public ulong? ReadPointer(ulong address) => Read(address, 8) is { } data
            ? BinaryPrimitives.ReadUInt64LittleEndian(data)
            : null;

        internal void SetObject(ulong address, ulong methodTable) => SetPointer(address, methodTable);
        internal void SetByte(ulong address, byte value) => bytes[address] = value;
        internal void SetInt32(ulong address, int value) => Set(address, BitConverter.GetBytes(value));
        internal void SetDouble(ulong address, double value) => Set(address, BitConverter.GetBytes(value));
        internal void SetPointer(ulong address, ulong value) => Set(address, BitConverter.GetBytes(value));

        private byte[]? Read(ulong address, int count)
        {
            int occurrence = reads.GetValueOrDefault(address) + 1;
            reads[address] = occurrence;
            BeforeRead?.Invoke(address, occurrence);
            ReadOperationCount++;
            var result = new byte[count];
            for (int index = 0; index < count; index++)
            {
                if (!bytes.TryGetValue(address + checked((ulong)index), out result[index]))
                    return null;
            }
            BytesRead += count;
            return result;
        }

        private void Set(ulong address, byte[] value)
        {
            for (int index = 0; index < value.Length; index++)
                bytes[address + checked((ulong)index)] = value[index];
        }
    }
}
