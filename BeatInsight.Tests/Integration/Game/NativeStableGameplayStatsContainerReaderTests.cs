using BeatInsight.Diagnostics.NativeStable;
using BeatInsight.Integration.Game;

namespace BeatInsight.Tests.Integration.Game;

public sealed class NativeStableGameplayStatsContainerReaderTests
{
    [Fact]
    public void UniqueSignatureResolvesContainerAndReadsStats()
    {
        Fixture fixture = Fixture.Valid();
        NativeStableGameplayStats stats = Assert.IsType<NativeStableGameplayStats>(
            fixture.Reader.Read(fixture.Context, "7:generation"));

        Assert.Equal(28, stats.CurrentCombo);
        Assert.Equal(90, stats.Count300);
        Assert.Equal(["HD", "DT"], stats.Mods.Select(mod => mod.Acronym));
        Assert.Equal(NativeStableGameplayStatsDiscoveryOutcome.ContainerResolved,
            fixture.Reader.Diagnostics.LastDiscoveryReport.Outcome);
        Assert.Equal(Fixture.Container, fixture.Reader.Diagnostics.LastDiscoveryReport.ResolvedRoot);
        Assert.Equal(1, fixture.Reader.Diagnostics.DiscoveryAttempts);
        Assert.Equal(1, fixture.Reader.Diagnostics.SuccessfulDiscoveryCount);
    }

    [Fact]
    public void ZeroSignaturesFailsClosed()
    {
        Fixture fixture = Fixture.Valid();
        fixture.Memory.Clear(Fixture.Match, 9);

        Assert.Null(fixture.Reader.Read(fixture.Context, "7:generation"));

        Assert.False(fixture.Reader.Diagnostics.ReaderResolved);
        Assert.Equal(NativeStableGameplayStatsDiscoveryFailure.SignatureAbsent,
            fixture.Reader.Diagnostics.LastDiscoveryFailure);
    }

    [Fact]
    public void MultipleSignaturesFailsClosed()
    {
        Fixture fixture = Fixture.Valid();
        fixture.WriteSignature(Fixture.Match2, Fixture.Container);

        Assert.Null(fixture.Reader.Read(fixture.Context, "7:generation"));

        Assert.False(fixture.Reader.Diagnostics.ReaderResolved);
        Assert.Equal(NativeStableGameplayStatsDiscoveryFailure.MultipleSignatureMatches,
            fixture.Reader.Diagnostics.LastDiscoveryFailure);
        Assert.Equal(2, fixture.Reader.Diagnostics.LastDiscoveryReport.RawCandidateCount);
    }

    [Fact]
    public void UnreadableContainerOperandFailsClosed()
    {
        Fixture fixture = Fixture.Valid();
        fixture.Memory.FailingAddress = Fixture.Match - 0xb;

        Assert.Null(fixture.Reader.Read(fixture.Context, "7:generation"));

        Assert.False(fixture.Reader.Diagnostics.ReaderResolved);
        Assert.Equal(NativeStableGameplayStatsDiscoveryFailure.ContainerOperandUnreadable,
            fixture.Reader.Diagnostics.LastDiscoveryFailure);
    }

    [Fact]
    public void NullActiveSlotReturnsNullButKeepsResolvedContainer()
    {
        Fixture fixture = Fixture.Valid();
        fixture.Memory.WriteUInt32(Fixture.Container + 0x4, 0);

        Assert.Null(fixture.Reader.Read(fixture.Context, "7:generation"));

        Assert.True(fixture.Reader.Diagnostics.ReaderResolved);
        Assert.Equal(NativeStableGameplayStatsFailureReason.PointerChainUnavailable,
            fixture.Reader.Diagnostics.FailureReason);
        Assert.Equal(1, fixture.Reader.Diagnostics.DiscoveryAttempts);
    }

    [Fact]
    public void ActiveRulesetChangeBetweenTicksUsesNewChain()
    {
        Fixture fixture = Fixture.Valid();
        _ = fixture.Resolve();
        fixture.WriteChain(Fixture.Ruleset2, Fixture.Gameplay2, Fixture.Score2,
            Fixture.Accuracy2, Fixture.Mods2, combo: 4, n300: 7);
        fixture.Memory.WriteUInt32(Fixture.Container + 0x4, (uint)Fixture.Ruleset2);

        NativeStableGameplayStats stats = Assert.IsType<NativeStableGameplayStats>(
            fixture.Reader.ReadCached(fixture.Context, "7:generation", out _));

        Assert.Equal(4, stats.CurrentCombo);
        Assert.Equal(7, stats.Count300);
    }

