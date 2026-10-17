namespace BeatInsight.CommunityOverlays.Poc.Adapter;

/// <summary>
/// Source de données pour les overlays communautaires.
/// POC : seule <see cref="MockCommunityOverlayDataSource"/> existe.
/// Intégration future : un adaptateur lisant les snapshots live de
/// BeatInsight (aucun branchement production dans cette passe).
/// </summary>
public interface ICommunityOverlayDataSource : IDisposable
{
    CommunityOverlaySnapshot Current { get; }

    /// <summary>Levé sur le thread UI à chaque nouvel instantané.</summary>
    event EventHandler<CommunityOverlaySnapshot>? SnapshotChanged;

    void Start();
}
