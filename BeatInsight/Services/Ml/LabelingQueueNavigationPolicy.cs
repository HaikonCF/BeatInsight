namespace BeatInsight.Services.Ml;

/// <summary>
/// Identifie la provenance de la map affichée par les outils de labellisation.
/// DiscoveryReview gèle tosu comme les files existantes, mais ne dispose pas
/// d'une navigation automatique : une validation reste sur la map revue.
/// </summary>
internal enum LabelingQueueKind
{
    None,
    FastUnlabeled,
    Calibration,
    DiscoveryReview,
}

internal static class LabelingQueueNavigationPolicy
{
    /// <summary>
    /// Cible de Skip. None conserve le comportement historique (Fast
    /// Unlabeled), tandis qu'une revue Discovery ne saute jamais vers une
    /// autre file sans action explicite de l'utilisateur.
    /// </summary>
    internal static LabelingQueueKind? GetSkipTarget(
        LabelingQueueKind activeQueue) =>
        activeQueue switch
        {
            LabelingQueueKind.FastUnlabeled => LabelingQueueKind.FastUnlabeled,
            LabelingQueueKind.Calibration => LabelingQueueKind.Calibration,
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
            or LabelingQueueKind.Calibration;
}
