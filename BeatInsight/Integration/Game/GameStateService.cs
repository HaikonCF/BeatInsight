using System.Collections.Immutable;
using BeatInsight.Models.AnalysisCache;

namespace BeatInsight.Integration.Game;

public sealed class GameStateService : IDisposable
{
    private const string ServiceName = nameof(GameStateService);
    private readonly object refreshGate = new();
    private readonly IGameStateProvider[] providers;
    private IGameplayPositionProvider? gameplayPositionProvider;
    private IGameRouteProvider? gameRouteProvider;
    private ILiveGameplayStatsProvider? liveGameplayStatsProvider;
    private GameStateSnapshot current = Unavailable(
        DateTimeOffset.MinValue,
        "Refresh has not run.",
        []);
    private int lifecycleState;

    public GameStateService(IGameStateProvider provider)
        : this([provider ?? throw new ArgumentNullException(nameof(provider))])
    {
    }

    public GameStateService(IEnumerable<IGameStateProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        this.providers = providers.ToArray();
        if (this.providers.Any(provider => provider is null))
        {
            throw new ArgumentException("Providers cannot contain null entries.", nameof(providers));
        }
    }

    public GameStateSnapshot Current => Volatile.Read(ref current);

    public GameStateSnapshot Refresh()
    {
        if (Volatile.Read(ref lifecycleState) != 0)
            return Current;

        lock (refreshGate)
        {
            if (Volatile.Read(ref lifecycleState) != 0)
                return Current;

            var attempts = ImmutableArray.CreateBuilder<ProviderAttempt>(providers.Length);
            foreach (IGameStateProvider provider in providers)
            {
                string providerName = SafeName(provider);
                GameStateSnapshot candidate;
                try
                {
                    candidate = provider.Refresh();
                    if (Volatile.Read(ref lifecycleState) != 0)
                        return Current;
                    if (candidate is null)
                    {
                        attempts.Add(new(
                            providerName,
                            GameStateAvailability.Error,
                            "Provider returned no snapshot."));
                        continue;
                    }
                }
                catch (Exception exception)
                {
                    attempts.Add(new(
                        providerName,
                        GameStateAvailability.Error,
                        $"Provider threw {exception.GetType().Name}."));
                    continue;
                }

                attempts.Add(new(
                    providerName,
                    candidate.Availability,
                    candidate.DiagnosticReason,
                    candidate.ClientName,
                    candidate.ProcessId,
                    candidate.ProcessIdentity));

                if (candidate.IsAvailable)
                {
                    GameStateSnapshot selected = candidate with
                    {
                        ProviderAttempts = attempts.ToImmutable()
                    };
                    Volatile.Write(ref gameplayPositionProvider, provider as IGameplayPositionProvider);
                    Volatile.Write(ref gameRouteProvider, provider as IGameRouteProvider);
                    Volatile.Write(ref liveGameplayStatsProvider, provider as ILiveGameplayStatsProvider);
                    Volatile.Write(ref current, selected);
                    return selected;
                }
            }

            ImmutableArray<ProviderAttempt> completedAttempts = attempts.ToImmutable();
            bool providerFailed = completedAttempts.Any(
                attempt => attempt.Availability == GameStateAvailability.Error);
            string reason = completedAttempts.IsEmpty
                ? "No providers configured."
                : string.Join(" | ", completedAttempts.Select(FormatAttempt));
            GameStateSnapshot unavailable = Unavailable(
                DateTimeOffset.UtcNow,
                reason,
                completedAttempts,
                providerFailed ? GameStateAvailability.Error : GameStateAvailability.ProviderUnavailable);
            Volatile.Write(ref gameplayPositionProvider, null);
            Volatile.Write(ref gameRouteProvider, null);
            Volatile.Write(ref liveGameplayStatsProvider, null);
            Volatile.Write(ref current, unavailable);
            return unavailable;
        }
    }

    public GameplayPositionSnapshot ReadGameplayPosition()
    {
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        GameStateSnapshot game = Current;
        if (Volatile.Read(ref lifecycleState) != 0)
            return GameplayPositionSnapshot.Unavailable(observedAt, game.ProcessIdentity);
        IGameplayPositionProvider? provider = Volatile.Read(ref gameplayPositionProvider);
        if (!game.IsAvailable || provider is null)
            return GameplayPositionSnapshot.Unavailable(observedAt, game.ProcessIdentity);

        try
        {
            GameplayPositionSnapshot position = provider.ReadGameplayPosition();
            if (!string.Equals(position.ProcessIdentity, game.ProcessIdentity, StringComparison.Ordinal) ||
                position.CurrentTime is not { } currentTime || currentTime < TimeSpan.Zero)
            {
                return GameplayPositionSnapshot.Unavailable(observedAt, game.ProcessIdentity);
            }

            return position;
        }
        catch (Exception)
        {
            return GameplayPositionSnapshot.Unavailable(observedAt, game.ProcessIdentity);
        }
    }

