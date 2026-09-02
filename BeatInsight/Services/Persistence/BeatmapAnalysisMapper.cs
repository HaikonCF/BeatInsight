using BeatInsight.Models;
using BeatInsight.Models.Persistence;

namespace BeatInsight.Services.Persistence;

/// <summary>
/// Conversion entre le domaine (Beatmap / GameplayProfile) et les
/// enregistrements persistables (BeatmapAnalysisRecord /
/// GameplayProfileRecord).
///
/// AUCUNE RÈGLE MÉTIER
///
/// Ce mapper ne calcule rien, n'applique aucun seuil et ne dérive
/// aucune valeur. Il recopie des champs déjà produits par
/// GameplayAnalyzer, qui reste la seule source de vérité. Il ne fait
/// aucune I/O : les informations de fichier sont fournies par
/// l'appelant.
///
/// PROPRIÉTÉS DÉRIVÉES
///
/// ClassificationReasons, FullName, TraitsDisplay, ConceptsDisplay,
/// LengthDisplay et CommunityTagsDisplay ne sont jamais recopiés.
/// Ils sont reconstruits automatiquement par les modèles existants à
/// partir de leurs champs sources, ce qui garantit qu'il n'existe
/// jamais deux sources de vérité divergentes.
///
/// ============================================================
/// LIMITATION IMPORTANTE : SNAPSHOT DE PRÉSENTATION
/// ============================================================
///
/// Le Beatmap produit par <see cref="ToBeatmap"/> est un SNAPSHOT
/// COMPATIBLE PRÉSENTATION, et non une beatmap complète.
///
/// Les membres suivants ne sont pas persistés et restent donc à leur
/// valeur par défaut (collection vide ou zéro) sur un objet
/// reconstruit :
///
/// - HitObjects et TimingPoints
/// - MovementAnalysis
/// - GameplayProfile.StreamSections / JumpSections / TechSections /
///   SpeedSections / ReadSections
/// - GameplayProfile.StreamSequences / JumpSequences / BurstSequences
/// - GameplayProfile.StyleProfile
/// - Les signaux et compteurs hors périmètre d'affichage
///   (TechIntensity, TechRatio, AnalysedCircleCount, etc.)
/// - CommunityTags, TagComparison et CommunityIdentityAgreement,
///   qui relèvent de Community Evidence et sont recalculés après
///   coup par l'appelant
///
/// Ces membres ne sont volontairement PAS reconstruits
/// artificiellement : fabriquer des sections ou des hit objects
/// factices produirait des données fausses se faisant passer pour
/// des résultats d'analyse.
///
/// CONSÉQUENCE À CONNAÎTRE
///
/// GameplayProfile.ReadSections est vide sur un snapshot. Le nombre
/// de sections Reading reste toutefois disponible via la propriété
/// scalaire GameplayProfile.ReadSectionCount, qui est persistée et
/// restaurée.
///
/// Les consommateurs doivent donc lire ReadSectionCount et jamais
/// ReadSections.Count, ce dernier valant 0 sur un snapshot.
/// </summary>
internal static class BeatmapAnalysisMapper
{
    // ============================================================
    // DOMAINE -> PERSISTANCE
    // ============================================================

