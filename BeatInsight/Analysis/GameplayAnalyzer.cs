using BeatInsight.Models;
using System.Diagnostics;
using BeatInsight.Diagnostics;

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
/// - ReadAnalysisRead
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

    private const double PrimaryIdentityThreshold = 20.0;

    // ============================================================
    // TECH PATTERN DETECTION
    // ============================================================

    /// <summary>
    /// Poids des sliders complexes dans la détection
    /// des patterns Tech.
    /// </summary>
    private const double TechSliderPatternWeight = 0.80;

    /// <summary>
    /// Poids des cercles présentant une structure Tech
    /// dans la détection des patterns Tech.
    /// </summary>
    private const double TechCirclePatternWeight = 0.20;


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
    /// La persistance est volontairement neutralisée pour Reading V1.
    /// Elle sera recalibrée lorsque sa sémantique future sera définie.
    /// </summary>
    private const double ReadPersistenceWeight = 0.0;

    /// <summary>
    /// Nombre minimum d'informations futures visibles nécessaire
    /// pour qu'un objet participe à une zone Reading.
    /// </summary>
    private const int ReadMinimumFutureVisibleObjects = 2;

    /// <summary>
    /// Nombre d'informations futures visibles à partir duquel
    /// la densité Reading commence à augmenter.
    /// </summary>
    private const int ReadDensityBaselineFutureObjects = 1;

    /// <summary>
    /// Nombre d'informations futures visibles auquel le signal
    /// de densité Reading est saturé.
    /// </summary>
    private const int ReadDensitySaturationFutureObjects = 6;

    /// <summary>
    /// Distance à partir de laquelle deux objets sont considérés
    /// comme suffisamment proches pour participer à la surcharge visuelle.
    /// </summary>
    private const double ReadClutterDistance = 140.0;

    /// <summary>
    /// Séparation temporelle à laquelle l'ambiguïté visuelle Reading
    /// atteint sa contribution maximale.
    /// </summary>
    private const double ReadAmbiguityTemporalSaturationMs = 300.0;

    /// <summary>
    /// Nombre minimum d'objets Reading dans une section valide.
    /// </summary>
    private const int ReadMinimumSectionObjects = 2;

    /// <summary>
    /// Écart temporel maximum entre deux objets Reading consécutifs
    /// d'une même section.
    /// </summary>
    private const double ReadMaximumSectionGapMs = 300.0;

    private const double ReadActiveSignalWeight =
        ReadDensityWeight + ReadClutterWeight + ReadPersistenceWeight;


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
        // Sections temporelles
        //
        // Une section représente une zone cohérente dans le temps
        // où une famille de gameplay est réellement présente.
        //
        // Cela permet de différencier :
        // - un petit nombre d'objets isolés
        // - une vraie zone Stream / Jump / Tech
        // --------------------------------------------------------


        List<GameplaySection> streamSections =
            BuildGameplaySections(
                objects,
                streamObjects,
                "Stream");

        List<GameplaySection> jumpSections =
            BuildGameplaySections(
                objects,
                jumpObjects,
                "Jump");

        BeatInsight.Diagnostics.DebugLogger.Detailed(
            $"SECTIONS DEBUG | " +
            $"Stream={streamSections.Count} " +
            $"Jump={jumpSections.Count}");

        // --------------------------------------------------------
        // Coverage temporelle
        //
        // Mesure la proportion du temps de la map occupée
        // par les sections détectées.
        // --------------------------------------------------------

        double streamCoverage =
            CalculateSectionCoverage(
                streamSections,
                objects);

        double jumpCoverage =
            CalculateSectionCoverage(
                jumpSections,
                objects);

        // --------------------------------------------------------
        // Analyses spécialisées
        // --------------------------------------------------------

        TechAnalysis tech =
            AnalyzeTech(objects);

        BeatInsight.Diagnostics.DebugLogger.Detailed(
            $"SECTIONS DEBUG | " +
            $"Tech={tech.TechSections.Count}");

        // TechCoverage doit utiliser la même base circle-based que
        // TechRatio : les sliders peuvent structurer une section Tech,
        // sans être comptés dans sa couverture.
        int techCircleCount =
            CountSectionCircles(
                tech.TechSections,
                objects);

        double techCoverage =
            CalculateRatio(
                techCircleCount,
                objects.Count(IsCircle));

        BeatInsight.Diagnostics.DebugLogger.Detailed(
            $"COVERAGE DEBUG | " +
            $"Stream={streamCoverage:P1} " +
            $"Jump={jumpCoverage:P1} " +
            $"Tech={techCoverage:P1}");

        // --------------------------------------------------------
        // Ajustement Tech selon sa présence réelle
        // --------------------------------------------------------

        double techCoverageMultiplier =
            0.45 +
            0.55 * Math.Clamp(
                techCoverage,
                0.0,
                1.0);

        double rawTechScore = tech.Score;

        tech = tech with
        {
            Score =
                Math.Clamp(
                    tech.Score * techCoverageMultiplier,
                    0.0,
                    100.0)
        };

        BeatInsight.Diagnostics.DebugLogger.Detailed(
            $"TECH COVERAGE ADJUSTMENT | " +
            $"Raw={rawTechScore:F1} " +
            $"Coverage={techCoverage:P1} " +
            $"Multiplier={techCoverageMultiplier:F3} " +
            $"Final={tech.Score:F1}");

        // --------------------------------------------------------
        // Read / Speed / Aim / Style
        // --------------------------------------------------------

        ReadAnalysis read =
            AnalyzeRead(
                beatmap,
                objects);

        double readCoverage =
            read.Coverage;

        string readProfile =
            GetReadPresenceProfile(
                readCoverage);


        SpeedAnalysis speed =
            AnalyzeSpeed(
                objects,
                beatmap);

        int analysedCircles =
            objects.Count(IsCircle);

        double speedCoverage =
            speed.Presence;

        string speedProfile =
            GetSpeedPresenceProfile(
                speedCoverage,
                speed.Score);

        AimAnalysis aim =
            AnalyzeAim(
                objects,
                beatmap);
        GameplayStyleProfile style =
            AnalyzeGameplayStyle(
                aim,
                speed,
                read);

        // --------------------------------------------------------
        // Statistiques générales.
        // --------------------------------------------------------


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
            CalculateRatio(
                streamObjectCount,
                analysedCircles);

        double jumpRatio =
            CalculateRatio(
                jumpObjectCount,
                analysedCircles);

        double burstRatio =
            CalculateRatio(
                burstObjectCount,
                analysedCircles);

        double techRatio =
            CalculateRatio(
                techCircleCount,
                analysedCircles);

        // AimCoverage représente désormais la présence de mouvements Aim
        // significatifs, et non le nombre de transitions rapides qualifiées.
        double aimCoverage =
            aim.Presence;

        // --------------------------------------------------------
        // Classification globale de la map.
        // --------------------------------------------------------

        AimProfile aimProfile =
            GetAimProfile(
                aimCoverage,
                aim.Intensity);

        string primaryType =
            DeterminePrimaryType(
                streamCoverage,
                jumpCoverage,
                techCoverage,
                tech.Score);

        double confidence =
            CalculatePrimaryConfidence(
                primaryType,
                streamCoverage,
                jumpCoverage,
                techCoverage,
                tech.Score);



        confidence =
            Math.Clamp(
                confidence,
                0.0,
                100.0);

        DebugLogger.Detailed(
            $"PRIMARY DEBUG | " +
            $"Stream={streamRatio:P1} " +
            $"Jump={jumpRatio:P1} " +
            $"TechRatio={techRatio:P1} " +
            $"Coverage(S/J/T)=" +
            $"{streamCoverage:P1}/" +
            $"{jumpCoverage:P1}/" +
            $"{techCoverage:P1} " +
            $"TechScore={tech.Score:F1} " +
            $"=> {primaryType}");

        string gameplayIdentity =
            BuildGameplayIdentity(
                primaryType);

        // --------------------------------------------------------
        // Identity
        // --------------------------------------------------------

        GameplayIdentity identity =
                AnalyzeGameplayIdentity(
                    primaryType,
                    streamCoverage,
                    jumpCoverage,
                    techCoverage,
                    aim,
                    speed,
                    tech,
                    read);

        BeatInsight.Diagnostics.DebugLogger.Detailed(
                "===== IDENTITY INPUT DEBUG =====");

        BeatInsight.Diagnostics.DebugLogger.Detailed(
           $"streamCoverage = {streamCoverage:F3} " +
            $"=> StreamScore = {streamCoverage * 100.0:F3}");

        BeatInsight.Diagnostics.DebugLogger.Detailed(
            $"jumpCoverage = {jumpCoverage:F3} " +
            $"=> JumpScore = {jumpCoverage * 100.0:F3}");

        BeatInsight.Diagnostics.DebugLogger.Detailed(
           $"techCoverage = {techCoverage:F3}");

        BeatInsight.Diagnostics.DebugLogger.Detailed(
            $"tech.Score = {tech.Score:F3}");

        BeatInsight.Diagnostics.DebugLogger.Detailed(
            "================================");
        // --------------------------------------------------------
        // Construction du profil final.
        // --------------------------------------------------------
        double readPredictabilityPenalty = 1.0;

        if (read.ReadPredictability > 0.80)
        {
            double excess =
                Math.Clamp(
                    (read.ReadPredictability - 0.80) / 0.20,
                    0.0,
                    1.0);

            double curvedExcess =
                Math.Sqrt(excess);

            readPredictabilityPenalty =
                1.0 - (0.80 * curvedExcess);
        }

        double finalReadScore =
            Math.Clamp(
                (read.Score / 100.0) * readPredictabilityPenalty,
                0.0,
                1.0) * 100.0;
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
            StreamSections = streamSections,

            // ----------------------------
            // Jump
            // ----------------------------

            JumpObjectCount = jumpObjectCount,
            JumpSequenceCount = jumps.Count,
            JumpRatio = jumpRatio,
            JumpSequences = jumps,
            JumpSections = jumpSections,

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

            TechObjectCount = techCircleCount,
            TechRatio = techRatio,
            TechPresence = techCoverage,
            TechIntensity = rawTechScore,
            TechScore = tech.Score,
            TechLevel = GetTechLevel(tech.Score, techCoverage),
            TechProfile = GetTechProfile(techCoverage, tech.Score),
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

            TechSections = tech.TechSections,

            // ----------------------------
            // Read
            // ----------------------------

            ReadObjectCount =
                read.ReadObjectCount,
            ReadRatio =
                read.Ratio,
            ReadScore =
                finalReadScore,
            ReadLevel =
                GetReadLevel(finalReadScore),
            ReadSections =
                read.ReadSections,

            ReadDensitySignal =
                read.DensitySignal,

            ReadClutterSignal =
                read.ClutterSignal,

            ReadPersistenceSignal =
                read.PersistenceSignal,

            ReadCSSignal =
                read.CSSignal,

            ReadPredictability =
                read.ReadPredictability,
            ReadNovelty =
                read.ReadNovelty,
            ReadTemporalRegularity =
                read.ReadTemporalRegularity,
            ReadSpacingRegularity =
                read.ReadSpacingRegularity,
            ReadTrajectoryRepetition =
                read.ReadTrajectoryRepetition,
            ReadAmbiguity =
                read.ReadAmbiguity,

            ReadCoverage =
                readCoverage,
            ReadProfile =
                readProfile,

            ReadIntensity =
                GetReadIntensity(read.Intensity),

            // ----------------------------
            // Speed
            // ----------------------------

            SpeedObjectCount =
                    speed.SpeedObjectCount,

            SpeedCoverage =
                    speedCoverage,

            SpeedProfile =
                    speedProfile,

            SpeedIntensity =
                GetSpeedIntensity(speed.Intensity * 100.0),

            SpeedSections =
                speed.SpeedSections,

            SpeedScore = speed.Score,
            SpeedLevel = GetSpeedLevel(speed.Score),
            SpeedRatio = speed.SpeedRatio,
            SpeedFastObjectRatio = speed.FastObjectRatio,
            SpeedDensitySignal = speed.DensitySignal,
            SpeedARSignal = speed.ARSignal,


            // ----------------------------
            // Aim
            // ----------------------------

            AimScore = aim.Score,
            AimLevel = GetAimLevel(aim.Score),
            AimDistanceSignal = aim.DistanceSignal,
            AimSpeedSignal = aim.SpeedSignal,
            AimAngleSignal = aim.AngleSignal,
            AimTemporalSignal = aim.TemporalSignal,
            AimTemporalModifier = aim.TemporalModifier,
            AimRawIntensity = aim.RawIntensity,
            AimPrecisionCS = beatmap.CS,
            AimPrecisionModifier = aim.PrecisionModifier,
            AimAdjustedIntensity = aim.Intensity,
            AimCoverage = aimProfile.Coverage,
            AimProfile = aimProfile.Profile,
            AimIntensity = aimProfile.Intensity,

            // ----------------------------
            // Classification
            // ----------------------------

            PrimaryType = primaryType,
            GameplayIdentity = gameplayIdentity,
            StyleProfile = style,
            Identity = identity
        };

        GameplayDebug.Identity(profile.Identity);
        GameplayDebug.Tech(profile);
        GameplayDebug.Read(profile);
        GameplayDebug.Speed(profile);
        GameplayDebug.Aim(profile);
        GameplayDebug.Summary(profile);

        beatmap.GameplayProfile = profile;

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

    private static int complexSliderDebugCount = 0;
    private static int complexContextDebugCount = 0;
    private static void DebugTechTransition(
    ref int debugCount,
    int index,
    HitObject current,
    HitObject previous,
    HitObject next,
    double angle,
    double averageDistance,
    double averageInterval,
    bool isSharp,
    bool isAwkward,
    bool isStructural,
    bool isCompact,
    bool isAlternating,
    bool spacingVariation)
    {
        if (debugCount >= 50)
            return;

        if (averageDistance > 160)
            return;

        BeatInsight.Diagnostics.DebugLogger.Detailed(
        $"TECH TRANSITION DEBUG #{debugCount + 1} | " +
        $"i={index} " +
        $"Time={current.Time} " +
        $"Angle={angle:F1}° " +
        $"AvgDist={averageDistance:F1} " +
        $"AvgInterval={averageInterval:F1}ms " +
        $"Sharp={isSharp} " +
        $"Awkward={isAwkward} " +
        $"Structural={isStructural} " +
        $"Compact={isCompact} " +
        $"Alternating={isAlternating} " +
        $"SpacingVariation={spacingVariation} " +
        $"Prev=({previous.X},{previous.Y}) " +
        $"Curr=({current.X},{current.Y}) " +
        $"Next=({next.X},{next.Y}) " +
        $"Types={previous.Type}/{current.Type}/{next.Type} " +
        $"Curves={previous.SliderCurveType}/{current.SliderCurveType}/{next.SliderCurveType}");



        debugCount++;

    }

    // ============================================================
    // 10. TECH
    // ============================================================

    /// <summary>
    /// Analyse les structures Tech.
    ///
    /// Le Tech repose principalement sur :
    /// - les changements de direction,
    /// - les angles awkward,
    /// - les transitions compactes,
    /// - les variations de spacing,
    /// - les sliders complexes,
    /// - les alternances structurelles.
    ///
    /// Les grands déplacements sont volontairement exclus afin
    /// d'éviter de confondre Jump/Aim et Tech.
    /// </summary>
    private static TechAnalysis AnalyzeTech(
        IReadOnlyList<HitObject> objects)
    {
        complexSliderDebugCount = 0;
        int complexSignalDebugCount = 0;


        bool[] techObjects =
    new bool[objects.Count];

        double[] techPatternEvidence =
            new double[objects.Count];

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

        int compactTransitions = 0;

        int awkwardTransitions = 0;

        int spacingVariationTransitions = 0;

        // --------------------------------------------------------
        // Analyse locale
        // --------------------------------------------------------
        int debugCount = 0;


        for (int i = 1; i < objects.Count - 1; i++)
        {
            HitObject previous =
                objects[i - 1];

            HitObject current =
                objects[i];

            HitObject next =
                objects[i + 1];

            bool currentIsComplexSlider =
                 IsComplexSlider(current);

            bool previousIsComplexSlider =
                IsComplexSlider(previous);

            bool nextIsComplexSlider =
                IsComplexSlider(next);

            if (currentIsComplexSlider
    && complexContextDebugCount < 50)
            {
                double beforeDistance =
     Math.Sqrt(
         Math.Pow(current.X - previous.X, 2) +
         Math.Pow(current.Y - previous.Y, 2));

                double afterDistance =
                    Math.Sqrt(
                        Math.Pow(next.X - current.X, 2) +
                        Math.Pow(next.Y - current.Y, 2));

                double beforeInterval =
                    current.Time - previous.Time;

                double afterInterval =
                    next.Time - current.Time;

                double angle =
                    GetTurnAngle(
                        previous,
                        current,
                        next);

                double sliderStartX = current.X;
                double sliderStartY = current.Y;

                double sliderEndX = current.X;
                double sliderEndY = current.Y;

                if (current.SliderControlPoints.Count > 0)
                {
                    SliderControlPoint lastPoint =
                        current.SliderControlPoints[^1];

                    sliderEndX = lastPoint.X;
                    sliderEndY = lastPoint.Y;
                }

                double sliderTravelDistance =
                    Math.Sqrt(
                        Math.Pow(sliderEndX - sliderStartX, 2) +
                        Math.Pow(sliderEndY - sliderStartY, 2));
                double sliderTravelRatio =
                    current.Length > 0
                        ? sliderTravelDistance / current.Length
                        : 0;


                BeatInsight.Diagnostics.DebugLogger.Detailed(
                    $"COMPLEX CONTEXT DEBUG #{complexContextDebugCount + 1} | " +
                    $"i={i} " +
                    $"Time={current.Time} " +
                    $"Curve={current.SliderCurveType} " +
                    $"CP={current.SliderControlPoints.Count} " +
                    $"Slides={current.Slides} " +
                    $"Length={current.Length:F1} " +
                    $"SliderStart=({sliderStartX:F0},{sliderStartY:F0}) " +
                    $"SliderEnd=({sliderEndX:F0},{sliderEndY:F0}) " +
                    $"SliderTravel={sliderTravelDistance:F1} " +
                    $"TravelRatio={sliderTravelRatio:F2} " +
                    $"BeforeDist={beforeDistance:F1} " +
                    $"AfterDist={afterDistance:F1} " +
                    $"Prev=({previous.X},{previous.Y}) " +
                    $"Curr=({current.X},{current.Y}) " +
                    $"Next=({next.X},{next.Y}) " +
                    $"PrevType={previous.Type} " +
                    $"CurrType={current.Type} " +
                    $"NextType={next.Type}");




                complexContextDebugCount++;
            }

            // Les spinners ne participent pas au Tech.
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
                || secondInterval <= 0)
            {
                continue;
            }
            if (currentIsComplexSlider
               && complexSignalDebugCount < 50)
            {
                BeatInsight.Diagnostics.DebugLogger.Detailed(
                     $"COMPLEX SLIDER SIGNAL DEBUG | " +
                    $"i={i} " +
                    $"Time={current.Time} " +
                    $"Curve={current.SliderCurveType} " +
                    $"CP={current.SliderControlPoints.Count} " +
                    $"Length={current.Length:F1} " +
                    $"BeforeInterval={firstInterval:F1}ms " +
                    $"AfterInterval={secondInterval:F1}ms " +
                    $"PrevComplex={previousIsComplexSlider} " +
                    $"CurrentComplex={currentIsComplexSlider} " +
                    $"NextComplex={nextIsComplexSlider}");

                complexSignalDebugCount++;
            }


            // ----------------------------------------------------
            // Distances
            // ----------------------------------------------------

            var previousEnd =
     GetObjectEndPosition(previous);

            var currentStart =
                GetObjectStartPosition(current);

            var currentEnd =
                GetObjectEndPosition(current);

            var nextStart =
                GetObjectStartPosition(next);


            double firstDistance =
                Math.Sqrt(
                    Math.Pow(currentStart.X - previousEnd.X, 2) +
                    Math.Pow(currentStart.Y - previousEnd.Y, 2));

            double secondDistance =
                Math.Sqrt(
                    Math.Pow(nextStart.X - currentEnd.X, 2) +
                    Math.Pow(nextStart.Y - currentEnd.Y, 2));

            double averageDistance =
                (firstDistance + secondDistance) / 2.0;

            // ----------------------------------------------------
            // Les gros déplacements sont plutôt Jump/Aim.
            // ----------------------------------------------------

            if (firstDistance > 160
                || secondDistance > 160)
            {
                continue;
            }

            transitionCount++;

            // ----------------------------------------------------
            // Transition compacte
            // ----------------------------------------------------

            if (firstDistance <= 110
                && secondDistance <= 110)
            {
                compactTransitions++;
            }

            // ----------------------------------------------------
            // Angle
            // ----------------------------------------------------

            double turnAngle =
                GetTechTurnAngle(
                    previous,
                    current,
                    next);

            bool isStructural =
    turnAngle >= TechStructureAngle;

            bool isAwkward =
                turnAngle >= 45
                && turnAngle <= 150
                && averageDistance <= 125;

            bool isSharp =
                turnAngle >= 150
                && averageDistance <= 125;

            bool isCompact =
                firstDistance <= 110
                && secondDistance <= 110;

            bool isAlternating = false;

            bool spacingVariation =
                Math.Abs(
                    firstDistance
                    - secondDistance) >= 35;

            // ----------------------------------------------------
            // Structure
            // ----------------------------------------------------

            if (turnAngle >= TechStructureAngle)
            {
                structuralTransitions++;
            }

            // ----------------------------------------------------
            // Angles awkward
            //
            // On évite de considérer uniquement les 180°.
            // Les angles intermédiaires sont souvent beaucoup
            // plus intéressants pour détecter une structure Tech.
            // ----------------------------------------------------

            if (turnAngle >= 45
    && turnAngle <= 150
    && averageDistance <= 125)
            {
                awkwardTransitions++;

                techObjects[i - 1] = true;
                techObjects[i] = true;
                techObjects[i + 1] = true;

                techPatternEvidence[i - 1] =
                    Math.Max(
                        techPatternEvidence[i - 1],
                        previousIsComplexSlider
                            ? TechSliderPatternWeight
                            : TechCirclePatternWeight);

                techPatternEvidence[i] =
                    Math.Max(
                        techPatternEvidence[i],
                        currentIsComplexSlider
                            ? TechSliderPatternWeight
                            : TechCirclePatternWeight);

                techPatternEvidence[i + 1] =
                    Math.Max(
                        techPatternEvidence[i + 1],
                        nextIsComplexSlider
                            ? TechSliderPatternWeight
                            : TechCirclePatternWeight);
            }

            // ----------------------------------------------------
            // Sharp reversal
            // ----------------------------------------------------

            if (turnAngle >= 150
    && averageDistance <= 125)
            {
                sharpTransitionCount++;

                techObjects[i - 1] = true;
                techObjects[i] = true;
                techObjects[i + 1] = true;

                techPatternEvidence[i - 1] =
                    Math.Max(
                        techPatternEvidence[i - 1],
                        previousIsComplexSlider
                            ? TechSliderPatternWeight
                            : TechCirclePatternWeight);

                techPatternEvidence[i] =
                    Math.Max(
                        techPatternEvidence[i],
                        currentIsComplexSlider
                            ? TechSliderPatternWeight
                            : TechCirclePatternWeight);

                techPatternEvidence[i + 1] =
                    Math.Max(
                        techPatternEvidence[i + 1],
                        nextIsComplexSlider
                            ? TechSliderPatternWeight
                            : TechCirclePatternWeight);
            }

            // ----------------------------------------------------
            // Fast transition
            // ----------------------------------------------------

            if (firstInterval <= 125
                && secondInterval <= 125)
            {
                fastTransitionCount++;
            }

            // ----------------------------------------------------
            // Variation de spacing
            //
            // Un changement important de distance entre deux
            // mouvements peut créer une structure awkward.
            // ----------------------------------------------------

            double distanceDifference =
                Math.Abs(
                    firstDistance
                    - secondDistance);

            if (distanceDifference >= 35)
            {
                spacingVariationTransitions++;
            }

            // ----------------------------------------------------
            // Alternance
            // ----------------------------------------------------

            if (i >= 2)
            {
                HitObject previousPrevious =
                    objects[i - 2];

                double previousInterval =
                    previous.Time - previousPrevious.Time;

                if (!IsSpinner(previousPrevious)
                    && previousInterval > 0
                    && previousInterval <= TechStructureMaximumInterval
                    && firstInterval <= TechStructureMaximumInterval
                    && secondInterval <= TechStructureMaximumInterval)
                {
                    double previousAngle =
                        GetTechTurnAngle(
                            previousPrevious,
                            previous,
                            current);

                    if (previousAngle >= TechStructureAngle
                        && turnAngle >= TechStructureAngle)
                    {
                        alternatingTransitions++;

                        isAlternating = true;
                    }
                }
            }

            // ----------------------------------------------------
            // Alternance structurelle
            //
            // Deux angles structurels successifs, validés sur une
            // séquence localement continue, constituent une preuve
            // suffisante pour alimenter le masque Tech.
            // ----------------------------------------------------

            if (isStructural && isAlternating)
            {
                for (int techIndex = i - 2;
                     techIndex <= i + 1;
                     techIndex++)
                {
                    techObjects[techIndex] = true;

                    techPatternEvidence[techIndex] =
                        Math.Max(
                            techPatternEvidence[techIndex],
                            IsComplexSlider(objects[techIndex])
                                ? TechSliderPatternWeight
                                : TechCirclePatternWeight);
                }
            }

            DebugTechTransition(
     ref debugCount,
     i,
     previous,
     current,
     next,
     turnAngle,
     averageDistance,
     (firstInterval + secondInterval) / 2.0,
     isSharp,
     isAwkward,
     isStructural,
     isCompact,
     isAlternating,
     spacingVariation);

        }




        // --------------------------------------------------------
        // Signaux
        // --------------------------------------------------------

        double transitionSignal =
            CalculateRatio(
                sharpTransitionCount,
                transitionCount);

        double awkwardSignal =
            CalculateRatio(
                awkwardTransitions,
                transitionCount);

        double compactSignal =
            CalculateRatio(
                compactTransitions,
                transitionCount);

        double spacingVariationSignal =
            CalculateRatio(
                spacingVariationTransitions,
                transitionCount);

        double structureSignal =
            CalculateRatio(
                structuralTransitions,
                transitionCount);

        double alternatingSignal =
            CalculateRatio(
                alternatingTransitions,
                transitionCount);

        double temporalSignal =
            CalculateRatio(
                fastTransitionCount,
                transitionCount);

        double complexSliderRatio =
            CalculateRatio(
                complexSliderCount,
                sliders.Count);


        // --------------------------------------------------------
        // Structure
        // --------------------------------------------------------

        double rawStructureSignal =
            Math.Clamp(
                structureSignal * 0.45
                + alternatingSignal * 0.25
                + awkwardSignal * 0.30,
                0,
                1);

        // --------------------------------------------------------
        // Tech structure
        //
        // Les structures compactes sont beaucoup plus importantes
        // que la simple vitesse.
        // --------------------------------------------------------

        double structureCombinedSignal =
                Math.Clamp(
                rawStructureSignal * 0.80
                + compactSignal * 0.20,
                0,
                1);
        DebugLogger.Detailed(
            $"TECH STRUCTURE DEBUG | " +
            $"Structure={structureSignal:F3} " +
            $"Alternating={alternatingSignal:F3} " +
            $"Awkward={awkwardSignal:F3} " +
            $"Compact={compactSignal:F3} " +
            $"Raw={rawStructureSignal:F3} " +
            $"Combined={structureCombinedSignal:F3}");

        // --------------------------------------------------------
        // Transition signal
        // --------------------------------------------------------

        double sharpSignal =
            NormalizeAboveBaseline(
                transitionSignal,
                0.10,
                0.45);

        // --------------------------------------------------------
        // Spacing awkwardness
        // --------------------------------------------------------

        double spacingSignal =
            NormalizeAboveBaseline(
                spacingVariationSignal,
                0.15,
                0.60);

        // --------------------------------------------------------
        // Spatial overlap
        // --------------------------------------------------------

        double spatialSignal =
            sliders.Count == 0
                ? 0
                : Math.Clamp(
                    (double)sliderSpatialOverlapCount
                    / Math.Max(
                        1.0,
                        sliders.Count),
                    0,
                    1);

        double overlapSignal =
            sliders.Count == 0
                ? 0
                : Math.Min(
                    1,
                    sliderSpatialOverlapCount
                    / Math.Max(
                        1.0,
                        sliders.Count * 0.25));
        double sliderDensity =
    objects.Count == 0
        ? 0
        : (double)sliders.Count / objects.Count;

        DebugLogger.Detailed(
            $"TECH DEBUG | " +
            $"Sliders={sliders.Count} " +
            $"SliderDensity={sliderDensity:P1} " +
            $"Complex={complexSliderCount} " +
            $"ComplexRatio={complexSliderRatio:P1} " +
            $"Overlap={sliderSpatialOverlapCount} " +
            $"Sharp={sharpTransitionCount} " +
            $"Structural={structuralTransitions} " +
            $"Alternating={alternatingTransitions} " +
            $"Fast={fastTransitionCount}");

        // --------------------------------------------------------
        // Signaux intermédiaires
        // --------------------------------------------------------

        double sharpTransitionRatio =
            CalculateRatio(
                sharpTransitionCount,
                transitionCount);

        double fastTransitionRatio =
            CalculateRatio(
                fastTransitionCount,
                transitionCount);

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
        // SCORE TECH V1.5
        //
        // Le Tech ne doit pas dépendre principalement de la vitesse.
        // La structure, les transitions et les sliders complexes
        // constituent les signaux principaux.
        //
        // Temporal reste un signal secondaire.
        // --------------------------------------------------------

        double score =
              sharpTransitionSignal * 0.25
            + structureCombinedSignal * 0.30
            + complexSliderSignal * 0.20
            + overlapSignal * 0.10
            + temporalSignal * 0.05
            + spacingSignal * 0.10;

        double rawScore = Math.Clamp(score, 0, 1);

        int techObjectCount =
     techObjects.Count(
         value => value);

        BeatInsight.Diagnostics.DebugLogger.Detailed(
            $"TECH SCORE DEBUG | " +
            $"Sharp={sharpTransitionSignal:F3} " +
            $"Structure={structureCombinedSignal:F3} " +
            $"Complex={complexSliderSignal:F3} " +
            $"Overlap={overlapSignal:F3} " +
            $"Temporal={temporalSignal:F3} " +
            $"RawScore={rawScore:F3}");

        // --------------------------------------------------------
        // Score final V1.6
        //
        // Le score intrinsèque mesure la présence de signaux Tech.
        // La couverture mesure la proportion réelle d'objets Tech.
        // --------------------------------------------------------

        score =
    Math.Clamp(score, 0, 1);

        BeatInsight.Diagnostics.DebugLogger.Detailed(
                $"TECH INTENSITY DEBUG | Intensity={score * 100:F3}");

        score *= 100;

        // --------------------------------------------------------
        // Objets Tech
        // --------------------------------------------------------



        List<GameplaySection> techSections =
            BuildTechSections(
                objects,
                techObjects,
                techPatternEvidence);

        // --------------------------------------------------------
        // Résultat
        // --------------------------------------------------------

        return new TechAnalysis(
                score,
                techObjectCount,
                complexSliderCount,
                sliderSpatialOverlapCount,
                sharpTransitionCount,
                transitionSignal,
                structureCombinedSignal,
                spatialSignal,
                temporalSignal,
                techSections);

    }
    private static (double X, double Y) GetObjectStartPosition(
    HitObject hitObject)
    {
        return (
            hitObject.X,
            hitObject.Y);
    }

    private static (double X, double Y) GetObjectEndPosition(
    HitObject hitObject)
    {
        if (!IsSlider(hitObject))
        {
            return (
                hitObject.X,
                hitObject.Y);
        }

        return (
            hitObject.SliderEndPosition.X,
            hitObject.SliderEndPosition.Y);
    }

    private static double GetTechTurnAngle(
    HitObject previous,
    HitObject current,
    HitObject next)
    {
        var previousEnd =
            GetObjectEndPosition(previous);

        var currentStart =
            GetObjectStartPosition(current);

        var currentEnd =
            GetObjectEndPosition(current);

        var nextStart =
            GetObjectStartPosition(next);

        double firstX =
            currentStart.X - previousEnd.X;

        double firstY =
            currentStart.Y - previousEnd.Y;

        double secondX =
            nextStart.X - currentEnd.X;

        double secondY =
            nextStart.Y - currentEnd.Y;

        double firstLength =
            Math.Sqrt(
                firstX * firstX +
                firstY * firstY);

        double secondLength =
            Math.Sqrt(
                secondX * secondX +
                secondY * secondY);

        if (firstLength == 0 || secondLength == 0)
            return 0;

        double cosine =
            (
                firstX * secondX +
                firstY * secondY
            )
            / (firstLength * secondLength);

        return Math.Acos(
            Math.Clamp(cosine, -1, 1))
            * 180
            / Math.PI;
    }

    /// <summary>
    /// Construit les sections Tech à partir des preuves de patterns.
    /// 
    /// Les sliders complexes ont une influence de 80 %.
    /// Les cercles présentant une structure Tech ont une influence de 20 %.
    /// 
    /// Cette pondération sert uniquement à la détection des patterns.
    /// Elle n'influence pas directement le Tech.Score.
    /// </summary>
    private static List<GameplaySection> BuildTechSections(
        IReadOnlyList<HitObject> objects,
        bool[] techObjects,
        double[] techPatternEvidence)
    {
        List<GameplaySection> sections = [];

        const int minimumObjects = 4;
        const double maximumGapMs = 300;

        int start = -1;
        int end = -1;

        for (int i = 0; i < objects.Count; i++)
        {
            // Aucun signal Tech sur cet objet.
            if (!techObjects[i]
                || techPatternEvidence[i] <= 0)
            {
                if (start >= 0)
                {
                    AddTechSection(
                        sections,
                        objects,
                        start,
                        end,
                        minimumObjects);

                    start = -1;
                    end = -1;
                }

                continue;
            }

            // ----------------------------------------------------
            // Début d'une section
            // ----------------------------------------------------

            if (start < 0)
            {
                start = i;
                end = i;
                continue;
            }

            // ----------------------------------------------------
            // Continuité temporelle
            // ----------------------------------------------------

            double gap =
                objects[i].Time -
                objects[end].Time;

            if (gap <= maximumGapMs)
            {
                end = i;
            }
            else
            {
                AddTechSection(
                    sections,
                    objects,
                    start,
                    end,
                    minimumObjects);

                start = i;
                end = i;
            }
        }

        // --------------------------------------------------------
        // Dernière section
        // --------------------------------------------------------

        if (start >= 0)
        {
            AddTechSection(
                sections,
                objects,
                start,
                end,
                minimumObjects);
        }

        return sections;
    }

    private static void AddTechSection(
        List<GameplaySection> sections,
        IReadOnlyList<HitObject> objects,
        int start,
        int end,
        int minimumObjects)
    {
        bool containsCircle = false;

        for (int i = start; i <= end; i++)
        {
            if (IsCircle(objects[i]))
            {
                containsCircle = true;
                break;
            }
        }

        if (!containsCircle)
            return;

        AddGameplaySection(
            sections,
            objects,
            start,
            end,
            "Tech",
            minimumObjects);
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
    /// 

    private static bool IsComplexSlider(HitObject hitObject)
    {
        bool complexCurve =
            hitObject.SliderCurveType is "C" or "P";

        bool manyControlPoints =
            hitObject.SliderControlPoints.Count >= 3;

        bool isComplex =
            complexCurve || manyControlPoints;

        if (isComplex && complexSliderDebugCount < 50)
        {
            BeatInsight.Diagnostics.DebugLogger.Detailed(
                $"COMPLEX SLIDER DEBUG #{complexSliderDebugCount + 1} | " +
                $"Time={hitObject.Time} " +
                $"Pos=({hitObject.X},{hitObject.Y}) " +
                $"Curve={hitObject.SliderCurveType} " +
                $"ControlPoints={hitObject.SliderControlPoints.Count} " +
                $"Slides={hitObject.Slides} " +
                $"Length={hitObject.Length:F1} " +
                $"Reason=" +
                $"{(complexCurve ? "Curve" : "")}" +
                $"{(complexCurve && manyControlPoints ? "+" : "")}" +
                $"{(manyControlPoints ? "ControlPoints" : "")}");

            complexSliderDebugCount++;
        }

        return isComplex;
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
    /// Reading V1 repose sur l'anticipation des informations futures
    /// visibles avant leur hit.
    ///
    /// - densité d'informations futures ;
    /// - congestion spatiale entre ces informations ;
    /// - persistance actuellement neutralisée.
    ///
    /// Le score global combine l'intensité locale et la présence de
    /// sections Reading valides dans la map.
    /// </summary>
    private static ReadAnalysis AnalyzeRead(
        Beatmap beatmap,
        IReadOnlyList<HitObject> objects)
    {
        if (objects.Count == 0)
            return new ReadAnalysis(
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                Array.Empty<GameplaySection>());

        double approachTime =
            GetApproachTime(beatmap.AR);

        int analysedReadObjects =
            objects.Count(IsReadVisualObject);

        if (analysedReadObjects == 0)
            return new ReadAnalysis(
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                Array.Empty<GameplaySection>());

        int readObjectCount = 0;
        List<int> readObjectIndices = new();

        double totalDensitySignal = 0;
        double totalClutterSignal = 0;
        double totalIntensity = 0;

        double totalPredictability = 0;
        double totalTemporalRegularity = 0;
        double totalSpacingRegularity = 0;
        double totalTrajectoryRepetition = 0;
        double totalAmbiguity = 0;

        int predictabilitySampleCount = 0;
        int temporalRegularitySampleCount = 0;
        int spacingRegularitySampleCount = 0;
        int trajectoryRepetitionSampleCount = 0;
        int ambiguitySampleCount = 0;

        // --------------------------------------------------------
        // Analyse objet par objet.
        //
        // Reading V1 observe les informations FUTURES déjà visibles
        // au moment où l'objet courant doit être joué.
        // --------------------------------------------------------

        for (int i = 0;
             i < objects.Count;
             i++)
        {
            if (!IsReadVisualObject(objects[i]))
                continue;

            double currentTime =
                objects[i].Time;

            List<HitObject> visibleFutureObjects = new();

            // Le pipeline repose déjà sur des HitObjects chronologiques.
            // Dès que la fenêtre de visibilité est dépassée, les objets
            // suivants ne peuvent plus être des candidats Reading.
            for (int j = i + 1;
                 j < objects.Count;
                 j++)
            {
                double delay =
                    objects[j].Time - currentTime;

                if (delay > approachTime)
                    break;

                if (delay <= 0
                    || !IsReadVisualObject(objects[j]))
                    continue;

                visibleFutureObjects.Add(objects[j]);
            }

            // ----------------------------------------------------
            // 1. DENSITÉ D'INFORMATIONS FUTURES
            // ----------------------------------------------------

            double densitySignal =
                CalculateReadDensitySignal(
                    visibleFutureObjects.Count);

            // ----------------------------------------------------
            // 2. CONGESTION VISUELLE
            // ----------------------------------------------------

            double clutterSignal =
                CalculateReadClutterSignal(
                    visibleFutureObjects);

            // ----------------------------------------------------
            // 3. PERSISTANCE
            //
            // Neutralisée pour V1 : l'ancienne mesure reposait sur
            // des objets déjà joués et ne correspond plus à la
            // sémantique d'anticipation visuelle.
            // ----------------------------------------------------

            const double persistenceSignal = 0.0;

            // ----------------------------------------------------
            // Intensité Reading locale
            // ----------------------------------------------------

            double localIntensity =
                CalculateReadIntensity(
                    densitySignal,
                    clutterSignal,
                    persistenceSignal);

            // Un objet entre dans la présence Reading lorsqu'au
            // moins deux informations futures sont visibles.
            if (visibleFutureObjects.Count >=
                ReadMinimumFutureVisibleObjects)
            {
                readObjectCount++;

                readObjectIndices.Add(i);

                totalDensitySignal +=
                    densitySignal;

                totalClutterSignal +=
                    clutterSignal;

                totalIntensity +=
                    localIntensity;

                ReadPredictabilitySignals predictability =
                    CalculateReadPredictability(
                        objects[i],
                        visibleFutureObjects);

                if (predictability.Predictability is double localPredictability)
                {
                    totalPredictability += localPredictability;
                    predictabilitySampleCount++;
                }

                if (predictability.TemporalRegularity is double temporalRegularity)
                {
                    totalTemporalRegularity += temporalRegularity;
                    temporalRegularitySampleCount++;
                }

                if (predictability.SpacingRegularity is double spacingRegularity)
                {
                    totalSpacingRegularity += spacingRegularity;
                    spacingRegularitySampleCount++;
                }

                if (predictability.TrajectoryRepetition is double trajectoryRepetition)
                {
                    totalTrajectoryRepetition += trajectoryRepetition;
                    trajectoryRepetitionSampleCount++;
                }

                double? localAmbiguity =
                    CalculateReadAmbiguity(
                        visibleFutureObjects);

                if (localAmbiguity is double ambiguity)
                {
                    totalAmbiguity += ambiguity;
                    ambiguitySampleCount++;
                }
            }
        }

        // --------------------------------------------------------
        // Ratio global
        // --------------------------------------------------------

        double readRatio =
            CalculateRatio(
                readObjectCount,
                analysedReadObjects);

        // --------------------------------------------------------
        // Moyennes des signaux et de l'intensité locale.
        // --------------------------------------------------------

        double averageDensitySignal = 0;
        double averageClutterSignal = 0;
        double averagePersistenceSignal = 0;
        double readIntensity = 0;
        double readPredictability = 0;
        double readNovelty = 0;
        double readTemporalRegularity = 0;
        double readSpacingRegularity = 0;
        double readTrajectoryRepetition = 0;
        double readAmbiguity = 0;

        if (readObjectCount > 0)
        {
            averageDensitySignal =
                totalDensitySignal / readObjectCount;

            averageClutterSignal =
                totalClutterSignal / readObjectCount;

            readIntensity =
                totalIntensity / readObjectCount;
        }

        if (predictabilitySampleCount > 0)
        {
            readPredictability =
                totalPredictability / predictabilitySampleCount;

            readNovelty =
                1.0 - readPredictability;
        }

        if (temporalRegularitySampleCount > 0)
        {
            readTemporalRegularity =
                totalTemporalRegularity / temporalRegularitySampleCount;
        }

        if (spacingRegularitySampleCount > 0)
        {
            readSpacingRegularity =
                totalSpacingRegularity / spacingRegularitySampleCount;
        }

        if (trajectoryRepetitionSampleCount > 0)
        {
            readTrajectoryRepetition =
                totalTrajectoryRepetition / trajectoryRepetitionSampleCount;
        }

        if (ambiguitySampleCount > 0)
        {
            readAmbiguity =
                totalAmbiguity / ambiguitySampleCount;
        }

        // --------------------------------------------------------
        // Sections et présence Reading.
        //
        // Les sections ne retiennent que les groupes cohérents
        // d'objets Read originaux, ce qui évite qu'un singleton très
        // dense devienne à lui seul une présence importante.
        // --------------------------------------------------------

        List<GameplaySection> readSections =
            BuildReadSections(
                readObjectIndices,
                objects);

        double readCoverage =
            CalculateReadSectionCoverage(
                readSections,
                analysedReadObjects);

        // --------------------------------------------------------
        // Score Read global.
        //
        // L'intensité décrit la difficulté locale des passages.
        // La présence décrit la proportion de la map couverte par
        // des sections Reading valides.
        // --------------------------------------------------------

        double score =
            readIntensity * readCoverage;

        score =
            Math.Clamp(score, 0, 1) * 100.0;

        return new ReadAnalysis(
            readObjectCount,
            readRatio,
            readCoverage,
            readIntensity * 100.0,
            score,
            averageDensitySignal,
            averageClutterSignal,
            averagePersistenceSignal,
            0.0,
            readPredictability,
            readNovelty,
            readTemporalRegularity,
            readSpacingRegularity,
            readTrajectoryRepetition,
            readAmbiguity,
            readSections);
    }

    private static double CalculateReadDensitySignal(
        int visibleFutureObjectCount)
    {
        return Math.Clamp(
            (double)(visibleFutureObjectCount
                     - ReadDensityBaselineFutureObjects)
            / (ReadDensitySaturationFutureObjects
               - ReadDensityBaselineFutureObjects),
            0,
            1);
    }

    private static double CalculateReadClutterSignal(
        IReadOnlyList<HitObject> visibleFutureObjects)
    {
        if (visibleFutureObjects.Count < 2)
            return 0;

        int pairCount = 0;
        int clutteredPairCount = 0;

        for (int firstIndex = 0;
             firstIndex < visibleFutureObjects.Count - 1;
             firstIndex++)
        {
            for (int secondIndex = firstIndex + 1;
                 secondIndex < visibleFutureObjects.Count;
                 secondIndex++)
            {
                pairCount++;

                if (Distance(
                        visibleFutureObjects[firstIndex],
                        visibleFutureObjects[secondIndex])
                    <= ReadClutterDistance)
                {
                    clutteredPairCount++;
                }
            }
        }

        return pairCount == 0
            ? 0
            : Math.Clamp(
                (double)clutteredPairCount / pairCount,
                0,
                1);
    }

    /// <summary>
    /// Mesure l'ambiguïté visuelle de paires futures : deux objets proches
    /// spatialement, mais séparés dans leur ordre temporel, contribuent
    /// davantage. Cette métrique est strictement observationnelle.
    /// </summary>
    private static double? CalculateReadAmbiguity(
        IReadOnlyList<HitObject> visibleFutureObjects)
    {
        if (visibleFutureObjects.Count < 2)
            return null;

        int pairCount = 0;
        double totalAmbiguity = 0;

        for (int firstIndex = 0;
             firstIndex < visibleFutureObjects.Count - 1;
             firstIndex++)
        {
            for (int secondIndex = firstIndex + 1;
                 secondIndex < visibleFutureObjects.Count;
                 secondIndex++)
            {
                HitObject first =
                    visibleFutureObjects[firstIndex];

                HitObject second =
                    visibleFutureObjects[secondIndex];

                double spatialProximity =
                    Math.Clamp(
                        1.0 - Distance(first, second)
                        / ReadClutterDistance,
                        0,
                        1);

                double temporalSeparation =
                    Math.Clamp(
                        Math.Abs(first.Time - second.Time)
                        / ReadAmbiguityTemporalSaturationMs,
                        0,
                        1);

                totalAmbiguity +=
                    spatialProximity * temporalSeparation;

                pairCount++;
            }
        }

        return pairCount == 0
            ? null
            : Math.Clamp(
                totalAmbiguity / pairCount,
                0,
                1);
    }

    /// <summary>
    /// Mesure la prévisibilité d'une fenêtre Reading avec trois signaux
    /// indépendants du score actuel : régularité temporelle, régularité
    /// du spacing et répétition directionnelle de la trajectoire.
    ///
    /// Les signaux qui ne disposent pas de suffisamment de données sont
    /// exclus de la moyenne plutôt que ramenés artificiellement à zéro.
    /// </summary>
    private static ReadPredictabilitySignals CalculateReadPredictability(
        HitObject current,
        IReadOnlyList<HitObject> visibleFutureObjects)
    {
        List<HitObject> readingWindow =
            new(visibleFutureObjects.Count + 1)
            {
                current
            };

        foreach (HitObject future in visibleFutureObjects)
            readingWindow.Add(future);

        List<double> intervals = [];
        List<double> distances = [];
        List<(double X, double Y)> vectors = [];

        for (int index = 1;
             index < readingWindow.Count;
             index++)
        {
            HitObject previous =
                readingWindow[index - 1];

            HitObject next =
                readingWindow[index];

            intervals.Add(next.Time - previous.Time);

            double x = next.X - previous.X;
            double y = next.Y - previous.Y;

            distances.Add(
                Math.Sqrt(x * x + y * y));

            vectors.Add((x, y));
        }

        double? temporalRegularity =
            CalculateReadRobustRegularity(
                intervals,
                requireStrictlyPositiveValues: true);

        double? spacingRegularity =
            CalculateReadRobustRegularity(
                distances,
                requireStrictlyPositiveValues: false);

        double? trajectoryRepetition =
            CalculateReadTrajectoryRepetition(vectors);

        List<double> validComponents = [];

        if (temporalRegularity is double temporal)
            validComponents.Add(temporal);

        if (spacingRegularity is double spacing)
            validComponents.Add(spacing);

        if (trajectoryRepetition is double trajectory)
            validComponents.Add(trajectory);

        double? predictability =
            validComponents.Count == 0
                ? null
                : Math.Clamp(
                    validComponents.Average(),
                    0,
                    1);

        return new ReadPredictabilitySignals(
            predictability,
            temporalRegularity,
            spacingRegularity,
            trajectoryRepetition);
    }

    /// <summary>
    /// Calcule 1 - clamp(MAD(values) / median(values), 0, 1).
    /// Le signal est indisponible lorsque l'échantillon est insuffisant,
    /// contient une valeur non finie ou possède une médiane non positive.
    /// </summary>
    private static double? CalculateReadRobustRegularity(
        IReadOnlyList<double> values,
        bool requireStrictlyPositiveValues)
    {
        if (values.Count < 2
            || values.Any(value => !double.IsFinite(value))
            || (requireStrictlyPositiveValues
                && values.Any(value => value <= 0)))
        {
            return null;
        }

        double median =
            CalculateReadMedian(values);

        if (!double.IsFinite(median)
            || median <= 0)
        {
            return null;
        }

        List<double> absoluteDeviations =
            values
                .Select(value => Math.Abs(value - median))
                .ToList();

        double mad =
            CalculateReadMedian(absoluteDeviations);

        if (!double.IsFinite(mad))
            return null;

        return 1.0 - Math.Clamp(
            mad / median,
            0,
            1);
    }

    /// <summary>
    /// Mesure la répétition directionnelle des mouvements avec les décalages
    /// 1, 2 et 3. Pour chaque décalage, les cosinus des vecteurs normalisés
    /// sont convertis de [-1, 1] vers [0, 1], puis le meilleur décalage est
    /// retenu. Une longueur de spacing identique sans direction répétée ne
    /// peut donc pas produire un score maximal.
    /// </summary>
    private static double? CalculateReadTrajectoryRepetition(
        IReadOnlyList<(double X, double Y)> vectors)
    {
        double? bestLagScore = null;

        for (int lag = 1;
             lag <= 3;
             lag++)
        {
            List<double> similarities = [];

            for (int index = lag;
                 index < vectors.Count;
                 index++)
            {
                (double X, double Y) previous =
                    vectors[index - lag];

                (double X, double Y) current =
                    vectors[index];

                double previousLength =
                    Math.Sqrt(
                        previous.X * previous.X
                        + previous.Y * previous.Y);

                double currentLength =
                    Math.Sqrt(
                        current.X * current.X
                        + current.Y * current.Y);

                if (!double.IsFinite(previousLength)
                    || !double.IsFinite(currentLength)
                    || previousLength <= 0
                    || currentLength <= 0)
                {
                    continue;
                }

                double cosine =
                    (previous.X * current.X
                     + previous.Y * current.Y)
                    / (previousLength * currentLength);

                double directionSimilarity =
                    (Math.Clamp(cosine, -1, 1) + 1.0) / 2.0;

                similarities.Add(directionSimilarity);
            }

            if (similarities.Count == 0)
                continue;

            double lagScore =
                Math.Clamp(
                    similarities.Average(),
                    0,
                    1);

            bestLagScore = bestLagScore is null
                ? lagScore
                : Math.Max(bestLagScore.Value, lagScore);
        }

        return bestLagScore;
    }

    private static double CalculateReadMedian(
        IReadOnlyList<double> values)
    {
        List<double> sortedValues =
            values.OrderBy(value => value).ToList();

        int middle = sortedValues.Count / 2;

        return sortedValues.Count % 2 == 0
            ? (sortedValues[middle - 1] + sortedValues[middle]) / 2.0
            : sortedValues[middle];
    }

    private static double CalculateReadIntensity(
        double densitySignal,
        double clutterSignal,
        double persistenceSignal)
    {
        return Math.Clamp(
            (densitySignal * ReadDensityWeight
             + clutterSignal * ReadClutterWeight
             + persistenceSignal * ReadPersistenceWeight)
            / ReadActiveSignalWeight,
            0,
            1);
    }

    private static List<GameplaySection> BuildReadSections(
        IReadOnlyList<int> readObjectIndices,
        IReadOnlyList<HitObject> objects)
    {
        List<GameplaySection> sections = new();

        if (readObjectIndices.Count == 0)
            return sections;

        int sectionStart = readObjectIndices[0];
        int previousIndex = readObjectIndices[0];

        for (int i = 1; i < readObjectIndices.Count; i++)
        {
            int currentIndex = readObjectIndices[i];

            bool consecutive =
                currentIndex == previousIndex + 1;

            double gap =
                objects[currentIndex].Time -
                objects[previousIndex].Time;

            if (!consecutive
                || gap <= 0
                || gap > ReadMaximumSectionGapMs)
            {
                AddReadSection(
                    sections,
                    objects,
                    sectionStart,
                    previousIndex);

                sectionStart = currentIndex;
            }

            previousIndex = currentIndex;
        }

        AddReadSection(
            sections,
            objects,
            sectionStart,
            previousIndex);

        return sections;
    }

    private static void AddReadSection(
        List<GameplaySection> sections,
        IReadOnlyList<HitObject> objects,
        int startIndex,
        int endIndex)
    {
        int objectCount =
            endIndex - startIndex + 1;

        if (objectCount < ReadMinimumSectionObjects)
            return;

        sections.Add(
            CreateGameplaySection(
                "Read",
                startIndex,
                endIndex,
                objects));
    }

    private static double CalculateReadSectionCoverage(
        IReadOnlyList<GameplaySection> sections,
        int analysedReadObjects)
    {
        if (sections.Count == 0 || analysedReadObjects == 0)
            return 0;

        int coveredReadObjects =
            sections.Sum(section => section.ObjectCount);

        return Math.Clamp(
            (double)coveredReadObjects / analysedReadObjects,
            0,
            1);
    }

    private static GameplaySection CreateGameplaySection(
    string type,
    int startIndex,
    int endIndex,
    IReadOnlyList<HitObject> objects)
    {
        return new GameplaySection(
            type,
            startIndex,
            endIndex,
            objects[startIndex].Time,
            objects[endIndex].Time,
            endIndex - startIndex + 1);
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
    /// Le score Speed combine l'intensité de cadence des sections
    /// rapides et leur présence réelle dans la map.
    /// </summary>
    private const double SpeedLightThreshold = 0.25;
    private const double SpeedModerateThreshold = 0.50;
    private const double SpeedStrongThreshold = 0.75;
    private const double SpeedCadenceSaturationObjectsPerSecond = 12.0;
    private const int SpeedNoContributionSectionObjects = 2;
    private const int SpeedFullContributionSectionObjects = 8;
    private static SpeedAnalysis AnalyzeSpeed(IReadOnlyList<HitObject> objects, Beatmap beatmap)
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
                0,
                0,
                0,
                0,
                Array.Empty<GameplaySection>());
        }

        bool[] fastObjects =
            new bool[objects.Count];

        bool[] fastTransitionEnds =
            new bool[objects.Count];

        int fastTransitions = 0;
        int totalTransitions = 0;

        // 125 ms correspond à environ 8 objets/seconde.
        const double fastInterval = 125;

        // --------------------------------------------------------
        // Détection des transitions rapides.
        // --------------------------------------------------------

        for (int i = 1;
             i < objects.Count;
             i++)
        {
            if (!IsCircle(objects[i - 1])
                || !IsCircle(objects[i]))
            {
                continue;
            }

            double interval =
                objects[i].Time -
                objects[i - 1].Time;

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

                fastTransitionEnds[i] = true;
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

        int speedObjectCount =
            fastObjects.Count(value => value);

        List<GameplaySection> speedSections =
            BuildSpeedSections(
                objects,
                fastTransitionEnds);

        double intensity =
            CalculateSpeedIntensity(
                speedSections);

        double presence =
            CalculateSpeedPresence(
                speedSections,
                circles.Count);

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
            intensity * presence;

        score =
            Math.Clamp(score, 0, 1) * 100;

        return new SpeedAnalysis(
                speedObjectCount,
                score,
                fastRatio,
                fastObjectRatio,
                densitySignal,
                arSignal,
                intensity,
                presence,
                speedSections);
    }

    private static double CalculateSpeedIntensity(
        IReadOnlyList<GameplaySection> speedSections)
    {
        double weightedIntensity = 0;
        double totalWeight = 0;

        foreach (GameplaySection section in speedSections)
        {
            double duration =
                section.EndTime - section.StartTime;

            if (duration <= 0)
                continue;

            double lengthFactor =
                CalculateSpeedSectionLengthFactor(
                    section.ObjectCount);

            if (lengthFactor <= 0)
                continue;

            double cadence =
                (section.ObjectCount - 1) * 1000.0
                / duration;

            double cadenceSignal =
                Math.Clamp(
                    cadence / SpeedCadenceSaturationObjectsPerSecond,
                    0,
                    1);

            double weight =
                section.ObjectCount * lengthFactor;

            weightedIntensity +=
                cadenceSignal * weight;

            totalWeight += weight;
        }

        return totalWeight == 0
            ? 0
            : Math.Clamp(
                weightedIntensity / totalWeight,
                0,
                1);
    }

    private static double CalculateSpeedPresence(
        IReadOnlyList<GameplaySection> speedSections,
        int analysedCircles)
    {
        if (analysedCircles == 0)
            return 0;

        double weightedObjectCount = 0;

        foreach (GameplaySection section in speedSections)
        {
            weightedObjectCount +=
                section.ObjectCount
                * CalculateSpeedSectionLengthFactor(
                    section.ObjectCount);
        }

        return Math.Clamp(
            weightedObjectCount / analysedCircles,
            0,
            1);
    }

    private static double CalculateSpeedSectionLengthFactor(
        int objectCount)
    {
        return Math.Clamp(
            (double)(objectCount - SpeedNoContributionSectionObjects)
            / (SpeedFullContributionSectionObjects
               - SpeedNoContributionSectionObjects),
            0,
            1);
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

    private static string GetSpeedIntensity(double score)
    {
        return score switch
        {
            >= 70 => "High",
            >= 40 => "Medium",
            _ => "Low"
        };
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

    /// <summary>
    /// Détermine la présence de Speed dans la map
    /// à partir de sa couverture et de son score.
    /// </summary>
    private static string GetSpeedPresenceProfile(
    double coverage,
    double score)
    {
        if (coverage >= 0.70 && score >= 60)
            return "Dominant Speed Presence";

        if (coverage >= 0.50 && score >= 45)
            return "Strong Speed Presence";

        if (coverage >= 0.30 && score >= 35)
            return "Moderate Speed Presence";

        if (coverage >= 0.15 && score >= 25)
            return "Light Speed Presence";

        return "Minimal Speed Presence";
    }
    /// <summary>
    /// Construit les sections Speed à partir des objets
    /// considérés comme rapides.
    /// </summary>
    private static List<GameplaySection> BuildSpeedSections(
        IReadOnlyList<HitObject> objects,
        IReadOnlyList<bool> fastTransitionEnds)
    {
        List<GameplaySection> sections = new();

        if (objects.Count == 0 ||
            fastTransitionEnds.Count != objects.Count)
            return sections;

        int sectionStart = -1;

        for (int i = 1; i < objects.Count; i++)
        {
            bool isFastTransition =
                fastTransitionEnds[i];

            // ----------------------------------------------------
            // Début d'une section Speed.
            // ----------------------------------------------------

            if (isFastTransition && sectionStart == -1)
            {
                sectionStart = i - 1;
                continue;
            }

            // ----------------------------------------------------
            // Fin d'une section Speed.
            // ----------------------------------------------------

            if (!isFastTransition && sectionStart != -1)
            {
                int sectionEnd = i - 1;

                int objectCount =
                    sectionEnd - sectionStart + 1;

                if (objectCount >= 2)
                {
                    sections.Add(
                        new GameplaySection(
                            "Speed",
                            sectionStart,
                            sectionEnd,
                            objects[sectionStart].Time,
                            objects[sectionEnd].Time,
                            objectCount));
                }

                sectionStart = -1;
            }
        }

        // --------------------------------------------------------
        // Ferme une éventuelle section en fin de map.
        // --------------------------------------------------------

        if (sectionStart != -1)
        {
            int sectionEnd =
                objects.Count - 1;

            int objectCount =
                sectionEnd - sectionStart + 1;

            if (objectCount >= 2)
            {
                sections.Add(
                    new GameplaySection(
                        "Speed",
                        sectionStart,
                        sectionEnd,
                        objects[sectionStart].Time,
                        objects[sectionEnd].Time,
                        objectCount));
            }
        }

        return sections;
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
        double precisionModifier =
            Math.Clamp(
                1.0 + 0.05 * (beatmap.CS - 4.0),
                0.85,
                1.20);

        double totalSignificantDistance = 0;
        double totalMovementSpeed = 0;

        int movementCount = 0;
        int significantMovementCount = 0;
        int aimObjectCount = 0;

        // ---------------------------------------------------------
        // DISTANCE + VITESSE
        // ---------------------------------------------------------

        for (int i = 1; i < objects.Count; i++)
        {
            HitObject previous = objects[i - 1];
            HitObject current = objects[i];

            if (!IsCircle(previous)
                || !IsCircle(current))
            {
                continue;
            }

            double distance =
                Distance(previous, current);

            double interval =
                current.Time - previous.Time;

            if (interval <= 0)
                continue;

            double movementSpeed =
                distance / (interval / 1000.0);

            // ---------------------------------------------------------
            // IDENTIFICATION D'UN MOUVEMENT AIM
            // ---------------------------------------------------------

            bool isAimMovement =
                distance >= AimDistanceBaseline
                && movementSpeed >= AimSpeedBaseline;

            if (isAimMovement)
            {
                aimObjectCount++;
            }

            movementCount++;

            if (distance >= AimDistanceBaseline)
            {
                totalSignificantDistance += distance;
                totalMovementSpeed += movementSpeed;
                significantMovementCount++;
            }
        }

        if (movementCount == 0)
            return new AimAnalysis(
                        0,
                        0,
                        0,
                        precisionModifier,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0.60);

        double averageDistance =
            significantMovementCount == 0
                ? 0
                : totalSignificantDistance / significantMovementCount;

        double averageMovementSpeed =
            significantMovementCount == 0
                ? 0
                : totalMovementSpeed / significantMovementCount;


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
            CalculateAimAngleSignal(objects);


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
            CalculateAimTemporalSignal(objects);


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

        double temporalModifier =
            0.60 + 0.40 * temporalSignal;

        double rawIntensity =
            Math.Clamp(
                baseAim * temporalModifier,
                0,
                1);

        double intensity =
            Math.Clamp(
                rawIntensity * precisionModifier,
                0,
                1);

        int analysedCircles =
            objects.Count(IsCircle);

        double presence =
            CalculateRatio(
                significantMovementCount,
                analysedCircles);

        double score =
            intensity * presence * 100.0;

        return new AimAnalysis(
             aimObjectCount,
             score,
             rawIntensity,
             precisionModifier,
             intensity,
             presence,
             distanceSignal,
             speedSignal,
             angleSignal,
             temporalSignal,
             temporalModifier);
    }

    private static double CalculateAimTemporalSignal(
    IReadOnlyList<HitObject> objects)
    {
        if (objects.Count < 2)
            return 0;

        int validTransitions = 0;
        int pressuredTransitions = 0;

        // En dessous de cette valeur, le déplacement doit être
        // effectué suffisamment rapidement pour créer une vraie
        // pression temporelle.
        const double temporalThresholdMs = 180;

        for (int i = 1; i < objects.Count; i++)
        {
            if (!IsCircle(objects[i - 1])
                || !IsCircle(objects[i]))
            {
                continue;
            }

            double distance =
                Distance(objects[i - 1], objects[i]);

            if (distance < AimDistanceBaseline)
                continue;

            double interval =
                objects[i].Time - objects[i - 1].Time;

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
        IReadOnlyList<HitObject> objects)
    {
        if (objects.Count < 3)
            return 0;

        int angleCount = 0;
        int sharpAngleCount = 0;
        int reverseCount = 0;

        for (int i = 1; i < objects.Count - 1; i++)
        {
            HitObject previous = objects[i - 1];
            HitObject current = objects[i];
            HitObject next = objects[i + 1];

            if (!IsCircle(previous)
                || !IsCircle(current)
                || !IsCircle(next))
            {
                continue;
            }

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

            if (firstLength < AimDistanceBaseline
                || secondLength < AimDistanceBaseline)
            {
                continue;
            }

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
       int AimObjectCount,
       double Score,
       double RawIntensity,
       double PrecisionModifier,
       double Intensity,
       double Presence,
       double DistanceSignal,
       double SpeedSignal,
       double AngleSignal,
       double TemporalSignal,
       double TemporalModifier);


    private static string DeterminePrimaryType(
    double streamCoverage,
    double jumpCoverage,
    double techCoverage,
    double techScore)
    {
        // --------------------------------------------------------
        // Dominance basée principalement sur la couverture réelle.
        // --------------------------------------------------------

        double maxCoverage = Math.Max(
            streamCoverage,
            Math.Max(jumpCoverage, techCoverage));

        // Pas suffisamment de couverture spécialisée :
        // on considère la map comme Classic / Mixed.
        if (maxCoverage < 0.10)
        {
            return "Classic / Mixed";
        }

        // --------------------------------------------------------
        // Vérification de la dominance.
        // --------------------------------------------------------

        // Stream dominant
        if (streamCoverage >= jumpCoverage &&
            streamCoverage >= techCoverage)
        {
            // Si Stream domine réellement la couverture,
            // TechScore ne doit pas écraser cette classification.
            if (streamCoverage >= 0.20)
                return "Stream";

            // Entre deux profils proches => Mixed.
            if (streamCoverage - Math.Max(jumpCoverage, techCoverage) < 0.05)
                return "Classic / Mixed";

            return "Stream";
        }

        // Jump dominant
        if (jumpCoverage >= streamCoverage &&
            jumpCoverage >= techCoverage)
        {
            if (jumpCoverage >= 0.20)
                return "Jump";

            if (jumpCoverage - Math.Max(streamCoverage, techCoverage) < 0.05)
                return "Classic / Mixed";

            return "Jump";
        }

        // --------------------------------------------------------
        // Tech dominant
        // --------------------------------------------------------

        if (techCoverage >= streamCoverage &&
            techCoverage >= jumpCoverage)
        {
            // Tech doit être suffisamment présent ET avoir
            // un score technique cohérent.
            if (techCoverage >= 0.10 && techScore >= 55)
                return "Tech";

            if (techCoverage - Math.Max(streamCoverage, jumpCoverage) < 0.05)
                return "Classic / Mixed";

            return "Classic / Mixed";
        }

        return "Classic / Mixed";
    }

    private static string BuildGameplayIdentity(
    string primaryType)
    {
        if (string.IsNullOrWhiteSpace(primaryType))
            return "Classic / Mixed";

        return primaryType;
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

    private static List<string> GenerateGameplayConcepts(
    string primaryType,
    AimAnalysis aim,
    SpeedAnalysis speed,
    TechAnalysis tech,
    ReadAnalysis read)
    {
        HashSet<string> concepts = [];

        // ============================================================
        // SPEED
        // ============================================================

        if (speed.Score >= 35)
            concepts.Add("speed");

        if (speed.SpeedRatio >= 0.15)
            concepts.Add("stream");

        if (speed.SpeedRatio >= 0.20)
            concepts.Add("burst");

        // ============================================================
        // AIM
        // ============================================================

        if (aim.Score >= 35)
            concepts.Add("aim");

        if (aim.DistanceSignal >= 0.45)
            concepts.Add("spacing");

        if (aim.AngleSignal >= 0.60)
            concepts.Add("angle");

        if (aim.TemporalSignal >= 0.60)
            concepts.Add("timing");

        // ============================================================
        // READING
        // ============================================================

        if (read.Score >= 35)
            concepts.Add("reading");

        if (read.DensitySignal >= 0.45)
            concepts.Add("density");

        if (read.ClutterSignal >= 0.45)
            concepts.Add("clutter");

        if (read.PersistenceSignal >= 0.45)
            concepts.Add("persistence");

        if (read.CSSignal >= 0.50)
            concepts.Add("cs");

        // ============================================================
        // TECH
        // ============================================================

        if (tech.Score >= 35)
            concepts.Add("tech");

        if (tech.StructureSignal >= 0.45)
            concepts.Add("structure");

        if (tech.TransitionSignal >= 0.45)
            concepts.Add("transition");

        if (tech.SpatialSignal >= 0.45)
            concepts.Add("spatial");

        if (tech.TemporalSignal >= 0.45)
            concepts.Add("timing");

        return concepts.ToList();
    }


    // ============================================================
    // 14. NIVEAUX DE DIFFICULTÉ
    // ============================================================

    /// <summary>
    /// Convertit le score Tech en niveau.
    /// </summary>
    private static string GetTechLevel(
    double score,
    double coverage)
    {
        if (coverage < 0.05)
            return "Minor";

        if (score < 25)
            return "Low";

        if (score < 50)
            return "Medium";

        if (score < 75)
            return "High";

        return "Extreme";
    }

    private static string GetTechProfile(
    double coverage,
    double score)
    {
        if (coverage < 0.05)
            return "Minor Technical Presence";

        if (coverage < 0.10)
            return score >= 60
                ? "Focused Technical Presence"
                : "Moderate Technical Presence";

        if (coverage < 0.20)
            return score >= 60
                ? "Strong Technical Presence"
                : "Moderate Technical Presence";

        return score >= 60
            ? "Dominant Technical Presence"
            : "Strong Technical Presence";
    }

    private static double GetStreamIdentityScore(
    string primaryType,
    SpeedAnalysis speed)
    {
        if (primaryType.Contains(
                "Stream",
                StringComparison.OrdinalIgnoreCase))
        {
            return 100.0;
        }

        if (speed.SpeedRatio >= 0.20)
        {
            return speed.SpeedRatio * 100.0;
        }

        return speed.SpeedRatio * 70.0;
    }

    private static double GetJumpIdentityScore(
        string primaryType)
    {
        if (primaryType.Contains(
                "Jump",
                StringComparison.OrdinalIgnoreCase))
        {
            return 100.0;
        }

        return 0.0;
    }

    private static GameplayIdentity AnalyzeGameplayIdentity(
    string primaryType,
    double streamCoverage,
    double jumpCoverage,
    double techCoverage,
    AimAnalysis aim,
    SpeedAnalysis speed,
    TechAnalysis tech,
    ReadAnalysis read)
    {
        // ============================================================
        // SCORES STRUCTURELS
        //
        // La couverture représente la présence réelle de chaque
        // famille de gameplay dans la map.
        //
        // Aim / Speed / Reading ne participent PAS à l'identité.
        // ============================================================

        double streamStructuralScore =
            streamCoverage * 100.0;

        double jumpStructuralScore =
            jumpCoverage * 100.0;

        double techStructuralScore =
            CalculateTechIdentityScore(
                streamCoverage,
                jumpCoverage,
                techCoverage,
                tech.Score);
        DebugLogger.Detailed(
            $"IDENTITY SCORES | " +
            $"Stream={streamStructuralScore:F1} | " +
            $"Jump={jumpStructuralScore:F1} | " +
            $"Tech={techStructuralScore:F1}");
        DebugLogger.Detailed(
            $"TECH IDENTITY DEBUG | " +
            $"Coverage={techCoverage:P1} " +
            $"TechScore={tech.Score:F1} " +
            $"IdentityScore={techStructuralScore:F1}");


        var structuralScores =
            new List<(string Name, double Score)>
            {
            ("Stream", streamStructuralScore),
            ("Jump", jumpStructuralScore),
            ("Tech", techStructuralScore)
            };

        List<(string Name, double Score)> ordered =
            structuralScores
                .OrderByDescending(x => x.Score)
                .ToList();

        // ============================================================
        // PRIMARY
        //
        // L'identité structurelle devient la source de vérité.
        //
        // Stream / Jump utilisent leur couverture structurelle.
        // Tech utilise son TechIdentityScore.
        //
        // Aim / Speed / Reading ne participent jamais à la
        // classification primaire.
        // ============================================================

        string primary =
            "Classic / Mixed";

        if (ordered.Count > 0)
        {
            (string Name, double Score) candidate =
                ordered[0];

            if (candidate.Score >= PrimaryIdentityThreshold)
            {
                primary = candidate.Name;
            }
        }

        // ============================================================
        // SCORE DU PRIMARY
        // ============================================================

        double primaryScore =
            ordered
                .FirstOrDefault(
                    x => x.Name.Equals(
                        primary,
                        StringComparison.OrdinalIgnoreCase))
                .Score;

        // ============================================================
        // SECONDARY
        //
        // Seules les identités structurelles peuvent être Secondary.
        //
        // Conditions :
        // - au moins 10 % de couverture
        // - pas trop éloigné du Primary
        //
        // Classic / Mixed n'est jamais un Secondary.
        // ============================================================

        string secondary = "";

        double secondaryScore = 0.0;

        if (!primary.Equals(
                "Classic / Mixed",
                StringComparison.OrdinalIgnoreCase))
        {
            foreach ((string Name, double Score) candidate in ordered)
            {
                if (candidate.Name.Equals(
                        primary,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Présence structurelle minimale.
                if (candidate.Score < 10.0)
                    continue;

                // Le Secondary doit avoir une présence significative
                // par rapport au Primary.
                //
                // Exemple :
                // Jump 32,7 / Tech 23,8 -> accepté
                // Jump 70 / Tech 8      -> refusé
                double relativePresence =
                    primaryScore > 0.0
                        ? candidate.Score / primaryScore
                        : 0.0;

                if (relativePresence >= 0.50)
                {
                    secondary =
                        candidate.Name;

                    secondaryScore =
                        candidate.Score;

                    break;
                }
            }
        }

        // ============================================================
        // PATTERN
        // ============================================================

        string pattern =
            string.IsNullOrWhiteSpace(secondary)
                ? primary
                : $"{primary} / {secondary}";

        // ============================================================
        // TRAITS
        // ============================================================

        List<string> traits =
            GenerateGameplayTraits(
                primary,
                aim,
                speed,
                tech,
                read);

        // ============================================================
        // CONCEPTS
        // ============================================================

        List<string> concepts =
            GenerateGameplayConcepts(
                primary,
                aim,
                speed,
                tech,
                read);

        // ============================================================
        // CONFIDENCE
        //
        // UNIQUEMENT basée sur la structure.
        //
        // Plus le Primary domine son Secondary,
        // plus la confiance augmente.
        // ============================================================

        double confidence;

        if (primary.Equals(
                "Classic / Mixed",
                StringComparison.OrdinalIgnoreCase))
        {
            confidence = 55.0;
        }
        else
        {
            double separation =
                primaryScore > 0.0
                    ? (primaryScore - secondaryScore)
                      / primaryScore
                    : 0.0;

            separation =
                Math.Clamp(
                    separation,
                    0.0,
                    1.0);

            confidence =
                50.0
                + (primaryScore * 0.40)
                + (separation * 30.0);

            confidence =
                Math.Clamp(
                    confidence,
                    50.0,
                    95.0);
        }




        // ============================================================
        // RESULTAT
        // ============================================================

        return new GameplayIdentity
        {
            Primary = primary,
            Secondary = secondary,
            Pattern = pattern,
            Confidence = confidence,
            StreamScore = streamStructuralScore,
            JumpScore = jumpStructuralScore,
            TechScore = techStructuralScore,
            Traits = CleanTraits(traits),
            Concepts = concepts
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
        // PRESSIONS PRINCIPALES
        // ============================================================

        if (speed.Score >= 60)
        {
            traits.Add("High Speed Pressure");
        }
        else if (speed.Score >= 35)
        {
            traits.Add("Speed Influence");
        }

        if (aim.Score >= 60)
        {
            traits.Add("High Aim Pressure");
        }
        else if (aim.Score >= 35)
        {
            traits.Add("Aim Influence");
        }

        if (read.Score >= 60)
        {
            traits.Add("High Reading Demand");
        }
        else if (read.Score >= 35)
        {
            traits.Add("Reading Influence");
        }

        if (tech.Score >= 60)
        {
            traits.Add("High Technical Pressure");
        }
        else if (tech.Score >= 35)
        {
            traits.Add("Technical Influence");
        }

        // ============================================================
        // STRUCTURES
        // ============================================================

        if (primaryType.Contains(
                "Stream",
                StringComparison.OrdinalIgnoreCase))
        {
            traits.Add("Stream Heavy");
        }
        else if (speed.SpeedRatio >= 0.15)
        {
            traits.Add("Stream Influence");
        }

        if (primaryType.Contains(
                "Jump",
                StringComparison.OrdinalIgnoreCase))
        {
            traits.Add("Jump Heavy");
        }

        // ============================================================
        // BURSTS
        // ============================================================

        if (primaryType.Contains(
                "Stream",
                StringComparison.OrdinalIgnoreCase)
            || speed.SpeedRatio >= 0.20)
        {
            // Le Burst est particulièrement pertinent
            // lorsque la pression Speed est présente.
            traits.Add("Burst Influence");
        }

        // ============================================================
        // SPEED STRUCTURE
        // ============================================================

        if (speed.FastObjectRatio >= 0.30)
        {
            traits.Add("Fast Patterns");
        }

        if (speed.DensitySignal >= 0.40)
        {
            traits.Add("High Density");
        }

        // ============================================================
        // AIM STRUCTURE
        // ============================================================

        if (aim.DistanceSignal >= 0.45)
        {
            traits.Add("Large Spacing");
        }

        if (aim.AngleSignal >= 0.60)
        {
            traits.Add("Directional Changes");
        }

        if (aim.TemporalSignal >= 0.60)
        {
            traits.Add("Aim Timing Pressure");
        }

        // ============================================================
        // TECH STRUCTURE
        // ============================================================

        if (tech.Score >= 45)
        {
            traits.Add("Technical Patterns");
        }

        if (tech.StructureSignal >= 0.45)
        {
            traits.Add("Structured Patterns");
        }

        if (tech.TransitionSignal >= 0.45)
        {
            traits.Add("Sharp Transitions");
        }

        // ============================================================
        // READ STRUCTURE
        // ============================================================

        if (read.DensitySignal >= 0.45)
        {
            traits.Add("Reading Density");
        }

        if (read.ClutterSignal >= 0.45)
        {
            traits.Add("Visual Clutter");
        }

        if (read.PersistenceSignal >= 0.45)
        {
            traits.Add("Persistent Reading");
        }

        // ============================================================
        // SECONDARY TYPE
        // ============================================================

        if (primaryType.Contains(" / "))
        {
            string[] parts =
                primaryType.Split('/');

            if (parts.Length >= 2)
            {
                string secondary =
                    parts[1].Trim();

                traits.Add(
                    $"{secondary} Secondary");
            }
        }

        return traits
            .Distinct()
            .ToList();
    }

    private static GameplayStyleProfile AnalyzeGameplayStyle(
    AimAnalysis aim,
    SpeedAnalysis speed,
    ReadAnalysis read)
    {
        double aimValue =
            aim.Score;

        double speedValue =
            speed.Score;

        double readValue =
            read.Score;

        // --------------------------------------------------------
        // Détermination du skill dominant.
        //
        // Aim / Speed / Reading sont des dimensions transversales.
        // Elles ne définissent jamais l'identité structurelle primaire.
        // --------------------------------------------------------

        string dominantSkill;

        double highestValue =
            Math.Max(
                aimValue,
                Math.Max(
                    speedValue,
                    readValue));

        // Aucun skill ne présente une influence suffisamment forte.
        if (highestValue < 40)
        {
            dominantSkill = "Balanced";
        }
        else if (aimValue >= speedValue
                 && aimValue >= readValue)
        {
            dominantSkill = "Aim";
        }
        else if (speedValue >= aimValue
                 && speedValue >= readValue)
        {
            dominantSkill = "Speed";
        }
        else
        {
            dominantSkill = "Reading";
        }

        // --------------------------------------------------------
        // Description du skill dominant.
        // --------------------------------------------------------

        string description =
            dominantSkill switch
            {
                "Aim" =>
                    "Focuses on cursor control and movement.",

                "Speed" =>
                    "Requires fast tapping and sustained speed.",

                "Reading" =>
                    "Requires strong visual processing.",

                _ =>
                    "Balanced gameplay."
            };

        return new GameplayStyleProfile
        {
            DominantSkill = dominantSkill,

            AimInfluence = aimValue,

            SpeedInfluence = speedValue,

            ReadInfluence = readValue,

            Description = description
        };
    }

    private static double CalculateTechIdentityScore(
    double streamCoverage,
    double jumpCoverage,
    double techCoverage,
    double techScore)
    {
        // --------------------------------------------------------
        // 1. Présence structurelle Tech
        // --------------------------------------------------------

        double coverageComponent =
            Math.Clamp(
                techCoverage,
                0.0,
                1.0);

        // --------------------------------------------------------
        // 2. Force intrinsèque des patterns Tech
        //
        // Le TechScore renforce l'identité,
        // mais ne doit jamais compenser une faible couverture.
        // --------------------------------------------------------

        double scoreComponent =
            Math.Clamp(
                techScore / 60.0,
                0.0,
                1.0);

        // --------------------------------------------------------
        // 3. Dominance structurelle
        // --------------------------------------------------------

        double strongestCompetitor =
            Math.Max(
                streamCoverage,
                jumpCoverage);

        double dominanceComponent;

        if (techCoverage <= 0.0)
        {
            dominanceComponent = 0.0;
        }
        else if (strongestCompetitor <= 0.0)
        {
            dominanceComponent = 1.0;
        }
        else
        {
            dominanceComponent =
                techCoverage /
                (techCoverage + strongestCompetitor);
        }

        // --------------------------------------------------------
        // 4. Identité finale
        //
        // La couverture domine.
        // Le score Tech renforce.
        // La dominance départage.
        // --------------------------------------------------------

        double identityScore =
            (coverageComponent * 0.60)
            + (scoreComponent * 0.15)
            + (dominanceComponent * 0.25);

        // --------------------------------------------------------
        // DEBUG
        // --------------------------------------------------------

        DebugLogger.Detailed(
            "===== TECH IDENTITY SCORE DEBUG =====");

        DebugLogger.Detailed(
            $"Tech Coverage       = {techCoverage:F3}");

        DebugLogger.Detailed(
            $"Tech Score          = {techScore:F3}");

        DebugLogger.Detailed(
            $"Stream Coverage     = {streamCoverage:F3}");

        DebugLogger.Detailed(
            $"Jump Coverage       = {jumpCoverage:F3}");

        DebugLogger.Detailed(
            $"Coverage Component  = {coverageComponent:F3}");

        DebugLogger.Detailed(
             $"Score Component     = {scoreComponent:F3}");

        DebugLogger.Detailed(
            $"Dominance Component = {dominanceComponent:F3}");

        DebugLogger.Detailed(
            $"Identity Score      = {identityScore * 100.0:F3}");

        DebugLogger.Detailed(
            "====================================");

        return identityScore * 100.0;
    }

    private static double CalculatePrimaryConfidence(
    string primaryType,
    double streamCoverage,
    double jumpCoverage,
    double techCoverage,
    double techScore)
    {
        double stream = streamCoverage;
        double jump = jumpCoverage;

        double tech =
            techCoverage *
            (0.5 + 0.5 * (techScore / 100.0));

        double[] values =
        {
        stream,
        jump,
        tech
    };

        Array.Sort(values);

        double highest = values[2];
        double secondHighest = values[1];

        if (highest <= 0)
            return 0;

        double dominance =
            highest / (highest + secondHighest);

        double separation =
            (highest - secondHighest) / highest;

        double confidence =
            (dominance * 0.6) +
            (separation * 0.4);

        return Math.Clamp(
            confidence * 100.0,
            0.0,
            100.0);
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
    /// Vérifie si l'objet apporte une information visuelle Reading.
    /// Les sliders ne contribuent que via leur head/start ; les
    /// spinners restent exclus.
    /// </summary>
    private static bool IsReadVisualObject(
        HitObject hitObject)
    {
        if (IsSpinner(hitObject))
            return false;

        return IsCircle(hitObject)
            || IsSlider(hitObject);
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
            return 0;
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

    /// <summary>
    /// Ajoute une section Jump si elle respecte le nombre minimum
    /// d'objets.
    /// </summary>
    private static void AddJumpSection(
        List<GameplaySection> sections,
        IReadOnlyList<HitObject> objects,
        int start,
        int end,
        int minimumObjects)
    {
        if (start < 0 ||
            end < start ||
            end >= objects.Count)
        {
            return;
        }

        int objectCount =
            end - start + 1;

        if (objectCount < minimumObjects)
            return;

        sections.Add(
            new GameplaySection(
                "Jump",
                start,
                end,
                objects[start].Time,
                objects[end].Time,
                objectCount));
    }

    // ============================================================
    // SECTIONS DE GAMEPLAY
    // ============================================================

    /// <summary>
    /// Construit les sections temporelles d'une famille de gameplay.
    ///
    /// Une section correspond à une zone continue de la map où
    /// plusieurs objets appartiennent au même pattern.
    ///
    /// Cela permet de distinguer :
    ///
    /// - quelques objets isolés
    /// - de véritables zones de gameplay cohérentes
    ///
    /// Le masque fourni doit utiliser le même index que
    /// Beatmap.HitObjects.
    /// </summary>
    private static List<GameplaySection> BuildGameplaySections(
        IReadOnlyList<HitObject> objects,
        bool[] patternObjects,
        string type)
    {
        List<GameplaySection> sections = [];

        // --------------------------------------------------------
        // Paramètres communs
        // --------------------------------------------------------

        const int minimumObjects = 4;

        // Deux objets appartenant à la même section doivent
        // rester suffisamment proches dans le temps.
        const double maximumGapMs = 300;

        int start = -1;
        int end = -1;

        // --------------------------------------------------------
        // Parcours des HitObjects
        // --------------------------------------------------------

        for (int i = 0; i < objects.Count; i++)
        {
            // Seuls les cercles participent actuellement
            // aux sections Stream / Jump / Tech.
            if (!IsCircle(objects[i])
                || !patternObjects[i])
            {
                if (start >= 0)
                {
                    AddGameplaySection(
                        sections,
                        objects,
                        start,
                        end,
                        type,
                        minimumObjects);

                    start = -1;
                    end = -1;
                }

                continue;
            }

            // ----------------------------------------------------
            // Début d'une nouvelle section
            // ----------------------------------------------------

            if (start < 0)
            {
                start = i;
                end = i;
                continue;
            }

            // ----------------------------------------------------
            // Écart temporel avec le dernier objet de la section
            // ----------------------------------------------------

            double gap =
                objects[i].Time -
                objects[end].Time;

            if (gap <= maximumGapMs)
            {
                end = i;
            }
            else
            {
                // La section précédente est terminée.
                AddGameplaySection(
                    sections,
                    objects,
                    start,
                    end,
                    type,
                    minimumObjects);

                // Nouvelle section.
                start = i;
                end = i;
            }
        }

        // --------------------------------------------------------
        // Dernière section
        // --------------------------------------------------------

        if (start >= 0)
        {
            AddGameplaySection(
                sections,
                objects,
                start,
                end,
                type,
                minimumObjects);
        }

        return sections;
    }

    /// <summary>
    /// Calcule la proportion de la durée de la map couverte
    /// par les sections d'un type de gameplay.
    ///
    /// Les sections représentent des zones temporelles cohérentes.
    /// On mesure donc leur durée totale plutôt que simplement
    /// le nombre d'objets détectés.
    /// </summary>
    private static double CalculateSectionCoverage(
    IReadOnlyList<GameplaySection> sections,
    IReadOnlyList<HitObject> objects)
    {
        int totalObjects =
            objects.Count(IsCircle);

        if (totalObjects == 0 || sections.Count == 0)
            return 0;

        int coveredObjects =
            sections.Sum(section => section.ObjectCount);

        return Math.Clamp(
            (double)coveredObjects / totalObjects,
            0,
            1);
    }

    private static int CountSectionCircles(
    IReadOnlyList<GameplaySection> sections,
    IReadOnlyList<HitObject> objects)
    {
        int count = 0;

        foreach (GameplaySection section in sections)
        {
            int start = Math.Max(0, section.StartObjectIndex);
            int end = Math.Min(objects.Count - 1, section.EndObjectIndex);

            for (int i = start; i <= end; i++)
            {
                if (IsCircle(objects[i]))
                    count++;
            }
        }

        return count;
    }


    /// <summary>
    /// Ajoute une section si elle contient suffisamment d'objets.
    /// </summary>
    private static void AddGameplaySection(
        List<GameplaySection> sections,
        IReadOnlyList<HitObject> objects,
        int start,
        int end,
        string type,
        int minimumObjects)
    {
        int objectCount =
            end - start + 1;

        if (objectCount < minimumObjects)
            return;

        sections.Add(
            new GameplaySection(
                type,
                start,
                end,
                objects[start].Time,
                objects[end].Time,
                objectCount));
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
       double TemporalSignal,
       List<GameplaySection> TechSections);

    /// <summary>
    /// Résultat intermédiaire de l'analyse Read.
    /// </summary>
    private sealed record ReadAnalysis(
    int ReadObjectCount,
    double Ratio,
    double Coverage,
    double Intensity,
    double Score,
    double DensitySignal,
    double ClutterSignal,
    double PersistenceSignal,
    double CSSignal,
    double ReadPredictability,
    double ReadNovelty,
    double ReadTemporalRegularity,
    double ReadSpacingRegularity,
    double ReadTrajectoryRepetition,
    double ReadAmbiguity,
    IReadOnlyList<GameplaySection> ReadSections);

    private sealed record ReadPredictabilitySignals(
    double? Predictability,
    double? TemporalRegularity,
    double? SpacingRegularity,
    double? TrajectoryRepetition);

    private static string GetReadPresenceProfile(double readRatio)
    {
        if (readRatio < 0.20)
            return "Minimal Reading Presence";

        if (readRatio < 0.40)
            return "Light Reading Presence";

        if (readRatio < 0.60)
            return "Moderate Reading Presence";

        if (readRatio < 0.80)
            return "Focused Reading Presence";

        return "Dominant Reading Presence";
    }

    private static AimProfile GetAimProfile(
    double aimPresence,
    double aimIntensity)
    {
        string profile;

        if (aimPresence >= 0.75)
            profile = "Dominant Aim Presence";
        else if (aimPresence >= 0.55)
            profile = "Strong Aim Presence";
        else if (aimPresence >= 0.30)
            profile = "Moderate Aim Presence";
        else if (aimPresence >= 0.12)
            profile = "Light Aim Presence";
        else
            profile = "Minimal Aim Presence";

        string intensity;

        if (aimIntensity >= 0.70)
            intensity = "High";
        else if (aimIntensity >= 0.40)
            intensity = "Medium";
        else
            intensity = "Low";

        return new AimProfile(
            aimPresence,
            aimIntensity,
            profile,
            intensity);
    }

    private static string GetReadIntensity(double score)
    {
        if (score < 30)
            return "Low";

        if (score < 50)
            return "Medium";

        if (score < 70)
            return "High";

        if (score < 85)
            return "Very High";

        return "Extreme";
    }

    private static string GetSpeedProfile(
    double coverage,
    double score)
    {
        if (coverage < SpeedLightThreshold)
            return "Minimal Speed Presence";

        if (coverage < SpeedModerateThreshold)
            return "Light Speed Presence";

        if (coverage < SpeedStrongThreshold)
            return "Moderate Speed Presence";

        return "Strong Speed Presence";
    }


    private sealed record AimProfile(
    double Coverage,
    double IntensityValue,
    string Profile,
    string Intensity);

    /// <summary>
    /// Résultat intermédiaire de l'analyse Speed.
    /// </summary>
    public record SpeedAnalysis(
    int SpeedObjectCount,
    double Score,
    double SpeedRatio,
    double FastObjectRatio,
    double DensitySignal,
    double ARSignal,
    double Intensity,
    double Presence,
    IReadOnlyList<GameplaySection> SpeedSections);
}