    public GameRouteSnapshot ReadGameRoute()
    {
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        GameStateSnapshot game = Current;
        if (Volatile.Read(ref lifecycleState) != 0)
            return GameRouteSnapshot.Unknown(observedAt, game.ProcessIdentity);
        IGameRouteProvider? provider = Volatile.Read(ref gameRouteProvider);
        if (!game.IsAvailable || provider is null)
            return GameRouteSnapshot.Unknown(observedAt, game.ProcessIdentity);

        try
        {
            GameRouteSnapshot route = provider.ReadGameRoute();
            if (!ReferenceEquals(game, Current) ||
                !string.Equals(route.ProcessIdentity, game.ProcessIdentity, StringComparison.Ordinal))
            {
                return GameRouteSnapshot.Unknown(observedAt, game.ProcessIdentity);
            }
            return route;
        }
        catch (Exception)
        {
            return GameRouteSnapshot.Unknown(observedAt, game.ProcessIdentity);
        }
    }

    /// <summary>
    /// V3.14-COM-KEY-05 : découverte osu!stable partagée (lecture seule, aucun scan). Interroge les
    /// providers qui l'exposent, sans dépendre de la sélection courante : les signatures et le conteneur
    /// peuvent être connus avant qu'une beatmap soit localisée. Null si indisponible ou service arrêté.
    /// </summary>
    internal bool HasSharedStableDiscoveryProvider =>
        providers.Any(provider => provider is INativeStableSharedDiscoveryProvider);

    /// <summary>
    /// V3.14-COM-PP-03 : adresse du statut osu!stable déjà découverte (lecture seule, aucun scan), disponible
    /// dès la sélection des maps. Null si indisponible ou service arrêté.
    /// </summary>
    internal NativeStableSharedStatusDiscovery? TryGetSharedStableStatusDiscovery()
    {
        if (Volatile.Read(ref lifecycleState) != 0)
            return null;
        foreach (IGameStateProvider provider in providers)
        {
            if (provider is INativeStableSharedDiscoveryProvider shared &&
                shared.TryGetSharedStatusDiscovery() is { } discovery)
            {
                return discovery;
            }
        }
        return null;
    }

    internal NativeStableSharedDiscovery? TryGetSharedStableDiscovery()
    {
        if (Volatile.Read(ref lifecycleState) != 0)
            return null;
        foreach (IGameStateProvider provider in providers)
        {
            if (provider is INativeStableSharedDiscoveryProvider shared &&
                shared.TryGetSharedDiscovery() is { } discovery)
            {
                return discovery;
            }
        }
        return null;
    }

    public LiveGameplayStatsSnapshot? ReadLiveGameplayStats()
        => ReadLiveGameplayStats(out _);

