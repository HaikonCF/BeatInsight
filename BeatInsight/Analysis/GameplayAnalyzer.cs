using BeatInsight.Models;
using BeatInsight.Parser;
using System.Diagnostics;

namespace BeatInsight.Analysis;

/// <summary>
/// Analyse le gameplay d'une beatmap.
///
/// Cette classe regroupe les différents détecteurs structurels de BeatInsight.
/// Chaque famille possède son propre calcul et produit des signaux indépendants.
///
/// Les analyses actuelles sont :
/// - Stream
/// - Jump
/// - Burst
/// - Tech
/// - Read
/// - Speed
///
/// IMPORTANT : ces analyses décrivent actuellement le gameplay.
/// Elles n'influencent pas directement le Star Rating existant.
/// </summary>
public static class GameplayAnalyzer
{
    // ============================================================
    // 1. CONSTANTES / SEUILS GÉNÉRAUX
    // ============================================================

    /// <summary>
    /// Nombre minimum d'objets nécessaires pour considérer
    /// une séquence comme un pattern.
    /// </summary>
    private const int MinimumSequenceLength = 4;

    /// <summary>
    /// Pour qu'un type devienne le type principal d'une map,
    /// il doit représenter au moins 50 % des cercles analysés.
    /// </summary>
    private const double PrimaryTypeThreshold = 0.40;
    
    private const double SecondaryTypeThreshold = 0.20;


    // ============================================================
    // 2. SEUILS STREAM
    // ============================================================

    /// <summary>
    /// Intervalle minimum entre deux objets d'un stream.
    /// </summary>
    private const double StreamMinimumIntervalMs = 30;

    /// <summary>
    /// Intervalle maximum entre deux objets d'un stream.
    ///
    /// Au-dessus de cette valeur, une succession régulière
    /// ressemble davantage à du Jump qu'à du Stream.
    /// </summary>
    private const double StreamMaximumIntervalMs = 130;

    /// <summary>
    /// Distance maximum entre deux objets d'un stream.
    /// </summary>
    private const double StreamMaximumDistance = 200;

    /// <summary>
    /// Distance moyenne maximum autorisée dans un stream.
    /// </summary>
    private const double StreamMaximumAverageDistance = 115;

    /// <summary>
    /// Variation maximale autorisée entre les intervalles
    /// temporels d'un stream.
    /// </summary>
    private const double StreamMaximumIntervalVariation = 0.50;

    /// <summary>
    /// Angle de rotation maximum entre trois objets consécutifs.
    ///
    /// Un retour presque complet casse la continuité
    /// spatiale attendue d'un stream.
    /// </summary>
    private const double StreamMaximumTurnAngle = 165;


    // ============================================================
    // 3. SEUILS JUMP
    // ============================================================

    /// <summary>
    /// Intervalle minimum entre deux objets d'un Jump.
    /// </summary>
    private const double JumpMinimumIntervalMs = 30;

    /// <summary>
    /// Intervalle maximum entre deux objets d'un Jump.
    /// </summary>
    private const double JumpMaximumIntervalMs = 600;

    /// <summary>
    /// Distance minimum entre deux objets pour considérer
    /// le déplacement comme un Jump.
    /// </summary>
    private const double JumpMinimumDistance = 30;


    // ============================================================
    // 4. SEUILS BURST
    // ============================================================

    /// <summary>
    /// Nombre minimum d'objets dans un Burst.
    /// </summary>
    private const int MinimumBurstLength = 3;

    /// <summary>
    /// Intervalle minimum entre deux objets d'un Burst.
    /// </summary>
    private const double BurstMinimumIntervalMs = 30;

    /// <summary>
    /// Intervalle maximum entre deux objets d'un Burst.
    /// </summary>
    private const double BurstMaximumIntervalMs = 250;

    /// <summary>
    /// Distance maximum entre deux objets d'un Burst.
    /// </summary>
    private const double BurstMaximumDistance = 60;

    /// <summary>
    /// Distance moyenne maximum autorisée dans un Burst.
    /// </summary>
    private const double BurstMaximumAverageDistance = 45;


    // ============================================================
    // 5. SEUILS READ
    // ============================================================

    /// <summary>
    /// Poids de la densité temporelle dans le score Read.
    /// </summary>
    private const double ReadDensityWeight = 0.40;

    /// <summary>
    /// Poids de la surcharge visuelle dans le score Read.
    /// </summary>
    private const double ReadClutterWeight = 0.35;

    /// <summary>
    /// Poids de la persistance visuelle dans le score Read.
    /// </summary>
    private const double ReadPersistenceWeight = 0.25;

    /// <summary>
    /// Nombre minimum d'objets simultanément visibles pour
    /// considérer qu'un cercle participe réellement au Read.
    /// </summary>
    private const int ReadMinimumVisibleObjects = 3;

    /// <summary>
    /// Distance à partir de laquelle deux objets sont considérés
    /// comme suffisamment proches pour participer à la surcharge visuelle.
    /// </summary>
    private const double ReadClutterDistance = 140.0;

    /// <summary>
    /// CS à partir duquel le Read commence à recevoir
    /// un bonus lié à la précision spatiale demandée.
    /// </summary>
    private const double ReadCSBaseline = 4.0;

    /// <summary>
    /// CS auquel le bonus Read atteint son maximum.
    /// </summary>
    private const double ReadCSSaturation = 7.0;

    /// <summary>
    /// Bonus maximum apporté par le CS au Read.
    /// 0.25 = +25 % maximum.
    /// </summary>
    private const double ReadCSMaximumBonus = 0.25;


    // ============================================================
    // 6. ANALYSE PRINCIPALE
    // ============================================================

