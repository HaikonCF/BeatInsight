namespace BeatInsight.Models.Persistence;

/// <summary>
/// Représentation persistable d'un résultat d'analyse gameplay.
///
/// Cette classe ne réalise aucun calcul et ne contient aucune règle
/// métier. Elle est un simple transport de données entre le domaine
/// (GameplayProfile / GameplayIdentity) et le stockage.
///
/// PÉRIMÈTRE VOLONTAIREMENT MINIMAL
///
/// Seules les valeurs SOURCES réellement consommées sont présentes.
/// Ce périmètre correspond exactement à l'union de :
///
/// - les bindings de MainWindow.xaml ;
/// - les dépendances de GameplayProfile.ClassificationReasons ;
/// - le rapport détaillé CopyAnalysis_Click ;
/// - le rapport d'issue ReportClassification_Click.
///
/// NE SONT DÉLIBÉRÉMENT PAS PERSISTÉS
///
/// - Les propriétés calculées (ClassificationReasons, FullName,
///   TraitsDisplay, ConceptsDisplay) : elles se reconstruisent
///   intégralement à partir des champs ci-dessous. Les stocker
///   créerait une seconde source de vérité pouvant diverger du domaine.
/// - Les HitObjects et TimingPoints : jamais consommés par l'UI.
/// - Les collections Sections / Sequences : documentées comme
///   « debug / futurs écrans » et non consommées, à l'exception du
///   seul nombre de sections Read (voir <see cref="ReadSectionCount"/>).
/// - Toute donnée issue de la communauté (CommunityTags,
///   TagComparison, CommunityIdentityAgreement, MatchedFamilies,
///   ConflictingFamilies) : Community Evidence reste séparé de
///   l'analyse locale et n'est jamais mis en cache ici.
/// </summary>
internal sealed class GameplayProfileRecord
{
    // ============================================================
    // IDENTITÉ STRUCTURELLE
    //
    // Source : GameplayIdentity.
    // Seules les valeurs sources sont conservées : FullName est
    // dérivé de Pattern et Primary, et n'est donc pas persisté.
    // ============================================================

    /// <summary>
    /// Identité gameplay structurelle principale.
    ///
    /// Valeurs possibles : "Jump", "Stream", "Tech",
    /// "Classic / Mixed".
    ///
    /// Aim, Speed et Reading ne peuvent jamais apparaître ici.
    /// </summary>
    internal string IdentityPrimary { get; init; } = "";

    /// <summary>
    /// Identité gameplay structurelle secondaire.
    ///
    /// Ne peut représenter qu'une structure gameplay, jamais une
    /// pression de skill. Burst n'est jamais une identité
    /// secondaire : il est transversal.
    /// </summary>
    internal string IdentitySecondary { get; init; } = "";

    /// <summary>
    /// Motif structurel global associé au gameplay.
    ///
    /// Nécessaire pour reconstruire GameplayIdentity.FullName.
    /// </summary>
    internal string IdentityPattern { get; init; } = "";

    /// <summary>
    /// Niveau de confiance de l'identité structurelle primaire.
    /// </summary>
    internal double IdentityConfidence { get; init; }

    /// <summary>
    /// Caractéristiques particulières détectées dans le gameplay.
    ///
    /// Conservé sous forme de collection ordonnée de chaînes, forme
    /// directement sérialisable en tableau JSON. La projection vers
    /// une colonne de stockage relèvera du mapping, pas de ce DTO.
    ///
    /// TraitsDisplay n'est pas persisté : il s'agit d'une simple
    /// concaténation recalculée à l'affichage.
    /// </summary>
    internal IReadOnlyList<string> Traits { get; init; } = [];


    // ============================================================
    // FAMILLES STRUCTURELLES
    // ============================================================

    /// <summary>Proportion de gameplay Stream détectée.</summary>
    internal double StreamRatio { get; init; }

    /// <summary>Proportion de gameplay Jump détectée.</summary>
    internal double JumpRatio { get; init; }

    /// <summary>
    /// Proportion de Burst détectée.
    ///
    /// Burst est transversal : il peut coexister avec Stream, Jump
    /// ou Tech et ne constitue jamais une identité primaire ou
    /// secondaire.
    /// </summary>
    internal double BurstRatio { get; init; }


