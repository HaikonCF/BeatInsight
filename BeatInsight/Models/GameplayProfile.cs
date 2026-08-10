namespace BeatInsight.Models;

/// <summary>
/// Profil structurel d'une beatmap.
///
/// GameplayProfile rassemble les résultats des différents analyseurs
/// de BeatInsight.
///
/// Ces données décrivent actuellement le gameplay de la map et
/// n'influencent pas directement le Star Rating.
/// </summary>
public sealed class GameplayProfile
{
    // ============================================================
    // 1. STATISTIQUES GÉNÉRALES
    // ============================================================

    /// <summary>
    /// Nombre total de cercles pouvant être analysés
    /// par les détecteurs gameplay.
    /// </summary>
    public int AnalysedCircleCount { get; init; }


    // ============================================================
    // 2. STREAM
    // ============================================================

    /// <summary>
    /// Nombre de cercles appartenant à des structures Stream.
    /// </summary>
    public int StreamObjectCount { get; init; }

    /// <summary>
    /// Nombre de séquences Stream détectées.
    /// </summary>
    public int StreamSequenceCount { get; init; }

    /// <summary>
    /// Proportion des cercles appartenant à des Streams.
    /// </summary>
    public double StreamRatio { get; init; }


    // ============================================================
    // 3. JUMP
    // ============================================================

    /// <summary>
    /// Nombre de cercles appartenant à des structures Jump.
    /// </summary>
    public int JumpObjectCount { get; init; }

    /// <summary>
    /// Nombre de séquences Jump détectées.
    /// </summary>
    public int JumpSequenceCount { get; init; }

    /// <summary>
    /// Proportion des cercles appartenant à des Jumps.
    /// </summary>
    public double JumpRatio { get; init; }


    // ============================================================
    // 4. BURST
    // ============================================================

    /// <summary>
    /// Nombre de cercles appartenant à des structures Burst.
    /// </summary>
    public int BurstObjectCount { get; init; }

    /// <summary>
    /// Nombre de séquences Burst détectées.
    /// </summary>
    public int BurstSequenceCount { get; init; }

    /// <summary>
    /// Taille de la plus longue séquence Burst détectée.
    /// </summary>
    public int LongestBurstLength { get; init; }

    /// <summary>
    /// Proportion des cercles appartenant à des Bursts.
    /// </summary>
    public double BurstRatio { get; init; }

    /// <summary>
    /// Indique si au moins un Burst a été détecté.
    /// </summary>
    public bool HasBursts =>
        BurstSequenceCount > 0;


    // ============================================================
    // 5. TECH
    // ============================================================

    /// <summary>
    /// Nombre de cercles impliqués dans les structures
    /// identifiées comme Tech.
    /// </summary>
    public int TechObjectCount { get; init; }

    /// <summary>
    /// Proportion des cercles impliqués dans des structures Tech.
    /// </summary>
    public double TechRatio { get; init; }

    /// <summary>
    /// Score composite Tech sur 100.
    ///
    /// Ce score représente le signal Tech détecté par l'analyseur.
    /// </summary>
    public double TechScore { get; init; }

    /// <summary>
    /// Niveau qualitatif du signal Tech :
    /// Low / Medium / High.
    /// </summary>
    public string TechLevel { get; init; } = "Low";

    public double TechTransitionSignal { get; init; }
    public double TechStructureSignal { get; init; }
    public double TechSpatialSignal { get; init; }
    public double TechTemporalSignal { get; init; }

    /// <summary>
    /// Nombre de sliders considérés comme complexes.
    /// </summary>
    public int ComplexSliderCount { get; init; }

    /// <summary>
    /// Nombre de situations où des sliders présentent
    /// une proximité spatiale inhabituelle.
    /// </summary>
    public int SliderSpatialOverlapCount { get; init; }

    /// <summary>
    /// Nombre de transitions présentant un changement
    /// de direction particulièrement marqué.
    /// </summary>
    public int SharpTechTransitionCount { get; init; }


    // ============================================================
    // 6. READ
    // ============================================================

    /// <summary>
    /// Nombre de cercles considérés comme participant
    /// à une situation de Read.
    /// </summary>
    public int ReadObjectCount { get; init; }

    /// <summary>
    /// Proportion des cercles concernés par le Read.
    /// </summary>
    public double ReadRatio { get; init; }

    /// <summary>
    /// Score Read sur 100.
    ///
    /// Le score combine :
    /// - 40 % densité temporelle,
    /// - 35 % surcharge visuelle,
    /// - 25 % persistance.
    /// </summary>
    public double ReadScore { get; init; }

