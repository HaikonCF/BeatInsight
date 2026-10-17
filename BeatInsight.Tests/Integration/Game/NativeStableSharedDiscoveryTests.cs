using BeatInsight.Diagnostics.NativeStable;
using BeatInsight.Integration.Game;
using Xunit;

namespace BeatInsight.Tests.Integration.Game;

/// <summary>
/// V3.14-COM-KEY-05 : exposition en lecture seule de la découverte osu!stable du provider.
/// Ne publie jamais un conteneur non résolu, ni un conteneur d'une autre génération de processus.
/// </summary>
public class NativeStableSharedDiscoveryTests
{
    private const int Pid = 4242;
    private static readonly DateTimeOffset Start = new(2026, 10, 15, 14, 0, 31, TimeSpan.Zero);
    private static string Identity(int pid, DateTimeOffset start) =>
        $"{pid}:{start.UtcDateTime:O}";

    [Fact]
    public void Nothing_is_shared_before_discovery()
    {
        var live = new FakeLive();
        var stats = new FakeStats();
        Assert.Null(Shared(live, stats));
    }

    [Fact]
    public void Live_signatures_alone_are_not_shared_without_a_resolved_container()
    {
        var live = new FakeLive { Addresses = new(0x1000, 0x2000, 0x3000), ProcessId = Pid, Start = Start };
        var stats = new FakeStats { Diag = Diag(Identity(Pid, Start), NativeStableGameplayStatsDiscoveryState.PendingRetry, 0) };
        Assert.Null(Shared(live, stats));
        stats.Diag = Diag(Identity(Pid, Start), NativeStableGameplayStatsDiscoveryState.ObservingCandidates, 0x04C343CC);
        Assert.Null(Shared(live, stats)); // adresse présente mais pas « Resolved » : jamais publiée
    }

    [Fact]
    public void Container_of_another_process_generation_is_never_shared()
    {
        var live = new FakeLive { Addresses = new(0x1000, 0x2000, 0x3000), ProcessId = Pid, Start = Start };
        var stats = new FakeStats { Diag = Diag(Identity(Pid, Start.AddSeconds(-90)), NativeStableGameplayStatsDiscoveryState.Resolved, 0x04C343CC) };
        Assert.Null(Shared(live, stats)); // ancienne instance d'osu! (même PID, autre démarrage)
    }

    [Fact]
    public void Resolved_and_coherent_discovery_is_shared_with_its_generation()
    {
        var live = new FakeLive { Addresses = new(0x1000, 0x2000, 0x3000), ProcessId = Pid, Start = Start };
        var stats = new FakeStats { Diag = Diag(Identity(Pid, Start), NativeStableGameplayStatsDiscoveryState.Resolved, 0x04C343CC, successes: 1) };

        NativeStableSharedDiscovery? shared = Shared(live, stats, out var provider);

        Assert.NotNull(shared);
        Assert.Equal(Identity(Pid, Start), shared!.ProcessIdentity);
        Assert.Equal((nuint)0x1000, shared.StatusValueAddress);
        Assert.Equal((nuint)0x2000, shared.PlayTimeValueAddress);
        Assert.Equal((nuint)0x04C343CC, shared.ContainerAddress);
        Assert.Equal(1, shared.ContainerDiscoveryGeneration);
        Assert.Equal(0, live.Scans); // l'exposition ne déclenche aucune découverte

        // Même génération : horodatage de validation stable.
        DateTimeOffset first = shared.ContainerValidatedAtUtc;
        Assert.Equal(first, ((INativeStableSharedDiscoveryProvider)provider).TryGetSharedDiscovery()!.ContainerValidatedAtUtc);
    }

    [Fact]
    public void Disposed_provider_shares_nothing()
    {
        var live = new FakeLive { Addresses = new(0x1000, 0x2000, 0x3000), ProcessId = Pid, Start = Start };
        var stats = new FakeStats { Diag = Diag(Identity(Pid, Start), NativeStableGameplayStatsDiscoveryState.Resolved, 0x04C343CC) };
        _ = Shared(live, stats, out var provider);
        provider.Dispose();
        Assert.Null(((INativeStableSharedDiscoveryProvider)provider).TryGetSharedDiscovery());
    }

    [Fact]
    public void Live_state_reader_reports_nothing_until_discovered_and_never_scans_on_query()
    {
        var reader = new NativeStableLiveStateReader(new ThrowingDiscovery());
        Assert.False(reader.TryGetDiscovered(out var addresses, out _, out _));
        Assert.Null(addresses);
    }