    // ============================================================
    // TECH
    //
    // Les trois grandeurs Tech ont des sémantiques distinctes et ne
    // doivent jamais être confondues :
    //
    // - TechPresence  = couverture structurelle Tech
    // - TechIntensity = intensité technique brute
    // - TechScore     = pression technique finale
    //
    // TechPresence et TechScore sont tous deux consommés, le premier
    // par les bindings UI, le second par les rapports et par
    // ClassificationReasons : les deux sont donc persistés côte à
    // côte, sans fusion.
    //
    // TechIntensity n'est consommé par aucun des quatre chemins du
    // périmètre et n'est volontairement pas persisté.
    //
    // Le Identity Tech Candidate (GameplayIdentity.TechScore) est une
    // grandeur distincte de GameplayProfile.TechScore ; il n'est pas
    // consommé par le périmètre et n'est donc pas repris ici.
    // ============================================================

    /// <summary>Couverture structurelle Tech.</summary>
    internal double TechPresence { get; init; }

    /// <summary>Pression technique finale.</summary>
    internal double TechScore { get; init; }

    /// <summary>Signal Tech de transition.</summary>
    internal double TechTransitionSignal { get; init; }

    /// <summary>Signal Tech de structure.</summary>
    internal double TechStructureSignal { get; init; }

    /// <summary>Signal Tech spatial.</summary>
    internal double TechSpatialSignal { get; init; }

    /// <summary>Signal Tech temporel.</summary>
    internal double TechTemporalSignal { get; init; }


    // ============================================================
    // READING
    //
    // Reading est une pression de skill, jamais une identité
    // primaire.
    // ============================================================

    /// <summary>Pression de lecture finale.</summary>
    internal double ReadScore { get; init; }

    /// <summary>Couverture temporelle des zones de lecture.</summary>
    internal double ReadCoverage { get; init; }

    /// <summary>
    /// Libellé d'intensité de lecture.
    ///
    /// Seule valeur non numérique du profil : conservée telle quelle
    /// car affichée verbatim par le rapport détaillé.
    /// </summary>
    internal string ReadIntensity { get; init; } = "";

    /// <summary>
    /// Nombre de sections Read détectées.
    ///
    /// Seul le cardinal de GameplayProfile.ReadSections est consommé
    /// (rapport détaillé). Les sections elles-mêmes ne sont pas
    /// persistées : ce serait une sur-normalisation pour une valeur
    /// réduite à un entier.
    /// </summary>
    internal int ReadSectionCount { get; init; }

    /// <summary>Signal de densité de lecture.</summary>
    internal double ReadDensitySignal { get; init; }

    /// <summary>Signal d'encombrement visuel.</summary>
    internal double ReadClutterSignal { get; init; }

    /// <summary>Signal lié au Circle Size.</summary>
    internal double ReadCSSignal { get; init; }

    /// <summary>Prévisibilité des motifs de lecture.</summary>
    internal double ReadPredictability { get; init; }

    /// <summary>Nouveauté des motifs de lecture.</summary>
    internal double ReadNovelty { get; init; }

    /// <summary>Régularité temporelle de lecture.</summary>
    internal double ReadTemporalRegularity { get; init; }

    /// <summary>Régularité d'espacement de lecture.</summary>
    internal double ReadSpacingRegularity { get; init; }

    /// <summary>Répétition de trajectoire de lecture.</summary>
    internal double ReadTrajectoryRepetition { get; init; }

    /// <summary>Ambiguïté de lecture.</summary>
    internal double ReadAmbiguity { get; init; }


    // ============================================================
    // SPEED
    //
    // Speed est une pression de skill, jamais une identité primaire.
    // ============================================================

    /// <summary>Pression de vitesse finale.</summary>
    internal double SpeedScore { get; init; }

    /// <summary>Proportion d'objets rapides.</summary>
    internal double SpeedFastObjectRatio { get; init; }

    /// <summary>Signal de densité temporelle.</summary>
    internal double SpeedDensitySignal { get; init; }

    /// <summary>Signal lié à l'Approach Rate.</summary>
    internal double SpeedARSignal { get; init; }


    // ============================================================
    // AIM
    //
    // Aim est une pression de skill, jamais une identité primaire.
    // ============================================================

    /// <summary>Pression d'aim finale.</summary>
    internal double AimScore { get; init; }

    /// <summary>Signal de distance d'aim.</summary>
    internal double AimDistanceSignal { get; init; }

    /// <summary>Signal de vitesse d'aim.</summary>
    internal double AimSpeedSignal { get; init; }

    /// <summary>Signal d'angle d'aim.</summary>
    internal double AimAngleSignal { get; init; }

    /// <summary>Signal temporel d'aim.</summary>
    internal double AimTemporalSignal { get; init; }
}