    [Fact]
    public void GameplayBaseChangeBetweenTicksUsesNewChild()
    {
        Fixture fixture = Fixture.Valid();
        _ = fixture.Resolve();
        fixture.WriteGameplay(Fixture.Gameplay2, Fixture.Score2, Fixture.Accuracy2,
            Fixture.Mods2, combo: 5, n300: 8);
        fixture.Memory.WriteUInt32(Fixture.Ruleset + 0x64, (uint)Fixture.Gameplay2);

        NativeStableGameplayStats stats = Assert.IsType<NativeStableGameplayStats>(
            fixture.Reader.ReadCached(fixture.Context, "7:generation", out _));

        Assert.Equal(5, stats.CurrentCombo);
        Assert.Equal(8, stats.Count300);
    }

    [Fact]
    public void ScoreBaseChangeBetweenTicksUsesNewChild()
    {
        Fixture fixture = Fixture.Valid();
        _ = fixture.Resolve();
        fixture.WriteGameplay(Fixture.Gameplay, Fixture.Score2, Fixture.Accuracy2,
            Fixture.Mods2, combo: 6, n300: 9);

        NativeStableGameplayStats stats = Assert.IsType<NativeStableGameplayStats>(
            fixture.Reader.ReadCached(fixture.Context, "7:generation", out _));

        Assert.Equal(6, stats.CurrentCombo);
        Assert.Equal(9, stats.Count300);
    }

    [Fact]
    public void RetryRebindsChildrenWithoutContainerRediscovery()
    {
        Fixture fixture = Fixture.Valid();
        _ = fixture.Resolve();
        fixture.WriteChain(Fixture.Ruleset2, Fixture.Gameplay2, Fixture.Score2,
            Fixture.Accuracy2, Fixture.Mods2, combo: 1, n300: 1);
        fixture.Memory.WriteUInt32(Fixture.Container + 0x4, (uint)Fixture.Ruleset2);

        Assert.NotNull(fixture.Reader.Read(fixture.Context, "7:generation"));
        Assert.Equal(1, fixture.Reader.Diagnostics.DiscoveryAttempts);
        Assert.Equal(1, fixture.Reader.Diagnostics.SuccessfulDiscoveryCount);
    }

    [Fact]
    public void MapChangeRebindsChildrenWithoutContainerRediscovery()
    {
        Fixture fixture = Fixture.Valid();
        _ = fixture.Resolve();
        fixture.WriteChain(Fixture.Ruleset3, Fixture.Gameplay3, Fixture.Score3,
            Fixture.Accuracy3, Fixture.Mods3, combo: 2, n300: 2);
        fixture.Memory.WriteUInt32(Fixture.Container + 0x4, (uint)Fixture.Ruleset3);

        Assert.NotNull(fixture.Reader.Read(fixture.Context, "7:generation"));
        Assert.Equal(1, fixture.Reader.Diagnostics.DiscoveryAttempts);
        Assert.Equal(1, fixture.Reader.Diagnostics.SuccessfulDiscoveryCount);
    }

    [Fact]
    public void ResultsOrSongSelectDoesNotReturnStaleStats()
    {
        Fixture fixture = Fixture.Valid();
        _ = fixture.Resolve();
        fixture.Memory.WriteUInt32(Fixture.Container + 0x4, (uint)Fixture.RouteRuleset);
        fixture.Memory.WriteUInt32(Fixture.RouteRuleset + 0x64, 0);

        NativeStableGameplayStats? result = fixture.Reader.ReadCached(
            fixture.Context, "7:generation", out NativeStableGameplayStatsFailureReason failure);

        Assert.Null(result);
        Assert.Equal(NativeStableGameplayStatsFailureReason.PointerChainUnavailable, failure);
        Assert.Equal(1, fixture.Reader.Diagnostics.DiscoveryAttempts);
    }

    [Fact]
    public void NewProcessIdentityRediscoversContainer()
    {
        Fixture fixture = Fixture.Valid();
        _ = fixture.Resolve();
        fixture.Memory.WriteUInt32(Fixture.Container2 + 0x4, (uint)Fixture.Ruleset2);
        fixture.WriteChain(Fixture.Ruleset2, Fixture.Gameplay2, Fixture.Score2,
            Fixture.Accuracy2, Fixture.Mods2, combo: 3, n300: 3);
        fixture.Memory.WriteUInt32(Fixture.Match - 0xb, (uint)Fixture.Container2);

        Assert.NotNull(fixture.Reader.Read(fixture.Context, "8:generation"));

        Assert.Equal("8:generation", fixture.Reader.Diagnostics.ProcessIdentity);
        Assert.Equal(Fixture.Container2, fixture.Reader.Diagnostics.LastDiscoveryReport.ResolvedRoot);
        Assert.Equal(1, fixture.Reader.Diagnostics.DiscoveryAttempts);
    }

