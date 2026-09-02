namespace BeatInsight.Models.Persistence;

/// <summary>
/// Labels humains autorisés pour le futur classifieur structurel.
///
/// Les labels sont délibérément distincts de GameplayIdentity : ils
/// représentent une annotation humaine optionnelle, jamais une
/// prédiction ni une copie de la classification actuelle.
/// </summary>
internal enum MlHumanLabel
{
    Stream,
    Jump,
    Tech,
    ClassicMixed,
}

/// <summary>
/// Instantané persistant destiné au futur dataset ML.
///
/// Ce DTO ne réalise aucune extraction, classification ou prédiction.
/// Les contenus JSON restent opaques à cette couche afin que la
/// future capture de features puisse évoluer indépendamment du cache
/// runtime et de ses modèles de présentation.
/// </summary>
internal sealed class MlDatasetSample
{
    /// <summary>
    /// Clé technique attribuée par SQLite. Vaut 0 avant la première
    /// insertion d'un échantillon.
    /// </summary>
    internal long SampleId { get; init; }

    /// <summary>Chemin absolu du fichier .osu source.</summary>
    internal string SourceFilePath { get; init; } = "";

    /// <summary>Identifiant osu! optionnel de la difficulté.</summary>
    internal int? BeatmapId { get; init; }

    /// <summary>Empreinte MD5 optionnelle du fichier source.</summary>
    internal string? Md5 { get; init; }

    /// <summary>Taille du fichier source au moment de la capture.</summary>
    internal long FileSize { get; init; }

    /// <summary>Date UTC de dernière écriture du fichier source.</summary>
    internal DateTime FileLastWriteUtc { get; init; }

    /// <summary>Version de la forme des features exportées.</summary>
    internal int FeatureSchemaVersion { get; init; }

    /// <summary>Version de l'analyse ayant produit les features.</summary>
    internal int AnalyzerVersion { get; init; }

    /// <summary>Date UTC de capture de l'échantillon.</summary>
    internal DateTime CapturedAtUtc { get; init; }

    /// <summary>
    /// Features globales brutes, sous forme de document JSON.
    /// Obligatoire car tout futur échantillon doit contenir au moins
    /// ce niveau de données.
    /// </summary>
    internal string RawFeaturesJson { get; init; } = "";

    /// <summary>
    /// Features de sections optionnelles, sous forme de document JSON.
    /// Aucune section n'est encore extraite à cette étape.
    /// </summary>
    internal string? SectionFeaturesJson { get; init; }

    /// <summary>Annotation humaine optionnelle.</summary>
    internal MlHumanLabel? HumanLabel { get; init; }

    /// <summary>
    /// Indique qu'un humain a validé l'annotation associée à cet
    /// échantillon. Cette valeur reste indépendante de toute sortie
    /// du classifieur futur.
    /// </summary>
    internal bool HumanValidated { get; init; }

    /// <summary>
    /// Preuve communautaire optionnelle, encapsulée en JSON. Elle ne
    /// participe à aucune logique de classification dans ce DTO.
    /// </summary>
    internal string? CommunityEvidenceJson { get; init; }

    /// <summary>Date UTC de collecte de la preuve communautaire.</summary>
    internal DateTime? CommunityCapturedAtUtc { get; init; }
}
