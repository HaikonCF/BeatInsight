using BeatInsight.Models;

namespace BeatInsight.Diagnostics;

public static class GameplayDebug
{
    // ============================================================
    // DEBUG SWITCHES
    // ============================================================

    public static bool IdentityEnabled = true;
    public static bool TechEnabled = true;
    public static bool ReadEnabled = false;
    public static bool SpeedEnabled = false;
    public static bool AimEnabled = false;
    public static bool SummaryEnabled = false;
    public static bool CommunityTagsEnabled = true;

    // ============================================================
    // IDENTITY
    // ============================================================

    public static void Identity(
        GameplayIdentity identity)
    {
        if (!IdentityEnabled)
            return;

        DebugLogger.Log(
            "===== TAG / GAMEPLAY IDENTITY =====");

        DebugLogger.Log(
            $"GAMEPLAY IDENTITY = {identity.FullName}");

        DebugLogger.Log(
            $"PRIMARY = {identity.Primary}");

        DebugLogger.Log(
            $"SECONDARY = {identity.Secondary}");

        DebugLogger.Log(
            $"PATTERN = {identity.Pattern}");

        DebugLogger.Log(
            $"IDENTITY CONFIDENCE = {identity.Confidence:F1}%");

        DebugLogger.Log(
            $"TRAITS = {(identity.Traits.Count > 0
                ? string.Join(" | ", identity.Traits)
                : "None")}");