    /// <summary>
    /// Niveau qualitatif du Read :
    /// Low / Medium / High.
    /// </summary>
    public string ReadLevel { get; init; } = "Low";

    /// <summary>
    /// Signal moyen de densité temporelle du Read.
    /// </summary>
    public double ReadDensitySignal { get; init; }

    /// <summary>
    /// Signal moyen de surcharge visuelle du Read.
    /// </summary>
    public double ReadClutterSignal { get; init; }

    /// <summary>
    /// Signal moyen de persistance visuelle du Read.
    /// </summary>
    public double ReadPersistenceSignal { get; init; }
    
    /// <summary>
    /// 
    /// </summary>
    public double ReadCSSignal { get; init; }


    // ============================================================
    // 7. SPEED
    // ============================================================

    /// <summary>
    /// Score Speed sur 100.
    ///
    /// Le score actuel combine :
    /// - proportion d'objets rapides,
    /// - densité locale,
    /// - influence de l'AR.
    /// </summary>
    public double SpeedScore { get; init; }

    /// <summary>
    /// Niveau qualitatif du Speed :
    /// Low / Medium / High.
    /// </summary>
    public string SpeedLevel { get; init; } = "Low";

    /// <summary>
    /// Proportion des transitions considérées comme rapides.
    /// </summary>
    public double SpeedRatio { get; init; }

    /// <summary>
    /// Proportion des cercles appartenant à une transition rapide.
    /// </summary>
    public double SpeedFastObjectRatio { get; init; }

    /// <summary>
    /// Signal de densité utilisé pour le calcul Speed.
    /// </summary>
    public double SpeedDensitySignal { get; init; }

    /// <summary>
    /// Signal Speed provenant de l'AR.
    /// </summary>
    public double SpeedARSignal { get; init; }

    // ============================================================
    // 8. Aim
    // ============================================================

    // Aim V1
    public double AimScore { get; init; }
    public string AimLevel { get; init; } = "Low";
    public double AimDistanceSignal { get; init; }
    public double AimSpeedSignal { get; init; }
    public double AimAngleSignal { get; init; }
    public double AimTemporalSignal { get; init; }


    // ============================================================
    // 9. CLASSIFICATION
    // ============================================================

    /// <summary>
    /// Type principal estimé de la map.
    ///
    /// Valeurs actuelles :
    /// - Stream
    /// - Jump
    /// - Tech
    /// - Classic / Mixed
    /// </summary>
    public string PrimaryType { get; init; } = "Classic / Mixed";
    public string GameplayIdentity { get; set; } = "";


    // ============================================================
    // 10. DONNÉES CONSERVÉES POUR DEBUG / FUTURS ÉCRANS
    // ============================================================

    /// <summary>
    /// Séquences Stream détectées.
    ///
    /// Les index correspondent directement aux HitObjects
    /// de la Beatmap.
    /// </summary>
    public IReadOnlyList<PatternSequence> StreamSequences { get; init; } = [];

    /// <summary>
    /// Séquences Jump détectées.
    ///
    /// Les index correspondent directement aux HitObjects
    /// de la Beatmap.
    /// </summary>
    public IReadOnlyList<PatternSequence> JumpSequences { get; init; } = [];

    /// <summary>
    /// Séquences Burst détectées.
    ///
    /// Les index correspondent directement aux HitObjects
    /// de la Beatmap.
    /// </summary>
    public IReadOnlyList<PatternSequence> BurstSequences { get; init; } = [];

    // V1.1 STYLE
    public GameplayStyleProfile StyleProfile { get; set; } = new();

    private static string BuildGameplayIdentity(
    string primaryType,
    GameplayStyleProfile style)
    {
        return $"{primaryType} {style.PrimaryStyle}";
    }

    public GameplayIdentity Identity { get; init; } = new();
}


// ================================================================
// 11. STRUCTURE D'UNE SÉQUENCE
// ================================================================

/// <summary>
/// Représente une séquence continue d'objets appartenant
/// à une même famille de gameplay.
///
/// StartObjectIndex et EndObjectIndex correspondent aux index
/// présents dans Beatmap.HitObjects.
/// </summary>
public sealed record PatternSequence(
    int StartObjectIndex,
    int EndObjectIndex)
{
    /// <summary>
    /// Nombre d'objets contenus dans la séquence.
    /// </summary>
    public int ObjectCount =>
        EndObjectIndex - StartObjectIndex + 1;

}