    /// <summary>
    /// Construit un enregistrement persistable à partir d'une
    /// beatmap analysée.
    ///
    /// Les informations de fichier sont fournies par l'appelant :
    /// ce mapper ne touche jamais au disque.
    /// </summary>
    /// <param name="beatmap">Beatmap analysée par le pipeline local.</param>
    /// <param name="filePath">Chemin absolu du fichier .osu.</param>
    /// <param name="fileSize">Taille du fichier en octets.</param>
    /// <param name="fileLastWriteUtc">Date UTC de dernière écriture.</param>
    /// <param name="analysedAtUtc">Date UTC de production de l'analyse.</param>
    /// <param name="beatmapId">
    /// Identifiant osu! lorsqu'il est connu. Absent de Beatmap : il
    /// provient de tosu et doit donc être transmis par l'appelant.
    /// </param>
    /// <param name="md5">Empreinte MD5, réservée et non alimentée.</param>
    internal static BeatmapAnalysisRecord ToRecord(
        Beatmap beatmap,
        string filePath,
        long fileSize,
        DateTime fileLastWriteUtc,
        DateTime analysedAtUtc,
        int? beatmapId = null,
        string? md5 = null)
    {
        ArgumentNullException.ThrowIfNull(beatmap);
        ArgumentNullException.ThrowIfNull(filePath);

        return new BeatmapAnalysisRecord
        {
            // Identité et fraîcheur
            FilePath = filePath,
            FileSize = fileSize,
            FileLastWriteUtc = fileLastWriteUtc,
            AnalyzerVersion = Analysis.AnalyzerVersion.Current,
            SchemaVersion = PersistenceSchemaVersion.Current,

            // Clés secondaires
            BeatmapId = beatmapId,
            Md5 = md5,

            // Traçabilité
            AnalysedAtUtc = analysedAtUtc,

            // Métadonnées
            Title = beatmap.Title,
            Artist = beatmap.Artist,
            Creator = beatmap.Creator,
            Version = beatmap.Version,
            LengthTicks = beatmap.Length.Ticks,
            BPM = beatmap.BPM,
            MaxCombo = beatmap.MaxCombo,
            AR = beatmap.AR,
            OD = beatmap.OD,
            CS = beatmap.CS,
            HP = beatmap.HP,
            CircleCount = beatmap.CircleCount,
            SliderCount = beatmap.SliderCount,
            SpinnerCount = beatmap.SpinnerCount,
            OsuStarRating = beatmap.OsuStarRating,
            BeatInsightRating = beatmap.BeatInsightRating,

            // Analyse
            Profile = ToProfileRecord(beatmap.GameplayProfile),
        };
    }

    /// <summary>
    /// Construit un enregistrement de profil persistable à partir
    /// d'un GameplayProfile.
    /// </summary>
    internal static GameplayProfileRecord ToProfileRecord(
        GameplayProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        GameplayIdentity identity = profile.Identity;

        return new GameplayProfileRecord
        {
            // Identité structurelle
            IdentityPrimary = identity.Primary,
            IdentitySecondary = identity.Secondary,
            IdentityPattern = identity.Pattern,
            IdentityConfidence = identity.Confidence,
            Traits = identity.Traits.ToList(),

            // Familles structurelles
            StreamRatio = profile.StreamRatio,
            JumpRatio = profile.JumpRatio,
            BurstRatio = profile.BurstRatio,

            // Tech
            TechPresence = profile.TechPresence,
            TechScore = profile.TechScore,
            TechTransitionSignal = profile.TechTransitionSignal,
            TechStructureSignal = profile.TechStructureSignal,
            TechSpatialSignal = profile.TechSpatialSignal,
            TechTemporalSignal = profile.TechTemporalSignal,

            // Reading
            ReadScore = profile.ReadScore,
            ReadCoverage = profile.ReadCoverage,
            ReadIntensity = profile.ReadIntensity,
            ReadSectionCount = profile.ReadSectionCount,
            ReadDensitySignal = profile.ReadDensitySignal,
            ReadClutterSignal = profile.ReadClutterSignal,
            ReadCSSignal = profile.ReadCSSignal,
            ReadPredictability = profile.ReadPredictability,
            ReadNovelty = profile.ReadNovelty,
            ReadTemporalRegularity = profile.ReadTemporalRegularity,
            ReadSpacingRegularity = profile.ReadSpacingRegularity,
            ReadTrajectoryRepetition = profile.ReadTrajectoryRepetition,
            ReadAmbiguity = profile.ReadAmbiguity,

            // Speed
            SpeedScore = profile.SpeedScore,
            SpeedFastObjectRatio = profile.SpeedFastObjectRatio,
            SpeedDensitySignal = profile.SpeedDensitySignal,
            SpeedARSignal = profile.SpeedARSignal,

            // Aim
            AimScore = profile.AimScore,
            AimDistanceSignal = profile.AimDistanceSignal,
            AimSpeedSignal = profile.AimSpeedSignal,
            AimAngleSignal = profile.AimAngleSignal,
            AimTemporalSignal = profile.AimTemporalSignal,
        };
    }


