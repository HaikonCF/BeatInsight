namespace BeatInsight.Models.Persistence;

/// <summary>
/// Représentation persistable d'une beatmap analysée.
///
/// Un enregistrement correspond à un fichier .osu analysé, soit une
/// difficulté unique, et non un mapset.
///
/// Cette classe ne réalise aucun calcul et ne contient aucune règle
/// métier. Elle transporte uniquement des valeurs déjà produites par
/// le pipeline local.
///
/// COMPOSITION
///
/// - Les champs d'identité et de fraîcheur du fichier.
/// - Les métadonnées de beatmap consommées par les bindings UI.
/// - Le résultat d'analyse, dans <see cref="Profile"/>.
///
/// NE SONT DÉLIBÉRÉMENT PAS PERSISTÉS
///
/// - CommunityTags, TagComparison et CommunityIdentityAgreement :
///   Community Evidence dépend de l'API osu! et doit rester séparé
///   du scan local.
/// - HitObjects et TimingPoints : jamais consommés par l'UI et bien
///   trop volumineux pour un index.
/// - MovementAnalysis : membre interne non consommé par l'UI ;
///   seule sa sortie agrégée BeatInsightRating est conservée.
/// - Les propriétés calculées (CommunityTagsDisplay, LengthDisplay) :
///   recalculées à l'affichage depuis les champs sources.
/// - SliderMultiplier et SliderTickRate : utilisés uniquement pendant
///   le parsing, non consommés par l'UI.
/// </summary>
internal sealed class BeatmapAnalysisRecord
{
    // ============================================================
    // IDENTITÉ DU FICHIER
    // ============================================================

    /// <summary>
    /// Chemin absolu du fichier .osu analysé.
    ///
    /// Sert d'identité de l'enregistrement : c'est la seule donnée
    /// dont MainWindow dispose avant toute analyse.
    /// </summary>
    internal string FilePath { get; init; } = "";


    // ============================================================
    // FRAÎCHEUR / INVALIDATION
    //
    // Un enregistrement n'est réutilisable que si les quatre champs
    // suivants concordent avec l'état courant. Toute divergence doit
    // provoquer un recalcul complet par le pipeline local.
    // ============================================================

    /// <summary>
    /// Taille du fichier .osu au moment de l'analyse, en octets.
    ///
    /// Obtenue sans coût supplémentaire via FileInfo.
    /// </summary>
    internal long FileSize { get; init; }

    /// <summary>
    /// Date UTC de dernière écriture du fichier .osu au moment de
    /// l'analyse.
    ///
    /// Combinée à <see cref="FileSize"/> pour détecter une édition
    /// de la map sans avoir à relire son contenu.
    /// </summary>
    internal DateTime FileLastWriteUtc { get; init; }

    /// <summary>
    /// Version de l'analyse ayant produit cet enregistrement.
    ///
    /// À comparer à BeatInsight.Analysis.AnalyzerVersion.Current :
    /// une divergence signifie que les formules ont changé et que le
    /// résultat stocké n'est plus valide métier.
    /// </summary>
    internal int AnalyzerVersion { get; init; }

    /// <summary>
    /// Version du schéma de persistance ayant produit cet
    /// enregistrement.
    ///
    /// À comparer à <see cref="PersistenceSchemaVersion.Current"/> :
    /// une divergence signifie que la forme stockée n'est plus
    /// interprétable de manière fiable.
    /// </summary>
    internal int SchemaVersion { get; init; }


    // ============================================================
    // CLÉS SECONDAIRES
    // ============================================================

    /// <summary>
    /// Identifiant osu! de la difficulté, lorsqu'il est connu.
    ///
    /// Nullable volontairement : une map locale ou non soumise n'a
    /// pas d'identifiant exploitable. Ne peut donc pas servir
    /// d'identité d'enregistrement, mais reste utile pour regrouper
    /// les difficultés d'un même mapset et pour recroiser Community
    /// Evidence.
    /// </summary>
    internal int? BeatmapId { get; init; }

    /// <summary>
    /// Empreinte MD5 du fichier .osu.
    ///
    /// RÉSERVÉ : non renseigné à ce stade. Le pipeline actuel ne
    /// calcule aucune empreinte et tosu n'en fournit pas. Le champ
    /// existe pour permettre plus tard la déduplication et la
    /// stabilité au déplacement du dossier Songs, sans imposer de
    /// changement de schéma.
    ///
    /// Doit rester nullable tant qu'il n'est pas alimenté.
    /// </summary>
    internal string? Md5 { get; init; }


    // ============================================================
    // TRAÇABILITÉ
    // ============================================================

    /// <summary>
    /// Date UTC à laquelle l'analyse a été produite.
    ///
    /// Purement informative : ne participe pas à l'invalidation.
    /// </summary>
    internal DateTime AnalysedAtUtc { get; init; }


    // ============================================================
    // MÉTADONNÉES DE BEATMAP
    //
    // Source : Beatmap. Périmètre restreint aux bindings de
    // MainWindow.xaml et aux en-têtes des deux rapports.
    // ============================================================

    /// <summary>Titre du morceau.</summary>
    internal string Title { get; init; } = "";

    /// <summary>Artiste du morceau.</summary>
    internal string Artist { get; init; } = "";

    /// <summary>Créateur de la beatmap.</summary>
    internal string Creator { get; init; } = "";

    /// <summary>Nom de la difficulté.</summary>
    internal string Version { get; init; } = "";

    /// <summary>
    /// Durée de la beatmap, en ticks.
    ///
    /// Beatmap.Length est un TimeSpan ; il est stocké en ticks pour
    /// rester trivialement représentable par un entier signé. La
    /// propriété d'affichage LengthDisplay n'est pas persistée : elle
    /// est recalculée depuis cette valeur.
    /// </summary>
    internal long LengthTicks { get; init; }

    /// <summary>Tempo principal en battements par minute.</summary>
    internal int BPM { get; init; }

    /// <summary>Combo maximal théorique.</summary>
    internal int MaxCombo { get; init; }

    /// <summary>Approach Rate.</summary>
    internal double AR { get; init; }

    /// <summary>Overall Difficulty.</summary>
    internal double OD { get; init; }

    /// <summary>Circle Size.</summary>
    internal double CS { get; init; }

    /// <summary>HP Drain.</summary>
    internal double HP { get; init; }

    /// <summary>Nombre de cercles.</summary>
    internal int CircleCount { get; init; }

    /// <summary>Nombre de sliders.</summary>
    internal int SliderCount { get; init; }

    /// <summary>Nombre de spinners.</summary>
    internal int SpinnerCount { get; init; }

    /// <summary>
    /// Star Rating calculé par le système de difficulté d'osu!.
    ///
    /// Persister cette valeur est l'objectif principal de l'index :
    /// son calcul représente environ 72 % du temps de traitement
    /// local d'une map.
    /// </summary>
    internal double OsuStarRating { get; init; }

    /// <summary>
    /// Rating de difficulté propre à BeatInsight.
    ///
    /// Ne représente pas le Star Rating officiel d'osu!.
    /// </summary>
    internal double BeatInsightRating { get; init; }


    // ============================================================
    // RÉSULTAT D'ANALYSE
    // ============================================================

    /// <summary>
    /// Résultat d'analyse gameplay associé à cette beatmap.
    ///
    /// GameplayAnalyzer reste la seule source de vérité : ce champ
    /// n'est qu'une copie transportable de son résultat.
    /// </summary>
    internal GameplayProfileRecord Profile { get; init; } = new();
}
