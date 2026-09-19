namespace BeatInsight.Services.Ml;

/// <summary>
/// Identifie la provenance de la map affichée par les outils de labellisation.
/// DiscoveryReview et TechCandidates gèlent tosu comme les files existantes.
/// TechCandidates avance uniquement après une validation humaine réussie ;
/// DiscoveryReview reste toujours sur la map revue.
/// </summary>
internal enum LabelingQueueKind
{
    None,
    FastUnlabeled,
    Calibration,
    TechCandidates,
    DiscoveryReview,
}

internal static class LabelingQueueNavigationPolicy
{
    /// <summary>
    /// Cible de Skip. None conserve le comportement historique (Fast
    /// Unlabeled), tandis que DiscoveryReview et TechCandidates ne sautent
    /// jamais vers une autre file via l'action Skip.
    /// </summary>
    internal static LabelingQueueKind? GetSkipTarget(
        LabelingQueueKind activeQueue) =>
        activeQueue switch
        {
            LabelingQueueKind.FastUnlabeled => LabelingQueueKind.FastUnlabeled,
            LabelingQueueKind.Calibration => LabelingQueueKind.Calibration,
            LabelingQueueKind.TechCandidates => null,
            LabelingQueueKind.None => LabelingQueueKind.FastUnlabeled,
            LabelingQueueKind.DiscoveryReview => null,
            _ => throw new ArgumentOutOfRangeException(
                nameof(activeQueue),
                activeQueue,
                "Unsupported labeling queue."),
        };

    internal static bool ShouldAutoAdvanceAfterValidation(
        LabelingQueueKind activeQueue) =>
        activeQueue is LabelingQueueKind.FastUnlabeled
            or LabelingQueueKind.Calibration
            or LabelingQueueKind.TechCandidates;

    internal static bool ShouldAutoAdvanceAfterSuccessfulValidation(
        bool saveSucceeded,
        LabelingQueueKind activeQueue) =>
        saveSucceeded && ShouldAutoAdvanceAfterValidation(activeQueue);
}