    // ============================================================
    // PERSISTANCE -> DOMAINE
    // ============================================================

    /// <summary>
    /// Reconstruit un snapshot de présentation à partir d'un
    /// enregistrement persisté.
    ///
    /// Voir la limitation documentée sur la classe : l'objet retourné
    /// est destiné à l'affichage et aux rapports, pas à une nouvelle
    /// analyse.
    /// </summary>
    internal static Beatmap ToBeatmap(BeatmapAnalysisRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new Beatmap
        {
            Title = record.Title,
            Artist = record.Artist,
            Creator = record.Creator,
            Version = record.Version,
            Length = TimeSpan.FromTicks(record.LengthTicks),
            BPM = record.BPM,
            MaxCombo = record.MaxCombo,
            AR = record.AR,
            OD = record.OD,
            CS = record.CS,
            HP = record.HP,
            CircleCount = record.CircleCount,
            SliderCount = record.SliderCount,
            SpinnerCount = record.SpinnerCount,
            OsuStarRating = record.OsuStarRating,
            BeatInsightRating = record.BeatInsightRating,
            GameplayProfile = ToGameplayProfile(record.Profile),
        };
    }

    /// <summary>
    /// Reconstruit un GameplayProfile de présentation à partir d'un
    /// enregistrement de profil.
    /// </summary>
    internal static GameplayProfile ToGameplayProfile(
        GameplayProfileRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new GameplayProfile
        {
            // Familles structurelles
            StreamRatio = record.StreamRatio,
            JumpRatio = record.JumpRatio,
            BurstRatio = record.BurstRatio,

            // Tech
            TechPresence = record.TechPresence,
            TechScore = record.TechScore,
            TechTransitionSignal = record.TechTransitionSignal,
            TechStructureSignal = record.TechStructureSignal,
            TechSpatialSignal = record.TechSpatialSignal,
            TechTemporalSignal = record.TechTemporalSignal,

            // Reading
            ReadScore = record.ReadScore,
            ReadCoverage = record.ReadCoverage,
            ReadIntensity = record.ReadIntensity,

            // Le compteur est restauré alors que ReadSections reste
            // volontairement vide : aucune section n'est fabriquée.
            ReadSectionCount = record.ReadSectionCount,

            ReadDensitySignal = record.ReadDensitySignal,
            ReadClutterSignal = record.ReadClutterSignal,
            ReadCSSignal = record.ReadCSSignal,
            ReadPredictability = record.ReadPredictability,
            ReadNovelty = record.ReadNovelty,
            ReadTemporalRegularity = record.ReadTemporalRegularity,
            ReadSpacingRegularity = record.ReadSpacingRegularity,
            ReadTrajectoryRepetition = record.ReadTrajectoryRepetition,
            ReadAmbiguity = record.ReadAmbiguity,

            // Speed
            SpeedScore = record.SpeedScore,
            SpeedFastObjectRatio = record.SpeedFastObjectRatio,
            SpeedDensitySignal = record.SpeedDensitySignal,
            SpeedARSignal = record.SpeedARSignal,

            // Aim
            AimScore = record.AimScore,
            AimDistanceSignal = record.AimDistanceSignal,
            AimSpeedSignal = record.AimSpeedSignal,
            AimAngleSignal = record.AimAngleSignal,
            AimTemporalSignal = record.AimTemporalSignal,

            // Identité structurelle
            Identity = ToGameplayIdentity(record),
        };
    }

    /// <summary>
    /// Reconstruit une GameplayIdentity à partir d'un enregistrement
    /// de profil.
    ///
    /// FullName et TraitsDisplay ne sont pas assignés : ils sont
    /// dérivés par le modèle depuis Pattern, Primary et Traits.
    /// </summary>
    private static GameplayIdentity ToGameplayIdentity(
        GameplayProfileRecord record)
    {
        return new GameplayIdentity
        {
            Primary = record.IdentityPrimary,
            Secondary = record.IdentitySecondary,
            Pattern = record.IdentityPattern,
            Confidence = record.IdentityConfidence,
            Traits = record.Traits.ToList(),
        };
    }
}
