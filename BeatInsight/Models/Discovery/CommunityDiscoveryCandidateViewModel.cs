using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace BeatInsight.Models.Discovery;

/// <summary>
/// Projection de présentation sans logique de découverte ni données de label.
/// Elle garde les bindings WPF publics tout en laissant les modèles backend
/// V2.4.1 internes à l'assemblage.
///
/// Les champs statiques (titre, mapper, evidence...) sont figés à la
/// création. AlreadyOwned/le statut de téléchargement sont mutables et
/// notifient WPF : V2.4.3 rafraîchit ces cartes après un import confirmé
/// sans relancer de recherche complète.
/// </summary>
public sealed class CommunityDiscoveryCandidateViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public int BeatmapId { get; init; }

    public int BeatmapSetId { get; init; }

    public string ArtistTitle { get; init; } = "";

    public string Difficulty { get; init; } = "";

    public string Mapper { get; init; } = "";

    public string StarRating { get; init; } = "";

    public string SamplingFamily { get; init; } = "";

    public string EvidenceScore { get; init; } = "";

    public string CommunityTags { get; init; } = "";

    private string alreadyOwnedText = "";

    public string AlreadyOwned
    {
        get => alreadyOwnedText;
        set => SetField(ref alreadyOwnedText, value);
    }

    private string alreadyInMlDatasetText = "";

    public string AlreadyInMlDataset
    {
        get => alreadyInMlDatasetText;
        set => SetField(ref alreadyInMlDatasetText, value);
    }

    public string HumanValidated { get; init; } = "";

    private bool isOwned;

    /// <summary>
    /// Distinct du texte <see cref="AlreadyOwned"/> : pilote la
    /// visibilité Download/Load for Review sans reparser une chaîne.
    /// </summary>
    public bool IsOwned
    {
        get => isOwned;
        set
        {
            if (SetField(ref isOwned, value))
            {
                OnPropertyChanged(nameof(DownloadButtonVisibility));
                OnPropertyChanged(nameof(LoadForReviewButtonVisibility));
            }
        }
    }

    private bool isInstalledLocally;

    /// <summary>
    /// "Présent dans le dossier Songs", distinct d'<see cref="IsOwned"/>
    /// ("déjà analysé et indexé par BeatInsight" — voir
    /// RepositoryCommunityBeatmapLocalStateSource). Un import osu! réussi
    /// rend cette valeur vraie immédiatement, avant même qu'une analyse
    /// locale n'ait eu lieu : voir <c>IBeatmapInstallationProbe</c>
    /// (V2.4.3a). Ne pilote jamais Load for Review, qui reste conditionné
    /// à <see cref="IsOwned"/> puisque ce chargement dépend de
    /// BeatmapAnalysisRepository pour résoudre le fichier local.
    /// </summary>
    public bool IsInstalledLocally
    {
        get => isInstalledLocally;
        set
        {
            if (SetField(ref isInstalledLocally, value))
            {
                OnPropertyChanged(nameof(DownloadButtonVisibility));
            }
        }
    }

    public Visibility DownloadButtonVisibility =>
        IsOwned || IsInstalledLocally ? Visibility.Collapsed : Visibility.Visible;

    public Visibility LoadForReviewButtonVisibility =>
        IsOwned ? Visibility.Visible : Visibility.Collapsed;

    private bool isDownloadOperationRunning;

    /// <summary>Empêche un double-clic pendant Download/Import en cours.</summary>
    public bool IsDownloadOperationRunning
    {
        get => isDownloadOperationRunning;
        set
        {
            if (SetField(ref isDownloadOperationRunning, value))
            {
                OnPropertyChanged(nameof(IsDownloadButtonEnabled));
                OnPropertyChanged(nameof(CancelButtonVisibility));
            }
        }
    }

    public bool IsDownloadButtonEnabled => !IsDownloadOperationRunning;

    public Visibility CancelButtonVisibility =>
        IsDownloadOperationRunning ? Visibility.Visible : Visibility.Collapsed;

    private string downloadStatusText = "";

    /// <summary>
    /// "", "Opening download page...", "Waiting for osu! import...",
    /// "Imported", ou "Failed: <raison>". Jamais un secret/jeton.
    /// </summary>
    public string DownloadStatusText
    {
        get => downloadStatusText;
        set => SetField(ref downloadStatusText, value);
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// Projection textuelle destinée au rendu WPF. Elle ne touche ni au dataset
/// ni aux sélections de labels humains : le candidat reste une preuve de
/// sampling communautaire en lecture seule.
/// </summary>
internal static class CommunityDiscoveryCandidateViewFactory
{
    internal static CommunityDiscoveryCandidateViewModel Create(
        CommunityBeatmapCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        // CommunityDetailsAvailable=false (sans tag connu non plus) signifie
        // que l'enrichissement communautaire détaillé n'a pas été tenté ou a
        // échoué — jamais une preuve mesurée à zéro. Les deux états doivent
        // rester visuellement distincts : "unavailable" n'est jamais un
        // score numérique, et un score numérique n'est jamais affiché pour
        // un candidat non enrichi.
        bool hasCommunityDetails = candidate.CommunityDetailsAvailable
            || candidate.UserTags.Count > 0;

        string tags = !hasCommunityDetails
            ? "unavailable"
            : candidate.UserTags.Count == 0
            ? "None"
            : string.Join(
                " · ",
                candidate.UserTags.Select(tag => tag.Votes > 0
                    ? $"{tag.Name} ({tag.Votes})"
                    : tag.Name));

        string evidence = hasCommunityDetails
            ? $"Community evidence: {candidate.EvidenceScore:F2}"
            : "Community evidence: unavailable";

        return new CommunityDiscoveryCandidateViewModel
        {
            BeatmapId = candidate.BeatmapId,
            BeatmapSetId = candidate.BeatmapSetId,
            ArtistTitle = $"{candidate.Artist} — {candidate.Title}",
            Difficulty = $"Difficulty: {candidate.DifficultyName}",
            Mapper = $"Mapper: {candidate.Mapper}",
            StarRating = $"★ {candidate.StarRating:F2}",
            // "Search match" décrit la provenance de sampling/discovery,
            // jamais un Human Label : le libellé le rend explicite plutôt
            // que de laisser sous-entendre une identité BeatInsight/humaine.
            SamplingFamily = $"Search match: {candidate.SamplingFamily}",
            EvidenceScore = evidence,
            CommunityTags = $"Tags: {tags}",
            AlreadyOwned = $"Already owned: {YesNo(candidate.AlreadyOwned)}",
            AlreadyInMlDataset =
                $"In ML Dataset: {YesNo(candidate.AlreadyInMlDataset)}",
            HumanValidated =
                $"Human validated: {YesNo(candidate.HumanValidated)}",
            IsOwned = candidate.AlreadyOwned,
        };
    }

    private static string YesNo(bool value) => value ? "Yes" : "No";
}