    [Fact]
    public void Game_state_service_forwards_shared_discovery_without_requiring_selection()
    {
        var live = new FakeLive { Addresses = new(0x1000, 0x2000, 0x3000), ProcessId = Pid, Start = Start };
        var stats = new FakeStats { Diag = Diag(Identity(Pid, Start), NativeStableGameplayStatsDiscoveryState.Resolved, 0x04C343CC) };
        using var provider = Provider(live, stats);
        using var service = new GameStateService(provider);
        Assert.True(service.HasSharedStableDiscoveryProvider);
        Assert.Equal((nuint)0x04C343CC, service.TryGetSharedStableDiscovery()!.ContainerAddress);
    }

    private static NativeStableSharedDiscovery? Shared(FakeLive live, FakeStats stats) => Shared(live, stats, out _);

    private static NativeStableSharedDiscovery? Shared(FakeLive live, FakeStats stats, out NativeStableGameProvider provider)
    {
        provider = Provider(live, stats);
        return ((INativeStableSharedDiscoveryProvider)provider).TryGetSharedDiscovery();
    }

    private static NativeStableGameProvider Provider(FakeLive live, FakeStats stats) =>
        new(new UnusedRuntime(), new UnusedLocator(), live, new NativeStableBeatmapIdentityReader(), stats, null);

    private static NativeStableGameplayStatsReaderDiagnostics Diag(
        string? identity, NativeStableGameplayStatsDiscoveryState state, nuint container, int successes = 1) =>
        new(identity, state, container != 0, NativeStableGameplayStatsFailureReason.None, 1, successes,
            NativeStableGameplayStatsDiscoveryFailure.None, NativeStableGameplayStatsDiscoveryReport.NotAttempted,
            null, null, null, container);

    private sealed class FakeLive : INativeStableLiveStateReader
    {
        public NativeStableLivePatternAddresses? Addresses;
        public int ProcessId;
        public DateTimeOffset Start;
        public int Scans;

        public bool EnsureDiscovered(NativeStableGameProcessContext context) { Scans++; return Addresses is not null; }
        public int? ReadStatus(NativeStableGameProcessContext context) => null;
        public int? ReadPlayTime(NativeStableGameProcessContext context) => null;
        public void Invalidate() => Addresses = null;

        public bool TryGetDiscovered(out NativeStableLivePatternAddresses? addresses, out int processId, out DateTimeOffset start)
        {
            addresses = Addresses;
            processId = ProcessId;
            start = Start;
            return Addresses is not null;
        }
    }

    private sealed class FakeStats : INativeStableGameplayStatsReader
    {
        public NativeStableGameplayStatsReaderDiagnostics Diag = new(
            null, NativeStableGameplayStatsDiscoveryState.NotAttempted, false, NativeStableGameplayStatsFailureReason.DiscoveryNotReady,
            0, 0, NativeStableGameplayStatsDiscoveryFailure.None, NativeStableGameplayStatsDiscoveryReport.NotAttempted, null, null, null);

        public void ObserveProcessIdentity(string processIdentity) { }
        public NativeStableGameplayStats? Read(NativeStableGameProcessContext context, string processIdentity) => throw new InvalidOperationException("scan interdit");
        public NativeStableGameplayStats? ReadCached(NativeStableGameProcessContext context, string processIdentity, out NativeStableGameplayStatsFailureReason failureReason) =>
            throw new InvalidOperationException("lecture interdite");
        public NativeStableGameplayStatsReaderDiagnostics Diagnostics => Diag;
        public void Invalidate() { }
    }

    private sealed class ThrowingDiscovery : INativeStableLivePatternDiscovery
    {
        public NativeStableLivePatternAddresses? Discover(NativeStableGameProcessContext context) =>
            throw new InvalidOperationException("scan interdit");
    }

    private sealed class UnusedRuntime : INativeStableGameRuntime
    {
        public NativeStableGameProcessContext? TryOpen() => null;
        public string? TryResolveBeatmapPath(NativeStableGameProcessContext context, string folder, string filename) => null;
    }

    private sealed class UnusedLocator : INativeStableGameLocator
    {
        public NativeStableGameLocatorOutcome Locate(NativeStableGameProcessContext context, NativeStableAutonomousQProfile profile) =>
            throw new InvalidOperationException();
    }
}