        DebugLogger.Log(
            $"CONCEPTS = {(identity.Concepts.Count > 0
                ? string.Join(" | ", identity.Concepts)
                : "None")}");
    }
    public static void IdentityScores(
    double streamStructuralScore,
    double jumpStructuralScore,
    double techIdentityScore,
    double primaryTechStructuralScore,
    double secondaryTechStructuralScore,
    bool enabled = false)
    {
        if (!enabled)
            return;

        DebugLogger.Log("===== IDENTITY SCORES =====");

        DebugLogger.Log(
            $"STREAM = {streamStructuralScore:F2}");

        DebugLogger.Log(
            $"JUMP = {jumpStructuralScore:F2}");

        DebugLogger.Log(
            $"TECH RAW = {techIdentityScore:F2}");

        DebugLogger.Log(
            $"TECH PRIMARY = {primaryTechStructuralScore:F2}");

        DebugLogger.Log(
            $"TECH SECONDARY = {secondaryTechStructuralScore:F2}");
    }

    // ============================================================
    // COMMUNITY IDENTITY AGREEMENT
    // ============================================================

    public static void CommunityIdentityAgreement(
        CommunityIdentityAgreement agreement)
    {
        if (!CommunityTagsEnabled)
            return;

        DebugLogger.Log(
            "===== COMMUNITY IDENTITY AGREEMENT =====");

        if (!agreement.HasStructuralEvidence)
        {
            DebugLogger.Log(
                "AGREEMENT = Unavailable | No structural community evidence");

            DebugLogger.Log(
                $"RELIABILITY = {agreement.Reliability:P1}");

            return;
        }

        DebugLogger.Log(
            $"AGREEMENT = {agreement.Agreement!.Value:P1}");

        DebugLogger.Log(
            $"RELIABILITY = {agreement.Reliability:P1}");

        DebugLogger.Log(
            $"RELEVANT VOTES = {agreement.RelevantVotes}");

        DebugLogger.Log(
            $"VOTE MASS = {agreement.RelevantVoteMass:F3}");

        DebugLogger.Log(
            $"MATCHED FAMILIES = {(agreement.MatchedFamilies.Count > 0
                ? string.Join(" | ", agreement.MatchedFamilies)
                : "None")}");

        DebugLogger.Log(
            $"CONFLICTING FAMILIES = {(agreement.ConflictingFamilies.Count > 0
                ? string.Join(" | ", agreement.ConflictingFamilies)
                : "None")}");
    }

    // ============================================================
    // TECH
    // ============================================================

    public static void Tech(
        GameplayProfile profile)
    {
        if (!TechEnabled)
            return;

        DebugLogger.Log(
            "===== TECH PROFILE =====");

        DebugLogger.Log(
            $"TECH = {profile.TechObjectCount} circles / " +
            $"{profile.TechRatio:P2} / " +
            $"Score {profile.TechScore:F0}/100 " +
            $"({profile.TechLevel})");

        DebugLogger.Log(
            $"TECH PRESENCE = {profile.TechPresence:P0}");

        DebugLogger.Log(
            $"TECH INTENSITY = {profile.TechIntensity:F0}/100");

        DebugLogger.Log(
            $"TECH SCORE FINAL = {profile.TechScore:F0}/100");

        DebugLogger.Log(
            $"TECH SIGNALS = " +
            $"Transition {profile.TechTransitionSignal:P0} / " +
            $"Structure {profile.TechStructureSignal:P0} / " +
            $"Spatial {profile.TechSpatialSignal:P0} / " +
            $"Temporal {profile.TechTemporalSignal:P0}");

        DebugLogger.Log(
            $"TECH PROFILE = {profile.TechProfile}");
    }

    public static void TechIdentity(
    double techCoverage,
    double techIdentityScore,
    bool techPrimaryEligible,
    bool techSecondaryEligible,
    bool enabled = false)
    {
        if (!enabled)
            return;

        DebugLogger.Log("===== TECH IDENTITY DEBUG =====");

        DebugLogger.Log(
            $"TECH COVERAGE = {techCoverage:P2}");

        DebugLogger.Log(
            $"TECH IDENTITY RAW = {techIdentityScore:F2}");

        DebugLogger.Log(
            $"TECH PRIMARY ELIGIBLE = {techPrimaryEligible}");

        DebugLogger.Log(
            $"TECH SECONDARY ELIGIBLE = {techSecondaryEligible}");
    }

    // ============================================================
    // READ
    // ============================================================

    public static void Read(
        GameplayProfile profile)
    {
        if (!ReadEnabled)
            return;

        DebugLogger.Log(
            "===== READ PROFILE =====");

        DebugLogger.Log(
            $"READ = {profile.ReadObjectCount} visual objects / " +
            $"{profile.ReadRatio:P2} / " +
            $"Final Score {profile.ReadScore:F0}/100 " +
            $"({profile.ReadLevel})");

        DebugLogger.Log(
            $"READ SIGNALS = " +
            $"Density {profile.ReadDensitySignal:P0} / " +
            $"Clutter {profile.ReadClutterSignal:P0} / " +
            "Persistence neutralized / " +
            "CS neutralized");

        DebugLogger.Log(
            $"READ PRESENCE = {profile.ReadCoverage:P2} / " +
            $"Sections {profile.ReadSections.Count}");

        DebugLogger.Log(
            $"READ PROFILE = {profile.ReadProfile}");

        DebugLogger.Log(
            $"READ INTENSITY = {profile.ReadIntensity}");

        DebugLogger.Log(
            $"READ PREDICTABILITY = {profile.ReadPredictability:P0} / " +
            $"Novelty {profile.ReadNovelty:P0}");

        DebugLogger.Log(
            $"READ REGULARITY = " +
            $"Temporal {profile.ReadTemporalRegularity:P0} / " +
            $"Spacing {profile.ReadSpacingRegularity:P0} / " +
            $"Trajectory {profile.ReadTrajectoryRepetition:P0}");

        DebugLogger.Log(
            $"READ AMBIGUITY = {profile.ReadAmbiguity:P0}");
    }

    // ============================================================
    // SPEED
    // ============================================================

    public static void Speed(
        GameplayProfile profile)
    {
        if (!SpeedEnabled)
            return;

        DebugLogger.Log(
            "===== SPEED PROFILE =====");

        DebugLogger.Log(
            $"SPEED = {profile.SpeedObjectCount} circles / " +
            $"{profile.SpeedRatio:P2} / " +
            $"Score {profile.SpeedScore:F0}/100 " +
            $"({profile.SpeedLevel})");

        DebugLogger.Log(
            $"SPEED COVERAGE = {profile.SpeedCoverage:P2}");

        DebugLogger.Log(
            $"SPEED PROFILE = {profile.SpeedProfile}");

        DebugLogger.Log(
            $"SPEED INTENSITY = {profile.SpeedIntensity}");

        DebugLogger.Log(
            $"SPEED SIGNALS = " +
            $"Density {profile.SpeedDensitySignal:P0} / " +
            $"AR {profile.SpeedARSignal:P0}");
    }

    // ============================================================
    // AIM
    // ============================================================

    public static void Aim(
        GameplayProfile profile)
    {
        if (!AimEnabled)
            return;

        DebugLogger.Log(
            "===== AIM PROFILE =====");

        DebugLogger.Log(
            $"AIM = Score {profile.AimScore:F0}/100 " +
            $"({profile.AimLevel})");

        DebugLogger.Log(
            $"AIM SIGNALS = " +
            $"Distance {profile.AimDistanceSignal:P0} / " +
            $"Speed {profile.AimSpeedSignal:P0} / " +
            $"Angle {profile.AimAngleSignal:P0} / " +
            $"Temporal {profile.AimTemporalSignal:P0}");

        DebugLogger.Log(
            $"AIM COVERAGE = {profile.AimCoverage:P2}");

        DebugLogger.Log(
            $"AIM TEMPORAL = Signal {profile.AimTemporalSignal:P0} / " +
            $"Modifier {profile.AimTemporalModifier:F3}");

        DebugLogger.Log(
            $"AIM PRECISION = CS {profile.AimPrecisionCS:F2} / " +
            $"Modifier {profile.AimPrecisionModifier:F3}");

        DebugLogger.Log(
            $"AIM PROFILE = {profile.AimProfile}");

        DebugLogger.Log(
            $"AIM INTENSITY = " +
            $"Raw {profile.AimRawIntensity:P0} / " +
            $"Final {profile.AimAdjustedIntensity:P0} " +
            $"({profile.AimIntensity})");
    }

    // ============================================================
    // SUMMARY
    // ============================================================

    public static void Summary(
        GameplayProfile profile)
    {
        if (!SummaryEnabled)
            return;

        DebugLogger.Log(
            "===== GAMEPLAY PROFILE =====");

        DebugLogger.Log(
            $"ANALYSED CIRCLES = {profile.AnalysedCircleCount}");

        DebugLogger.Log(
            $"PRIMARY TYPE = {profile.PrimaryType}");

        DebugLogger.Log(
            $"STREAMS = {profile.StreamSequenceCount} sequences / " +
            $"{profile.StreamObjectCount} circles / " +
            $"{profile.StreamRatio:P2}");

        DebugLogger.Log(
            $"JUMPS = {profile.JumpSequenceCount} sequences / " +
            $"{profile.JumpObjectCount} circles / " +
            $"{profile.JumpRatio:P2}");

        DebugLogger.Log(
            $"BURSTS = {profile.BurstSequenceCount} sequences / " +
            $"{profile.BurstObjectCount} circles / " +
            $"{profile.BurstRatio:P2} / " +
            $"Max {profile.LongestBurstLength}");
    }
}
