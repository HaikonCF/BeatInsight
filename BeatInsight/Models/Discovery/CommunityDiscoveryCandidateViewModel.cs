namespace BeatInsight.Models.Discovery;

/// <summary>
/// Projection de présentation sans logique de découverte ni données de label.
/// Elle garde les bindings WPF publics tout en laissant les modèles backend
/// V2.4.1 internes à l'assemblage.
/// </summary>
public sealed class CommunityDiscoveryCandidateViewModel
{
    public int BeatmapId { get; init; }

    public int BeatmapSetId { get; init; }

    public string ArtistTitle { get; init; } = "";

    public string Difficulty { get; init; } = "";

    public string Mapper { get; init; } = "";

    public string StarRating { get; init; } = "";

    public string SamplingFamily { get; init; } = "";

    public string EvidenceScore { get; init; } = "";

    public string CommunityTags { get; init; } = "";

    public string AlreadyOwned { get; init; } = "";

    public string AlreadyInMlDataset { get; init; } = "";

    public string HumanValidated { get; init; } = "";
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
        };
    }

    private static string YesNo(bool value) => value ? "Yes" : "No";
}
