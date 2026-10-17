using BeatInsight.Diagnostics.NativeStable;
using BeatInsight.Integration.Game;

namespace BeatInsight.Tests.Integration.Game;

public sealed class NativeStableGameplayStatsReaderTests
{
    [Fact]
    public void DiscoversValidatedRulesetAndReadsProviderNeutralStats()
    {
        Fixture fixture = Fixture.Valid();

        NativeStableGameplayStats stats = fixture.Resolve("7:generation");

        Assert.Equal(28, stats.CurrentCombo);
        Assert.Equal(42, stats.MaxComboReached);
        Assert.Equal(90, stats.Count300);
        Assert.Equal(3, stats.Count100);
        Assert.Equal(1, stats.Count50);
        Assert.Equal(2, stats.CountMiss);
        Assert.Equal(27350d / 28800d, stats.Accuracy, 6);
        Assert.Equal(["HD", "DT"], stats.Mods.Select(item => item.Acronym));
        Assert.Equal(NativeStableGameplayStatsDiscoveryState.Resolved,
            fixture.Reader.Diagnostics.DiscoveryState);
        Assert.Equal(1, fixture.Reader.Diagnostics.DiscoveryAttempts);
        Assert.Equal(1, fixture.Reader.Diagnostics.SuccessfulDiscoveryCount);
        NativeStableGameplayStatsDiscoveryReport report =
            fixture.Reader.Diagnostics.LastDiscoveryReport;
        Assert.Equal(NativeStableGameplayStatsDiscoveryOutcome.ContainerResolved,
            report.Outcome);
        Assert.Equal(1, report.RawCandidateCount);
        Assert.Equal(1, report.StructuralCandidateCount);
        Assert.Equal(1, report.ValidatedCandidateCount);
        Assert.Equal(0, report.ActiveCandidateCount);
        Assert.Equal(Fixture.Container, report.ResolvedRoot);
    }

    [Fact]
    public void ResolvedRootFollowsNewGameplayObjectWithoutRediscovery()
    {
        Fixture fixture = Fixture.Valid();
        _ = fixture.Resolve("7:generation");
        fixture.WriteGameplay(Fixture.GameplayBase2, Fixture.ScoreBase2, Fixture.AccuracyBase2,
            combo: 1, maxCombo: 1, n300: 1, n100: 0, n50: 0, miss: 0);
        fixture.Memory.WriteUInt32(Fixture.Ruleset + 0x64, (uint)Fixture.GameplayBase2);

        NativeStableGameplayStats stats = Assert.IsType<NativeStableGameplayStats>(
            fixture.Reader.Read(fixture.Context, "7:generation"));

        Assert.Equal(1, stats.CurrentCombo);
        Assert.Equal(1, stats.Count300);
        Assert.Equal(0, stats.CountMiss);
        Assert.Equal(1, fixture.Reader.Diagnostics.DiscoveryAttempts);
        Assert.Equal(1, fixture.Reader.Diagnostics.SuccessfulDiscoveryCount);
    }

