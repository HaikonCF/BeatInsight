namespace BeatInsight.Models.Discovery;

/// <summary>
/// Taxonomie de découverte destinée à l'échantillonnage ML. Elle est
/// volontairement distincte de MlHumanLabel : une preuve communautaire ne
/// devient jamais une annotation humaine.
/// </summary>
internal enum CommunitySamplingFamily
{
    Jump,
    Stream,
    Tech,
    Reading,
    Hybrid,
}

/// <summary>
/// Filtres d'une requête de découverte communautaire. Les statuts de qualité
/// sont inclus par défaut afin de privilégier les maps avec une preuve
/// publique et stable, sans imposer un choix de label humain.
/// </summary>
internal sealed class CommunityDiscoveryRequest
{
    internal required CommunitySamplingFamily SamplingFamily { get; init; }

    internal int MaxResults { get; init; } = 30;

    internal double? MinStarRating { get; init; }

    internal double? MaxStarRating { get; init; }

    internal bool IncludeRanked { get; init; } = true;

    internal bool IncludeApproved { get; init; } = true;

    internal bool IncludeLoved { get; init; } = true;

    internal bool ExcludeAlreadyHumanValidated { get; init; } = true;
}

/// <summary>
/// Une preuve de tag communautaire distante. Votes est conservé séparément de
/// toute annotation humaine et vaut zéro lorsqu'une source connaît seulement
/// la présence du tag, sans son décompte public.
/// </summary>
internal sealed class CommunityBeatmapUserTag
{
    internal required string Name { get; init; }

    internal int Votes { get; init; }
}

/// <summary>
/// Forme normalisée produite par une source distante avant le filtrage et
/// l'enrichissement local. GameMode suit le mode osu! numérique : 0 = osu!.
/// </summary>
internal sealed class CommunityBeatmapRemoteCandidate
{
    internal required int BeatmapId { get; init; }

    internal required int BeatmapSetId { get; init; }

    internal string Artist { get; init; } = "";

    internal string Title { get; init; } = "";

    internal string DifficultyName { get; init; } = "";

    internal string Mapper { get; init; } = "";

    internal double StarRating { get; init; }

    internal double? BPM { get; init; }

    internal string Status { get; init; } = "";

    internal int GameMode { get; init; }

    internal IReadOnlyList<CommunityBeatmapUserTag> UserTags { get; init; } =
        [];
}

/// <summary>
/// État local résolu par BeatmapId. Ce modèle ne contient aucun label humain :
/// HumanValidated est uniquement une information de filtrage pour la future
/// UI de découverte.
/// </summary>
internal readonly record struct CommunityBeatmapLocalState(
    bool AlreadyOwned,
    bool AlreadyInMlDataset,
    bool HumanValidated);

/// <summary>
/// Résultat prêt à être affiché par une future UI ML Lab. Il combine les
/// métadonnées distantes avec un enrichissement local en lecture seule.
/// </summary>
internal sealed class CommunityBeatmapCandidate
{
    internal required int BeatmapId { get; init; }

    internal required int BeatmapSetId { get; init; }

    internal string Artist { get; init; } = "";

    internal string Title { get; init; } = "";

    internal string DifficultyName { get; init; } = "";

    internal string Mapper { get; init; } = "";

    internal double StarRating { get; init; }

    internal double? BPM { get; init; }

    internal string Status { get; init; } = "";

    internal IReadOnlyList<CommunityBeatmapUserTag> UserTags { get; init; } =
        [];

    internal required CommunitySamplingFamily SamplingFamily { get; init; }

    /// <summary>Score de preuve communautaire du sampling demandé.</summary>
    internal double EvidenceScore { get; init; }

    internal bool AlreadyOwned { get; init; }

    internal bool AlreadyInMlDataset { get; init; }

    internal bool HumanValidated { get; init; }
}