    [Fact]
    public void MultipleShapeCandidatesAreIrrelevantToContainerSelection()
    {
        Fixture fixture = Fixture.Valid();
        fixture.WriteShapeDecoy(Fixture.ShapeDecoy1, Fixture.Gameplay2);
        fixture.WriteShapeDecoy(Fixture.ShapeDecoy2, Fixture.Gameplay3);

        Assert.NotNull(fixture.Reader.Read(fixture.Context, "7:generation"));

        Assert.Equal(NativeStableGameplayStatsDiscoveryOutcome.ContainerResolved,
            fixture.Reader.Diagnostics.LastDiscoveryReport.Outcome);
        Assert.Equal(1, fixture.Reader.Diagnostics.LastDiscoveryReport.RawCandidateCount);
        Assert.Empty(fixture.Reader.Diagnostics.LastDiscoveryReport.Candidates);
    }

    [Fact]
    public void InitiallyZeroStatsStillResolveStructurally()
    {
        Fixture fixture = Fixture.Valid();
        fixture.WriteZeroGameplay(Fixture.Gameplay, Fixture.Score, Fixture.Accuracy, Fixture.Mods);

        NativeStableGameplayStats stats = Assert.IsType<NativeStableGameplayStats>(
            fixture.Reader.Read(fixture.Context, "7:generation"));

        Assert.Equal(0, stats.CurrentCombo);
        Assert.Equal(0, stats.Count300);
        Assert.Equal(0, stats.Accuracy);
        Assert.True(fixture.Reader.Diagnostics.ReaderResolved);
    }

    private sealed class Fixture
    {
        internal const nuint Match = 0x11020;
        internal const nuint Match2 = 0x11120;
        internal const nuint Container = 0x12000;
        internal const nuint Container2 = 0x12100;
        internal const nuint Ruleset = 0x13000;
        internal const nuint Gameplay = 0x14000;
        internal const nuint Score = 0x15000;
        internal const nuint Accuracy = 0x16000;
        internal const nuint Mods = 0x17000;
        internal const nuint Ruleset2 = 0x18000;
        internal const nuint Gameplay2 = 0x19000;
        internal const nuint Score2 = 0x1a000;
        internal const nuint Accuracy2 = 0x1b000;
        internal const nuint Mods2 = 0x1c000;
        internal const nuint Ruleset3 = 0x1d000;
        internal const nuint Gameplay3 = 0x1e000;
        internal const nuint Score3 = 0x1f000;
        internal const nuint Accuracy3 = 0x20000;
        internal const nuint Mods3 = 0x21000;
        internal const nuint RouteRuleset = 0x22000;
        internal const nuint ShapeDecoy1 = 0x23000;
        internal const nuint ShapeDecoy2 = 0x24000;

        internal SyntheticMemory Memory { get; } = new();
        internal NativeStableGameplayStatsReader Reader { get; } = new();
        internal NativeStableGameProcessContext Context { get; }

        private Fixture()
        {
            IReadOnlyList<NativeStableMemoryRegion> regions =
                [new(0x10000, 0x20000, 0x1000, 0x40, 0x20000, 0x10000)];
            Context = new(7, DateTimeOffset.UnixEpoch, "osu!.exe", Memory, regions, []);
        }

        internal static Fixture Valid()
        {
            var fixture = new Fixture();
            fixture.WriteSignature(Match, Container);
            fixture.Memory.WriteUInt32(Container + 0x4, (uint)Ruleset);
            fixture.WriteChain(Ruleset, Gameplay, Score, Accuracy, Mods, combo: 28, n300: 90);
            return fixture;
        }

        internal NativeStableGameplayStats Resolve() =>
            Assert.IsType<NativeStableGameplayStats>(Reader.Read(Context, "7:generation"));

        internal void WriteSignature(nuint match, nuint container)
        {
            Memory.WriteBytes(match,
                [0x7d, 0x15, 0xa1, 0x11, 0x22, 0x33, 0x44, 0x85, 0xc0]);
            Memory.WriteUInt32(match - 0xb, (uint)container);
        }