    public LiveGameplayStatsSnapshot? ReadLiveGameplayStats(
        out LiveGameplayStatsDiagnostics diagnostics)
    {
        GameStateSnapshot game = Current;
        if (Volatile.Read(ref lifecycleState) != 0)
        {
            diagnostics = LiveGameplayStatsDiagnostics.Unavailable(
                LiveGameplayStatsFailureReason.ProviderUnavailable);
            return null;
        }
        ILiveGameplayStatsProvider? provider = Volatile.Read(ref liveGameplayStatsProvider);
        if (!game.IsAvailable)
        {
            diagnostics = LiveGameplayStatsDiagnostics.Unavailable(
                LiveGameplayStatsFailureReason.GameUnavailable);
            return null;
        }
        if (provider is null)
        {
            diagnostics = LiveGameplayStatsDiagnostics.Unavailable(
                LiveGameplayStatsFailureReason.ProviderUnavailable);
            return null;
        }

        try
        {
            LiveGameplayStatsReadResult read;
            if (provider is ILiveGameplayStatsDiagnosticsProvider diagnosticProvider)
            {
                read = diagnosticProvider.ReadLiveGameplayStatsWithDiagnostics();
            }
            else
            {
                LiveGameplayStatsSnapshot? providerStats = provider.ReadLiveGameplayStats();
                read = new(
                    providerStats,
                    LiveGameplayStatsDiagnostics.Unavailable(
                        providerStats is null
                            ? LiveGameplayStatsFailureReason.ProviderReturnedNull
                            : LiveGameplayStatsFailureReason.None));
            }
            LiveGameplayStatsSnapshot? stats = read.Snapshot;
            diagnostics = read.Diagnostics;
            if (stats is null)
                return null;
            if (!ReferenceEquals(game, Current))
            {
                diagnostics = diagnostics with
                {
                    FailureReason = LiveGameplayStatsFailureReason.GameStateChangedDuringRead
                };
                return null;
            }
            if (!string.Equals(stats.ProcessIdentity, game.ProcessIdentity, StringComparison.Ordinal))
            {
                diagnostics = diagnostics with
                {
                    FailureReason = LiveGameplayStatsFailureReason.ProcessIdentityMismatch
                };
                return null;
            }
            if (!string.Equals(stats.BeatmapId, game.CurrentBeatmapId, StringComparison.Ordinal))
            {
                diagnostics = diagnostics with
                {
                    FailureReason = LiveGameplayStatsFailureReason.BeatmapIdMismatch
                };
                return null;
            }
            if (!string.Equals(stats.BeatmapSetId, game.CurrentBeatmapSetId, StringComparison.Ordinal))
            {
                diagnostics = diagnostics with
                {
                    FailureReason = LiveGameplayStatsFailureReason.BeatmapSetIdMismatch
                };
                return null;
            }
            if (stats.CurrentTime < TimeSpan.Zero || stats.CurrentCombo < 0 ||
                stats.MaxComboReached < stats.CurrentCombo || stats.Count300 < 0 ||
                stats.Count100 < 0 || stats.Count50 < 0 || stats.CountMiss < 0 ||
                !double.IsFinite(stats.Accuracy) || stats.Accuracy is < 0 or > 1)
            {
                diagnostics = diagnostics with
                {
                    FailureReason = LiveGameplayStatsFailureReason.SnapshotValuesInvalid
                };
                return null;
            }
            return stats;
        }
        catch (Exception)
        {
            diagnostics = LiveGameplayStatsDiagnostics.Unavailable(
                LiveGameplayStatsFailureReason.ProviderException);
            return null;
        }
    }

    public LiveGameplaySnapshot? ReadLiveGameplaySnapshot(
        BeatmapAnalysisSnapshot? analysis) =>
        ReadLiveGameplaySnapshot(analysis, out _, out _);

    internal LiveGameplaySnapshot? ReadLiveGameplaySnapshot(
        BeatmapAnalysisSnapshot? analysis,
        out GameplayPositionSnapshot position,
        out LiveGameplayCompositionFailure failure)
    {
        GameStateSnapshot game = Current;
        if (Volatile.Read(ref lifecycleState) != 0)
        {
            position = GameplayPositionSnapshot.Unavailable(
                DateTimeOffset.UtcNow,
                game.ProcessIdentity);
            failure = LiveGameplayCompositionFailure.GameUnavailable;
            return null;
        }
        position = ReadGameplayPosition();

        // Reject a refresh/map switch racing the lightweight position read.
        if (!ReferenceEquals(game, Current))
        {
            failure = LiveGameplayCompositionFailure.GameStateChangedDuringPositionRead;
            return null;
        }

        return LiveGameplaySnapshotComposer.TryCompose(
            game,
            position,
            analysis,
            out failure);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref lifecycleState, 1) != 0)
            return;

        Volatile.Write(ref gameplayPositionProvider, null);
        Volatile.Write(ref gameRouteProvider, null);
        Volatile.Write(ref liveGameplayStatsProvider, null);
        Volatile.Write(ref current, Unavailable(
            DateTimeOffset.UtcNow,
            "Service is shutting down.",
            []));

        HashSet<object> disposed = new(ReferenceEqualityComparer.Instance);
        foreach (IGameStateProvider provider in providers)
        {
            if (provider is IDisposable disposable && disposed.Add(provider))
                disposable.Dispose();
        }
    }

    private static string FormatAttempt(ProviderAttempt attempt) =>
        $"{attempt.ProviderName} [{attempt.Availability}]: " +
        (string.IsNullOrWhiteSpace(attempt.DiagnosticReason)
            ? "No diagnostic reason provided."
            : attempt.DiagnosticReason);

    private static string SafeName(IGameStateProvider provider)
    {
        try
        {
            string? name = provider.Name;
            return string.IsNullOrWhiteSpace(name)
                ? provider.GetType().Name
                : name;
        }
        catch (Exception)
        {
            return provider.GetType().Name;
        }
    }

    private static GameStateSnapshot Unavailable(
        DateTimeOffset observedAt,
        string reason,
        ImmutableArray<ProviderAttempt> attempts,
        GameStateAvailability availability = GameStateAvailability.ProviderUnavailable) =>
        new(
            ServiceName,
            null,
            null,
            null,
            null,
            null,
            null,
            observedAt,
            availability,
            reason)
        {
            ProviderAttempts = attempts
        };
}