    /// <summary>
    /// Lance toutes les analyses gameplay de la beatmap.
    ///
    /// Cette méthode :
    /// 1. détecte les patterns,
    /// 2. calcule les ratios,
    /// 3. calcule Tech / Read / Speed,
    /// 4. détermine le type principal,
    /// 5. construit le GameplayProfile final.
    /// </summary>
    public static GameplayProfile Analyze(Beatmap beatmap)
    {
        ArgumentNullException.ThrowIfNull(beatmap);

        IReadOnlyList<HitObject> objects = beatmap.HitObjects;



        // --------------------------------------------------------
        // Préparation des tableaux de présence.
        //
        // Chaque index correspond à un HitObject de la beatmap.
        // --------------------------------------------------------

        bool[] streamObjects = new bool[objects.Count];
        bool[] jumpObjects = new bool[objects.Count];
        bool[] burstObjects = new bool[objects.Count];

        // --------------------------------------------------------
        // Détection des familles de gameplay.
        // --------------------------------------------------------

        List<PatternSequence> streams =
            FindStreams(objects, streamObjects);

        List<PatternSequence> jumps =
            FindJumps(objects, streamObjects, jumpObjects);

        // Un Burst peut exister dans une map Stream, Jump ou Tech.
        // Il est donc volontairement analysé indépendamment.
        List<PatternSequence> bursts =
            FindBursts(
                objects,
                burstObjects,
                streamObjects,
                jumpObjects);


        // --------------------------------------------------------
        // Analyses spécialisées.
        // --------------------------------------------------------

        TechAnalysis tech =
            AnalyzeTech(objects);

        ReadAnalysis read =
            AnalyzeRead(beatmap, objects);

        SpeedAnalysis speed =
            AnalyzeSpeed(objects, beatmap);

        AimAnalysis aim = 
            AnalyzeAim(objects, beatmap);

        GameplayStyleProfile style =
            AnalyzeGameplayStyle(
             aim,
             speed,
             tech,
             read);

        // --------------------------------------------------------
        // Statistiques générales.
        // --------------------------------------------------------

        int analysedCircles =
            objects.Count(IsCircle);

        int streamObjectCount =
            streamObjects.Count(value => value);

        int jumpObjectCount =
            jumpObjects.Count(value => value);

        int burstObjectCount =
            burstObjects.Count(value => value);

        // --------------------------------------------------------
        // Ratios.
        // --------------------------------------------------------

        double streamRatio =
            CalculateRatio(streamObjectCount, analysedCircles);

        double jumpRatio =
            CalculateRatio(jumpObjectCount, analysedCircles);

        double burstRatio =
            CalculateRatio(burstObjectCount, analysedCircles);

        double techRatio =
            CalculateRatio(tech.TechObjectCount, analysedCircles);

        // --------------------------------------------------------
        // Classification globale de la map.
        // --------------------------------------------------------

        string primaryType =
            DeterminePrimaryType(
                streamRatio,
                jumpRatio,
                tech.Score);
        string gameplayIdentity =
            BuildGameplayIdentity(
                primaryType,
                style);

        // --------------------------------------------------------
        // Identity
        // --------------------------------------------------------

        GameplayIdentity identity =
            AnalyzeGameplayIdentity(
                primaryType,
                aim,
                speed,
                tech,
                read);

        // --------------------------------------------------------
        // Construction du profil final.
        // --------------------------------------------------------

        GameplayProfile profile = new()
        {
            // ----------------------------
            // Statistiques générales
            // ----------------------------

            AnalysedCircleCount = analysedCircles,

            // ----------------------------
            // Stream
            // ----------------------------

            StreamObjectCount = streamObjectCount,
            StreamSequenceCount = streams.Count,
            StreamRatio = streamRatio,
            StreamSequences = streams,

            // ----------------------------
            // Jump
            // ----------------------------

            JumpObjectCount = jumpObjectCount,
            JumpSequenceCount = jumps.Count,
            JumpRatio = jumpRatio,
            JumpSequences = jumps,

            // ----------------------------
            // Burst
            // ----------------------------

            BurstObjectCount = burstObjectCount,
            BurstSequenceCount = bursts.Count,
            LongestBurstLength =
                bursts.Count == 0
                    ? 0
                    : bursts.Max(sequence => sequence.ObjectCount),
            BurstRatio = burstRatio,
            BurstSequences = bursts,

            // ----------------------------
            // Tech
            // ----------------------------

            TechObjectCount = tech.TechObjectCount,
            TechRatio = techRatio,
            TechScore = tech.Score,
            TechLevel = GetTechLevel(tech.Score),
            TechTransitionSignal = tech.TransitionSignal,
            TechStructureSignal = tech.StructureSignal,
            TechSpatialSignal = tech.SpatialSignal,
            TechTemporalSignal = tech.TemporalSignal,

            ComplexSliderCount =
                tech.ComplexSliderCount,

            SliderSpatialOverlapCount =
                tech.SliderSpatialOverlapCount,

            SharpTechTransitionCount =
                tech.SharpTransitionCount,

            // ----------------------------
            // Read
            // ----------------------------

            ReadObjectCount = 
                read.ReadObjectCount,
            ReadRatio = 
                read.Ratio,
            ReadScore = 
                read.Score,
            ReadLevel = 
                GetReadLevel(read.Score),

            ReadDensitySignal =
                read.DensitySignal,

            ReadClutterSignal =
                read.ClutterSignal,

            ReadPersistenceSignal =
                read.PersistenceSignal,

            ReadCSSignal =
                read.CSSignal,

            // ----------------------------
            // Speed
            // ----------------------------

            SpeedScore = speed.Score,
            SpeedLevel = GetSpeedLevel(speed.Score),
            SpeedRatio = speed.SpeedRatio,
            SpeedFastObjectRatio =speed.FastObjectRatio,
            SpeedDensitySignal =speed.DensitySignal,
            SpeedARSignal =speed.ARSignal,

            // ----------------------------
            // Aim
            // ----------------------------

            AimScore = aim.Score,
            AimLevel = GetAimLevel(aim.Score),
            AimDistanceSignal = aim.DistanceSignal,
            AimSpeedSignal = aim.SpeedSignal,
            AimAngleSignal = aim.AngleSignal,
            AimTemporalSignal = aim.TemporalSignal,

            // ----------------------------
            // Classification
            // ----------------------------

            PrimaryType = primaryType,
            GameplayIdentity = gameplayIdentity,
            StyleProfile = style,
            Identity = identity
        };

        // Debug console uniquement.
        WriteDebug(profile);

        return profile;
    }



    // ============================================================
    // 7. STREAM
    // ============================================================

    /// <summary>
    /// Recherche les séquences de cercles pouvant être considérées
    /// comme des Streams.
    /// </summary>
    private static List<PatternSequence> FindStreams(
        IReadOnlyList<HitObject> objects,
        bool[] streamObjects)
    {
        List<PatternSequence> sequences = [];

        for (int start = 0; start < objects.Count; start++)
        {
            if (!IsCircle(objects[start]))
                continue;

            int end = start;

            // Continue tant que les objets suivants respectent
            // les contraintes temporelles et spatiales d'un Stream.
            while (end + 1 < objects.Count
                && IsCircle(objects[end + 1])
                && IsStreamLink(
                    objects[end],
                    objects[end + 1]))
            {
                end++;
            }

            // Une séquence doit avoir au minimum 4 objets
            // et rester suffisamment régulière.
            if (end - start + 1 >= MinimumSequenceLength
                && IsRegularStream(objects, start, end))
            {
                MarkRange(
                    streamObjects,
                    start,
                    end);

                sequences.Add(
                    new PatternSequence(start, end));

                // On saute directement à la fin du Stream trouvé.
                start = end;
            }
        }

        return sequences;
    }

    /// <summary>
    /// Vérifie si deux objets peuvent appartenir au même Stream.
    /// </summary>
    private static bool IsStreamLink(
        HitObject previous,
        HitObject current)
    {
        double interval =
            current.Time - previous.Time;

        double distance =
            Distance(previous, current);

        return interval >= StreamMinimumIntervalMs
            && interval <= StreamMaximumIntervalMs
            && distance > 0
            && distance <= StreamMaximumDistance;
    }

    /// <summary>
    /// Vérifie que les intervalles d'un Stream restent suffisamment réguliers
    /// et que ses déplacements restent compacts.
    /// </summary>
    private static bool IsRegularStream(
        IReadOnlyList<HitObject> objects,
        int start,
        int end)
    {
        List<double> intervals = [];

        for (int i = start + 1; i <= end; i++)
        {
            intervals.Add(
                objects[i].Time -
                objects[i - 1].Time);
        }

        double averageInterval =
            intervals.Average();

        if (averageInterval <= 0)
            return false;

        // --------------------------------------------------------
        // Régularité temporelle
        // --------------------------------------------------------

        double largestDeviation =
            intervals.Max(interval =>
                Math.Abs(interval - averageInterval));

        if (largestDeviation / averageInterval
            > StreamMaximumIntervalVariation)
        {
            return false;
        }

        // --------------------------------------------------------
        // Compacité spatiale
        // --------------------------------------------------------

        double averageDistance = 0;

        for (int i = start + 1; i <= end; i++)
        {
            averageDistance +=
                Distance(
                    objects[i - 1],
                    objects[i]);
        }

        averageDistance /= intervals.Count;

        if (averageDistance >
            StreamMaximumAverageDistance)
        {
            return false;
        }

        // --------------------------------------------------------
        // Continuité de trajectoire
        // --------------------------------------------------------

        for (int i = start + 1; i < end; i++)
        {
            if (GetTurnAngle(
                    objects[i - 1],
                    objects[i],
                    objects[i + 1])
                > StreamMaximumTurnAngle)
            {
                return false;
            }
        }

        return true;
    }


    // ============================================================
    // 8. JUMP
    // ============================================================

    /// <summary>
    /// Recherche les séquences de Jump qui ne sont pas déjà
    /// identifiées comme Stream.
    /// </summary>
    private static List<PatternSequence> FindJumps(
        IReadOnlyList<HitObject> objects,
        bool[] streamObjects,
        bool[] jumpObjects)
    {
        List<PatternSequence> sequences = [];

        for (int start = 0; start < objects.Count; start++)
        {
            // Les objets Stream sont exclus du détecteur Jump.
            if (!IsCircle(objects[start])
                || streamObjects[start])
            {
                continue;
            }

            int end = start;

            while (end + 1 < objects.Count
                && IsCircle(objects[end + 1])
                && !streamObjects[end + 1]
                && IsJumpLink(
                    objects[end],
                    objects[end + 1]))
            {
                end++;
            }

            if (end - start + 1 >= MinimumSequenceLength)
            {
                MarkRange(
                    jumpObjects,
                    start,
                    end);

                sequences.Add(
                    new PatternSequence(start, end));

                start = end;
            }
        }

        return sequences;
    }

