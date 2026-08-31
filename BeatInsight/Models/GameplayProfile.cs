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

    public double Confidence { get; init; }
    public IReadOnlyList<GameplaySection> ReadSections { get; init; }
       = Array.Empty<GameplaySection>();

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
    public string TechProfile { get; init; } = "";

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

    public IReadOnlyList<GameplaySection> TechSections { get; init; } = [];


    // ============================================================
    // 6. READ
    // ============================================================

    /// <summary>
    /// Nombre de circles ou sliders-head considérés comme participant
    /// à une situation de Reading.
    /// </summary>
    public int ReadObjectCount { get; init; }

    /// <summary>
    /// Proportion des informations visuelles éligibles concernées
    /// par le Reading avant validation des sections.
    /// </summary>
    public double ReadRatio { get; init; }
    /// <summary>
    /// Proportion des informations visuelles éligibles appartenant
    /// à des sections Reading validées.
    /// </summary>
    public double ReadCoverage { get; init; }
    public string ReadProfile { get; init; } = string.Empty;

    /// <summary>
    /// Niveau qualitatif de l'intensité locale des zones Reading.
    /// Il est distinct de la présence globale représentée par
    /// ReadCoverage.
    /// </summary>
    public string ReadIntensity { get; set; } = "";

    /// <summary>
    /// Score Read sur 100.
    ///
    /// Le score global combine l'intensité locale des zones Reading
    /// et leur présence validée dans la map.
    ///
    /// Reading V1 utilise la densité et le clutter des informations
    /// futures visibles. La persistance et le CS sont neutralisés.
    /// </summary>
    public double ReadScore { get; init; }

    /// <summary>
    /// Niveau qualitatif du Read :
    /// Low / Medium / High.
    /// </summary>
    public string ReadLevel { get; init; } = "Low";

    /// <summary>
    /// Signal moyen de densité des informations futures visibles.
    /// </summary>
    public double ReadDensitySignal { get; init; }

    /// <summary>
    /// Signal moyen de surcharge visuelle du Read.
    /// </summary>
    public double ReadClutterSignal { get; init; }

    /// <summary>
    /// Signal de persistance visuelle du Read. Neutralisé à 0 dans
    /// Reading V1, en attente d'une sémantique future cohérente.
    /// </summary>
    public double ReadPersistenceSignal { get; init; }
    
    /// <summary>
    /// Signal CS conservé pour compatibilité. Neutralisé à 0 en
    /// Reading V1 : le CS ne contribue pas au ReadScore.
    /// </summary>
    public double ReadCSSignal { get; init; }

    /// <summary>
    /// Prévisibilité moyenne des séquences Reading qualifiées.
    /// Cette métrique d'observation ne contribue pas au ReadScore.
    /// </summary>
    public double ReadPredictability { get; init; }

    /// <summary>
    /// Complément de la prévisibilité Reading : 1 - ReadPredictability.
    /// Cette métrique d'observation ne contribue pas au ReadScore.
    /// </summary>
    public double ReadNovelty { get; init; }

    /// <summary>
    /// Régularité temporelle moyenne des séquences Reading qualifiées.
    /// </summary>
    public double ReadTemporalRegularity { get; init; }

    /// <summary>
    /// Régularité spatiale moyenne des séquences Reading qualifiées.
    /// </summary>
    public double ReadSpacingRegularity { get; init; }

    /// <summary>
    /// Répétition directionnelle moyenne des trajectoires Reading qualifiées.
    /// </summary>
    public double ReadTrajectoryRepetition { get; init; }


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

    public int SpeedObjectCount { get; init; }

    public double SpeedCoverage { get; init; }

    public string SpeedProfile { get; init; } = "";
    public string SpeedIntensity { get; set; } = "";
    public IReadOnlyList<GameplaySection> SpeedSections { get; set; }
    = Array.Empty<GameplaySection>();


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
    public double AimCoverage { get; init; }

    public string AimProfile { get; init; } = "";

    public string AimIntensity { get; init; } = "";


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

    /// <summary>
    /// Explications automatiques utilisées par l'interface
    /// pour expliquer les principaux signaux ayant influencé
    /// la classification gameplay.
    /// </summary>
    public IReadOnlyList<string> ClassificationReasons =>
        BuildClassificationReasons()
            .Take(3)
            .ToList();

    private List<string> BuildClassificationReasons()
    {
        List<string> reasons = [];

        // ============================================================
        // PATTERNS
        // ============================================================

        if (JumpRatio >= 0.40)
        {
            reasons.Add(
                $"Jump presence is high ({JumpRatio:P0})");
        }
        else if (JumpRatio >= 0.20)
        {
            reasons.Add(
                $"Jump presence is significant ({JumpRatio:P0})");
        }

        if (StreamRatio >= 0.40)
        {
            reasons.Add(
                $"Stream presence is high ({StreamRatio:P0})");
        }
        else if (StreamRatio >= 0.20)
        {
            reasons.Add(
                $"Stream presence is significant ({StreamRatio:P0})");
        }
        else if (StreamRatio <= 0.05)
        {
            reasons.Add(
                $"Stream presence is very low ({StreamRatio:P0})");
        }

        if (BurstRatio >= 0.08)
        {
            reasons.Add(
                $"Burst presence is significant ({BurstRatio:P0})");
        }

        // ============================================================
        // GAMEPLAY SIGNALS
        // ============================================================

        if (SpeedScore >= 60)
        {
            reasons.Add(
                $"Speed pressure is high ({SpeedScore:F0}/100)");
        }
        else if (SpeedScore >= 40)
        {
            reasons.Add(
                $"Speed pressure is moderate ({SpeedScore:F0}/100)");
        }
        else
        {
            reasons.Add(
                $"Speed pressure is low ({SpeedScore:F0}/100)");
        }

        if (AimScore >= 60)
        {
            reasons.Add(
                $"Aim pressure is high ({AimScore:F0}/100)");
        }
        else if (AimScore >= 40)
        {
            reasons.Add(
                $"Aim pressure is moderate ({AimScore:F0}/100)");
        }

        if (ReadScore >= 60)
        {
            reasons.Add(
                $"Reading demand is high ({ReadScore:F0}/100)");
        }
        else if (ReadScore >= 40)
        {
            reasons.Add(
                $"Reading demand is moderate ({ReadScore:F0}/100)");
        }
        else
        {
            reasons.Add(
                $"Reading demand is low ({ReadScore:F0}/100)");
        }

        if (TechScore >= 60)
        {
            reasons.Add(
                $"Technical pressure is high ({TechScore:F0}/100)");
        }
        else if (TechScore >= 40)
        {
            reasons.Add(
                $"Technical influence is moderate ({TechScore:F0}/100)");
        }

        return reasons;
    }


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

    // ============================================================
    // SECTIONS TEMPORELLES
    // ============================================================

    /// <summary>
    /// Zones temporelles contenant des patterns Stream.
    /// </summary>
    public IReadOnlyList<GameplaySection> StreamSections { get; init; } = [];

    /// <summary>
    /// Zones temporelles contenant des patterns Jump.
    /// </summary>
    public IReadOnlyList<GameplaySection> JumpSections { get; init; } = [];

    /// <summary>
    /// Zones temporelles contenant des patterns Tech.
    /// </summary>
    

    // V1.1 STYLE
    public GameplayStyleProfile StyleProfile { get; set; } = new();

    

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

};



public sealed record GameplaySection(
    string Type,
    int StartObjectIndex,
    int EndObjectIndex,
    double StartTime,
    double EndTime,
    int ObjectCount)
{
    public double Duration =>
        EndTime - StartTime;
}
