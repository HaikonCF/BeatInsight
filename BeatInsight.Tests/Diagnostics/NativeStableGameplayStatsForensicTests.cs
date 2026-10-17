using BeatInsight.Diagnostics.NativeStable;

namespace BeatInsight.Tests.Diagnostics;

public sealed class NativeStableGameplayStatsForensicTests
{
    [Fact]
    public void StructuralDiscoveryFindsCompletePlausibleStatsChain()
    {
        Fixture fixture = Fixture.Valid();

        NativeStableGameplayStatsSample sample = Assert.Single(
            NativeStableGameplayStatsScanner.Discover(fixture.Memory, fixture.Regions));

        Assert.Equal(Fixture.GameplayBase, sample.GameplayBase);
        Assert.Equal(Fixture.ScoreBase, sample.ScoreBase);
        Assert.Equal(27350d / 28800d, sample.Accuracy, 6);
        Assert.Equal(28, sample.CurrentCombo);
        Assert.Equal(42, sample.MaxComboReached);
        Assert.Equal(90, sample.Count300);
        Assert.Equal(3, sample.Count100);
        Assert.Equal(1, sample.Count50);
        Assert.Equal(2, sample.CountMiss);
        Assert.Equal(123456, sample.Score);
        Assert.Equal(0x48, sample.Mods);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    public void InvalidAccuracyIsRejected(double accuracy)
    {
        Fixture fixture = Fixture.Valid();
        fixture.Memory.WriteDouble(Fixture.AccuracyBase + 0x0c, accuracy);

        Assert.Empty(NativeStableGameplayStatsScanner.Discover(fixture.Memory, fixture.Regions));
    }

    [Fact]
    public void PercentageAccuracyIsNormalizedToRatio()
    {
        Fixture fixture = Fixture.Valid();
        fixture.Memory.WriteDouble(Fixture.AccuracyBase + 0x0c, (27350d / 28800d) * 100d);

        NativeStableGameplayStatsSample sample = Assert.IsType<NativeStableGameplayStatsSample>(
            NativeStableGameplayStatsScanner.Read(fixture.Memory, Fixture.GameplayBase));

        Assert.Equal(27350d / 28800d, sample.Accuracy, 6);
    }

    [Fact]
    public void MateriallyIncoherentAccuracyIsRejected()
    {
        Fixture fixture = Fixture.Valid();
        fixture.Memory.WriteDouble(Fixture.AccuracyBase + 0x0c, 0.50);

        Assert.Null(NativeStableGameplayStatsScanner.Read(fixture.Memory, Fixture.GameplayBase));
    }

    [Fact]
    public void CounterChangesAndComboResetAreReadWithoutPreviousState()
    {
        Fixture fixture = Fixture.Valid();
        fixture.Memory.WriteUInt16(Fixture.ScoreBase + 0x88, 4);
        fixture.Memory.WriteUInt16(Fixture.ScoreBase + 0x8c, 2);
        fixture.Memory.WriteUInt16(Fixture.ScoreBase + 0x92, 3);
        fixture.Memory.WriteUInt16(Fixture.ScoreBase + 0x94, 0);
        fixture.Memory.WriteDouble(Fixture.AccuracyBase + 0x0c, 27500d / 29700d);

        NativeStableGameplayStatsSample sample = Assert.IsType<NativeStableGameplayStatsSample>(
            NativeStableGameplayStatsScanner.Read(fixture.Memory, Fixture.GameplayBase));

        Assert.Equal(4, sample.Count100);
        Assert.Equal(2, sample.Count50);
        Assert.Equal(3, sample.CountMiss);
        Assert.Equal(0, sample.CurrentCombo);
    }

    [Fact]
    public void PartialScoreReadFailsClosed()
    {
        Fixture fixture = Fixture.Valid();
        fixture.Memory.FailingAddress = Fixture.ScoreBase;

        Assert.Null(NativeStableGameplayStatsScanner.Read(
            fixture.Memory,
            Fixture.GameplayBase));
    }

    private sealed class Fixture
    {
        internal const nuint GameplayBase = 0x10100;
        internal const nuint ScoreBase = 0x11000;
        internal const nuint AccuracyBase = 0x12000;
        private const nuint ModsBase = 0x13000;

        internal SyntheticMemory Memory { get; } = new();
        internal IReadOnlyList<NativeStableMemoryRegion> Regions { get; } =
        [new(0x10000, 0x4000, 0x1000, 0x04, 0x20000, 0x10000)];

        internal static Fixture Valid()
        {
            var fixture = new Fixture();
            fixture.Memory.WriteUInt32(GameplayBase + 0x38, (uint)ScoreBase);
            fixture.Memory.WriteUInt32(GameplayBase + 0x48, (uint)AccuracyBase);
            fixture.Memory.WriteUInt32(ScoreBase + 0x1c, (uint)ModsBase);
            fixture.Memory.WriteUInt16(ScoreBase + 0x68, 42);
            fixture.Memory.WriteInt32(ScoreBase + 0x78, 123456);
            fixture.Memory.WriteUInt16(ScoreBase + 0x88, 3);
            fixture.Memory.WriteUInt16(ScoreBase + 0x8a, 90);
            fixture.Memory.WriteUInt16(ScoreBase + 0x8c, 1);
            fixture.Memory.WriteUInt16(ScoreBase + 0x8e, 2);
            fixture.Memory.WriteUInt16(ScoreBase + 0x90, 1);
            fixture.Memory.WriteUInt16(ScoreBase + 0x92, 2);
            fixture.Memory.WriteUInt16(ScoreBase + 0x94, 28);
            fixture.Memory.WriteDouble(AccuracyBase + 0x0c, 27350d / 28800d);
            fixture.Memory.WriteInt32(ModsBase + 0x08, 0x08);
            fixture.Memory.WriteInt32(ModsBase + 0x0c, 0x40);
            return fixture;
        }
    }

    private sealed class SyntheticMemory : INativeStableMemoryReader
    {
        private readonly Dictionary<nuint, byte> bytes = [];
        internal nuint? FailingAddress { get; set; }
        public bool Is32Bit => true;

        internal void WriteUInt16(nuint address, ushort value) => Write(address, BitConverter.GetBytes(value));
        internal void WriteUInt32(nuint address, uint value) => Write(address, BitConverter.GetBytes(value));
        internal void WriteInt32(nuint address, int value) => Write(address, BitConverter.GetBytes(value));
        internal void WriteDouble(nuint address, double value) => Write(address, BitConverter.GetBytes(value));

        public NativeStableMemoryReadResult ReadBytes(nuint address, int count)
        {
            if (FailingAddress == address)
                return NativeStableMemoryReadResult.Failed;

            byte[] result = new byte[count];
            for (int index = 0; index < count; index++)
                bytes.TryGetValue(address + checked((nuint)index), out result[index]);
            return new(result, true, count);
        }

        public bool TryReadPointer(nuint address, out nuint value)
        {
            NativeStableMemoryReadResult read = ReadBytes(address, sizeof(uint));
            value = read.Complete ? BitConverter.ToUInt32(read.Data, 0) : 0;
            return value != 0;
        }

        private void Write(nuint address, byte[] value)
        {
            for (int index = 0; index < value.Length; index++)
                bytes[address + checked((nuint)index)] = value[index];
        }
    }
}