    /// <summary>
    /// Vérifie si le déplacement entre deux objets correspond
    /// aux critères d'un Jump.
    /// </summary>
    private static bool IsJumpLink(
        HitObject previous,
        HitObject current)
    {
        double interval =
            current.Time - previous.Time;

        double distance =
            Distance(previous, current);

        return interval >= JumpMinimumIntervalMs
            && interval <= JumpMaximumIntervalMs
            && distance >= JumpMinimumDistance;
    }


    // ============================================================
    // 9. BURST
    // ============================================================

    /// <summary>
    /// Recherche les petits groupes rapides et compacts de cercles.
    /// </summary>
    private static List<PatternSequence> FindBursts(
    IReadOnlyList<HitObject> objects,
    bool[] burstObjects,
    bool[] streamObjects,
    bool[] jumpObjects)
    {
        List<PatternSequence> sequences = [];

        for (int start = 0; start < objects.Count; start++)
        {
            if (!IsCircle(objects[start])
                || streamObjects[start]
                || jumpObjects[start])
            {
                continue;
            }

            int end = start;

            while (end + 1 < objects.Count
                && IsCircle(objects[end + 1])
                && IsBurstLink(
                    objects[end],
                    objects[end + 1]))
            {
                end++;
            }

            if (end - start + 1 >= MinimumBurstLength
                && IsCompactBurst(
                    objects,
                    start,
                    end))
            {
                MarkRange(
                    burstObjects,
                    start,
                    end);

                sequences.Add(
                    new PatternSequence(start, end));

                start = end;
            }
        }

        return sequences;
    }

    /// <summary>
    /// Vérifie si deux objets peuvent appartenir au même Burst.
    /// </summary>
    private static bool IsBurstLink(
        HitObject previous,
        HitObject current)
    {
        double interval =
            current.Time - previous.Time;

        double distance =
            Distance(previous, current);

        return interval >= BurstMinimumIntervalMs
            && interval <= BurstMaximumIntervalMs
            && distance <= BurstMaximumDistance;
    }

    /// <summary>
    /// Vérifie que la distance moyenne d'un Burst reste faible.
    /// </summary>
    private static bool IsCompactBurst(
        IReadOnlyList<HitObject> objects,
        int start,
        int end)
    {
        double totalDistance = 0;
        int movementCount = 0;

        for (int i = start + 1; i <= end; i++)
        {
            totalDistance +=
                Distance(
                    objects[i - 1],
                    objects[i]);

            movementCount++;
        }

        return movementCount > 0
            && totalDistance / movementCount
                <= BurstMaximumAverageDistance;
    }


    // ============================================================
    // 10. TECH
    // ============================================================

    /// <summary>
    /// Analyse les structures Tech :
    /// - sliders complexes,
    /// - superpositions spatiales,
    /// - transitions brusques.
    ///
    /// TechObjectCount représente les cercles impliqués dans les
    /// transitions Tech détectées.
    /// </summary>
    private static TechAnalysis AnalyzeTech(
        IReadOnlyList<HitObject> objects)
    {
        bool[] techObjects =
            new bool[objects.Count];

        // --------------------------------------------------------
        // Sliders
        // --------------------------------------------------------

        List<HitObject> sliders =
            objects.Where(IsSlider).ToList();

        int complexSliderCount =
            sliders.Count(IsComplexSlider);

        int sliderSpatialOverlapCount =
            CountSliderSpatialOverlaps(sliders);

        // --------------------------------------------------------
        // Transitions
        // --------------------------------------------------------

        int transitionCount = 0;
        int sharpTransitionCount = 0;
        int fastTransitionCount = 0;
        int structuralTransitions = 0;
        int alternatingTransitions = 0;

        for (int i = 1; i < objects.Count - 1; i++)
        {
            HitObject previous =
                objects[i - 1];

            HitObject current =
                objects[i];

            HitObject next =
                objects[i + 1];

            // Les spinners ne participent pas à cette analyse.
            if (IsSpinner(previous)
                || IsSpinner(current)
                || IsSpinner(next))
            {
                continue;
            }

            double firstInterval =
                current.Time - previous.Time;

            double secondInterval =
                next.Time - current.Time;

            if (firstInterval <= 0
                || secondInterval <= 0
                || firstInterval > 220
                || secondInterval > 220)
            {
                continue;
            }

            // Les très grands déplacements sont davantage
            // caractéristiques du Jump.
            if (Distance(previous, current) > 160
                || Distance(current, next) > 160)
            {
                continue;
            }

            transitionCount++;

            double turnAngle =
                GetTurnAngle(
                    previous,
                    current,
                    next);

            // --------------------------------------------------------
            // STRUCTURE TECH V1.2
            // --------------------------------------------------------

            // Une transition devient structurelle lorsqu'elle implique
            // un changement de direction suffisamment important.
            if (turnAngle >= TechStructureAngle)
            {
                structuralTransitions++;
            }

            // --------------------------------------------------------
            // ALTERNANCE DE DIRECTION
            // --------------------------------------------------------

            if (i >= 2 && i + 1 < objects.Count)
            {
                double previousAngle =
                    GetTurnAngle(
                        objects[i - 2],
                        objects[i - 1],
                        objects[i]);

                if (previousAngle >= TechStructureAngle
                    && turnAngle >= TechStructureAngle)
                {
                    if (firstInterval > 0
                        && secondInterval > 0
                        && firstInterval <= TechStructureMaximumInterval
                        && secondInterval <= TechStructureMaximumInterval)
                    {
                        alternatingTransitions++;
                    }
                }
            }
            if (turnAngle >= 150)
            {
                sharpTransitionCount++;

                // Les trois objets entourant la transition
                // sont marqués comme objets Tech.
                techObjects[i - 1] = true;
                techObjects[i] = true;
                techObjects[i + 1] = true;
            }

            if (firstInterval <= 125
                && secondInterval <= 125)
            {
                fastTransitionCount++;
            }
        }

        // --------------------------------------------------------
        // Signaux intermédiaires
        // --------------------------------------------------------

        double complexSliderRatio =
            CalculateRatio(
                complexSliderCount,
                sliders.Count);

        double sharpTransitionRatio =
            CalculateRatio(
                sharpTransitionCount,
                transitionCount);

        double fastTransitionRatio =
            CalculateRatio(
                fastTransitionCount,
                transitionCount);
        // --------------------------------------------------------
        // SIGNAUX TECH V1.1
        // --------------------------------------------------------

        // Proportion de transitions réellement brusques.
        double transitionSignal =
            CalculateRatio(
                sharpTransitionCount,
                transitionCount);

        // Proportion de sliders complexes.
        double sliderSignal =
            CalculateRatio(
                complexSliderCount,
                sliders.Count);

        // Proportion de superpositions spatiales.
        double spatialSignal =
            sliders.Count == 0
                ? 0
                : Math.Clamp(
                    (double)sliderSpatialOverlapCount
                    / Math.Max(1.0, sliders.Count),
                    0,
                    1);

        // Pression temporelle des transitions.
        double temporalSignal =
            fastTransitionRatio;

        // --------------------------------------------------------
        // STRUCTURE SIGNAL
        // --------------------------------------------------------

        double structureSignal =
            CalculateRatio(
                structuralTransitions,
                transitionCount);

        // --------------------------------------------------------
        // ALTERNANCE SIGNAL
        // --------------------------------------------------------

        double alternatingSignal =
            CalculateRatio(
                alternatingTransitions,
                transitionCount);

        double overlapSignal =
            sliders.Count == 0
                ? 0
                : Math.Min(
                    1,
                    sliderSpatialOverlapCount
                    / Math.Max(
                        1.0,
                        sliders.Count * 0.25));

        double rawStructureSignal =
      Math.Clamp(
          structureSignal * 0.60
          + alternatingSignal * 0.40,
          0,
          1);

        double structureTemporalModifier =
            0.30
            + temporalSignal * 0.70;

        double structureCombinedSignal =
            rawStructureSignal
            * structureTemporalModifier;

        // --------------------------------------------------------
        // Normalisation
        // --------------------------------------------------------

        double complexSliderSignal =
            NormalizeAboveBaseline(
                complexSliderRatio,
                0.20,
                0.70);

        double sharpTransitionSignal =
            NormalizeAboveBaseline(
                sharpTransitionRatio,
                0.15,
                0.50);

        // --------------------------------------------------------
        // Score Tech
        // --------------------------------------------------------

        double score =
            (
                  sharpTransitionSignal * 0.20
                + structureCombinedSignal * 0.55
                + complexSliderSignal * 0.10
                + overlapSignal * 0.15
            )
            * (0.50 + fastTransitionRatio * 0.50);

        score =
            Math.Clamp(score, 0, 1) * 100;

        int techObjectCount =
            techObjects.Count(value => value);

        return new TechAnalysis(
            score,
            techObjectCount,
            complexSliderCount,
            sliderSpatialOverlapCount,
            sharpTransitionCount,
            transitionSignal,
            structureCombinedSignal,
            spatialSignal,
            temporalSignal);
    }