    [Fact]
    public void InitialUnreadableContainerOperandRetriesAfterOneSecondAndResolves()
    {
        Fixture fixture = Fixture.Valid();
        fixture.Memory.FailingAddress = Fixture.SignatureMatch - 0xb;

        Assert.Null(fixture.Reader.Read(fixture.Context, "7:generation"));
        Assert.Equal(NativeStableGameplayStatsDiscoveryState.PendingRetry,
            fixture.Reader.Diagnostics.DiscoveryState);
        Assert.Equal(NativeStableGameplayStatsDiscoveryFailure.ContainerOperandUnreadable,
            fixture.Reader.Diagnostics.LastDiscoveryFailure);
        Assert.Equal(TimeSpan.FromSeconds(1),
            fixture.Reader.Diagnostics.NextDiscoveryRetry);

        fixture.Memory.FailingAddress = null;
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));

        Assert.NotNull(fixture.Reader.Read(fixture.Context, "7:generation"));
        Assert.Equal(NativeStableGameplayStatsDiscoveryState.Resolved,
            fixture.Reader.Diagnostics.DiscoveryState);
        Assert.Equal(2, fixture.Reader.Diagnostics.DiscoveryAttempts);
        Assert.Equal(1, fixture.Reader.Diagnostics.SuccessfulDiscoveryCount);
        Assert.Null(fixture.Reader.Diagnostics.NextDiscoveryRetry);
    }

    [Fact]
    public void RepeatedDiscoveryFailuresUseDeterministicBoundedBackoff()
    {
        Fixture fixture = Fixture.Valid();
        fixture.Memory.FailingAddress = Fixture.SignatureMatch - 0xb;

        Assert.Null(fixture.Reader.Read(fixture.Context, "7:generation"));
        Assert.Equal(TimeSpan.FromSeconds(1), fixture.Reader.Diagnostics.NextDiscoveryRetry);

        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(fixture.Reader.Read(fixture.Context, "7:generation"));
        Assert.Equal(2, fixture.Reader.Diagnostics.DiscoveryAttempts);
        Assert.Equal(TimeSpan.FromSeconds(2), fixture.Reader.Diagnostics.NextDiscoveryRetry);

        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Null(fixture.Reader.Read(fixture.Context, "7:generation"));
        Assert.Equal(3, fixture.Reader.Diagnostics.DiscoveryAttempts);
        Assert.Equal(TimeSpan.FromSeconds(5), fixture.Reader.Diagnostics.NextDiscoveryRetry);

        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Null(fixture.Reader.Read(fixture.Context, "7:generation"));
        Assert.Equal(4, fixture.Reader.Diagnostics.DiscoveryAttempts);
        Assert.Equal(TimeSpan.FromSeconds(5), fixture.Reader.Diagnostics.NextDiscoveryRetry);
    }

    [Fact]
    public void PendingRetryDoesNotDiscoverOnEveryPollingTick()
    {
        Fixture fixture = Fixture.Valid();
        fixture.Memory.FailingAddress = Fixture.SignatureMatch - 0xb;

        Assert.Null(fixture.Reader.Read(fixture.Context, "7:generation"));
        for (int tick = 0; tick < 10; tick++)
        {
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(50));
            Assert.Null(fixture.Reader.Read(fixture.Context, "7:generation"));
        }

        Assert.Equal(1, fixture.Reader.Diagnostics.DiscoveryAttempts);
        Assert.Equal(NativeStableGameplayStatsFailureReason.DiscoveryNotReady,
            fixture.Reader.Diagnostics.FailureReason);
    }

    [Fact]
    public void UnreadableContainerOperandReportsExactDiscoveryGuard()
    {
        Fixture fixture = Fixture.Valid();
        fixture.Memory.FailingAddress = Fixture.SignatureMatch - 0xb;

        Assert.Null(fixture.Reader.Read(fixture.Context, "7:generation"));

        NativeStableGameplayStatsDiscoveryReport report =
            fixture.Reader.Diagnostics.LastDiscoveryReport;
        Assert.Equal(NativeStableGameplayStatsDiscoveryOutcome.ContainerOperandUnreadable,
            report.Outcome);
        Assert.Equal(1, report.RawCandidateCount);
        Assert.Equal(0, report.StructuralCandidateCount);
        Assert.Equal(0, report.ValidatedCandidateCount);
        Assert.Empty(report.Candidates);
        Assert.Equal(NativeStableGameplayStatsDiscoveryFailure.ContainerOperandUnreadable,
            fixture.Reader.Diagnostics.LastDiscoveryFailure);
    }

    [Fact]
    public void AdditionalShapeCompatibleRootDoesNotAffectContainerSelection()
    {
        Fixture fixture = Fixture.Valid();
        fixture.AddRuleset(Fixture.Ruleset2, Fixture.ShapeBase2, Fixture.GameplayBase);

        NativeStableGameplayStats stats = Assert.IsType<NativeStableGameplayStats>(
            fixture.Reader.Read(fixture.Context, "7:generation"));

        NativeStableGameplayStatsDiscoveryReport report =
            fixture.Reader.Diagnostics.LastDiscoveryReport;
        Assert.Equal(NativeStableGameplayStatsDiscoveryOutcome.ContainerResolved,
            report.Outcome);
        Assert.Equal(Fixture.Container, report.ResolvedRoot);
        Assert.Empty(report.Candidates);
        Assert.Equal(28, stats.CurrentCombo);
        Assert.True(fixture.Reader.Diagnostics.ReaderResolved);
    }

    [Fact]
    public void TwentyFourShapeCompatibleDecoysDoNotTriggerHeuristicSelection()
    {
        Fixture fixture = Fixture.Valid();
        fixture.WriteInactiveGameplay(
            Fixture.InactiveGameplayBase,
            Fixture.InactiveScoreBase,
            Fixture.InactiveAccuracyBase,
            Fixture.InactiveModsBase);
        for (int index = 0; index < 23; index++)
        {
            fixture.AddRuleset(
                0x10200u + checked((nuint)(index * 0x100)),
                0x22000u + checked((nuint)(index * 0x100)),
                Fixture.InactiveGameplayBase);
        }

        NativeStableGameplayStats stats = Assert.IsType<NativeStableGameplayStats>(
            fixture.Reader.Read(fixture.Context, "7:generation"));

        NativeStableGameplayStatsDiscoveryReport report =
            fixture.Reader.Diagnostics.LastDiscoveryReport;
        Assert.Equal(NativeStableGameplayStatsDiscoveryOutcome.ContainerResolved,
            report.Outcome);
        Assert.Equal(1, report.RawCandidateCount);
        Assert.Equal(1, report.StructuralCandidateCount);
        Assert.Equal(1, report.ValidatedCandidateCount);
        Assert.Equal(0, report.ActiveCandidateCount);
        Assert.Equal(Fixture.Container, report.ResolvedRoot);
        Assert.Empty(report.Candidates);
        Assert.Equal(28, stats.CurrentCombo);
    }

    [Fact]
    public void StructurallySelectedContainerPublishesCoherentFrozenZeroCounters()
    {
        Fixture fixture = Fixture.Valid();
        fixture.WriteInactiveGameplay(
            Fixture.GameplayBase,
            Fixture.ScoreBase,
            Fixture.AccuracyBase,
            Fixture.ModsBase);

        NativeStableGameplayStats stats = Assert.IsType<NativeStableGameplayStats>(
            fixture.Reader.Read(fixture.Context, "7:generation"));

        NativeStableGameplayStatsDiscoveryReport report =
            fixture.Reader.Diagnostics.LastDiscoveryReport;
        Assert.Equal(NativeStableGameplayStatsDiscoveryOutcome.ContainerResolved,
            report.Outcome);
        Assert.Equal(1, report.ValidatedCandidateCount);
        Assert.Equal(0, report.ActiveCandidateCount);
        Assert.Equal(0, stats.Count300);
        Assert.Equal(0, stats.Score);
        Assert.True(fixture.Reader.Diagnostics.ReaderResolved);
    }

    [Fact]
    public void TransientCachedReadFailureKeepsResolvedRootAndDoesNotRediscover()
    {
        Fixture fixture = Fixture.Valid();
        _ = fixture.Resolve("7:generation");
        Assert.Equal(1, fixture.Reader.Diagnostics.DiscoveryAttempts);

        fixture.Memory.FailingAddress = Fixture.ScoreBase;
        Assert.Null(fixture.Reader.Read(fixture.Context, "7:generation"));
        Assert.True(fixture.Reader.Diagnostics.ReaderResolved);
        Assert.Equal(NativeStableGameplayStatsDiscoveryState.Resolved,
            fixture.Reader.Diagnostics.DiscoveryState);
        Assert.Equal(NativeStableGameplayStatsFailureReason.ScoreReadIncomplete,
            fixture.Reader.Diagnostics.FailureReason);
        Assert.Equal(1, fixture.Reader.Diagnostics.DiscoveryAttempts);

        fixture.Memory.FailingAddress = null;
        Assert.NotNull(fixture.Reader.ReadCached(
            fixture.Context,
            "7:generation",
            out NativeStableGameplayStatsFailureReason failureReason));
        Assert.Equal(NativeStableGameplayStatsFailureReason.None, failureReason);
        Assert.Equal(1, fixture.Reader.Diagnostics.DiscoveryAttempts);
    }

    [Fact]
    public void TransientAccuracyMismatchRetriesFreshReadWithoutRediscovery()
    {
        Fixture fixture = Fixture.Valid();
        _ = fixture.Resolve("7:generation");
        double coherentAccuracy = (27350d / 28800d) * 100d;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            fixture.Memory.QueueRead(
                Fixture.AccuracyBase + 0x0c,
                BitConverter.GetBytes(25d));
        }
        fixture.Memory.QueueRead(
            Fixture.AccuracyBase + 0x0c,
            BitConverter.GetBytes(coherentAccuracy));

        NativeStableGameplayStats stats = Assert.IsType<NativeStableGameplayStats>(
            fixture.Reader.Read(fixture.Context, "7:generation"));

        Assert.Equal(27350d / 28800d, stats.Accuracy, 6);
        Assert.Equal(1, fixture.Reader.Diagnostics.DiscoveryAttempts);
        Assert.True(fixture.Reader.Diagnostics.ReaderResolved);
    }

    [Fact]
    public void AccuracyUpdateDelayedWithinOneFrameRecoversWithoutRediscovery()
    {
        Fixture fixture = Fixture.Valid();
        _ = fixture.Resolve("7:generation");
        double coherentAccuracy = (27350d / 28800d) * 100d;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            fixture.Memory.QueueRead(
                Fixture.AccuracyBase + 0x0c,
                BitConverter.GetBytes(25d));
        }
        fixture.Memory.QueueRead(
            Fixture.AccuracyBase + 0x0c,
            BitConverter.GetBytes(coherentAccuracy));

        NativeStableGameplayStats stats = Assert.IsType<NativeStableGameplayStats>(
            fixture.Reader.Read(fixture.Context, "7:generation"));

        Assert.Equal(27350d / 28800d, stats.Accuracy, 6);
        Assert.Equal(1, fixture.Reader.Diagnostics.DiscoveryAttempts);
        Assert.Equal(1, fixture.Reader.Diagnostics.SuccessfulDiscoveryCount);
        Assert.True(fixture.Reader.Diagnostics.ReaderResolved);
    }

    [Fact]
    public void PersistentAccuracyMismatchStillFailsClosedWithoutRediscovery()
    {
        Fixture fixture = Fixture.Valid();
        _ = fixture.Resolve("7:generation");
        fixture.Memory.WriteDouble(Fixture.AccuracyBase + 0x0c, 0.25);

        Assert.Null(fixture.Reader.Read(fixture.Context, "7:generation"));

        Assert.Equal(NativeStableGameplayStatsFailureReason.AccuracyMismatch,
            fixture.Reader.Diagnostics.FailureReason);
        Assert.Equal(1, fixture.Reader.Diagnostics.DiscoveryAttempts);
        Assert.True(fixture.Reader.Diagnostics.ReaderResolved);
    }

    [Fact]
    public void InitiallyInvalidLiveValuesFailClosedThenRecoverWithoutRediscovery()
    {
        Fixture fixture = Fixture.Valid();
        fixture.Memory.WriteDouble(Fixture.AccuracyBase + 0x0c, 0.25);

        Assert.Null(fixture.Reader.Read(fixture.Context, "7:generation"));
        Assert.True(fixture.Reader.Diagnostics.ReaderResolved);
        Assert.Equal(1, fixture.Reader.Diagnostics.DiscoveryAttempts);
        Assert.Equal(NativeStableGameplayStatsFailureReason.AccuracyMismatch,
            fixture.Reader.Diagnostics.FailureReason);

        fixture.WriteGameplay(Fixture.GameplayBase, Fixture.ScoreBase, Fixture.AccuracyBase,
            combo: 28, maxCombo: 42, n300: 90, n100: 3, n50: 1, miss: 2);
        Assert.NotNull(fixture.Reader.Read(fixture.Context, "7:generation"));
        Assert.True(fixture.Reader.Diagnostics.ReaderResolved);
        Assert.Equal(1, fixture.Reader.Diagnostics.DiscoveryAttempts);
    }

    [Fact]
    public void InvalidAccuracyFailsClosed()
    {
        Fixture fixture = Fixture.Valid();
        fixture.Memory.WriteDouble(Fixture.AccuracyBase + 0x0c, 0.25);
        Assert.Null(fixture.Reader.Read(fixture.Context, "7:generation"));
        Assert.True(fixture.Reader.Diagnostics.ReaderResolved);
        Assert.Equal(NativeStableGameplayStatsDiscoveryFailure.None,
            fixture.Reader.Diagnostics.LastDiscoveryFailure);
        Assert.Equal(NativeStableGameplayStatsFailureReason.AccuracyMismatch,
            fixture.Reader.Diagnostics.FailureReason);
    }

    [Fact]
    public void PendingRetryReturnsNullWithoutPublishingStaleStats()
    {
        Fixture fixture = Fixture.Valid();
        _ = fixture.Resolve("7:first");

        fixture.Memory.FailingAddress = Fixture.SignatureMatch - 0xb;
        Assert.Null(fixture.Reader.Read(fixture.Context, "7:second"));
        Assert.Null(fixture.Reader.ReadCached(
            fixture.Context,
            "7:second",
            out NativeStableGameplayStatsFailureReason failureReason));

        Assert.Equal(NativeStableGameplayStatsFailureReason.DiscoveryNotReady,
            failureReason);
        Assert.False(fixture.Reader.Diagnostics.ReaderResolved);
        Assert.Equal(NativeStableGameplayStatsDiscoveryState.PendingRetry,
            fixture.Reader.Diagnostics.DiscoveryState);
        Assert.Equal(1, fixture.Reader.Diagnostics.DiscoveryAttempts);
        Assert.Equal(0, fixture.Reader.Diagnostics.SuccessfulDiscoveryCount);
    }

    [Fact]
    public void NewProcessIdentityResetsDiscoveryStateAndAttempts()
    {
        Fixture fixture = Fixture.Valid();
        _ = fixture.Resolve("7:first");

        _ = fixture.Resolve("8:second");

        Assert.Equal("8:second", fixture.Reader.Diagnostics.ProcessIdentity);
        Assert.Equal(NativeStableGameplayStatsDiscoveryState.Resolved,
            fixture.Reader.Diagnostics.DiscoveryState);
        Assert.Equal(1, fixture.Reader.Diagnostics.DiscoveryAttempts);
        Assert.Equal(1, fixture.Reader.Diagnostics.SuccessfulDiscoveryCount);
    }

    [Fact]
    public void ZeroInitialSampleResolvesWithoutTemporalObservation()
    {
        Fixture fixture = Fixture.Valid();
        fixture.WriteZeroGameplay(
            Fixture.GameplayBase,
            Fixture.ScoreBase,
            Fixture.AccuracyBase,
            Fixture.ModsBase);

        NativeStableGameplayStats stats = Assert.IsType<NativeStableGameplayStats>(
            fixture.Reader.Read(fixture.Context, "7:generation"));
        Assert.Equal(0, stats.CurrentCombo);
        Assert.Equal(0, stats.Score);
        Assert.Equal(NativeStableGameplayStatsDiscoveryState.Resolved,
            fixture.Reader.Diagnostics.DiscoveryState);
        Assert.Empty(fixture.Reader.Diagnostics.LastDiscoveryReport.Candidates);
    }

    [Fact]
    public void FrozenNonZeroSampleResolvesStructurallyWithoutTemporalGate()
    {
        Fixture fixture = Fixture.Valid();

        Assert.NotNull(fixture.Reader.Read(fixture.Context, "7:generation"));
        for (int sample = 0; sample < 10; sample++)
        {
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(100));
            Assert.NotNull(fixture.Reader.Read(fixture.Context, "7:generation"));
        }

        Assert.True(fixture.Reader.Diagnostics.ReaderResolved);
        Assert.Equal(NativeStableGameplayStatsDiscoveryFailure.None,
            fixture.Reader.Diagnostics.LastDiscoveryFailure);
        Assert.Empty(fixture.Reader.Diagnostics.LastDiscoveryReport.Candidates);
        Assert.Equal(1, fixture.Reader.Diagnostics.DiscoveryAttempts);
    }

    [Fact]
    public void ContainerChainWinsOverProgressingOrFrozenShapeDecoys()
    {
        Fixture fixture = Fixture.Valid();
        fixture.WriteZeroGameplay(
            Fixture.GameplayBase,
            Fixture.ScoreBase,
            Fixture.AccuracyBase,
            Fixture.ModsBase);
        fixture.AddRuleset(Fixture.Ruleset2, Fixture.ShapeBase2, Fixture.InactiveGameplayBase);
        fixture.WriteGameplay(
            Fixture.InactiveGameplayBase,
            Fixture.InactiveScoreBase,
            Fixture.InactiveAccuracyBase,
            combo: 28, maxCombo: 42, n300: 90, n100: 3, n50: 1, miss: 2);

        NativeStableGameplayStats stats = Assert.IsType<NativeStableGameplayStats>(
            fixture.Reader.Read(fixture.Context, "7:generation"));
        Assert.Equal(0, stats.CurrentCombo);
        Assert.Equal(Fixture.Container,
            fixture.Reader.Diagnostics.LastDiscoveryReport.ResolvedRoot);
        Assert.Equal(0, fixture.Reader.Diagnostics.LastDiscoveryReport.ActiveCandidateCount);
    }

    [Fact]
    public void AbsurdStaleDecoyWithManiaKeyModIsRejectedAsNotPlausible()
    {
        Fixture fixture = Fixture.Valid();
        fixture.Memory.WriteUInt16(Fixture.ScoreBase + 0x68, 57538);
        fixture.Memory.WriteInt32(Fixture.ScoreBase + 0x78, 1_145_569_280);
        fixture.Memory.WriteUInt16(Fixture.ScoreBase + 0x88, 0);
        fixture.Memory.WriteUInt16(Fixture.ScoreBase + 0x8a, 0);
        fixture.Memory.WriteUInt16(Fixture.ScoreBase + 0x8c, 0);
        fixture.Memory.WriteUInt16(Fixture.ScoreBase + 0x92, 17429);
        fixture.Memory.WriteUInt16(Fixture.ScoreBase + 0x94, 19968);
        fixture.Memory.WriteDouble(Fixture.AccuracyBase + 0x0c, 0);
        fixture.Memory.WriteInt32(
            Fixture.ModsBase + 0x08,
            (1 << 0) | (1 << 3) | (1 << 8) | (1 << 17));
        fixture.Memory.WriteInt32(Fixture.ModsBase + 0x0c, 0);

        Assert.Null(fixture.Reader.Read(fixture.Context, "7:generation"));

        Assert.Equal(NativeStableGameplayStatsFailureReason.ModsUnsupportedForStandard,
            fixture.Reader.Diagnostics.FailureReason);
        Assert.Empty(fixture.Reader.Diagnostics.LastDiscoveryReport.Candidates);
        Assert.True(fixture.Reader.Diagnostics.ReaderResolved);
    }

    [Fact]
    public void ChildChainChangeIsFollowedImmediatelyWithoutRediscovery()
    {
        Fixture fixture = Fixture.Valid();
        fixture.WriteZeroGameplay(
            Fixture.GameplayBase,
            Fixture.ScoreBase,
            Fixture.AccuracyBase,
            Fixture.ModsBase);

        Assert.NotNull(fixture.Reader.Read(fixture.Context, "7:generation"));
        fixture.WriteZeroGameplay(
            Fixture.GameplayBase2,
            Fixture.ScoreBase2,
            Fixture.AccuracyBase2,
            Fixture.ModsBase);
        fixture.Memory.WriteUInt32(Fixture.Ruleset + 0x64, (uint)Fixture.GameplayBase2);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(100));
        NativeStableGameplayStats zero = Assert.IsType<NativeStableGameplayStats>(
            fixture.Reader.Read(fixture.Context, "7:generation"));
        Assert.Equal(0, zero.CurrentCombo);

        fixture.WriteGameplay(Fixture.GameplayBase2, Fixture.ScoreBase2, Fixture.AccuracyBase2,
            combo: 1, maxCombo: 1, n300: 1, n100: 0, n50: 0, miss: 0);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(900));
        Assert.NotNull(fixture.Reader.Read(fixture.Context, "7:generation"));
        Assert.Equal(1, fixture.Reader.Diagnostics.DiscoveryAttempts);
    }

    [Fact]
    public void TransientReadFailureDoesNotDiscardResolvedContainer()
    {
        Fixture fixture = Fixture.Valid();
        fixture.WriteZeroGameplay(
            Fixture.GameplayBase,
            Fixture.ScoreBase,
            Fixture.AccuracyBase,
            Fixture.ModsBase);

        Assert.NotNull(fixture.Reader.Read(fixture.Context, "7:generation"));
        fixture.Memory.FailingAddress = Fixture.ScoreBase;
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Null(fixture.Reader.Read(fixture.Context, "7:generation"));
        Assert.True(fixture.Reader.Diagnostics.ReaderResolved);
        Assert.Equal(NativeStableGameplayStatsFailureReason.ScoreReadIncomplete,
            fixture.Reader.Diagnostics.FailureReason);

        fixture.Memory.FailingAddress = null;
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.NotNull(fixture.Reader.Read(fixture.Context, "7:generation"));
        fixture.WriteGameplay(Fixture.GameplayBase, Fixture.ScoreBase, Fixture.AccuracyBase,
            combo: 1, maxCombo: 1, n300: 1, n100: 0, n50: 0, miss: 0);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(800));

        Assert.NotNull(fixture.Reader.Read(fixture.Context, "7:generation"));
        Assert.Equal(1, fixture.Reader.Diagnostics.DiscoveryAttempts);
    }

    private sealed class Fixture
    {
        internal const nuint Ruleset = 0x10100;
        internal const nuint Ruleset2 = 0x10200;
        internal const nuint ShapeBase = 0x12000;
        internal const nuint ShapeBase2 = 0x1a000;
        internal const nuint GameplayBase = 0x13100;
        internal const nuint ScoreBase = 0x14000;
        internal const nuint AccuracyBase = 0x15000;
        internal const nuint ModsBase = 0x16000;
        internal const nuint GameplayBase2 = 0x17100;
        internal const nuint ScoreBase2 = 0x18000;
        internal const nuint AccuracyBase2 = 0x19000;
        internal const nuint InactiveGameplayBase = 0x1b000;
        internal const nuint InactiveScoreBase = 0x1c000;
        internal const nuint InactiveAccuracyBase = 0x1d000;
        internal const nuint InactiveModsBase = 0x1e000;
        internal const nuint Container = 0x1f000;
        internal const nuint SignatureMatch = 0x1f120;

        internal SyntheticMemory Memory { get; } = new();
        internal ManualClock Clock { get; } = new();
        internal NativeStableGameplayStatsReader Reader { get; }
        internal NativeStableGameProcessContext Context { get; }
        private int score = 123456;

        private Fixture()
        {
            Reader = new(Clock.GetTimestamp, 1000);
            IReadOnlyList<NativeStableMemoryRegion> regions =
                [new(0x10000, 0x10000, 0x1000, 0x40, 0x20000, 0x10000)];
            Context = new(7, DateTimeOffset.UnixEpoch, "osu!.exe", Memory, regions, []);
        }

        internal static Fixture Valid()
        {
            var fixture = new Fixture();
            fixture.Memory.WriteBytes(SignatureMatch,
                [0x7d, 0x15, 0xa1, 0x11, 0x22, 0x33, 0x44, 0x85, 0xc0]);
            fixture.Memory.WriteUInt32(SignatureMatch - 0xb, (uint)Container);
            fixture.Memory.WriteUInt32(Container + 0x4, (uint)Ruleset);
            fixture.AddRuleset(Ruleset, ShapeBase, GameplayBase);
            fixture.WriteGameplay(GameplayBase, ScoreBase, AccuracyBase,
                combo: 28, maxCombo: 42, n300: 90, n100: 3, n50: 1, miss: 2);
            return fixture;
        }

        internal NativeStableGameplayStats Resolve(string processIdentity)
        {
            return Assert.IsType<NativeStableGameplayStats>(
                Reader.Read(Context, processIdentity));
        }

        internal void AdvancePrimary()
        {
            score += 300;
            Memory.WriteInt32(ScoreBase + 0x78, score);
        }

        internal void AddRuleset(nuint ruleset, nuint shapeBase, nuint gameplayBase)
        {
            for (uint index = 0; index < 8; index++)
            {
                Memory.WriteUInt32(
                    ruleset + (index * 4),
                    (uint)shapeBase + (index * 0x0c));
            }
            Memory.WriteUInt32(ruleset + 0x64, (uint)gameplayBase);
        }

        internal void WriteGameplay(nuint gameplay, nuint score, nuint accuracy,
            ushort combo, ushort maxCombo, ushort n300, ushort n100, ushort n50, ushort miss)
        {
            long total = n300 + n100 + n50 + miss;
            double ratio = ((300d * n300) + (100d * n100) + (50d * n50)) / (300d * total);
            Memory.WriteUInt32(gameplay + 0x38, (uint)score);
            Memory.WriteUInt32(gameplay + 0x48, (uint)accuracy);
            Memory.WriteUInt32(score + 0x1c, (uint)ModsBase);
            Memory.WriteUInt16(score + 0x68, maxCombo);
            Memory.WriteInt32(score + 0x78, this.score);
            Memory.WriteUInt16(score + 0x88, n100);
            Memory.WriteUInt16(score + 0x8a, n300);
            Memory.WriteUInt16(score + 0x8c, n50);
            Memory.WriteUInt16(score + 0x92, miss);
            Memory.WriteUInt16(score + 0x94, combo);
            Memory.WriteDouble(accuracy + 0x0c, ratio * 100d);
            Memory.WriteInt32(ModsBase + 0x08, 0x08);
            Memory.WriteInt32(ModsBase + 0x0c, 0x40);
        }

        internal void WriteInactiveGameplay(
            nuint gameplay,
            nuint score,
            nuint accuracy,
            nuint mods)
        {
            Memory.WriteUInt32(gameplay + 0x38, (uint)score);
            Memory.WriteUInt32(gameplay + 0x48, (uint)accuracy);
            Memory.WriteUInt32(score + 0x1c, (uint)mods);
            Memory.WriteUInt16(score + 0x68, 3212);
            Memory.WriteInt32(score + 0x78, 0);
            Memory.WriteUInt16(score + 0x88, 0);
            Memory.WriteUInt16(score + 0x8a, 0);
            Memory.WriteUInt16(score + 0x8c, 0);
            Memory.WriteUInt16(score + 0x92, 0);
            Memory.WriteUInt16(score + 0x94, 0);
            Memory.WriteDouble(accuracy + 0x0c, 0);
            Memory.WriteInt32(mods + 0x08, 0x08);
            Memory.WriteInt32(mods + 0x0c, 0x40);
        }

        internal void WriteZeroGameplay(
            nuint gameplay,
            nuint score,
            nuint accuracy,
            nuint mods)
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
            Memory.WriteDouble(accuracy + 0x0c, 100d);
            Memory.WriteInt32(mods + 0x08, 0x08);
            Memory.WriteInt32(mods + 0x0c, 0x40);
        }
    }

    private sealed class ManualClock
    {
        private long timestamp;

        internal long GetTimestamp() => timestamp;

        internal void Advance(TimeSpan duration) =>
            timestamp += checked((long)duration.TotalMilliseconds);
    }

    private sealed class SyntheticMemory : INativeStableMemoryReader
    {
        private readonly Dictionary<nuint, byte> bytes = [];
        private readonly Dictionary<nuint, Queue<byte[]>> queuedReads = [];
        internal nuint? FailingAddress { get; set; }
        public bool Is32Bit => true;
        internal void WriteBytes(nuint address, byte[] value) => Write(address, value);
        internal void WriteUInt16(nuint address, ushort value) => Write(address, BitConverter.GetBytes(value));
        internal void WriteUInt32(nuint address, uint value) => Write(address, BitConverter.GetBytes(value));
        internal void WriteInt32(nuint address, int value) => Write(address, BitConverter.GetBytes(value));
        internal void WriteDouble(nuint address, double value) => Write(address, BitConverter.GetBytes(value));
        internal void QueueRead(nuint address, byte[] value)
        {
            if (!queuedReads.TryGetValue(address, out Queue<byte[]>? queue))
            {
                queue = new();
                queuedReads[address] = queue;
            }
            queue.Enqueue(value);
        }
        public NativeStableMemoryReadResult ReadBytes(nuint address, int count)
        {
            if (FailingAddress == address) return NativeStableMemoryReadResult.Failed;
            if (queuedReads.TryGetValue(address, out Queue<byte[]>? queue) &&
                queue.Count > 0)
            {
                byte[] queued = queue.Dequeue();
                return queued.Length == count
                    ? new(queued, true, count)
                    : NativeStableMemoryReadResult.Failed;
            }
            byte[] result = new byte[count];
            for (int index = 0; index < count; index++)
                bytes.TryGetValue(address + checked((nuint)index), out result[index]);
            return new(result, true, count);
        }
        public bool TryReadPointer(nuint address, out nuint value)
        {
            NativeStableMemoryReadResult read = ReadBytes(address, 4);
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