        internal void WriteChain(nuint ruleset, nuint gameplay, nuint score,
            nuint accuracy, nuint mods, ushort combo, ushort n300)
        {
            Memory.WriteUInt32(ruleset + 0x64, (uint)gameplay);
            WriteGameplay(gameplay, score, accuracy, mods, combo, n300);
        }

        internal void WriteGameplay(nuint gameplay, nuint score, nuint accuracy,
            nuint mods, ushort combo, ushort n300)
        {
            const ushort n100 = 3;
            const ushort n50 = 1;
            const ushort miss = 2;
            ushort maxCombo = Math.Max(combo, (ushort)42);
            long total = n300 + n100 + n50 + miss;
            double ratio = ((300d * n300) + (100d * n100) + (50d * n50)) /
                (300d * total);
            Memory.WriteUInt32(gameplay + 0x38, (uint)score);
            Memory.WriteUInt32(gameplay + 0x48, (uint)accuracy);
            Memory.WriteUInt32(score + 0x1c, (uint)mods);
            Memory.WriteUInt16(score + 0x68, maxCombo);
            Memory.WriteInt32(score + 0x78, 123456);
            Memory.WriteUInt16(score + 0x88, n100);
            Memory.WriteUInt16(score + 0x8a, n300);
            Memory.WriteUInt16(score + 0x8c, n50);
            Memory.WriteUInt16(score + 0x92, miss);
            Memory.WriteUInt16(score + 0x94, combo);
            Memory.WriteDouble(accuracy + 0x0c, ratio * 100d);
            Memory.WriteInt32(mods + 0x08, 0x08);
            Memory.WriteInt32(mods + 0x0c, 0x40);
        }

        internal void WriteZeroGameplay(nuint gameplay, nuint score, nuint accuracy, nuint mods)
        {
            Memory.WriteUInt32(gameplay + 0x38, (uint)score);
            Memory.WriteUInt32(gameplay + 0x48, (uint)accuracy);
            Memory.WriteUInt32(score + 0x1c, (uint)mods);
            Memory.WriteUInt16(score + 0x68, 0);
            Memory.WriteInt32(score + 0x78, 0);
            Memory.WriteUInt16(score + 0x88, 0);
            Memory.WriteUInt16(score + 0x8a, 0);
            Memory.WriteUInt16(score + 0x8c, 0);
            Memory.WriteUInt16(score + 0x92, 0);
            Memory.WriteUInt16(score + 0x94, 0);
            Memory.WriteDouble(accuracy + 0x0c, 0);
            Memory.WriteInt32(mods + 0x08, 0);
            Memory.WriteInt32(mods + 0x0c, 0);
        }

        internal void WriteShapeDecoy(nuint ruleset, nuint gameplay)
        {
            const uint shape = 0x28000;
            for (uint index = 0; index < 8; index++)
                Memory.WriteUInt32(ruleset + (index * 4), shape + (index * 0x0c));
            Memory.WriteUInt32(ruleset + 0x64, (uint)gameplay);
        }
    }

    private sealed class SyntheticMemory : INativeStableMemoryReader
    {
        private readonly Dictionary<nuint, byte> bytes = [];
        internal nuint? FailingAddress { get; set; }
        public bool Is32Bit => true;

        internal void WriteBytes(nuint address, byte[] value) => Write(address, value);
        internal void WriteUInt16(nuint address, ushort value) => Write(address, BitConverter.GetBytes(value));
        internal void WriteUInt32(nuint address, uint value) => Write(address, BitConverter.GetBytes(value));
        internal void WriteInt32(nuint address, int value) => Write(address, BitConverter.GetBytes(value));
        internal void WriteDouble(nuint address, double value) => Write(address, BitConverter.GetBytes(value));

        internal void Clear(nuint address, int count)
        {
            for (int index = 0; index < count; index++)
                bytes.Remove(address + checked((nuint)index));
        }

        public NativeStableMemoryReadResult ReadBytes(nuint address, int count)
        {
            if (FailingAddress == address)
                return NativeStableMemoryReadResult.Failed;
            var result = new byte[count];
            for (int index = 0; index < count; index++)
                bytes.TryGetValue(address + checked((nuint)index), out result[index]);
            return new(result, true, count);
        }

        public bool TryReadPointer(nuint address, out nuint value)
        {
            NativeStableMemoryReadResult read = ReadBytes(address, sizeof(uint));
            value = read.Complete ? BitConverter.ToUInt32(read.Data, 0) : 0;
            return read.Complete && value != 0;
        }

        private void Write(nuint address, byte[] value)
        {
            for (int index = 0; index < value.Length; index++)
                bytes[address + checked((nuint)index)] = value[index];
        }
    }
}