    /// <summary>
    /// Compte les sliders dont les points de contrôle se rapprochent
    /// suffisamment dans une fenêtre temporelle locale.
    /// </summary>
    private static int CountSliderSpatialOverlaps(
        IReadOnlyList<HitObject> sliders)
    {
        int overlaps = 0;

        for (int firstIndex = 0;
             firstIndex < sliders.Count;
             firstIndex++)
        {
            for (int secondIndex = firstIndex + 1;
                 secondIndex < sliders.Count;
                 secondIndex++)
            {
                HitObject first =
                    sliders[firstIndex];

                HitObject second =
                    sliders[secondIndex];

                // Au-delà de 700 ms, les deux sliders sont
                // considérés comme trop éloignés temporellement.
                if (second.Time - first.Time > 700)
                    break;

                if (SliderAnchorsAreClose(
                    first,
                    second))
                {
                    overlaps++;
                }
            }
        }

        return overlaps;
    }

    /// <summary>
    /// Vérifie si deux sliders possèdent des points de contrôle proches.
    /// </summary>
    private static bool SliderAnchorsAreClose(
        HitObject first,
        HitObject second)
    {
        if (first.SliderControlPoints.Count == 0
            || second.SliderControlPoints.Count == 0)
        {
            return false;
        }

        foreach (SliderControlPoint firstAnchor
                 in first.SliderControlPoints)
        {
            foreach (SliderControlPoint secondAnchor
                     in second.SliderControlPoints)
            {
                double x =
                    secondAnchor.X - firstAnchor.X;

                double y =
                    secondAnchor.Y - firstAnchor.Y;

                double distance =
                    Math.Sqrt(x * x + y * y);

                if (distance <= 35)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Détermine si un slider possède une structure suffisamment
    /// complexe pour être considéré comme signal Tech.
    /// </summary>
    private static bool IsComplexSlider(
        HitObject hitObject)
    {
        if (!IsSlider(hitObject))
            return false;

        return hitObject.SliderCurveType is "C" or "P"
            || hitObject.SliderControlPoints.Count >= 3;
    }

    // ============================================================
    // SEUILS TECH V1.1
    // ============================================================

    /// <summary>
    /// Angle à partir duquel une transition est considérée comme brusque.
    /// </summary>
    private const double TechSharpAngle = 150.0;

    /// <summary>
    /// Distance maximale entre les objets d'une structure Tech locale.
    /// </summary>
    private const double TechCompactDistance = 160.0;

    /// <summary>
    /// Intervalle maximal permettant de considérer une transition
    /// comme temporellement compacte.
    /// </summary>
    private const double TechFastInterval = 125.0;

    /// <summary>
    /// Distance maximale entre deux ancres de sliders
    /// pour considérer une proximité spatiale.
    /// </summary>
    private const double TechSpatialDistance = 35.0;

    // ============================================================
    // TECH V1.2 — STRUCTURE
    // ============================================================

    /// <summary>
    /// Nombre minimum de transitions nécessaires pour analyser
    /// une structure Tech locale.
    /// </summary>
    private const int TechStructureMinimumTransitions = 3;

    /// <summary>
    /// Angle minimum pour considérer un changement de direction
    /// comme structurellement marqué.
    /// </summary>
    private const double TechStructureAngle = 110.0;

    /// <summary>
    /// Angle à partir duquel on considère qu'il y a un
    /// changement de direction très fort.
    /// </summary>
    private const double TechStructureSharpAngle = 150.0;

    /// <summary>
    /// Distance maximale permettant de considérer deux mouvements
    /// comme appartenant à une structure compacte.
    /// </summary>
    private const double TechStructureCompactDistance = 160.0;

    /// <summary>
    /// Intervalle maximum entre deux transitions d'une structure.
    /// </summary>
    private const double TechStructureMaximumInterval = 220.0;


    // ============================================================
    // 11. READ
    // ============================================================

    /// <summary>
    /// Analyse la difficulté de lecture de la map.
    ///
    /// Le Read repose sur trois composantes :
    ///
    /// 40 % : densité temporelle
    /// 35 % : surcharge visuelle
    /// 25 % : persistance
    ///
    /// Le score décrit donc la pression de lecture et non
    /// simplement la vitesse de la map.
    /// </summary>
    private static ReadAnalysis AnalyzeRead(
        Beatmap beatmap,
        IReadOnlyList<HitObject> objects)
    {
        if (objects.Count == 0)
            return new ReadAnalysis(
                0, 0, 0, 0, 0, 0, 0);

        double approachTime =
            GetApproachTime(beatmap.AR);

        double csSignal =
            CalculateReadCSSignal(beatmap.CS);

        int analysedCircles =
            objects.Count(IsCircle);

        if (analysedCircles == 0)
            return new ReadAnalysis(0, 0, 0, 0, 0, 0, 0);

        int readObjectCount = 0;

        double totalDensitySignal = 0;
        double totalClutterSignal = 0;
        double totalPersistenceSignal = 0;

        // --------------------------------------------------------
        // Analyse objet par objet.
        // --------------------------------------------------------

        for (int i = 0;
             i < objects.Count;
             i++)
        {
            if (!IsCircle(objects[i]))
                continue;

            double currentTime =
                objects[i].Time;

            int visibleObjects = 0;
            int clutteredObjects = 0;

            double totalObjectAge = 0;
            int ageCount = 0;

            // On regarde les 50 objets précédents au maximum.
            for (int j = Math.Max(0, i - 50);
                 j <= i;
                 j++)
            {
                if (!IsCircle(objects[j]))
                    continue;

                double age =
                    currentTime - objects[j].Time;

                // L'objet doit être visible dans la fenêtre
                // déterminée par l'AR.
                if (age < 0
                    || age > approachTime)
                {
                    continue;
                }

                visibleObjects++;

                // Plus l'objet est ancien dans la fenêtre,
                // plus sa persistance visuelle est importante.
                double persistence =
                    age / approachTime;

                totalObjectAge += persistence;
                ageCount++;

                // ------------------------------------------------
                // Surcharge spatiale
                // ------------------------------------------------

                if (j != i)
                {
                    double distance =
                        Distance(
                            objects[j],
                            objects[i]);

                    if (distance <= ReadClutterDistance)
                        clutteredObjects++;
                }
            }

            // ----------------------------------------------------
            // 1. DENSITÉ TEMPORELLE
            // ----------------------------------------------------

            double densitySignal =
                Math.Clamp(
                    (visibleObjects - 2) / 5.0,
                    0,
                    1);

            // ----------------------------------------------------
            // 2. SURCHARGE VISUELLE
            // ----------------------------------------------------

            double clutterSignal =
                Math.Clamp(
                    clutteredObjects / 3.0,
                    0,
                    1);

            // ----------------------------------------------------
            // 3. PERSISTANCE
            // ----------------------------------------------------

            double persistenceSignal = 0;

            if (ageCount > 0)
            {
                double averagePersistence =
                    totalObjectAge / ageCount;

                persistenceSignal =
                    Math.Clamp(
                        averagePersistence,
                        0,
                        1);
            }

            // ----------------------------------------------------
            // Score Read local
            // ----------------------------------------------------

            double localReadScore =
                densitySignal * ReadDensityWeight
                + clutterSignal * ReadClutterWeight
                + persistenceSignal * ReadPersistenceWeight;

            // Variable conservée volontairement :
            // elle représente le Read local de cet objet.
            _ = localReadScore;

            // Un objet entre dans le ratio Read lorsqu'au
            // moins 3 objets sont simultanément visibles.
            if (visibleObjects >= ReadMinimumVisibleObjects)
            {
                readObjectCount++;

                totalDensitySignal +=
                    densitySignal;

                totalClutterSignal +=
                    clutterSignal;

                totalPersistenceSignal +=
                    persistenceSignal;
            }
        }

        // --------------------------------------------------------
        // Ratio global
        // --------------------------------------------------------

        double readRatio =
            CalculateRatio(
                readObjectCount,
                analysedCircles);

        // --------------------------------------------------------
        // Moyennes des trois signaux
        // --------------------------------------------------------

        double averageDensitySignal = 0;
        double averageClutterSignal = 0;
        double averagePersistenceSignal = 0;

        if (readObjectCount > 0)
        {
            averageDensitySignal =
                totalDensitySignal / readObjectCount;

            averageClutterSignal =
                totalClutterSignal / readObjectCount;

            averagePersistenceSignal =
                totalPersistenceSignal / readObjectCount;
        }

        // --------------------------------------------------------
        // Score Read final
        // --------------------------------------------------------

        double baseScore =
            averageDensitySignal * ReadDensityWeight
            + averageClutterSignal * ReadClutterWeight
            + averagePersistenceSignal * ReadPersistenceWeight;

        // Le CS agit comme un modificateur.
        // Il ne peut pas créer du Read à lui seul.
        double csModifier =
            1.0 + csSignal * ReadCSMaximumBonus;

        double score =
            baseScore * csModifier;

        score =
            Math.Clamp(score, 0, 1) * 100.0;

        return new ReadAnalysis(
            readObjectCount,
            readRatio,
            score,
            averageDensitySignal,
            averageClutterSignal,
            averagePersistenceSignal,
            csSignal);
    }

    /// <summary>
    /// Convertit l'AR en durée approximative pendant laquelle
    /// les objets restent visibles.
    /// </summary>
    private static double GetApproachTime(
        double ar)
    {
        ar = Math.Clamp(ar, 0, 10);

        if (ar < 5)
            return 1800 - 120 * ar;

        return 1200 - 150 * (ar - 5);
    }

    /// <summary>
    /// Convertit le CS en signal de précision spatiale pour le Read.
    ///
    /// CS <= 4 : aucun bonus.
    /// CS >= 7 : bonus maximal.
    /// Entre les deux : interpolation linéaire.
    /// </summary>
    private static double CalculateReadCSSignal(double cs)
    {
        if (cs <= ReadCSBaseline)
            return 0;

        if (cs >= ReadCSSaturation)
            return 1;

        return Math.Clamp(
            (cs - ReadCSBaseline)
            / (ReadCSSaturation - ReadCSBaseline),
            0,
            1);
    }

    /// <summary>
    /// Convertit le score Read en niveau lisible.
    /// </summary>
    private static string GetReadLevel(
        double score)
    {
        return score switch
        {
            >= 70 => "High",
            >= 40 => "Medium",
            _ => "Low"
        };
    }


    // ============================================================
    // 12. SPEED
    // ============================================================

    /// <summary>
    /// Analyse la pression de vitesse de la map.
    ///
    /// Le score Speed actuel combine :
    /// - proportion d'objets rapides,
    /// - densité locale,
    /// - AR.
    /// </summary>
    private static SpeedAnalysis AnalyzeSpeed(IReadOnlyList<HitObject> objects,Beatmap beatmap)
    {
        List<HitObject> circles =
            objects
                .Where(IsCircle)
                .ToList();

        if (circles.Count < 2)
        {
            return new SpeedAnalysis(
                0,
                0,
                0,
                0,
                0);
        }

        bool[] fastObjects =
            new bool[circles.Count];

        int fastTransitions = 0;
        int totalTransitions = 0;

        // 125 ms correspond à environ 8 objets/seconde.
        const double fastInterval = 125;

        // --------------------------------------------------------
        // Détection des transitions rapides.
        // --------------------------------------------------------

        for (int i = 1;
             i < circles.Count;
             i++)
        {
            double interval =
                circles[i].Time -
                circles[i - 1].Time;

            if (interval <= 0)
                continue;

            totalTransitions++;

            if (interval <= fastInterval)
            {
                fastTransitions++;

                // Les deux objets de la transition sont
                // considérés comme objets Speed.
                fastObjects[i] = true;
                fastObjects[i - 1] = true;
            }
        }

        double fastRatio =
            totalTransitions == 0
                ? 0
                : (double)fastTransitions
                  / totalTransitions;

        double fastObjectRatio =
            (double)fastObjects.Count(value => value)
            / circles.Count;

        // --------------------------------------------------------
        // Densité locale.
        // --------------------------------------------------------

        double densitySignal =
            CalculateSpeedDensitySignal(circles);

        // --------------------------------------------------------
        // Influence de l'AR.
        // --------------------------------------------------------

        double arSignal =
            CalculateSpeedARSignal(beatmap.AR);

        // --------------------------------------------------------
        // Score Speed final.
        // --------------------------------------------------------

        double score =
              fastObjectRatio * 0.50
            + densitySignal * 0.30
            + arSignal * 0.20;

        score =
            Math.Clamp(score, 0, 1) * 100;

        return new SpeedAnalysis(
            score,
            fastRatio,
            fastObjectRatio,
            densitySignal,
            arSignal);
    }

    /// <summary>
    /// Mesure la proportion de fenêtres d'une seconde contenant
    /// au moins 8 cercles.
    /// </summary>
    private static double CalculateSpeedDensitySignal(
        IReadOnlyList<HitObject> circles)
    {
        if (circles.Count < 2)
            return 0;

        double firstTime =
            circles.First().Time;

        double lastTime =
            circles.Last().Time;

        if (lastTime <= firstTime)
            return 0;

        int denseWindows = 0;
        int totalWindows = 0;

        for (double windowStart = firstTime;
             windowStart < lastTime;
             windowStart += 1000)
        {
            double windowEnd =
                windowStart + 1000;

            int count = 0;

            foreach (HitObject circle in circles)
            {
                if (circle.Time >= windowStart
                    && circle.Time < windowEnd)
                {
                    count++;
                }
            }

            totalWindows++;

            // 8 objets/seconde = début d'une véritable
            // pression Speed.
            if (count >= 8)
                denseWindows++;
        }

        if (totalWindows == 0)
            return 0;

        return Math.Clamp(
            (double)denseWindows / totalWindows,
            0,
            1);
    }

    /// <summary>
    /// Convertit l'AR en signal Speed.
    ///
    /// AR <= 7 : aucune contribution.
    /// AR >= 10 : contribution maximale.
    /// Entre les deux : interpolation linéaire.
    /// </summary>
    private static double CalculateSpeedARSignal(
        double ar)
    {
        if (ar <= 7)
            return 0;

        if (ar >= 10)
            return 1;

        return (ar - 7) / 3.0;
    }

    /// <summary>
    /// Convertit le score Speed en niveau lisible.
    /// </summary>
    private static string GetSpeedLevel(
        double score)
    {
        return score switch
        {
            >= 70 => "High",
            >= 40 => "Medium",
            _ => "Low"
        };
    }

    // =============================================================
    // AIM V1
    // =============================================================
    //
    // Aim mesure la pression de mouvement demandée par la map.
    // Il repose sur 3 signaux :
    //
    // 45 % Distance
    // 30 % Vitesse de déplacement
    // 25 % Changements de direction
    //
    // L'objectif V1 est de mesurer la quantité de mouvement nécessaire,
    // sans encore utiliser le Star Rating.
    // =============================================================

    private const double AimDistanceBaseline = 80.0;
    private const double AimDistanceSaturation = 180.0;

    private const double AimSpeedBaseline = 300.0;
    private const double AimSpeedSaturation = 700.0;

    private const double AimDistanceWeight = 0.45;
    private const double AimSpeedWeight = 0.30;
    private const double AimAngleWeight = 0.25;

    private static AimAnalysis AnalyzeAim(
        IReadOnlyList<HitObject> objects,
        Beatmap beatmap)
    {
        List<HitObject> circles = objects
            .Where(IsCircle)
            .ToList();

        if (circles.Count < 2)
            return new AimAnalysis(0, 0, 0, 0, 0);

        double totalDistance = 0;
        double totalMovementSpeed = 0;

        int movementCount = 0;

        // ---------------------------------------------------------
        // DISTANCE + VITESSE
        // ---------------------------------------------------------

        for (int i = 1; i < circles.Count; i++)
        {
            HitObject previous = circles[i - 1];
            HitObject current = circles[i];

            double distance =
                Distance(previous, current);

            double interval =
                current.Time - previous.Time;

            if (interval <= 0)
                continue;

            double movementSpeed =
                distance / (interval / 1000.0);

            totalDistance += distance;
            totalMovementSpeed += movementSpeed;

            movementCount++;
        }

        if (movementCount == 0)
            return new AimAnalysis(0, 0, 0, 0, 0);

        double averageDistance =
            totalDistance / movementCount;

        double averageMovementSpeed =
            totalMovementSpeed / movementCount;


        // ---------------------------------------------------------
        // 1. DISTANCE
        // ---------------------------------------------------------
        //
        // Mesure l'amplitude moyenne des déplacements.
        //

        double distanceSignal =
            Math.Clamp(
                (averageDistance - AimDistanceBaseline)
                / (AimDistanceSaturation - AimDistanceBaseline),
                0,
                1);


        // ---------------------------------------------------------
        // 2. VITESSE DE DÉPLACEMENT
        // ---------------------------------------------------------
        //
        // Mesure la vitesse nécessaire pour effectuer
        // les déplacements Aim.
        //

        double speedSignal =
            Math.Clamp(
                (averageMovementSpeed - AimSpeedBaseline)
                / (AimSpeedSaturation - AimSpeedBaseline),
                0,
                1);


        // ---------------------------------------------------------
        // 3. CHANGEMENTS DE DIRECTION
        // ---------------------------------------------------------
        //
        // Les changements de direction importants augmentent
        // la difficulté mécanique du déplacement.
        //

        double angleSignal =
            CalculateAimAngleSignal(circles);


        // ---------------------------------------------------------
        // 4. PRESSION TEMPORELLE
        // ---------------------------------------------------------
        //
        // Un gros Jump lent ne doit pas être considéré comme
        // extrêmement difficile uniquement parce que la distance
        // est importante.
        //
        // On regarde donc à quelle fréquence les déplacements
        // importants doivent être effectués rapidement.
        //

        double temporalSignal =
            CalculateAimTemporalSignal(circles);


        // ---------------------------------------------------------
        // SCORE AIM V1.1
        // ---------------------------------------------------------
        //
        // Base mécanique :
        //
        // Distance = 35 %
        // Speed    = 40 %
        // Angle    = 25 %
        //
        // Puis la pression temporelle vient limiter la difficulté
        // lorsque les mouvements ne sont pas réellement rapides.
        //

        double baseAim =
              distanceSignal * 0.35
            + speedSignal * 0.40
            + angleSignal * 0.25;

        double temporalPressure =
            temporalSignal;

        double score =
            baseAim * temporalPressure;

        score =
            Math.Clamp(score, 0, 1) * 100;

        return new AimAnalysis(
            score,
            distanceSignal,
            speedSignal,
            angleSignal,
            temporalSignal);
    }

    private static double CalculateAimTemporalSignal(
    IReadOnlyList<HitObject> circles)
    {
        if (circles.Count < 2)
            return 0;

        int validTransitions = 0;
        int pressuredTransitions = 0;

        // En dessous de cette valeur, le déplacement doit être
        // effectué suffisamment rapidement pour créer une vraie
        // pression temporelle.
        const double temporalThresholdMs = 180;

        for (int i = 1; i < circles.Count; i++)
        {
            double interval =
                circles[i].Time - circles[i - 1].Time;

            if (interval <= 0)
                continue;

            validTransitions++;

            if (interval <= temporalThresholdMs)
                pressuredTransitions++;
        }

        if (validTransitions == 0)
            return 0;

        return Math.Clamp(
            (double)pressuredTransitions / validTransitions,
            0,
            1);
    }


    // =============================================================
    // AIM — ANGLE
    // =============================================================

    private static double CalculateAimAngleSignal(
        IReadOnlyList<HitObject> circles)
    {
        if (circles.Count < 3)
            return 0;

        int angleCount = 0;
        int sharpAngleCount = 0;
        int reverseCount = 0;

        for (int i = 1; i < circles.Count - 1; i++)
        {
            HitObject previous = circles[i - 1];
            HitObject current = circles[i];
            HitObject next = circles[i + 1];

            double firstX = current.X - previous.X;
            double firstY = current.Y - previous.Y;

            double secondX = next.X - current.X;
            double secondY = next.Y - current.Y;

            double firstLength =
                Math.Sqrt(firstX * firstX + firstY * firstY);

            double secondLength =
                Math.Sqrt(secondX * secondX + secondY * secondY);

            if (firstLength == 0 || secondLength == 0)
                continue;

            double cosine =
                (firstX * secondX + firstY * secondY)
                / (firstLength * secondLength);

            cosine = Math.Clamp(cosine, -1.0, 1.0);

            double angle =
                Math.Acos(cosine) * 180.0 / Math.PI;

            angleCount++;

            if (angle >= 90)
                sharpAngleCount++;

            if (angle >= 135)
                reverseCount++;
        }

        if (angleCount == 0)
            return 0;

        double sharpRatio =
            (double)sharpAngleCount / angleCount;

        double reverseRatio =
            (double)reverseCount / angleCount;

        // Les angles > 90° donnent le signal principal.
        // Les reverse > 135° renforcent légèrement le signal.
        return Math.Clamp(
            sharpRatio + reverseRatio * 0.5,
            0,
            1);
    }


    // =============================================================
    // AIM — NIVEAU
    // =============================================================

    private static string GetAimLevel(double score)
    {
        return score switch
        {
            >= 70 => "High",
            >= 40 => "Medium",
            _ => "Low"
        };
    }


    // =============================================================
    // AIM — RÉSULTAT DE L'ANALYSE
    // =============================================================

    private sealed record AimAnalysis(
        double Score,
        double DistanceSignal,
        double SpeedSignal,
        double AngleSignal,
        double TemporalSignal);


    // ============================================================
    // 13. CLASSIFICATION DE LA MAP
    // ============================================================

    /// <summary>
    /// Détermine le type principal de la map à partir des ratios
    /// Stream, Jump et Tech.
    ///
    /// Si aucun type ne dépasse 50 %, la map est considérée
    /// comme Classic / Mixed.
    /// </summary>
    private static string DeterminePrimaryType(
    double streamRatio,
    double jumpRatio,
    double techScore)
    {
        var types = new List<(string Name, double Ratio)>
    {
        ("Stream", streamRatio),
        ("Jump", jumpRatio)
    };

        var ordered =
            types
                .OrderByDescending(x => x.Ratio)
                .ToList();

        double techRatio =
            techScore / 100.0;


        // --------------------------------------------------------
        // TYPE PRINCIPAL
        // --------------------------------------------------------

        if (techRatio >= 0.40
            && techRatio >= ordered[0].Ratio)
        {
            return "Tech";
        }


        if (ordered[0].Ratio >= 0.40)
        {
            double difference = ordered[0].Ratio - ordered[1].Ratio;


            if (ordered[1].Ratio >= SecondaryTypeThreshold
                    && difference <= 0.18)
            {
                return $"{ordered[0].Name} / {ordered[1].Name}";
            }

            return ordered[0].Name;
        }


        // --------------------------------------------------------
        // MAP MIXED
        // --------------------------------------------------------

        return "Classic / Mixed";
    }

    private static string BuildStyleDescription(
    string style)
    {
        return style switch
        {
            "Speed Aim" =>
                "High speed with large cursor movement.",

            "Speed" =>
                "Requires fast tapping and stamina.",

            "Aim" =>
                "Focuses on cursor control and movement.",

            "Tech" =>
                "Requires pattern adaptation and precision.",

            "Reading" =>
                "Requires strong visual processing.",

            _ =>
                "Balanced gameplay."
        };
    }

    private static string BuildGameplayIdentity(string primaryType,GameplayStyleProfile style)
    {
        return $"{primaryType} {style.PrimaryStyle}";
    }

    private static int GetTraitPriority(string trait)
    {
        return trait switch
        {
            "High Speed Pressure" => 100,
            "High Aim Pressure" => 95,
            "High Reading Demand" => 90,
            "High Technical Pressure" => 85,

            "Stream Heavy" => 80,
            "Jump Heavy" => 80,
            "Burst Heavy" => 75,

            "Speed Influence" => 60,
            "Aim Influence" => 60,
            "Reading Influence" => 55,
            "Technical Influence" => 50,

            "Stream Secondary" => 35,
            "Jump Secondary" => 35,
            "Mixed Secondary" => 30,

            _ => 0
        };
    }

    private static List<string> CleanTraits(
    IEnumerable<string> traits)
    {
        HashSet<string> result =
            new(traits);


        if (result.Contains("High Speed Pressure"))
        {
            result.Remove("Speed Influence");
        }


        if (result.Contains("High Aim Pressure"))
        {
            result.Remove("Aim Influence");
        }


        if (result.Contains("High Reading Demand"))
        {
            result.Remove("Reading Influence");
        }


        return result
            .OrderByDescending(GetTraitPriority)
            .Take(5)
            .ToList();
    }


    // ============================================================
    // 14. NIVEAUX DE DIFFICULTÉ
    // ============================================================

    /// <summary>
    /// Convertit le score Tech en niveau.
    /// </summary>
    private static string GetTechLevel(
        double score)
    {
        return score switch
        {
            >= 70 => "High",
            >= 40 => "Medium",
            _ => "Low"
        };
    }

    private static GameplayIdentity AnalyzeGameplayIdentity(
    string primaryType,
    AimAnalysis aim,
    SpeedAnalysis speed,
    TechAnalysis tech,
    ReadAnalysis read)
    {
        string identity;
        string secondary = "";
        double confidence = 0;
        List<string> traits =
        GenerateGameplayTraits(
            primaryType,
            aim,
            speed,
            tech,
            read);

        // ============================================================
        // SPEED AIM
        // ============================================================

        if (speed.Score >= 65
            && aim.Score >= 50)
        {
            identity = "Speed Aim";
        }


        // ============================================================
        // STREAM SPEED
        // ============================================================

        else if (primaryType.Contains("Stream")
            && speed.Score >= 45)
        {
            identity = "Speed";
        }


        // ============================================================
        // STREAM FLOW
        // ============================================================

        else if (primaryType.Contains("Stream")
            && read.Score >= 50
            && speed.Score < 60)
        {
            identity = "Flow";
        }


        // ============================================================
        // JUMP AIM
        // ============================================================

        else if (primaryType.Contains("Jump")
            && aim.Score >= 50)
        {
            identity = "Aim";
        }


        // ============================================================
        // JUMP READING
        // ============================================================

        else if (primaryType.Contains("Jump")
            && read.Score >= 50)
        {
            identity = "Reading";
        }


        // ============================================================
        // TECH PRECISION
        // ============================================================

        else if (tech.Score >= 45
            && aim.Score >= 30)
        {
            identity = "Tech Precision";
        }


        // ============================================================
        // TECH READING
        // ============================================================

        else if (tech.Score >= 35
            && read.Score >= 45)
        {
            identity = "Tech Reading";
        }


        // ============================================================
        // PURE READING
        // ============================================================

        else if (read.Score >= 60)
        {
            identity = "Reading";
        }


        // ============================================================
        // AIM
        // ============================================================

        else if (aim.Score >= 60)
        {
            identity = "Aim";
        }


        // ============================================================
        // SPEED
        // ============================================================

        else if (speed.Score >= 60)
        {
            identity = "Speed";
        }


        // ============================================================
        // FALLBACK
        // ============================================================

        else
        {
            identity = "Balanced";
        }

        // ============================================================
        // SECONDARY PATTERN
        // ============================================================

        if (primaryType.Contains("Stream")
            && read.Score >= 50)
        {
            secondary = "Reading";
        }

        else if (primaryType.Contains("Stream")
            && aim.Score >= 40)
        {
            secondary = "Aim";
        }

        else if (primaryType.Contains("Jump")
            && speed.Score >= 50)
        {
            secondary = "Speed";
        }

        else if (primaryType.Contains("Jump")
            && read.Score >= 50)
        {
            secondary = "Reading";
        }

        double patternStrength = primaryType switch
        {
            var x when x.Contains("Stream") =>
                Math.Max(0, speed.Score),

            var x when x.Contains("Jump") =>
                Math.Max(0, aim.Score),

            var x when x.Contains("Tech") =>
                tech.Score,

            _ => 30
        };


        double gameplayStrength =
            Math.Max(
                Math.Max(speed.Score, aim.Score),
                Math.Max(read.Score, tech.Score));


        confidence =
            50
            + patternStrength * 0.35
            + gameplayStrength * 0.15;


        confidence =
            Math.Clamp(
                confidence,
                50,
                95);
        if (speed.Score >= 65)
        {
            traits.Add("High Speed Pressure");
        }

        if (aim.Score >= 60)
        {
            traits.Add("High Aim Pressure");
        }

        if (read.Score >= 60)
        {
            traits.Add("High Reading Demand");
        }

        if (tech.Score >= 45)
        {
            traits.Add("Technical Patterns");
        }


        if (primaryType.Contains("Stream"))
        {
            traits.Add("Stream Heavy");
        }

        if (primaryType.Contains("Jump"))
        {
            traits.Add("Jump Heavy");
        }


        if (primaryType.Contains(" / "))
        {
            string[] parts = primaryType.Split('/');

            traits.Add(
                $"{parts[1].Trim()} Secondary");
        }

        return new GameplayIdentity
        {
            Pattern = primaryType,
            Primary = identity,
            Secondary = secondary,
            Confidence = confidence,
            Traits = CleanTraits(traits)
        };
    }

    private static List<string> GenerateGameplayTraits(
    string primaryType,
    AimAnalysis aim,
    SpeedAnalysis speed,
    TechAnalysis tech,
    ReadAnalysis read)
    {
        HashSet<string> traits = [];

        // ============================================================
        // STRUCTURE
        // ============================================================

        if (primaryType.Contains("Stream"))
        {
            traits.Add("Stream Heavy");
        }

        if (primaryType.Contains("Jump"))
        {
            traits.Add("Jump Heavy");
        }
        
        if (primaryType.Contains(" / ")
            && !primaryType.Contains("Classic"))
        {
            string[] parts =
                primaryType.Split('/');

            if (parts.Length >= 2)
            {
                traits.Add(
                    $"{parts[1].Trim()} Secondary");
            }
        }

        // ============================================================
        // SPEED
        // ============================================================

        if (speed.Score >= 60)
        {
            traits.Add("High Speed Pressure");
        }
        else if (speed.Score >= 40)
        {
            traits.Add("Speed Influence");
        }

        // ============================================================
        // AIM
        // ============================================================

        if (aim.Score >= 60)
        {
            traits.Add("High Aim Pressure");
        }
        else if (aim.Score >= 40)
        {
            traits.Add("Aim Influence");
        }

        // ============================================================
        // READ
        // ============================================================

        if (read.Score >= 60)
        {
            traits.Add("High Reading Demand");
        }
        else if (read.Score >= 40)
        {
            traits.Add("Reading Influence");
        }

        // ============================================================
        // TECH
        // ============================================================

        if (tech.Score >= 60)
        {
            traits.Add("High Technical Pressure");
        }
        else if (tech.Score >= 40)
        {
            traits.Add("Technical Influence");
        }

        // ============================================================
        // CLASSIC / MIXED
        // ============================================================

        if (primaryType == "Classic / Mixed")
        {
            if (tech.Score >= 35)
            {
                traits.Add("Transition Heavy");
            }
        }

        return traits.ToList();
    }

    private static GameplayStyleProfile AnalyzeGameplayStyle(
    AimAnalysis aim,
    SpeedAnalysis speed,
    TechAnalysis tech,
    ReadAnalysis read)
    {
        double aimValue =
            aim.Score;

        double speedValue =
            speed.Score;

        double techValue =
            tech.Score;

        double readValue =
            read.Score;


        string primary;


        if (speedValue >= 65 && aimValue >= 50)
        {
            primary = "Speed Aim";
        }
        else if (aimValue >= 65)
        {
            primary = "Aim";
        }
        else if (speedValue >= 65)
        {
            primary = "Speed";
        }
        else if (techValue >= 45)
        {
            primary = "Tech";
        }
        else if (readValue >= 50)
        {
            primary = "Reading";
        }
        else
        {
            primary = "Balanced";
        }


        return new GameplayStyleProfile
        {
            PrimaryStyle = primary,

            AimInfluence = aimValue,
            SpeedInfluence = speedValue,
            TechInfluence = techValue,
            ReadInfluence = readValue,

            Description = BuildStyleDescription(
                primary)
        };
    }


    // ============================================================
    // 15. UTILITAIRES COMMUNS
    // ============================================================

    /// <summary>
    /// Vérifie si l'objet est un cercle.
    /// </summary>
    private static bool IsCircle(
        HitObject hitObject)
    {
        return (hitObject.Type & 1) == 1;
    }

    /// <summary>
    /// Vérifie si l'objet est un slider.
    /// </summary>
    private static bool IsSlider(
        HitObject hitObject)
    {
        return (hitObject.Type & 2) == 2;
    }

    /// <summary>
    /// Vérifie si l'objet est un spinner.
    /// </summary>
    private static bool IsSpinner(
        HitObject hitObject)
    {
        return (hitObject.Type & 8) == 8;
    }

    /// <summary>
    /// Calcule la distance euclidienne entre deux objets.
    /// </summary>
    private static double Distance(
        HitObject first,
        HitObject second)
    {
        double x =
            second.X - first.X;

        double y =
            second.Y - first.Y;

        return Math.Sqrt(
            x * x + y * y);
    }

    /// <summary>
    /// Calcule l'angle de changement de direction entre
    /// trois objets consécutifs.
    /// </summary>
    private static double GetTurnAngle(
        HitObject previous,
        HitObject current,
        HitObject next)
    {
        double firstX =
            current.X - previous.X;

        double firstY =
            current.Y - previous.Y;

        double secondX =
            next.X - current.X;

        double secondY =
            next.Y - current.Y;

        double firstLength =
            Math.Sqrt(
                firstX * firstX
                + firstY * firstY);

        double secondLength =
            Math.Sqrt(
                secondX * secondX
                + secondY * secondY);

        if (firstLength == 0
            || secondLength == 0)
        {
            return 180;
        }

        double cosine =
            (
                firstX * secondX
                + firstY * secondY
            )
            / (firstLength * secondLength);

        return Math.Acos(
            Math.Clamp(cosine, -1, 1))
            * 180
            / Math.PI;
    }

    /// <summary>
    /// Marque tous les objets d'une plage comme appartenant
    /// à un pattern.
    /// </summary>
    private static void MarkRange(
        bool[] membership,
        int start,
        int end)
    {
        for (int i = start; i <= end; i++)
            membership[i] = true;
    }

    /// <summary>
    /// Calcule le ratio d'objets appartenant à une catégorie.
    /// </summary>
    private static double CalculateRatio(
        int patternObjectCount,
        int analysedObjectCount)
    {
        return analysedObjectCount == 0
            ? 0
            : (double)patternObjectCount
              / analysedObjectCount;
    }

    /// <summary>
    /// Transforme un signal en valeur 0-1 uniquement lorsqu'il
    /// dépasse un niveau considéré comme normal.
    /// </summary>
    private static double NormalizeAboveBaseline(
        double value,
        double baseline,
        double saturation)
    {
        if (value <= baseline)
            return 0;

        return Math.Clamp(
            (value - baseline)
            / (saturation - baseline),
            0,
            1);
    }


    // ============================================================
    // 16. DEBUG
    // ============================================================

    /// <summary>
    /// Écrit le profil gameplay dans la sortie Debug de Visual Studio.
    ///
    /// Ces informations servent uniquement à calibrer les détecteurs
    /// et n'ont aucun effet sur le calcul du Star Rating.
    /// </summary>
    private static void WriteDebug(
        GameplayProfile profile)
    {
        Debug.WriteLine(
            "----- GAMEPLAY PROFILE V0 -----");

        Debug.WriteLine(
            $"ANALYSED CIRCLES = {profile.AnalysedCircleCount}");

        Debug.WriteLine(
            $"STREAMS = {profile.StreamSequenceCount} sequences / " +
            $"{profile.StreamObjectCount} circles / " +
            $"{profile.StreamRatio:P2}");

        Debug.WriteLine(
            $"JUMPS = {profile.JumpSequenceCount} sequences / " +
            $"{profile.JumpObjectCount} circles / " +
            $"{profile.JumpRatio:P2}");

        Debug.WriteLine(
            $"BURSTS = {profile.BurstSequenceCount} sequences / " +
            $"{profile.BurstObjectCount} circles / " +
            $"{profile.BurstRatio:P2} / " +
            $"Max {profile.LongestBurstLength}");

        Debug.WriteLine(
            $"PRIMARY TYPE = {profile.PrimaryType}");

        Debug.WriteLine(
            $"TECH = {profile.TechObjectCount} circles / " +
            $"{profile.TechRatio:P2} / " +
            $"Signal {profile.TechScore:F0}/100 " +
            $"({profile.TechLevel})");

        Debug.WriteLine(
            $"TECH SIGNALS = " +
            $"Transition {profile.TechTransitionSignal:P0} / " +
            $"Structure {profile.TechStructureSignal:P0} / " +
            $"Spatial {profile.TechSpatialSignal:P0} / " +
            $"Temporal {profile.TechTemporalSignal:P0}");

        Debug.WriteLine(
            $"READ = {profile.ReadObjectCount} circles / " +
            $"{profile.ReadRatio:P2} / " +
            $"Score {profile.ReadScore:F0}/100 " +
            $"({profile.ReadLevel})");

        Debug.WriteLine(
            $"READ SIGNALS = " +
            $"Density {profile.ReadDensitySignal:P0} / " +
            $"Clutter {profile.ReadClutterSignal:P0} / " +
            $"Persistence {profile.ReadPersistenceSignal:P0} / " +
            $"CS {profile.ReadCSSignal:P0}");

        Debug.WriteLine(
            $"SPEED = {profile.SpeedRatio:P2} / " +
            $"Score {profile.SpeedScore:F0}/100 " +
            $"({profile.SpeedLevel})");

        Debug.WriteLine(
            $"SPEED SIGNALS = " +
            $"Fast {profile.SpeedFastObjectRatio:P0} / " +
            $"Density {profile.SpeedDensitySignal:P0} / " +
            $"AR {profile.SpeedARSignal:P0}");

        Debug.WriteLine(
            $"AIM SIGNALS = " +
            $"Distance {profile.AimDistanceSignal:P0} / " +
            $"Speed {profile.AimSpeedSignal:P0} / " +
            $"Angle {profile.AimAngleSignal:P0} / " +
            $"Temporal {profile.AimTemporalSignal:P0}");


        Debug.WriteLine(
            $"AIM = Score {profile.AimScore:F0}/100 " +
            $"({profile.AimLevel})");

        Debug.WriteLine(
            $"STYLE = {profile.StyleProfile.PrimaryStyle}");

        Debug.WriteLine(
            $"STYLE SIGNALS = " +
            $"Aim {profile.StyleProfile.AimInfluence:F0} / " +
            $"Speed {profile.StyleProfile.SpeedInfluence:F0} / " +
            $"Tech {profile.StyleProfile.TechInfluence:F0} / " +
            $"Read {profile.StyleProfile.ReadInfluence:F0}");

        Debug.WriteLine(
            $"IDENTITY = {profile.Identity.Pattern} {profile.Identity.Primary}");

        Debug.WriteLine(
            $"CONFIDENCE = {profile.Identity.Confidence:F0}%");

        Debug.WriteLine(
            $"TRAITS = {string.Join(", ", profile.Identity.Traits)}");
    }


    // ============================================================
    // 17. TYPES INTERNES D'ANALYSE
    // ============================================================

    /// <summary>
    /// Résultat intermédiaire de l'analyse Tech.
    /// </summary>
    private sealed record TechAnalysis(
        double Score,
        int TechObjectCount,
        int ComplexSliderCount,
        int SliderSpatialOverlapCount,
        int SharpTransitionCount,
        double TransitionSignal,
        double StructureSignal,
        double SpatialSignal,
        double TemporalSignal);

    /// <summary>
    /// Résultat intermédiaire de l'analyse Read.
    /// </summary>
    private sealed record ReadAnalysis(
        int ReadObjectCount,
        double Ratio,
        double Score,
        double DensitySignal,
        double ClutterSignal,
        double PersistenceSignal,
        double CSSignal);

    /// <summary>
    /// Résultat intermédiaire de l'analyse Speed.
    /// </summary>
    private sealed record SpeedAnalysis(
        double Score,
        double SpeedRatio,
        double FastObjectRatio,
        double DensitySignal,
        double ARSignal);
}