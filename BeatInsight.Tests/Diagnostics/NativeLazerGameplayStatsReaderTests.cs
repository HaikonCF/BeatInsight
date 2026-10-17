using BeatInsight.Diagnostics.NativeLazer;

namespace BeatInsight.Tests.Diagnostics;

public sealed class NativeLazerGameplayStatsReaderTests
{
    private static readonly NativeLazerGenerationIdentity Generation =
        new(42, new DateTime(2026, 10, 17, 10, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void ValidNativeFields_AreReturnedExactly()
    {
        (NativeLazerGameplayStatsReader reader, NativeLazerGameplayStatsLayout layout,
            NativeLazerGameplayStatsMethodTables tables, FakeMemory memory) = Fixture();

        NativeLazerGameplayStatsResult result = reader.Read(
            Generation, Generation, 0x10000, 12_345, layout, tables, memory, _ => true);

        Assert.Equal(NativeLazerGameplayStatsOutcome.Valid, result.Outcome);
        Assert.Equal(123, result.CurrentCombo);
        Assert.Equal(150, result.HighestCombo);
        Assert.Equal(90, result.CountGreat);
        Assert.Equal(6, result.CountOk);
        Assert.Equal(2, result.CountMeh);
        Assert.Equal(1, result.CountMiss);
        Assert.Equal(543_210, result.Score);
        Assert.Empty(result.Mods);
    }

    [Fact]
    public void AccuracyOutsideNativeRange_FailsClosed()
    {
        (NativeLazerGameplayStatsReader reader, NativeLazerGameplayStatsLayout layout,
            NativeLazerGameplayStatsMethodTables tables, FakeMemory memory) = Fixture();
        memory.Doubles[0x22008] = 1.01;

        NativeLazerGameplayStatsResult result = reader.Read(
            Generation, Generation, 0x10000, 12_345, layout, tables, memory, _ => true);

        Assert.Equal(NativeLazerGameplayStatsOutcome.Invalid, result.Outcome);
        Assert.StartsWith("Score values are incoherent:", result.FailureReason, StringComparison.Ordinal);
        Assert.Null(result.CurrentCombo);
    }

    [Fact]
    public void LazerAccuracyWithTicksAndSliderTails_IsAcceptedAndReturnedExactly()
    {
        (NativeLazerGameplayStatsReader reader, NativeLazerGameplayStatsLayout layout,
            NativeLazerGameplayStatsMethodTables tables, FakeMemory memory) = Fixture();
        memory.Int32[0x26010] = 7;
        memory.Int32[0x26108] = 7;
        WriteEntry(memory, 0x26150, 10, 10);
        WriteEntry(memory, 0x26160, 9, 2);
        WriteEntry(memory, 0x26170, 16, 30);
        memory.Doubles[0x22008] =
            (90 * 300d + 6 * 100d + 2 * 50d + 10 * 30d + 30 * 150d) /
            (99 * 300d + 12 * 30d + 30 * 150d);

        NativeLazerGameplayStatsResult result = reader.Read(
            Generation, Generation, 0x10000, 12_345, layout, tables, memory, _ => true);

        Assert.Equal(NativeLazerGameplayStatsOutcome.Valid, result.Outcome);
        Assert.Equal(2, result.CountLargeTickMiss);
        Assert.Equal(30, result.CountSliderTailHit);
        Assert.Equal(memory.Doubles[0x22008], result.Accuracy);
    }

    [Fact]
    public void SparseNativeResultDictionary_TreatsAbsentJudgementsAsZero()
    {
        (NativeLazerGameplayStatsReader reader, NativeLazerGameplayStatsLayout layout,
            NativeLazerGameplayStatsMethodTables tables, FakeMemory memory) = Fixture();
        memory.Int32[0x26010] = 1;
        memory.Int32[0x26108] = 1;
        WriteEntry(memory, 0x26110, 3, 1);
        memory.Doubles[0x22008] = 1d / 3d;

        NativeLazerGameplayStatsResult result = reader.Read(
            Generation, Generation, 0x10000, 12_345, layout, tables, memory, _ => true);

        Assert.Equal(NativeLazerGameplayStatsOutcome.Valid, result.Outcome);
        Assert.Equal(0, result.CountGreat);
        Assert.Equal(1, result.CountOk);
        Assert.Equal(0, result.CountMeh);
        Assert.Equal(0, result.CountMiss);
    }

    [Fact]
    public void UnsupportedNativeMod_FailsClosedWithoutPartialStats()
    {
        (NativeLazerGameplayStatsReader reader, NativeLazerGameplayStatsLayout layout,
            NativeLazerGameplayStatsMethodTables tables, FakeMemory memory) = Fixture();
        memory.Int32[0x27008] = 1;
        memory.Pointers[0x27010] = 0x28000;
        memory.Pointers[0x28000] = 0xDEAD0000;

        NativeLazerGameplayStatsResult result = reader.Read(
            Generation, Generation, 0x10000, 12_345, layout, tables, memory, _ => true);

        Assert.Equal(NativeLazerGameplayStatsOutcome.Unavailable, result.Outcome);
        Assert.StartsWith("Mods are unavailable:", result.FailureReason, StringComparison.Ordinal);
        Assert.Empty(result.Mods);
    }

    [Fact]
    public void GenerationChangeDuringRead_FailsClosed()
    {
        (NativeLazerGameplayStatsReader reader, NativeLazerGameplayStatsLayout layout,
            NativeLazerGameplayStatsMethodTables tables, FakeMemory memory) = Fixture();
        int validations = 0;

        NativeLazerGameplayStatsResult result = reader.Read(
            Generation, Generation, 0x10000, 12_345, layout, tables, memory,
            _ => ++validations == 1);

        Assert.Equal(NativeLazerGameplayStatsOutcome.Unavailable, result.Outcome);
        Assert.Equal("ScoreProcessor changed during the read.", result.FailureReason);
    }

    private static (NativeLazerGameplayStatsReader, NativeLazerGameplayStatsLayout,
        NativeLazerGameplayStatsMethodTables, FakeMemory) Fixture()
    {
        var layout = new NativeLazerGameplayStatsLayout(
            PlayerScoreProcessor: 0x10,
            ScoreProcessorTotalScore: 0x10,
            ScoreProcessorAccuracy: 0x18,
            ScoreProcessorCombo: 0x20,
            ScoreProcessorHighestCombo: 0x28,
            ScoreProcessorMods: 0x30,
            ScoreProcessorResultCounts: 0x38,
            BindableLongValue: 0x8,
            BindableDoubleValue: 0x8,
            BindableIntValue: 0x8,
            BindableModsValue: 0x8,
            DictionaryEntries: 0x8,
            DictionaryCount: 0x10,
            ListItems: 0x8,
            ListSize: 0x10,
            EntryStride: 16,
            EntryNext: 0,
            EntryKey: 4,
            EntryValue: 8,
            SoloPlayerToken: 1,
            ScoreProcessorToken: 2,
            SupportedModTokens: new Dictionary<int, string>());
        var tables = new NativeLazerGameplayStatsMethodTables(
            Generation, 0xAA000, 0xBB000, new Dictionary<ulong, string>());
        var memory = new FakeMemory();
        memory.Pointers[0x10000] = 0xAA000;
        memory.Pointers[0x10010] = 0x20000;
        memory.Pointers[0x20000] = 0xBB000;

        ulong[] bindables = [0x21000, 0x22000, 0x23000, 0x24000, 0x25000];
        foreach (ulong address in bindables) memory.Pointers[address] = 0xCC000;
        memory.Pointers[0x20010] = 0x21000;
        memory.Pointers[0x20018] = 0x22000;
        memory.Pointers[0x20020] = 0x23000;
        memory.Pointers[0x20028] = 0x24000;
        memory.Pointers[0x20030] = 0x25000;
        memory.Pointers[0x20038] = 0x26000;
        memory.Pointers[0x26000] = 0xCC000;

        memory.Int64[0x21008] = 543_210;
        memory.Doubles[0x22008] = (90 * 300d + 6 * 100d + 2 * 50d) / (99 * 300d);
        memory.Int32[0x23008] = 123;
        memory.Int32[0x24008] = 150;

        memory.Pointers[0x26008] = 0x26100;
        memory.Int32[0x26010] = 4;
        memory.Int32[0x26108] = 4;
        WriteEntry(memory, 0x26110, 5, 90);
        WriteEntry(memory, 0x26120, 3, 6);
        WriteEntry(memory, 0x26130, 2, 2);
        WriteEntry(memory, 0x26140, 1, 1);

        memory.Pointers[0x25008] = 0x27000;
        memory.Int32[0x27008] = 0;
        return (new NativeLazerGameplayStatsReader(), layout, tables, memory);
    }

    private static void WriteEntry(FakeMemory memory, ulong address, int key, int value)
    {
        memory.Int32[address] = -1;
        memory.Int32[address + 4] = key;
        memory.Int32[address + 8] = value;
    }

    private sealed class FakeMemory : INativeLazerGameplayClockMemory
    {
        internal Dictionary<ulong, ulong> Pointers { get; } = [];
        internal Dictionary<ulong, int> Int32 { get; } = [];
        internal Dictionary<ulong, long> Int64 { get; } = [];
        internal Dictionary<ulong, double> Doubles { get; } = [];
        public long BytesRead { get; private set; }
        public int ReadOperationCount { get; private set; }
        public byte? ReadByte(ulong address) => null;
        public int? ReadInt32(ulong address) => Read(address, Int32, 4);
        public long? ReadInt64(ulong address) => Read(address, Int64, 8);
        public double? ReadDouble(ulong address) => Read(address, Doubles, 8);
        public ulong? ReadPointer(ulong address) => Read(address, Pointers, 8);

        private T? Read<T>(ulong address, Dictionary<ulong, T> values, int bytes) where T : struct
        {
            ReadOperationCount++;
            if (!values.TryGetValue(address, out T value)) return null;
            BytesRead += bytes;
            return value;
        }
    }
}
