using BeatInsight.Models;

namespace BeatInsight.Diagnostics;

public static class GameplayDebug
{
    public static bool GlobalTripletSpeedEnabled = false;
    public static bool RatingV2Enabled = false;
    public static bool IdentityV3Enabled = false;
    public static bool SectionAnalysisV2Enabled = false;
    public static bool SectionAnalysisV2PerformanceEnabled = false;
    public static bool SkillPressureEnabled = false;
    public static bool GameplayFusionEnabled = false;
    // ============================================================
    // DEBUG SWITCHES
    // ============================================================

    public static bool IdentityEnabled = false;
    public static bool TechEnabled = false;
    public static bool ReadEnabled = false;
    public static bool SpeedEnabled = false;
    public static bool AimEnabled = false;
    public static bool SummaryEnabled = false;
    public static bool CommunityTagsEnabled = true;
    public static bool EdgeJumpEnabled = false;
    public static bool EdgeJumpDetailedEnabled = false;
    public static bool SharpJumpEnabled = false;
    public static bool SharpJumpDetailedEnabled = false;
    public static bool TriangleJumpEnabled = false;
    public static bool TriangleJumpDetailedEnabled = false;
    public static bool OneTwoJumpEnabled = false;
    public static bool OneTwoJumpDetailedEnabled = false;
    public static bool SpacedJumpEnabled = false;
    public static bool SpacedJumpDetailedEnabled = false;
    public static bool WideAngleJumpEnabled = false;
    public static bool WideAngleJumpDetailedEnabled = false;
    public static bool ReversalJumpEnabled = false;
    public static bool ReversalJumpDetailedEnabled = false;
    public static bool ArcJumpEnabled = false;
    public static bool ArcJumpDetailedEnabled = false;
    public static bool JumpSpacingTrendEnabled = false;
    public static bool JumpSpacingTrendDetailedEnabled = false;
    public static bool AlternatingSpacingJumpEnabled = false;
    public static bool AlternatingSpacingJumpDetailedEnabled = false;
    public static bool BurstPatternEnabled = false;
    public static bool BurstPatternDetailedEnabled = false;
    public static bool ShortStreamPatternEnabled = false;
    public static bool ShortStreamPatternDetailedEnabled = false;
    public static bool LongStreamPatternEnabled = false;
    public static bool LongStreamPatternDetailedEnabled = false;
    public static bool DeathstreamPatternEnabled = false;
    public static bool DeathstreamPatternDetailedEnabled = false;
    public static bool StaminaStreamPatternEnabled = false;
    public static bool StaminaStreamPatternDetailedEnabled = false;
    public static bool IrregularStreamPatternEnabled = false;
    public static bool IrregularStreamPatternDetailedEnabled = false;
    public static bool SpeedStreamPatternEnabled = false;
    public static bool SpeedStreamPatternDetailedEnabled = false;
    public static bool FlowStreamPatternEnabled = false;
    public static bool FlowStreamPatternDetailedEnabled = false;
    public static bool AlternatingStreamPatternEnabled = false;
    public static bool AlternatingStreamPatternDetailedEnabled = false;
    public static bool SliderChainPatternEnabled = false;
    public static bool SliderChainPatternDetailedEnabled = false;
    public static bool ComplexSliderPatternEnabled = false;
    public static bool ComplexSliderPatternDetailedEnabled = false;
    public static bool SliderDirectionChangePatternEnabled = false;
    public static bool SliderDirectionChangePatternDetailedEnabled = false;
    public static bool CircleSliderTransitionPatternEnabled = false;
    public static bool CircleSliderTransitionPatternDetailedEnabled = false;
    public static bool RhythmTransitionPatternEnabled = false;
    public static bool RhythmTransitionPatternDetailedEnabled = false;
    public static bool SpatialAwkwardTransitionPatternEnabled = false;
    public static bool SpatialAwkwardTransitionPatternDetailedEnabled = false;
    public static bool OverlapPatternEnabled = false;
    public static bool OverlapPatternDetailedEnabled = false;

    public static void BurstPattern(
        BeatInsight.Models.StreamPatterns.BurstPatternAnalysis result,
        bool enabled = false,
        bool detailed = false)
    {
        if (!enabled) return;
        DebugLogger.Log("===== BURST PATTERN DEBUG =====");
        DebugLogger.Log($"Objects = {result.TotalObjects}");
        DebugLogger.Log($"Burst Sequences = {result.BurstSequenceCount}");
        DebugLogger.Log($"Burst Objects = {result.BurstObjectCount}");
        DebugLogger.Log($"Coverage = {result.Coverage:P2}");
        DebugLogger.Log($"Longest Burst = {result.LongestBurst}");
        DebugLogger.Log($"Average Burst Length = {result.AverageBurstLength:F2}");
        DebugLogger.Log($"Average Interval = {result.AverageInterval:F2}ms");
        DebugLogger.Log($"Average Duration = {result.AverageDuration:F2}ms");
        DebugLogger.Log($"Average Interval Consistency = {result.AverageIntervalConsistency:P2}");
        DebugLogger.Log($"Confidence = {result.Confidence:F3}");
        if (!detailed) return;
        foreach (var sequence in result.Runs)
        {
            DebugLogger.Log("BURST SEQUENCE");
            DebugLogger.Log($"StartIndex = {sequence.StartIndex}");
            DebugLogger.Log($"EndIndex = {sequence.EndIndex}");
            DebugLogger.Log($"StartTime = {sequence.StartTime}");
            DebugLogger.Log($"EndTime = {sequence.EndTime}");
            DebugLogger.Log($"ObjectCount = {sequence.ObjectCount}");
            DebugLogger.Log($"Intervals = [{string.Join(", ", sequence.Intervals.Select(interval => interval.ToString("F2")))}]");
            DebugLogger.Log($"AverageInterval = {sequence.AverageInterval:F2}");
            DebugLogger.Log($"MinInterval = {sequence.MinimumInterval:F2}");
            DebugLogger.Log($"MaxInterval = {sequence.MaximumInterval:F2}");
            DebugLogger.Log($"IntervalConsistency = {sequence.IntervalConsistency:F3}");
            DebugLogger.Log($"Duration = {sequence.Duration:F2}");
            DebugLogger.Log($"Qualified = {sequence.Qualified}");
        }
    }

    public static void ShortStreamPattern(
        BeatInsight.Models.StreamPatterns.ShortStreamPatternAnalysis result,
        bool enabled = false,
        bool detailed = false)
    {
        if (!enabled) return;
        DebugLogger.Log("===== SHORT STREAM PATTERN DEBUG =====");
        DebugLogger.Log($"Objects = {result.TotalObjects}");
        DebugLogger.Log($"Short Stream Sequences = {result.ShortStreamSequenceCount}");
        DebugLogger.Log($"Short Stream Objects = {result.ShortStreamObjectCount}");
        DebugLogger.Log($"Coverage = {result.Coverage:P2}");
        DebugLogger.Log($"Longest Short Stream = {result.LongestShortStream}");
        DebugLogger.Log($"Average Stream Length = {result.AverageShortStreamLength:F2}");
        DebugLogger.Log($"Average Interval = {result.AverageInterval:F2}ms");
        DebugLogger.Log($"Average Duration = {result.AverageDuration:F2}ms");
        DebugLogger.Log($"Average Interval Consistency = {result.AverageIntervalConsistency:P2}");
        DebugLogger.Log($"Confidence = {result.Confidence:F3}");
        if (!detailed) return;
        foreach (var sequence in result.Runs)
        {
            DebugLogger.Log("SHORT STREAM SEQUENCE");
            DebugLogger.Log($"StartIndex = {sequence.StartIndex}");
            DebugLogger.Log($"EndIndex = {sequence.EndIndex}");
            DebugLogger.Log($"StartTime = {sequence.StartTime}");
            DebugLogger.Log($"EndTime = {sequence.EndTime}");
            DebugLogger.Log($"ObjectCount = {sequence.ObjectCount}");
            DebugLogger.Log($"Intervals = [{string.Join(", ", sequence.Intervals.Select(interval => interval.ToString("F2")))}]");
            DebugLogger.Log($"AverageInterval = {sequence.AverageInterval:F2}");
            DebugLogger.Log($"MinInterval = {sequence.MinimumInterval:F2}");
            DebugLogger.Log($"MaxInterval = {sequence.MaximumInterval:F2}");
            DebugLogger.Log($"IntervalConsistency = {sequence.IntervalConsistency:F3}");
            DebugLogger.Log($"Duration = {sequence.Duration:F2}");
            DebugLogger.Log($"Qualified = {sequence.Qualified}");
        }
    }

    public static void LongStreamPattern(
        BeatInsight.Models.StreamPatterns.LongStreamPatternAnalysis result,
        bool enabled = false,
        bool detailed = false)
    {
        if (!enabled) return;
        DebugLogger.Log("===== LONG STREAM PATTERN DEBUG =====");
        DebugLogger.Log($"Objects = {result.TotalObjects}");
        DebugLogger.Log($"Long Stream Sequences = {result.LongStreamSequenceCount}");
        DebugLogger.Log($"Long Stream Objects = {result.LongStreamObjectCount}");
        DebugLogger.Log($"Coverage = {result.Coverage:P2}");
        DebugLogger.Log($"Longest Long Stream = {result.LongestLongStream}");
        DebugLogger.Log($"Average Stream Length = {result.AverageLongStreamLength:F2}");
        DebugLogger.Log($"Average Interval = {result.AverageInterval:F2}ms");
        DebugLogger.Log($"Average Duration = {result.AverageDuration:F2}ms");
        DebugLogger.Log($"Average Interval Consistency = {result.AverageIntervalConsistency:P2}");
        DebugLogger.Log($"Confidence = {result.Confidence:F3}");
        if (!detailed) return;
        foreach (var sequence in result.Runs)
        {
            DebugLogger.Log("LONG STREAM SEQUENCE");
            DebugLogger.Log($"StartIndex = {sequence.StartIndex}");
            DebugLogger.Log($"EndIndex = {sequence.EndIndex}");
            DebugLogger.Log($"StartTime = {sequence.StartTime}");
            DebugLogger.Log($"EndTime = {sequence.EndTime}");
            DebugLogger.Log($"ObjectCount = {sequence.ObjectCount}");
            DebugLogger.Log($"Intervals = [{string.Join(", ", sequence.Intervals.Select(interval => interval.ToString("F2")))}]");
            DebugLogger.Log($"AverageInterval = {sequence.AverageInterval:F2}");
            DebugLogger.Log($"MinInterval = {sequence.MinimumInterval:F2}");
            DebugLogger.Log($"MaxInterval = {sequence.MaximumInterval:F2}");
            DebugLogger.Log($"IntervalConsistency = {sequence.IntervalConsistency:F3}");
            DebugLogger.Log($"Duration = {sequence.Duration:F2}");
            DebugLogger.Log($"Qualified = {sequence.Qualified}");
        }
    }

    public static void DeathstreamPattern(
        BeatInsight.Models.StreamPatterns.DeathstreamPatternAnalysis result,
        bool enabled = false,
        bool detailed = false)
    {
        if (!enabled) return;
        DebugLogger.Log("===== DEATHSTREAM PATTERN DEBUG =====");
        DebugLogger.Log($"Objects = {result.TotalObjects}");
        DebugLogger.Log($"Deathstream Sequences = {result.DeathstreamSequenceCount}");
        DebugLogger.Log($"Deathstream Objects = {result.DeathstreamObjectCount}");
        DebugLogger.Log($"Coverage = {result.Coverage:P2}");
        DebugLogger.Log($"Longest Deathstream = {result.LongestDeathstream}");
        DebugLogger.Log($"Average Deathstream Length = {result.AverageDeathstreamLength:F2}");
        DebugLogger.Log($"Average Interval = {result.AverageInterval:F2}ms");
        DebugLogger.Log($"Average Duration = {result.AverageDuration:F2}ms");
        DebugLogger.Log($"Average Interval Consistency = {result.AverageIntervalConsistency:P2}");
        DebugLogger.Log($"Confidence = {result.Confidence:F3}");
        if (!detailed) return;
        foreach (var sequence in result.Runs)
        {
            DebugLogger.Log("DEATHSTREAM SEQUENCE");
            DebugLogger.Log($"StartIndex = {sequence.StartIndex}");
            DebugLogger.Log($"EndIndex = {sequence.EndIndex}");
            DebugLogger.Log($"StartTime = {sequence.StartTime}");
            DebugLogger.Log($"EndTime = {sequence.EndTime}");
            DebugLogger.Log($"ObjectCount = {sequence.ObjectCount}");
            DebugLogger.Log($"Intervals = [{string.Join(", ", sequence.Intervals.Select(interval => interval.ToString("F2")))}]");
            DebugLogger.Log($"AverageInterval = {sequence.AverageInterval:F2}");
            DebugLogger.Log($"MinInterval = {sequence.MinimumInterval:F2}");
            DebugLogger.Log($"MaxInterval = {sequence.MaximumInterval:F2}");
            DebugLogger.Log($"IntervalConsistency = {sequence.IntervalConsistency:F3}");
            DebugLogger.Log($"Duration = {sequence.Duration:F2}");
            DebugLogger.Log($"Qualified = {sequence.Qualified}");
        }
    }

    public static void StaminaStreamPattern(
        BeatInsight.Models.StreamPatterns.StaminaStreamPatternAnalysis result,
        bool enabled = false,
        bool detailed = false)
    {
        if (!enabled) return;
        DebugLogger.Log("===== STAMINA STREAM PATTERN DEBUG =====");
        DebugLogger.Log($"Objects = {result.TotalObjects}");
        DebugLogger.Log($"Eligible Rapid Runs = {result.EligibleRapidRunCount}");
        DebugLogger.Log($"Stamina Sequences = {result.StaminaSequenceCount}");
        DebugLogger.Log($"Stamina Objects = {result.StaminaObjectCount}");
        DebugLogger.Log($"Coverage = {result.Coverage:P2}");
        DebugLogger.Log($"Longest Stamina Objects = {result.LongestStaminaObjectCount}");
        DebugLogger.Log($"Longest Stamina Duration = {result.LongestStaminaDuration:F2}ms");
        DebugLogger.Log($"Average Runs / Sequence = {result.AverageRunsPerSequence:F2}");
        DebugLogger.Log($"Average Objects / Sequence = {result.AverageObjectsPerSequence:F2}");
        DebugLogger.Log($"Average Duration = {result.AverageSequenceDuration:F2}ms");
        DebugLogger.Log($"Average Recovery Gap = {result.AverageRecoveryGap:F2}ms");
        DebugLogger.Log($"Average Active Ratio = {result.AverageActiveRatio:P2}");
        DebugLogger.Log($"Average Interval = {result.AverageInterval:F2}ms");
        DebugLogger.Log($"Average Interval Consistency = {result.AverageIntervalConsistency:P2}");
        DebugLogger.Log($"Confidence = {result.Confidence:F3}");
        if (!detailed) return;
        foreach (var sequence in result.Candidates)
        {
            DebugLogger.Log("STAMINA STREAM SEQUENCE");
            DebugLogger.Log($"StartIndex = {sequence.StartIndex}");
            DebugLogger.Log($"EndIndex = {sequence.EndIndex}");
            DebugLogger.Log($"StartTime = {sequence.StartTime}");
            DebugLogger.Log($"EndTime = {sequence.EndTime}");
            DebugLogger.Log($"RunCount = {sequence.RunCount}");
            DebugLogger.Log($"TotalRapidObjects = {sequence.TotalRapidObjects}");
            DebugLogger.Log($"Duration = {sequence.Duration:F2}");
            DebugLogger.Log($"ActiveRapidDuration = {sequence.ActiveRapidDuration:F2}");
            DebugLogger.Log($"TotalRecoveryDuration = {sequence.TotalRecoveryDuration:F2}");
            DebugLogger.Log($"ActiveRatio = {sequence.ActiveRatio:P2}");
            DebugLogger.Log($"RecoveryGaps = [{string.Join(", ", sequence.RecoveryGaps.Select(gap => gap.ToString("F2")))}]");
            DebugLogger.Log($"AverageRecoveryGap = {sequence.AverageRecoveryGap:F2}");
            DebugLogger.Log($"MaximumRecoveryGapObserved = {sequence.MaximumRecoveryGapObserved:F2}");
            DebugLogger.Log($"AverageInterval = {sequence.AverageInterval:F2}");
            DebugLogger.Log($"AverageIntervalConsistency = {sequence.AverageIntervalConsistency:F3}");
            DebugLogger.Log($"Qualified = {sequence.Qualified}");
            DebugLogger.Log($"Reason = {sequence.Reason}");
        }
    }

    public static void IrregularStreamPattern(
        BeatInsight.Models.StreamPatterns.IrregularStreamPatternAnalysis result,
        bool enabled = false,
        bool detailed = false)
    {
        if (!enabled) return;
        DebugLogger.Log("===== IRREGULAR STREAM PATTERN DEBUG =====");
        DebugLogger.Log($"Objects = {result.TotalObjects}");
        DebugLogger.Log($"Rapid Runs = {result.RapidRunCount}");
        DebugLogger.Log($"Irregular Sequences = {result.IrregularStreamSequenceCount}");
        DebugLogger.Log($"Irregular Objects = {result.IrregularStreamObjectCount}");
        DebugLogger.Log($"Coverage = {result.Coverage:P2}");
        DebugLogger.Log($"Longest Irregular Stream = {result.LongestIrregularStream}");
        DebugLogger.Log($"Average Length = {result.AverageIrregularStreamLength:F2}");
        DebugLogger.Log($"Average Interval = {result.AverageInterval:F2}ms");
        DebugLogger.Log($"Average CV = {result.AverageCoefficientOfVariation:F3}");
        DebugLogger.Log($"Average Interval Delta = {result.AverageIntervalDelta:F2}ms");
        DebugLogger.Log($"Average Significant Delta Ratio = {result.AverageSignificantDeltaRatio:P2}");
        DebugLogger.Log($"Average Distinct Irregular Events = {result.AverageDistinctIrregularEvents:F2}");
        DebugLogger.Log($"Average Atypical Interval Ratio = {result.AverageAtypicalIntervalRatio:P2}");
        DebugLogger.Log($"Average Irregularity Score = {result.AverageIrregularityScore:F3}");
        DebugLogger.Log($"Confidence = {result.Confidence:F3}");
        if (!detailed) return;
        foreach (var sequence in result.Runs)
        {
            DebugLogger.Log("IRREGULAR STREAM SEQUENCE");
            DebugLogger.Log($"StartIndex = {sequence.StartIndex}");
            DebugLogger.Log($"EndIndex = {sequence.EndIndex}");
            DebugLogger.Log($"StartTime = {sequence.StartTime}");
            DebugLogger.Log($"EndTime = {sequence.EndTime}");
            DebugLogger.Log($"ObjectCount = {sequence.ObjectCount}");
            DebugLogger.Log($"Intervals = [{string.Join(", ", sequence.Intervals.Select(interval => interval.ToString("F2")))}]");
            DebugLogger.Log($"AverageInterval = {sequence.AverageInterval:F2}");
            DebugLogger.Log($"MinInterval = {sequence.MinimumInterval:F2}");
            DebugLogger.Log($"MaxInterval = {sequence.MaximumInterval:F2}");
            DebugLogger.Log($"IntervalRange = {sequence.IntervalRange:F2}");
            DebugLogger.Log($"StandardDeviation = {sequence.StandardDeviation:F3}");
            DebugLogger.Log($"CoefficientOfVariation = {sequence.CoefficientOfVariation:F3}");
            DebugLogger.Log($"IntervalDeltas = [{string.Join(", ", sequence.IntervalDeltas.Select(delta => delta.ToString("F2")))}]");
            DebugLogger.Log($"AverageIntervalDelta = {sequence.AverageIntervalDelta:F2}");
            DebugLogger.Log($"MaximumIntervalDelta = {sequence.MaximumIntervalDelta:F2}");
            DebugLogger.Log($"SignificantDeltaCount = {sequence.SignificantDeltaCount}");
            DebugLogger.Log($"SignificantDeltaRatio = {sequence.SignificantDeltaRatio:P2}");
            DebugLogger.Log($"MedianInterval = {sequence.MedianInterval:F2}");
            DebugLogger.Log($"AtypicalIntervalDeviations = [{string.Join(", ", sequence.AtypicalIntervalDeviations.Select(deviation => deviation.ToString("F2")))}]");
            DebugLogger.Log($"AtypicalIntervalMask = [{string.Join(", ", sequence.AtypicalIntervalMask)}]");
            DebugLogger.Log($"AtypicalIntervalCount = {sequence.AtypicalIntervalCount}");
            DebugLogger.Log($"AtypicalIntervalRatio = {sequence.AtypicalIntervalRatio:P2}");
            DebugLogger.Log($"DistinctIrregularEventCount = {sequence.DistinctIrregularEventCount}");
            DebugLogger.Log($"IrregularityScore = {sequence.IrregularityScore:F3}");
            DebugLogger.Log($"Qualified = {sequence.Qualified}");
            DebugLogger.Log($"Reason = {sequence.Reason}");
        }
    }

    public static void SpeedStreamPattern(
        BeatInsight.Models.StreamPatterns.SpeedStreamPatternAnalysis result,
        bool enabled = false,
        bool detailed = false)
    {
        if (!enabled) return;
        DebugLogger.Log("===== SPEED STREAM PATTERN DEBUG =====");
        DebugLogger.Log($"Objects = {result.TotalObjects}");
        DebugLogger.Log($"Rapid Runs = {result.RapidRunCount}");
        DebugLogger.Log($"Speed Stream Sequences = {result.SpeedStreamSequenceCount}");
        DebugLogger.Log($"Speed Stream Objects = {result.SpeedStreamObjectCount}");
        DebugLogger.Log($"Coverage = {result.Coverage:P2}");
        DebugLogger.Log($"Longest Speed Stream = {result.LongestSpeedStream}");
        DebugLogger.Log($"Average Speed Stream Length = {result.AverageSpeedStreamLength:F2}");
        DebugLogger.Log($"Average Interval = {result.AverageInterval:F2}ms");
        DebugLogger.Log($"Fastest Average Interval = {result.FastestAverageInterval:F2}ms");
        DebugLogger.Log($"Average Stream BPM = {result.AverageStreamBpm:F2}");
        DebugLogger.Log($"Maximum Stream BPM = {result.MaximumStreamBpm:F2}");
        DebugLogger.Log($"Average Speed Intensity = {result.AverageSpeedIntensity:F3}");
        DebugLogger.Log($"Average Interval Consistency = {result.AverageIntervalConsistency:P2}");
        DebugLogger.Log($"Confidence = {result.Confidence:F3}");
        if (!detailed) return;
        foreach (var sequence in result.Runs)
        {
            DebugLogger.Log("SPEED STREAM SEQUENCE");
            DebugLogger.Log($"StartIndex = {sequence.StartIndex}");
            DebugLogger.Log($"EndIndex = {sequence.EndIndex}");
            DebugLogger.Log($"StartTime = {sequence.StartTime}");
            DebugLogger.Log($"EndTime = {sequence.EndTime}");
            DebugLogger.Log($"ObjectCount = {sequence.ObjectCount}");
            DebugLogger.Log($"Intervals = [{string.Join(", ", sequence.Intervals.Select(interval => interval.ToString("F2")))}]");
            DebugLogger.Log($"AverageInterval = {sequence.AverageInterval:F2}");
            DebugLogger.Log($"MinInterval = {sequence.MinimumInterval:F2}");
            DebugLogger.Log($"MaxInterval = {sequence.MaximumInterval:F2}");
            DebugLogger.Log($"IntervalConsistency = {sequence.IntervalConsistency:F3}");
            DebugLogger.Log($"StreamBpm = {sequence.StreamBpm:F2}");
            DebugLogger.Log($"SpeedIntensity = {sequence.SpeedIntensity:F3}");
            DebugLogger.Log($"Duration = {sequence.Duration:F2}");
            DebugLogger.Log($"Qualified = {sequence.Qualified}");
            DebugLogger.Log($"Reason = {sequence.Reason}");
        }
    }

    public static void FlowStreamPattern(
        BeatInsight.Models.StreamPatterns.FlowStreamPatternAnalysis result,
        bool enabled = false,
        bool detailed = false)
    {
        if (!enabled) return;
        DebugLogger.Log("===== FLOW STREAM PATTERN DEBUG =====");
        DebugLogger.Log($"Objects = {result.TotalObjects}");
        DebugLogger.Log($"Rapid Runs = {result.RapidRunCount}");
        DebugLogger.Log($"Flow Sequences = {result.FlowStreamSequenceCount}");
        DebugLogger.Log($"Flow Objects = {result.FlowStreamObjectCount}");
        DebugLogger.Log($"Coverage = {result.Coverage:P2}");
        DebugLogger.Log($"Longest Flow Stream = {result.LongestFlowStream}");
        DebugLogger.Log($"Average Flow Length = {result.AverageFlowStreamLength:F2}");
        DebugLogger.Log($"Average Flow Pivot Ratio = {result.AverageFlowPivotRatio:P2}");
        DebugLogger.Log($"Average Turn Direction Consistency = {result.AverageTurnDirectionConsistency:P2}");
        DebugLogger.Log($"Average Turn Angle = {result.AverageTurnAngle:F2}°");
        DebugLogger.Log($"Average Spacing Consistency = {result.AverageSpacingConsistency:P2}");
        DebugLogger.Log($"Average Flow Score = {result.AverageFlowScore:F3}");
        DebugLogger.Log($"Confidence = {result.Confidence:F3}");
        if (!detailed) return;
        foreach (var sequence in result.Runs)
        {
            DebugLogger.Log("FLOW STREAM SEQUENCE");
            DebugLogger.Log($"StartIndex = {sequence.StartIndex}");
            DebugLogger.Log($"EndIndex = {sequence.EndIndex}");
            DebugLogger.Log($"StartTime = {sequence.StartTime}");
            DebugLogger.Log($"EndTime = {sequence.EndTime}");
            DebugLogger.Log($"ObjectCount = {sequence.ObjectCount}");
            DebugLogger.Log($"AverageInterval = {sequence.AverageInterval:F2}");
            DebugLogger.Log($"Distances = [{string.Join(", ", sequence.Distances.Select(distance => distance.ToString("F2")))}]");
            DebugLogger.Log($"AverageDistance = {sequence.AverageDistance:F2}");
            DebugLogger.Log($"MinDistance = {sequence.MinimumDistance:F2}");
            DebugLogger.Log($"MaxDistance = {sequence.MaximumDistance:F2}");
            DebugLogger.Log($"SpacingConsistency = {sequence.SpacingConsistency:F3}");
            DebugLogger.Log($"TurnAngles = [{string.Join(", ", sequence.TurnAngles.Select(angle => angle.ToString("F2")))}]");
            DebugLogger.Log($"TurnSigns = [{string.Join(", ", sequence.TurnSigns)}]");
            DebugLogger.Log($"PivotCount = {sequence.PivotCount}");
            DebugLogger.Log($"FlowPivotCount = {sequence.FlowPivotCount}");
            DebugLogger.Log($"FlowPivotRatio = {sequence.FlowPivotRatio:F3}");
            DebugLogger.Log($"DominantTurnDirection = {sequence.DominantTurnDirection}");
            DebugLogger.Log($"DominantTurnCount = {sequence.DominantTurnCount}");
            DebugLogger.Log($"TurnDirectionConsistency = {sequence.TurnDirectionConsistency:F3}");
            DebugLogger.Log($"AverageTurnAngle = {sequence.AverageTurnAngle:F2}");
            DebugLogger.Log($"MinTurnAngle = {sequence.MinimumTurnAngle:F2}");
            DebugLogger.Log($"MaxTurnAngle = {sequence.MaximumTurnAngle:F2}");
            DebugLogger.Log($"CurvatureConsistency = {sequence.CurvatureConsistency:F3}");
            DebugLogger.Log($"FlowScore = {sequence.FlowScore:F3}");
            DebugLogger.Log($"Qualified = {sequence.Qualified}");
            DebugLogger.Log($"Reason = {sequence.Reason}");
        }
    }

    public static void AlternatingStreamPattern(
        BeatInsight.Models.StreamPatterns.AlternatingStreamPatternAnalysis result,
        bool enabled = false,
        bool detailed = false)
    {
        if (!enabled) return;
        DebugLogger.Log("===== ALTERNATING STREAM PATTERN DEBUG =====");
        DebugLogger.Log($"Objects = {result.TotalObjects}");
        DebugLogger.Log($"Rapid Runs = {result.RapidRunCount}");
        DebugLogger.Log($"Alternating Sequences = {result.AlternatingStreamSequenceCount}");
        DebugLogger.Log($"Alternating Objects = {result.AlternatingStreamObjectCount}");
        DebugLogger.Log($"Coverage = {result.Coverage:P2}");
        DebugLogger.Log($"Longest Alternating Stream = {result.LongestAlternatingStream}");
        DebugLogger.Log($"Average Alternating Length = {result.AverageAlternatingStreamLength:F2}");
        DebugLogger.Log($"Average Valid Pivot Ratio = {result.AverageValidPivotRatio:P2}");
        DebugLogger.Log($"Average Alternation Ratio = {result.AverageAlternationRatio:P2}");
        DebugLogger.Log($"Average Turn Angle = {result.AverageTurnAngle:F2}°");
        DebugLogger.Log($"Average Spacing Consistency = {result.AverageSpacingConsistency:P2}");
        DebugLogger.Log($"Average Alternating Score = {result.AverageAlternatingScore:F3}");
        DebugLogger.Log($"Confidence = {result.Confidence:F3}");
        if (!detailed) return;
        foreach (var sequence in result.Runs)
        {
            DebugLogger.Log("ALTERNATING STREAM SEQUENCE");
            DebugLogger.Log($"StartIndex = {sequence.StartIndex}");
            DebugLogger.Log($"EndIndex = {sequence.EndIndex}");
            DebugLogger.Log($"StartTime = {sequence.StartTime}");
            DebugLogger.Log($"EndTime = {sequence.EndTime}");
            DebugLogger.Log($"ObjectCount = {sequence.ObjectCount}");
            DebugLogger.Log($"AverageInterval = {sequence.AverageInterval:F2}");
            DebugLogger.Log($"Distances = [{string.Join(", ", sequence.Distances.Select(distance => distance.ToString("F2")))}]");
            DebugLogger.Log($"AverageDistance = {sequence.AverageDistance:F2}");
            DebugLogger.Log($"MinDistance = {sequence.MinimumDistance:F2}");
            DebugLogger.Log($"MaxDistance = {sequence.MaximumDistance:F2}");
            DebugLogger.Log($"SpacingConsistency = {sequence.SpacingConsistency:F3}");
            DebugLogger.Log($"TurnAngles = [{string.Join(", ", sequence.TurnAngles.Select(angle => angle.ToString("F2")))}]");
            DebugLogger.Log($"TurnSigns = [{string.Join(", ", sequence.TurnSigns)}]");
            DebugLogger.Log($"PivotCount = {sequence.PivotCount}");
            DebugLogger.Log($"ValidAlternatingPivotCount = {sequence.ValidAlternatingPivotCount}");
            DebugLogger.Log($"ValidAlternatingPivotRatio = {sequence.ValidAlternatingPivotRatio:F3}");
            DebugLogger.Log($"AlternationCount = {sequence.AlternationCount}");
            DebugLogger.Log($"PossibleAlternations = {sequence.PossibleAlternations}");
            DebugLogger.Log($"AlternationRatio = {sequence.AlternationRatio:F3}");
            DebugLogger.Log($"AverageValidTurnAngle = {sequence.AverageValidTurnAngle:F2}");
            DebugLogger.Log($"MinValidTurnAngle = {sequence.MinimumValidTurnAngle:F2}");
            DebugLogger.Log($"MaxValidTurnAngle = {sequence.MaximumValidTurnAngle:F2}");
            DebugLogger.Log($"AlternatingAngleConsistency = {sequence.AlternatingAngleConsistency:F3}");
            DebugLogger.Log($"AlternatingScore = {sequence.AlternatingScore:F3}");
            DebugLogger.Log($"Qualified = {sequence.Qualified}");
            DebugLogger.Log($"Reason = {sequence.Reason}");
        }
    }

    public static void SliderChainPattern(
        BeatInsight.Models.TechPatterns.SliderChainPatternAnalysis result,
        bool enabled = false,
        bool detailed = false)
    {
        if (!enabled) return;
        DebugLogger.Log("===== SLIDER CHAIN PATTERN DEBUG =====");
        DebugLogger.Log($"Objects = {result.TotalObjects}");
        DebugLogger.Log($"Sliders = {result.TotalSliders}");
        DebugLogger.Log($"Slider Chain Sequences = {result.SliderChainSequenceCount}");
        DebugLogger.Log($"Slider Chain Objects = {result.SliderChainObjectCount}");
        DebugLogger.Log($"Slider Chain Sliders = {result.SliderChainSliderCount}");
        DebugLogger.Log($"Coverage = {result.Coverage:P2}");
        DebugLogger.Log($"Slider Coverage = {result.SliderCoverage:P2}");
        DebugLogger.Log($"Longest Chain = {result.LongestSliderChain}");
        DebugLogger.Log($"Average Chain Length = {result.AverageChainLength:F2}");
        DebugLogger.Log($"Average Slider Count = {result.AverageSliderCount:F2}");
        DebugLogger.Log($"Average Slider Ratio = {result.AverageSliderRatio:P2}");
        DebugLogger.Log($"Average Duration = {result.AverageDuration:F2}ms");
        DebugLogger.Log($"Confidence = {result.Confidence:F3}");
        if (!detailed) return;
        foreach (var sequence in result.Runs)
        {
            DebugLogger.Log("SLIDER CHAIN SEQUENCE");
            DebugLogger.Log($"StartIndex = {sequence.StartIndex}");
            DebugLogger.Log($"EndIndex = {sequence.EndIndex}");
            DebugLogger.Log($"StartTime = {sequence.StartTime}");
            DebugLogger.Log($"EndTime = {sequence.EndTime}");
            DebugLogger.Log($"ObjectCount = {sequence.ObjectCount}");
            DebugLogger.Log($"SliderCount = {sequence.SliderCount}");
            DebugLogger.Log($"CircleCount = {sequence.CircleCount}");
            DebugLogger.Log($"SliderRatio = {sequence.SliderRatio:F3}");
            DebugLogger.Log($"ObjectGaps = [{string.Join(", ", sequence.ObjectGaps.Select(gap => gap.ToString("F2")))}]");
            DebugLogger.Log($"AverageObjectGap = {sequence.AverageObjectGap:F2}");
            DebugLogger.Log($"MinObjectGap = {sequence.MinimumObjectGap:F2}");
            DebugLogger.Log($"MaxObjectGap = {sequence.MaximumObjectGapObserved:F2}");
            DebugLogger.Log($"Duration = {sequence.Duration:F2}");
            DebugLogger.Log($"Qualified = {sequence.Qualified}");
            DebugLogger.Log($"Reason = {sequence.Reason}");
        }
    }

    public static void ComplexSliderPattern(
        BeatInsight.Models.TechPatterns.ComplexSliderPatternAnalysis result,
        bool enabled = false,
        bool detailed = false)
    {
        if (!enabled) return;
        DebugLogger.Log("===== COMPLEX SLIDER PATTERN DEBUG =====");
        DebugLogger.Log($"Objects = {result.TotalObjects}");
        DebugLogger.Log($"Sliders = {result.TotalSliders}");
        DebugLogger.Log($"Complex Sliders = {result.ComplexSliderCount}");
        DebugLogger.Log($"Coverage = {result.Coverage:P2}");
        DebugLogger.Log($"Slider Coverage = {result.SliderCoverage:P2}");
        DebugLogger.Log($"Average Complexity Score = {result.AverageComplexityScore:F3}");
        DebugLogger.Log($"Maximum Complexity Score = {result.MaximumComplexityScore:F3}");
        DebugLogger.Log($"Average Control Points = {result.AverageControlPointCount:F2}");
        DebugLogger.Log($"Average Repeats = {result.AverageRepeatCount:F2}");
        DebugLogger.Log($"Average Path/Chord Ratio = {result.AveragePathToChordRatio:F3}");
        DebugLogger.Log($"Confidence = {result.Confidence:F3}");
        if (!detailed) return;
        foreach (var slider in result.Sliders)
        {
            DebugLogger.Log("COMPLEX SLIDER");
            DebugLogger.Log($"ObjectIndex = {slider.ObjectIndex}");
            DebugLogger.Log($"StartTime = {slider.StartTime}");
            DebugLogger.Log($"EndTime = {FormatOptional(slider.EndTime, "F2")}ms");
            DebugLogger.Log($"Duration = {FormatOptional(slider.Duration, "F2")}ms");
            DebugLogger.Log($"StartPosition = ({slider.StartPosition.X:F2},{slider.StartPosition.Y:F2})");
            DebugLogger.Log($"EndPosition = ({slider.EndPosition.X:F2},{slider.EndPosition.Y:F2})");
            DebugLogger.Log($"CurveType = {slider.CurveType}");
            DebugLogger.Log($"ControlPointCount = {slider.ControlPointCount}");
            DebugLogger.Log($"EffectiveControlPointCount = {slider.EffectiveControlPointCount}");
            DebugLogger.Log($"RepeatCount = {slider.RepeatCount}");
            DebugLogger.Log($"PathLength = {FormatOptional(slider.PathLength, "F2")}");
            DebugLogger.Log($"ChordLength = {slider.ChordLength:F2}");
            DebugLogger.Log($"PathToChordRatio = {FormatOptional(slider.PathToChordRatio, "F3")}");
            DebugLogger.Log($"InternalTurnCount = {slider.InternalTurnCount}");
            DebugLogger.Log($"AverageInternalTurnAngle = {slider.AverageInternalTurnAngle:F2}");
            DebugLogger.Log($"MaximumInternalTurnAngle = {slider.MaximumInternalTurnAngle:F2}");
            DebugLogger.Log($"ComplexityScore = {slider.SliderComplexityScore:F3}");
            DebugLogger.Log($"Qualified = {slider.Qualified}");
            DebugLogger.Log($"Reason = {slider.Reason}");
        }
    }

    public static void SliderDirectionChangePattern(
        BeatInsight.Models.TechPatterns.SliderDirectionChangePatternAnalysis result,
        bool enabled = false,
        bool detailed = false)
    {
        if (!enabled) return;
        DebugLogger.Log("===== SLIDER DIRECTION CHANGE DEBUG =====");
        DebugLogger.Log($"Objects = {result.TotalObjects}");
        DebugLogger.Log($"Sliders = {result.TotalSliders}");
        DebugLogger.Log($"Qualified Sliders = {result.DirectionChangeSliderCount}");
        DebugLogger.Log($"Coverage = {result.Coverage:P2}");
        DebugLogger.Log($"Slider Coverage = {result.SliderCoverage:P2}");
        DebugLogger.Log($"Entry Changes = {result.EntryChangeCount}");
        DebugLogger.Log($"Exit Changes = {result.ExitChangeCount}");
        DebugLogger.Log($"Both = {result.BothChangeCount}");
        DebugLogger.Log($"Average Angle = {result.AverageDirectionChangeAngle:F2}");
        DebugLogger.Log($"Maximum Angle = {result.MaximumDirectionChangeAngle:F2}");
        DebugLogger.Log($"Average Score = {result.AverageDirectionChangeScore:F3}");
        DebugLogger.Log($"Confidence = {result.Confidence:F3}");
        if (!detailed) return;
        foreach (var occurrence in result.Occurrences)
        {
            DebugLogger.Log("SLIDER DIRECTION CHANGE");
            DebugLogger.Log($"SliderIndex = {occurrence.SliderIndex}");
            DebugLogger.Log($"PreviousObjectIndex = {FormatOptional(occurrence.PreviousObjectIndex)}");
            DebugLogger.Log($"NextObjectIndex = {FormatOptional(occurrence.NextObjectIndex)}");
            DebugLogger.Log($"StartTime = {occurrence.StartTime}");
            DebugLogger.Log($"EndTime = {FormatOptional(occurrence.EndTime, "F2")}ms");
            DebugLogger.Log($"PreviousPosition = {FormatPosition(occurrence.PreviousPosition)}");
            DebugLogger.Log($"SliderStartPosition = {FormatPosition(occurrence.SliderStartPosition)}");
            DebugLogger.Log($"SliderBaseEndPosition = {FormatPosition(occurrence.SliderBaseEndPosition)}");
            DebugLogger.Log($"SliderFinalPosition = {FormatPosition(occurrence.SliderFinalPosition)}");
            DebugLogger.Log($"NextPosition = {FormatPosition(occurrence.NextPosition)}");
            DebugLogger.Log($"IncomingToSliderAngle = {FormatOptional(occurrence.IncomingToSliderAngle, "F2")}");
            DebugLogger.Log($"SliderToOutgoingAngle = {FormatOptional(occurrence.SliderToOutgoingAngle, "F2")}");
            DebugLogger.Log($"IncomingToOutgoingAngle = {FormatOptional(occurrence.IncomingToOutgoingAngle, "F2")}");
            DebugLogger.Log($"EntryDirectionChange = {occurrence.EntryDirectionChange}");
            DebugLogger.Log($"ExitDirectionChange = {occurrence.ExitDirectionChange}");
            DebugLogger.Log($"DirectionChangeCount = {occurrence.DirectionChangeCount}");
            DebugLogger.Log($"MaximumDirectionChangeAngle = {occurrence.MaximumDirectionChangeAngle:F2}");
            DebugLogger.Log($"AverageDirectionChangeAngle = {occurrence.AverageDirectionChangeAngle:F2}");
            DebugLogger.Log($"DirectionChangeScore = {occurrence.DirectionChangeScore:F3}");
            DebugLogger.Log($"Qualified = {occurrence.Qualified}");
            DebugLogger.Log($"Reason = {occurrence.Reason}");
        }
    }

    public static void CircleSliderTransitionPattern(
        BeatInsight.Models.TechPatterns.CircleSliderTransitionPatternAnalysis result,
        bool enabled = false,
        bool detailed = false)
    {
        if (!enabled) return;
        DebugLogger.Log("===== CIRCLE / SLIDER TRANSITION DEBUG =====");
        DebugLogger.Log($"Objects = {result.TotalObjects}");
        DebugLogger.Log($"Transition Sequences = {result.TransitionSequenceCount}");
        DebugLogger.Log($"Transition Objects = {result.TransitionObjectCount}");
        DebugLogger.Log($"Coverage = {result.Coverage:P2}");
        DebugLogger.Log($"Circle -> Slider = {result.CircleToSliderCount}");
        DebugLogger.Log($"Slider -> Circle = {result.SliderToCircleCount}");
        DebugLogger.Log($"Longest Sequence = {result.LongestTransitionSequence}");
        DebugLogger.Log($"Average Sequence Length = {result.AverageSequenceLength:F2}");
        DebugLogger.Log($"Average Transition Ratio = {result.AverageTransitionRatio:P2}");
        DebugLogger.Log($"Average Transition Count = {result.AverageTransitionCount:F2}");
        DebugLogger.Log($"Average Transition Score = {result.AverageTransitionScore:F3}");
        DebugLogger.Log($"Confidence = {result.Confidence:F3}");
        if (!detailed) return;
        foreach (var sequence in result.Runs)
        {
            DebugLogger.Log("CIRCLE / SLIDER TRANSITION");
            DebugLogger.Log($"StartIndex = {sequence.StartIndex}");
            DebugLogger.Log($"EndIndex = {sequence.EndIndex}");
            DebugLogger.Log($"StartTime = {sequence.StartTime}");
            DebugLogger.Log($"EndTime = {sequence.EndTime}");
            DebugLogger.Log($"ObjectTypes = [{string.Join(",", sequence.ObjectTypes.Select(ObjectTypeLabel))}]");
            DebugLogger.Log($"ObjectCount = {sequence.ObjectCount}");
            DebugLogger.Log($"CircleCount = {sequence.CircleCount}");
            DebugLogger.Log($"SliderCount = {sequence.SliderCount}");
            DebugLogger.Log($"TransitionCount = {sequence.TransitionCount}");
            DebugLogger.Log($"PossibleTransitions = {sequence.PossibleTransitions}");
            DebugLogger.Log($"TransitionRatio = {sequence.TransitionRatio:F3}");
            DebugLogger.Log($"CircleToSliderCount = {sequence.CircleToSliderCount}");
            DebugLogger.Log($"SliderToCircleCount = {sequence.SliderToCircleCount}");
            DebugLogger.Log($"TransitionDirectionBalance = {FormatOptional(sequence.TransitionDirectionBalance, "F3")}");
            DebugLogger.Log($"LongestSameTypeRun = {sequence.LongestSameTypeRun}");
            DebugLogger.Log($"ObjectGaps = [{string.Join(", ", sequence.ObjectGaps.Select(gap => gap.ToString("F2")))}]");
            DebugLogger.Log($"AverageObjectGap = {sequence.AverageObjectGap:F2}");
            DebugLogger.Log($"MinimumObjectGap = {sequence.MinimumObjectGap:F2}");
            DebugLogger.Log($"MaximumObjectGap = {sequence.MaximumObjectGapObserved:F2}");
            DebugLogger.Log($"Duration = {sequence.Duration:F2}");
            DebugLogger.Log($"TransitionScore = {sequence.TransitionScore:F3}");
            DebugLogger.Log($"Qualified = {sequence.Qualified}");
            DebugLogger.Log($"Reason = {sequence.Reason}");
        }
    }

    public static void RhythmTransitionPattern(
        BeatInsight.Models.TechPatterns.RhythmTransitionPatternAnalysis result,
        bool enabled = false,
        bool detailed = false)
    {
        if (!enabled) return;
        DebugLogger.Log("===== RHYTHM TRANSITION DEBUG =====");
        DebugLogger.Log($"Objects = {result.TotalObjects}");
        DebugLogger.Log($"Rhythm Transitions = {result.RhythmTransitionCount}");
        DebugLogger.Log($"Faster = {result.FasterTransitionCount}");
        DebugLogger.Log($"Slower = {result.SlowerTransitionCount}");
        DebugLogger.Log($"Coverage = {result.Coverage:P2}");
        DebugLogger.Log($"Average Absolute Change = {result.AverageAbsoluteIntervalChange:F2}ms");
        DebugLogger.Log($"Average Relative Change = {result.AverageRelativeIntervalChange:P2}");
        DebugLogger.Log($"Average Before Consistency = {result.AverageBeforeConsistency:P2}");
        DebugLogger.Log($"Average After Consistency = {result.AverageAfterConsistency:P2}");
        DebugLogger.Log($"Average Score = {result.AverageTransitionScore:F3}");
        DebugLogger.Log($"Maximum Score = {result.MaximumTransitionScore:F3}");
        DebugLogger.Log($"Confidence = {result.Confidence:F3}");
        if (!detailed) return;
        foreach (var occurrence in result.Occurrences)
        {
            DebugLogger.Log("RHYTHM TRANSITION");
            DebugLogger.Log($"PivotIndex = {occurrence.PivotIndex}");
            DebugLogger.Log($"StartIndex = {occurrence.StartIndex}");
            DebugLogger.Log($"EndIndex = {occurrence.EndIndex}");
            DebugLogger.Log($"StartTime = {occurrence.StartTime}");
            DebugLogger.Log($"EndTime = {occurrence.EndTime}");
            DebugLogger.Log($"BeforeIntervals = [{string.Join(", ", occurrence.BeforeIntervals.Select(interval => interval.ToString("F2")))}]");
            DebugLogger.Log($"AfterIntervals = [{string.Join(", ", occurrence.AfterIntervals.Select(interval => interval.ToString("F2")))}]");
            DebugLogger.Log($"BeforeAverage = {FormatOptional(occurrence.BeforeAverageInterval, "F2")}");
            DebugLogger.Log($"AfterAverage = {FormatOptional(occurrence.AfterAverageInterval, "F2")}");
            DebugLogger.Log($"BeforeConsistency = {FormatOptional(occurrence.BeforeConsistency, "F3")}");
            DebugLogger.Log($"AfterConsistency = {FormatOptional(occurrence.AfterConsistency, "F3")}");
            DebugLogger.Log($"AbsoluteChange = {FormatOptional(occurrence.AbsoluteIntervalChange, "F2")}");
            DebugLogger.Log($"RelativeChange = {FormatOptional(occurrence.RelativeIntervalChange, "F3")}");
            DebugLogger.Log($"Direction = {occurrence.Direction?.ToString() ?? "n/a"}");
            DebugLogger.Log($"TransitionScore = {occurrence.RhythmTransitionScore:F3}");
            DebugLogger.Log($"Qualified = {occurrence.Qualified}");
            DebugLogger.Log($"Reason = {occurrence.Reason}");
        }
    }

    public static void SpatialAwkwardTransitionPattern(
        BeatInsight.Models.TechPatterns.SpatialAwkwardTransitionPatternAnalysis result,
        bool enabled = false,
        bool detailed = false)
    {
        if (!enabled) return;
        DebugLogger.Log("===== SPATIAL / AWKWARD TRANSITION DEBUG =====");
        DebugLogger.Log($"Objects = {result.TotalObjects}");
        DebugLogger.Log($"Qualified = {result.SpatialAwkwardTransitionCount}");
        DebugLogger.Log($"Coverage = {result.Coverage:P2}");
        DebugLogger.Log($"Average Incoming Distance = {result.AverageIncomingDistance:F2}px");
        DebugLogger.Log($"Average Outgoing Distance = {result.AverageOutgoingDistance:F2}px");
        DebugLogger.Log($"Average Turn Angle = {result.AverageTurnAngle:F2} deg");
        DebugLogger.Log($"Average Asymmetry = {result.AverageAsymmetry:P2}");
        DebugLogger.Log($"Average Score = {result.AverageSpatialAwkwardScore:F3}");
        DebugLogger.Log($"Maximum Score = {result.MaximumSpatialAwkwardScore:F3}");
        DebugLogger.Log($"Confidence = {result.Confidence:F3}");
        if (!detailed) return;
        foreach (var occurrence in result.Occurrences)
        {
            DebugLogger.Log("SPATIAL / AWKWARD TRANSITION");
            DebugLogger.Log($"PivotIndex = {occurrence.PivotIndex}");
            DebugLogger.Log($"StartIndex = {occurrence.StartIndex}");
            DebugLogger.Log($"EndIndex = {occurrence.EndIndex}");
            DebugLogger.Log($"StartTime = {occurrence.StartTime}");
            DebugLogger.Log($"EndTime = {occurrence.EndTime}");
            DebugLogger.Log($"IncomingDistance = {occurrence.IncomingDistance:F2}px");
            DebugLogger.Log($"OutgoingDistance = {occurrence.OutgoingDistance:F2}px");
            DebugLogger.Log($"AverageDistance = {occurrence.AverageDistance:F2}px");
            DebugLogger.Log($"TurnAngle = {occurrence.TurnAngle:F2} deg");
            DebugLogger.Log($"DistanceRatio = {occurrence.DistanceRatio:F3}");
            DebugLogger.Log($"Asymmetry = {occurrence.Asymmetry:F3}");
            DebugLogger.Log($"DistanceSignal = {FormatOptional(occurrence.DistanceSignal, "F3")}");
            DebugLogger.Log($"TurnSignal = {FormatOptional(occurrence.TurnSignal, "F3")}");
            DebugLogger.Log($"AsymmetrySignal = {FormatOptional(occurrence.AsymmetrySignal, "F3")}");
            DebugLogger.Log($"AwkwardScore = {occurrence.SpatialAwkwardScore:F3}");
            DebugLogger.Log($"Qualified = {occurrence.Qualified}");
            DebugLogger.Log($"Reason = {occurrence.Reason}");
        }
    }

    public static void OverlapPattern(
        BeatInsight.Models.TechPatterns.OverlapPatternAnalysis result,
        bool enabled = false,
        bool detailed = false)
    {
        if (!enabled) return;
        DebugLogger.Log("===== OVERLAP PATTERN DEBUG =====");
        DebugLogger.Log($"Objects = {result.TotalObjects}");
        DebugLogger.Log($"Overlap Links = {result.OverlapLinkCount}");
        DebugLogger.Log($"Qualified Sequences = {result.QualifiedOverlapSequenceCount}");
        DebugLogger.Log($"Qualified Objects = {result.QualifiedOverlapObjectCount}");
        DebugLogger.Log($"Coverage = {result.Coverage:P2}");
        DebugLogger.Log($"Average Distance = {result.AverageDistance:F2}px");
        DebugLogger.Log($"Minimum Distance = {result.MinimumDistance:F2}px");
        DebugLogger.Log($"Average Strength = {result.AverageOverlapStrength:F3}");
        DebugLogger.Log($"Maximum Strength = {result.MaximumOverlapStrength:F3}");
        DebugLogger.Log($"Average Sequence Length = {result.AverageSequenceLength:F2}");
        DebugLogger.Log($"Confidence = {result.Confidence:F3}");
        if (!detailed) return;
        foreach (var sequence in result.Runs)
        {
            DebugLogger.Log("OVERLAP SEQUENCE");
            DebugLogger.Log($"StartIndex = {sequence.StartIndex}");
            DebugLogger.Log($"EndIndex = {sequence.EndIndex}");
            DebugLogger.Log($"ObjectCount = {sequence.ObjectCount}");
            DebugLogger.Log($"LinkCount = {sequence.LinkCount}");
            DebugLogger.Log($"StartTime = {sequence.StartTime}");
            DebugLogger.Log($"EndTime = {sequence.EndTime}");
            DebugLogger.Log($"MinimumDistance = {sequence.MinimumDistance:F2}px");
            DebugLogger.Log($"MaximumDistance = {sequence.MaximumDistance:F2}px");
            DebugLogger.Log($"AverageDistance = {sequence.AverageDistance:F2}px");
            DebugLogger.Log($"AverageOverlapStrength = {sequence.AverageOverlapStrength:F3}");
            DebugLogger.Log($"MaximumOverlapStrength = {sequence.MaximumOverlapStrength:F3}");
            DebugLogger.Log($"CircleCircle = {sequence.CircleCircleCount}");
            DebugLogger.Log($"CircleSlider = {sequence.CircleSliderCount}");
            DebugLogger.Log($"SliderCircle = {sequence.SliderCircleCount}");
            DebugLogger.Log($"SliderSlider = {sequence.SliderSliderCount}");
            DebugLogger.Log($"SequenceScore = {sequence.OverlapSequenceScore:F3}");
            DebugLogger.Log($"Qualified = {sequence.Qualified}");
            DebugLogger.Log($"Reason = {sequence.Reason}");
        }
    }

    private static string FormatOptional(double? value, string format) =>
        value.HasValue ? value.Value.ToString(format) : "n/a";

    private static string FormatOptional(int? value) =>
        value.HasValue ? value.Value.ToString() : "n/a";

    private static string FormatPosition(BeatInsight.Models.TechPatterns.SliderPatternPosition? position) =>
        position is null ? "n/a" : $"({position.X:F2},{position.Y:F2})";

    private static string ObjectTypeLabel(int type) => (type & 2) == 2 ? "S" : "C";

    public static void EdgeJump(BeatInsight.Models.JumpPatterns.EdgeJumpAnalysis result,
        bool enabled = false, bool detailed = false)
    {
        if (!enabled) return;
        DebugLogger.Log("===== EDGE JUMP DEBUG =====");
        DebugLogger.Log($"Objects = {result.TotalObjects}");
        DebugLogger.Log($"Edge Objects = {result.EdgeObjectCount}");
        DebugLogger.Log($"Edge Transitions = {result.EdgeTransitionCount}");
        DebugLogger.Log($"Strong Transitions = {result.StrongTransitionCount}");
        DebugLogger.Log($"Coverage = {result.Coverage:P2}");
        DebugLogger.Log($"Longest Sequence = {result.LongestSequence}");
        DebugLogger.Log($"Confidence = {result.Confidence:F3}");
        if (!detailed) return;
        foreach (var transition in result.Transitions)
            DebugLogger.Log($"EDGE TRANSITION | i = {transition.StartObjectIndex}->{transition.EndObjectIndex} " +
                $"| Time = {transition.StartTime}->{transition.EndTime} | From = {transition.From} " +
                $"| To = {transition.To} | Distance = {transition.Distance:F2} | Strong = {transition.Strong}");
    }

    public static void SharpJump(BeatInsight.Models.JumpPatterns.SharpJumpAnalysis result,
        bool enabled = false, bool detailed = false)
    {
        if (!enabled) return;
        DebugLogger.Log("===== SHARP JUMP DEBUG =====");
        DebugLogger.Log($"Objects = {result.TotalObjects}");
        DebugLogger.Log($"Sharp Points = {result.SharpPointCount}");
        DebugLogger.Log($"Sharp Objects = {result.SharpObjectCount}");
        DebugLogger.Log($"Coverage = {result.Coverage:P2}");
        DebugLogger.Log($"Longest Sequence = {result.LongestSequence}");
        DebugLogger.Log($"Average Sharp Angle = {result.AverageSharpAngle:F2}°");
        DebugLogger.Log($"Strongest Sharp Angle = {result.StrongestSharpAngle:F2}°");
        DebugLogger.Log($"Confidence = {result.Confidence:F3}");
        if (!detailed) return;
        foreach (var point in result.Points)
            DebugLogger.Log($"SHARP JUMP POINT | i = {point.ObjectIndex} | Time = {point.Time} " +
                $"| Previous = ({point.PreviousX},{point.PreviousY}) | Current = ({point.CurrentX},{point.CurrentY}) " +
                $"| Next = ({point.NextX},{point.NextY}) | Angle = {point.Angle:F2}° " +
                $"| PrevDistance = {point.PreviousDistance:F2} | NextDistance = {point.NextDistance:F2} | Qualified = True");
    }

    public static void TriangleJump(BeatInsight.Models.JumpPatterns.TriangleJumpAnalysis result,
        bool enabled = false, bool detailed = false)
    {
        if (!enabled) return;
        DebugLogger.Log("===== TRIANGLE JUMP DEBUG =====");
        DebugLogger.Log($"Objects = {result.TotalObjects}");
        DebugLogger.Log($"Triangle Points = {result.TrianglePointCount}");
        DebugLogger.Log($"Triangle Objects = {result.TriangleObjectCount}");
        DebugLogger.Log($"Coverage = {result.Coverage:P2}");
        DebugLogger.Log($"Longest Sequence = {result.LongestSequence}");
        DebugLogger.Log($"Average Angle = {result.AverageTriangleAngle:F2}°");
        DebugLogger.Log($"Average Height = {result.AverageHeight:F2}px");
        DebugLogger.Log($"Confidence = {result.Confidence:F3}");
        if (!detailed) return;
        foreach (var point in result.Points)
            DebugLogger.Log($"TRIANGLE JUMP POINT | i = {point.ObjectIndex} | Time = {point.Time} " +
                $"| A = ({point.PreviousX},{point.PreviousY}) | B = ({point.CurrentX},{point.CurrentY}) " +
                $"| C = ({point.NextX},{point.NextY}) | AB = {point.PreviousDistance:F2} " +
                $"| BC = {point.NextDistance:F2} | AC = {point.OppositeDistance:F2} " +
                $"| Angle = {point.Angle:F2}° | Height = {point.Height:F2} | Qualified = True");
    }

    public static void OneTwoJump(BeatInsight.Models.JumpPatterns.OneTwoJumpAnalysis result,
        bool enabled = false, bool detailed = false)
    {
        if (!enabled) return;
        DebugLogger.Log("===== 1-2 JUMP DEBUG =====");
        DebugLogger.Log($"Objects = {result.TotalObjects}");
        DebugLogger.Log($"1-2 Points = {result.OneTwoPointCount}");
        DebugLogger.Log($"1-2 Objects = {result.OneTwoObjectCount}");
        DebugLogger.Log($"Sequences = {result.SequenceCount}");
        DebugLogger.Log($"Coverage = {result.Coverage:P2}");
        DebugLogger.Log($"Longest Sequence = {result.LongestSequence}");
        DebugLogger.Log($"Average Distance = {result.AverageJumpDistance:F2}");
        DebugLogger.Log($"Distance Consistency = {result.DistanceConsistency:P2}");
        DebugLogger.Log($"Average Interval = {result.AverageInterval:F2}ms");
        DebugLogger.Log($"Interval Consistency = {result.IntervalConsistency:P2}");
        DebugLogger.Log($"Alternation Consistency = {result.AlternationConsistency:P2}");
        DebugLogger.Log($"Confidence = {result.Confidence:F3}");
        if (!detailed) return;
        foreach (var point in result.Points)
            DebugLogger.Log($"1-2 JUMP POINT | i = {point.ObjectIndex} | Time = {point.Time} " +
                $"| Previous = ({point.PreviousX},{point.PreviousY}) | Current = ({point.CurrentX},{point.CurrentY}) " +
                $"| Next = ({point.NextX},{point.NextY}) | PrevDistance = {point.PreviousDistance:F2} " +
                $"| NextDistance = {point.NextDistance:F2} | Angle = {point.Angle:F2}° " +
                $"| TurnDirection = {point.TurnDirection} | Interval = {point.PreviousInterval}/{point.NextInterval} " +
                $"| Alternating = {point.AlternatingWithPrevious} | Qualified = True");
    }

    public static void SpacedJump(BeatInsight.Models.JumpPatterns.SpacedJumpAnalysis result,
        bool enabled = false, bool detailed = false)
    {
        if (!enabled) return;
        DebugLogger.Log("===== SPACED JUMP DEBUG =====");
        DebugLogger.Log($"Objects = {result.TotalObjects}");
        DebugLogger.Log($"Spaced Transitions = {result.SpacedTransitionCount}");
        DebugLogger.Log($"Spaced Objects = {result.SpacedObjectCount}");
        DebugLogger.Log($"Sequences = {result.SequenceCount}");
        DebugLogger.Log($"Coverage = {result.Coverage:P2}");
        DebugLogger.Log($"Longest Sequence = {result.LongestSequence}");
        DebugLogger.Log($"Average Distance = {result.AverageDistance:F2}");
        DebugLogger.Log($"Minimum Distance = {result.MinimumDistance:F2}");
        DebugLogger.Log($"Maximum Distance = {result.MaximumDistance:F2}");
        DebugLogger.Log($"Distance Consistency = {result.DistanceConsistency:P2}");
        DebugLogger.Log($"Confidence = {result.Confidence:F3}");
        if (!detailed) return;
        foreach (var transition in result.Transitions)
            DebugLogger.Log($"SPACED JUMP LINK | i = {transition.StartObjectIndex}->{transition.EndObjectIndex} " +
                $"| Time = {transition.StartTime}->{transition.EndTime} " +
                $"| From = ({transition.FromX},{transition.FromY}) | To = ({transition.ToX},{transition.ToY}) " +
                $"| Distance = {transition.Distance:F2} | QualifiedJump = True | Spaced = True " +
                $"| SequenceId = {transition.SequenceId}");
    }

    public static void WideAngleJump(BeatInsight.Models.JumpPatterns.WideAngleJumpAnalysis result,
        bool enabled = false, bool detailed = false)
    {
        if (!enabled) return;
        DebugLogger.Log("===== WIDE ANGLE JUMP DEBUG =====");
        DebugLogger.Log($"Objects = {result.TotalObjects}");
        DebugLogger.Log($"Wide Points = {result.WidePointCount}");
        DebugLogger.Log($"Wide Objects = {result.WideObjectCount}");
        DebugLogger.Log($"Sequences = {result.SequenceCount}");
        DebugLogger.Log($"Coverage = {result.Coverage:P2}");
        DebugLogger.Log($"Longest Sequence = {result.LongestSequence}");
        DebugLogger.Log($"Average Angle = {result.AverageWideAngle:F2}°");
        DebugLogger.Log($"Minimum Angle = {result.MinimumWideAngleObserved:F2}°");
        DebugLogger.Log($"Maximum Angle = {result.MaximumWideAngleObserved:F2}°");
        DebugLogger.Log($"Average Distance = {result.AverageJumpDistance:F2}");
        DebugLogger.Log($"Confidence = {result.Confidence:F3}");
        if (!detailed) return;
        foreach (var point in result.Points)
            DebugLogger.Log($"WIDE ANGLE POINT | i = {point.ObjectIndex} | Time = {point.Time} " +
                $"| Previous = ({point.PreviousX},{point.PreviousY}) | Current = ({point.CurrentX},{point.CurrentY}) " +
                $"| Next = ({point.NextX},{point.NextY}) | PrevDistance = {point.PreviousDistance:F2} " +
                $"| NextDistance = {point.NextDistance:F2} | Angle = {point.Angle:F2}° " +
                "| QualifiedJumpLinks = True | Wide = True");
    }

    public static void ReversalJump(BeatInsight.Models.JumpPatterns.ReversalJumpAnalysis result,
        bool enabled = false, bool detailed = false)
    {
        if (!enabled) return;
        DebugLogger.Log("===== REVERSAL JUMP DEBUG =====");
        DebugLogger.Log($"Objects = {result.TotalObjects}");
        DebugLogger.Log($"Reversal Points = {result.ReversalPointCount}");
        DebugLogger.Log($"Reversal Objects = {result.ReversalObjectCount}");
        DebugLogger.Log($"Sequences = {result.SequenceCount}");
        DebugLogger.Log($"Coverage = {result.Coverage:P2}");
        DebugLogger.Log($"Longest Sequence = {result.LongestSequence}");
        DebugLogger.Log($"Average Angle = {result.AverageReversalAngle:F2}°");
        DebugLogger.Log($"Minimum Angle = {result.MinimumReversalAngleObserved:F2}°");
        DebugLogger.Log($"Maximum Angle = {result.MaximumReversalAngleObserved:F2}°");
        DebugLogger.Log($"Average Distance = {result.AverageJumpDistance:F2}");
        DebugLogger.Log($"Distance Consistency = {result.DistanceConsistency:P2}");
        DebugLogger.Log($"Average Return Distance = {result.AverageReturnDistance:F2}");
        DebugLogger.Log($"Confidence = {result.Confidence:F3}");
        if (!detailed) return;
        foreach (var point in result.Points)
            DebugLogger.Log($"REVERSAL JUMP POINT | i = {point.ObjectIndex} | Time = {point.Time} " +
                $"| Previous = ({point.PreviousX},{point.PreviousY}) | Current = ({point.CurrentX},{point.CurrentY}) " +
                $"| Next = ({point.NextX},{point.NextY}) | PrevDistance = {point.PreviousDistance:F2} " +
                $"| NextDistance = {point.NextDistance:F2} | Angle = {point.Angle:F2}° " +
                $"| ReturnDistance = {point.ReturnDistance:F2} | QualifiedJumpLinks = True | Reversal = True");
    }

    public static void ArcJump(BeatInsight.Models.JumpPatterns.ArcJumpAnalysis result,
        bool enabled = false, bool detailed = false)
    {
        if (!enabled) return;
        DebugLogger.Log("===== ARC JUMP DEBUG =====");
        DebugLogger.Log($"Objects = {result.TotalObjects}");
        DebugLogger.Log($"Arc Points = {result.ArcPointCount}");
        DebugLogger.Log($"Arc Objects = {result.ArcObjectCount}");
        DebugLogger.Log($"Sequences = {result.SequenceCount}");
        DebugLogger.Log($"Coverage = {result.Coverage:P2}");
        DebugLogger.Log($"Longest Sequence = {result.LongestSequence}");
        DebugLogger.Log($"Average Turn Angle = {result.AverageTurnAngle:F2}°");
        DebugLogger.Log($"Min Angle = {result.MinimumTurnAngleObserved:F2}°");
        DebugLogger.Log($"Max Angle = {result.MaximumTurnAngleObserved:F2}°");
        DebugLogger.Log($"Angle Consistency = {result.AngleConsistency:P2}");
        DebugLogger.Log($"Average Distance = {result.AverageJumpDistance:F2}");
        DebugLogger.Log($"Distance Consistency = {result.DistanceConsistency:P2}");
        DebugLogger.Log($"Average Cumulative Turn = {result.AverageCumulativeTurn:F2}°");
        DebugLogger.Log($"Clockwise Sequences = {result.ClockwiseSequenceCount}");
        DebugLogger.Log($"CounterClockwise Sequences = {result.CounterClockwiseSequenceCount}");
        DebugLogger.Log($"Confidence = {result.Confidence:F3}");
        if (!detailed) return;
        BeatInsight.Models.JumpPatterns.ArcTurnDirection? previousDirection = null;
        foreach (var point in result.Points)
        {
            bool sameDirection = previousDirection == point.TurnDirection;
            DebugLogger.Log($"ARC JUMP POINT | i = {point.ObjectIndex} | Time = {point.Time} " +
                $"| Previous = ({point.PreviousX},{point.PreviousY}) | Current = ({point.CurrentX},{point.CurrentY}) " +
                $"| Next = ({point.NextX},{point.NextY}) | PrevDistance = {point.PreviousDistance:F2} " +
                $"| NextDistance = {point.NextDistance:F2} | Angle = {point.Angle:F2}° " +
                $"| TurnDirection = {point.TurnDirection} | SameDirectionAsPrevious = {sameDirection} | Qualified = True");
            previousDirection = point.TurnDirection;
        }
    }

    public static void JumpSpacingTrend(BeatInsight.Models.JumpPatterns.JumpSpacingTrendAnalysis result,
        bool enabled = false, bool detailed = false)
    {
        if (!enabled) return;
        DebugLogger.Log("===== JUMP SPACING TREND DEBUG =====");
        LogTrend("Accelerating", result.Accelerating, result.AcceleratingCoverage);
        LogTrend("Decelerating", result.Decelerating, result.DeceleratingCoverage);
        if (!detailed) return;
        foreach (var sequence in result.Sequences)
            DebugLogger.Log($"JUMP SPACING TREND | StartIndex = {sequence.StartObjectIndex} " +
                $"| EndIndex = {sequence.EndObjectIndex} | Direction = {sequence.Direction} " +
                $"| Distances = [{string.Join(", ", sequence.Distances.Select(distance => distance.ToString("F2")))}] " +
                $"| Deltas = [{string.Join(", ", sequence.Deltas.Select(delta => delta.ToString("F2")))}] " +
                $"| TrendConsistency = {sequence.TrendConsistency:P2} " +
                $"| TrendMagnitude = {sequence.TrendMagnitude:P2} | Qualified = True");
    }

    private static void LogTrend(string label, BeatInsight.Models.JumpPatterns.JumpSpacingTrendMetrics metrics,
        double coverage)
    {
        DebugLogger.Log($"{label}:");
        DebugLogger.Log($"Sequences = {metrics.SequenceCount}");
        DebugLogger.Log($"Objects = {metrics.ObjectCount}");
        DebugLogger.Log($"Coverage = {coverage:P2}");
        DebugLogger.Log($"Longest = {metrics.LongestSequence}");
        DebugLogger.Log($"Average Start Distance = {metrics.AverageStartDistance:F2}");
        DebugLogger.Log($"Average End Distance = {metrics.AverageEndDistance:F2}");
        DebugLogger.Log($"Average Trend Magnitude = {metrics.AverageTrendMagnitude:P2}");
        DebugLogger.Log($"Average Trend Consistency = {metrics.AverageTrendConsistency:P2}");
        DebugLogger.Log($"Average Distance = {metrics.AverageDistance:F2}");
        DebugLogger.Log($"Confidence = {metrics.Confidence:F3}");
    }

    public static void AlternatingSpacingJump(BeatInsight.Models.JumpPatterns.AlternatingSpacingJumpAnalysis result,
        bool enabled = false, bool detailed = false)
    {
        if (!enabled) return;
        DebugLogger.Log("===== ALTERNATING SPACING JUMP DEBUG =====");
        DebugLogger.Log($"Sequences = {result.AlternatingSpacingSequenceCount}");
        DebugLogger.Log($"Objects = {result.AlternatingSpacingObjectCount}");
        DebugLogger.Log($"Coverage = {result.Coverage:P2}");
        DebugLogger.Log($"Longest = {result.LongestSequence}");
        DebugLogger.Log($"Average Distance = {result.AverageDistance:F2}");
        DebugLogger.Log($"Average High Spacing = {result.AverageHighSpacing:F2}");
        DebugLogger.Log($"Average Low Spacing = {result.AverageLowSpacing:F2}");
        DebugLogger.Log($"Average Contrast = {result.AverageSpacingContrast:P2}");
        DebugLogger.Log($"Average Alternation Consistency = {result.AverageAlternationConsistency:P2}");
        DebugLogger.Log($"Confidence = {result.Confidence:F3}");
        if (!detailed) return;
        foreach (var sequence in result.Sequences)
            DebugLogger.Log($"ALTERNATING SPACING RUN | StartIndex = {sequence.StartObjectIndex} " +
                $"| EndIndex = {sequence.EndObjectIndex} | Start = {sequence.Start} " +
                $"| Distances = [{string.Join(", ", sequence.Distances.Select(distance => distance.ToString("F2")))}] " +
                $"| Deltas = [{string.Join(", ", sequence.Deltas.Select(delta => delta.ToString("F2")))}] " +
                $"| Signs = [{string.Join(", ", sequence.Deltas.Select(delta => SpacingSignLabel(delta)))}] " +
                $"| HighAverage = {sequence.HighSpacingAverage:F2} | LowAverage = {sequence.LowSpacingAverage:F2} " +
                $"| SpacingContrast = {sequence.SpacingContrast:P2} " +
                $"| AlternationConsistency = {sequence.AlternationConsistency:P2} | Qualified = True");
    }

    private static string SpacingSignLabel(double delta) => delta >= 10 ? "+" : delta <= -10 ? "-" : "Neutral";

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
            $"Legacy {profile.TechPresence:P2} / V2 {profile.TechV2Presence:P2} / " +
            $"Score {profile.TechScore:F0}/100 " +
            $"({profile.TechLevel})");

        DebugLogger.Log(
            $"TECH PRESENCE | Legacy = {profile.TechPresence:P2} | " +
            $"V2 = {profile.TechV2Presence:P2}");

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
    bool enabled = true)
    {
        if (!enabled)
            return;

        DebugLogger.Log("===== TECH IDENTITY DEBUG =====");

        DebugLogger.Log(
            $"TECH COVERAGE = {techCoverage:P2}");

        DebugLogger.Log(
            $"TECH IDENTITY CANDIDATE = {techIdentityScore:F2}");

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
            $"Sections {profile.ReadSectionCount}");

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

        IReadOnlyList<GameplaySection> speedSections =
            profile.SpeedSections;

        double averageSectionLength =
            speedSections.Count == 0
                ? 0
                : speedSections.Average(
                    section => section.ObjectCount);

        int maxSectionLength =
            speedSections.Count == 0
                ? 0
                : speedSections.Max(
                    section => section.ObjectCount);

        double averageSectionCadence =
            speedSections
                .Where(section =>
                    section.EndTime > section.StartTime)
                .Select(section =>
                    (section.ObjectCount - 1) * 1000.0
                    / (section.EndTime - section.StartTime))
                .DefaultIfEmpty(0)
                .Average();

        DebugLogger.Log(
            $"SPEED SCORE = {profile.SpeedScore:F0}/100 " +
            $"({profile.SpeedLevel})");

        DebugLogger.Log(
            $"SPEED INTENSITY = {profile.SpeedIntensityValue:P0}");

        DebugLogger.Log(
            $"SPEED PRESENCE = {profile.SpeedCoverage:P2}");

        DebugLogger.Log(
            $"FAST TRANSITIONS = {profile.SpeedRatio:P2}");

        DebugLogger.Log(
            $"FAST OBJECTS = {profile.SpeedObjectCount} circles / " +
            $"{profile.SpeedFastObjectRatio:P2}");

        DebugLogger.Log(
            $"SPEED SECTIONS = {speedSections.Count}");

        DebugLogger.Log(
            $"AVG SECTION LENGTH = {averageSectionLength:F1}");

        DebugLogger.Log(
            $"MAX SECTION LENGTH = {maxSectionLength}");

        DebugLogger.Log(
            $"AVG CADENCE = {averageSectionCadence:F1} obj/s");

        DebugLogger.Log(
            $"TRANSITIONS 136-150 MS = " +
            $"{profile.SpeedNearThresholdTransitionCount} / " +
            $"{profile.SpeedNearThresholdTransitionRatio:P2}");

        DebugLogger.Log(
            $"SPEED PROFILE = {profile.SpeedProfile}");

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
            $"PRIMARY TYPE = {profile.Identity.Primary}");

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

        DebugLogger.Log(
            $"BURST PRESENCE = {profile.BurstPresence}");
    }

    public static void GameplayFusion(BeatInsight.Models.Fusion.GameplayFusionProfile profile)
    {
        if (!GameplayFusionEnabled)
            return;

        DebugLogger.Log("===== GAMEPLAY FUSION DEBUG =====");
        DebugLogger.Log("BASE SKILLS");
        DebugLogger.Log($"Aim = {profile.AimScore:F2}");
        DebugLogger.Log($"Speed = {profile.SpeedScore:F2}");
        DebugLogger.Log($"Reading = {profile.ReadingScore:F2}");
        DebugLogger.Log($"Tech = {profile.TechScore:F2}");
        DebugLogger.Log($"Stamina = {(profile.StaminaScore.HasValue ? profile.StaminaScore.Value.ToString("F2") : "N/A")}");
        DebugLogger.Log("PATTERN SUPPORT");
        DebugLogger.Log($"Jump = {profile.JumpPatternSupport:F3}");
        DebugLogger.Log($"Stream = {profile.StreamPatternSupport:F3}");
        DebugLogger.Log($"Tech = {profile.TechPatternSupport:F3}");
        DebugLogger.Log($"Reading targeted = {profile.ReadingPatternSupport:F3}");
        DebugLogger.Log("FINAL SKILLS");
        DebugLogger.Log($"Aim = {profile.FinalAim:F2}");
        DebugLogger.Log($"Speed = {profile.FinalSpeed:F2}");
        DebugLogger.Log($"Reading = {profile.FinalReading:F2}");
        DebugLogger.Log($"Tech = {profile.FinalTech:F2}");
        DebugLogger.Log($"Stamina = {(profile.FinalStamina.HasValue ? profile.FinalStamina.Value.ToString("F2") : "N/A")}");
        DebugLogger.Log("STRUCTURE");
        DebugLogger.Log($"Jump = {profile.JumpStructureScore:F2}");
        DebugLogger.Log($"Stream = {profile.StreamStructureScore:F2}");
        DebugLogger.Log($"Tech = {profile.TechStructureScore:F2}");
        DebugLogger.Log($"Mixed = {profile.MixedStructureScore:F2}");
        DebugLogger.Log($"Dominant = {profile.DominantStructure}");
        DebugLogger.Log($"Secondary = {profile.SecondaryStructure?.ToString() ?? "None"}");
        DebugLogger.Log($"Confidence = {profile.Confidence:F3}");
        DebugLogger.Log("FLAGS");
        DebugLogger.Log(profile.Flags.Count == 0 ? "None" : string.Join(", ", profile.Flags));
    }

    public static void SkillPressure(BeatInsight.Models.SkillPressure.SkillPressureProfile profile)
    {
        if (!SkillPressureEnabled)
            return;

        DebugLogger.Log("===== SKILL PRESSURE DEBUG =====");
        LogSkillPressure("STAMINA", profile.Stamina);
        LogSkillPressure("ENDURANCE", profile.Endurance);
        LogSkillPressure("FINGER CONTROL", profile.FingerControl);
        LogSkillPressure("BURST CONTROL", profile.BurstControl);
        LogSkillPressure("AIM CONTROL", profile.AimControl);
        LogSkillPressure("PRECISION", profile.Precision);
        LogSkillPressure("READING PRESSURE", profile.ReadingPressure);
        LogSkillPressure("RHYTHM COMPLEXITY", profile.RhythmComplexity);
        DebugLogger.Log("GLOBAL");
        DebugLogger.Log($"AvailableSkills = {profile.AvailableSkillCount}");
        DebugLogger.Log($"Confidence = {profile.Confidence:F3}");
        DebugLogger.Log($"Diagnostics = {(profile.Diagnostics.Count == 0 ? "None" : string.Join(", ", profile.Diagnostics))}");
    }

    private static void LogSkillPressure(
        string name,
        BeatInsight.Models.SkillPressure.SkillPressureMetric metric)
    {
        DebugLogger.Log(name);
        DebugLogger.Log($"Score = {(metric.Score.HasValue ? metric.Score.Value.ToString("F2") : "N/A")}");
        DebugLogger.Log($"Confidence = {metric.Confidence:F3}");
        DebugLogger.Log($"Available = {metric.Available}");
        DebugLogger.Log($"Reliable = {metric.Reliable}");
        DebugLogger.Log($"Signals = {(metric.Signals.Count == 0 ? "None" : string.Join(", ", metric.Signals.Select(signal => $"{signal.Key}:{signal.Value:F3}")))}");
    }

    public static void SectionAnalysisV2(
        BeatInsight.Models.Sections.SectionAnalysisResult result)
    {
        if (!SectionAnalysisV2Enabled)
            return;

        DebugLogger.Log("===== SECTION ANALYSIS V2 DEBUG =====");
        DebugLogger.Log("GLOBAL");
        DebugLogger.Log($"Sections = {result.SectionCount}");
        DebugLogger.Log($"Average Difficulty = {result.AverageLocalDifficulty:F2}");
        DebugLogger.Log($"Maximum Difficulty = {result.MaximumLocalDifficulty:F2}");
        DebugLogger.Log($"Max Section = {result.MaximumDifficultySectionIndex?.ToString() ?? "N/A"}");
        DebugLogger.Log($"Spikes = {result.DifficultySpikeCount}");
        DebugLogger.Log($"Transitions = {result.GameplayTransitionCount}");
        DebugLogger.Log($"Confidence = {result.GlobalConfidence:F3}");

        foreach (BeatInsight.Models.Sections.GameplaySectionV2Analysis section in result.Sections)
        {
            DebugLogger.Log($"SECTION #{section.SectionIndex}");
            DebugLogger.Log($"Time = {section.StartTime:F0} -> {section.EndTime:F0}");
            DebugLogger.Log($"Objects = {section.ObjectCount}");
            DebugLogger.Log($"Density = {section.ObjectsPerSecond:F3}");
            DebugLogger.Log("STRUCTURE");
            DebugLogger.Log($"Jump = {section.JumpScore:F2}");
            DebugLogger.Log($"Stream = {section.StreamScore:F2}");
            DebugLogger.Log($"Tech = {section.TechScore:F2}");
            DebugLogger.Log($"Dominant = {section.DominantStructure}");
            DebugLogger.Log($"Secondary = {section.SecondaryStructure?.ToString() ?? "None"}");
            DebugLogger.Log("BASE SKILLS");
            DebugLogger.Log($"Aim = {section.BaseSkills.Aim:F2}");
            DebugLogger.Log($"Speed = {section.BaseSkills.Speed:F2}");
            DebugLogger.Log($"Reading = {section.BaseSkills.Reading:F2}");
            DebugLogger.Log($"Tech = {section.BaseSkills.Tech:F2}");
            DebugLogger.Log("SKILL PRESSURE");
            LogSkillPressure("STAMINA", section.SkillPressure.Stamina);
            LogSkillPressure("ENDURANCE", section.SkillPressure.Endurance);
            LogSkillPressure("FINGER CONTROL", section.SkillPressure.FingerControl);
            LogSkillPressure("BURST CONTROL", section.SkillPressure.BurstControl);
            LogSkillPressure("AIM CONTROL", section.SkillPressure.AimControl);
            LogSkillPressure("PRECISION", section.SkillPressure.Precision);
            LogSkillPressure("READING PRESSURE", section.SkillPressure.ReadingPressure);
            LogSkillPressure("RHYTHM COMPLEXITY", section.SkillPressure.RhythmComplexity);
            DebugLogger.Log("PATTERN SUPPORT");
            DebugLogger.Log($"Jump = {section.JumpPatterns.Support:F3}");
            DebugLogger.Log($"Stream = {section.StreamPatterns.Support:F3}");
            DebugLogger.Log($"Tech = {section.TechPatterns.Support:F3}");
            DebugLogger.Log("DIFFICULTY");
            DebugLogger.Log($"Score = {section.LocalDifficultyScore:F2}");
            DebugLogger.Log($"Confidence = {section.LocalDifficultyConfidence:F3}");
            DebugLogger.Log("SPIKE");
            DebugLogger.Log($"IsSpike = {section.Spike.IsSpike}");
            DebugLogger.Log($"Strength = {section.Spike.Strength:F3}");
            DebugLogger.Log($"Delta = {section.Spike.Delta:F2}");
            DebugLogger.Log($"Ratio = {section.Spike.Ratio:F3}");
            DebugLogger.Log($"Contributors = {(section.Spike.Contributors.Count == 0 ? "None" : string.Join(", ", section.Spike.Contributors))}");
            DebugLogger.Log("TRANSITION");
            DebugLogger.Log($"IsTransition = {section.Transition.IsTransition}");
            DebugLogger.Log($"Previous = {section.Transition.PreviousDominantStructure?.ToString() ?? "None"}");
            DebugLogger.Log($"Current = {section.Transition.CurrentDominantStructure}");
            DebugLogger.Log($"Strength = {section.Transition.Strength:F3}");
        }
    }

    public static void SectionAnalysisV2Performance(
        SectionAnalysisV2PerformanceProfile profile)
    {
        if (!SectionAnalysisV2PerformanceEnabled)
            return;

        static SectionAnalysisV2ComponentTiming Aggregate(
            SectionAnalysisV2PerformanceProfile source,
            string name,
            Func<string, bool> predicate)
        {
            SectionAnalysisV2ComponentTiming[] matches = source.Components
                .Where(component => predicate(component.Name))
                .ToArray();
            return new SectionAnalysisV2ComponentTiming(
                name,
                matches.Sum(component => component.CallCount),
                TimeSpan.FromTicks(matches.Sum(component => component.TotalDuration.Ticks)),
                matches.Length == 0 ? TimeSpan.Zero : matches.Min(component => component.MinimumDuration),
                matches.Length == 0 ? TimeSpan.Zero : matches.Max(component => component.MaximumDuration),
                matches.Sum(component => component.AllocatedBytes));
        }

        SectionAnalysisV2ComponentTiming[] rollups =
        [
            Aggregate(profile, "GameplayAnalyzer.Local", name => name == "GameplayAnalyzer.Local"),
            Aggregate(profile, "Jump", name => name.StartsWith("Jump.", StringComparison.Ordinal)),
            Aggregate(profile, "Stream", name => name.StartsWith("Stream.", StringComparison.Ordinal)),
            Aggregate(profile, "Tech", name => name.StartsWith("Tech.", StringComparison.Ordinal)),
            Aggregate(profile, "Burst", name => name == "Stream.Burst"),
            Aggregate(profile, "Speed", name => name == "Stream.Speed"),
            Aggregate(profile, "LocalDifficultyScore", name => name == "LocalDifficultyScore"),
            Aggregate(profile, "SpikeDetection", name => name == "SpikeDetection"),
            Aggregate(profile, "TransitionDetection", name => name == "TransitionDetection")
        ];

        DebugLogger.Log(
            "SECTION ANALYSIS V2 PERF | " +
            $"Total={profile.TotalDuration.TotalMilliseconds:F1} ms | " +
            $"Sections={profile.SectionCount} | " +
            $"SectionAvg={profile.AverageSectionDuration.TotalMilliseconds:F2} ms | " +
            $"SectionMin={profile.MinimumSectionDuration.TotalMilliseconds:F2} ms | " +
            $"SectionMax={profile.MaximumSectionDuration.TotalMilliseconds:F2} ms | " +
            $"Allocated={profile.TotalAllocatedBytes} bytes | " +
            $"SectionAllocAvg={profile.AverageSectionAllocatedBytes} bytes | " +
            $"SectionAllocMin={profile.MinimumSectionAllocatedBytes} bytes | " +
            $"SectionAllocMax={profile.MaximumSectionAllocatedBytes} bytes");

        foreach (SectionAnalysisV2ComponentTiming component in rollups)
        {
            DebugLogger.Log(
                "SECTION ANALYSIS V2 PERF ROLLUP | " +
                $"Name={component.Name} | Calls={component.CallCount} | " +
                $"Total={component.TotalDuration.TotalMilliseconds:F2} ms | " +
                $"Allocated={component.AllocatedBytes} bytes");
        }

        foreach (SectionAnalysisV2ComponentTiming component in profile.Components
                     .OrderByDescending(component => component.TotalDuration))
        {
            DebugLogger.Log(
                "SECTION ANALYSIS V2 PERF COMPONENT | " +
                $"Name={component.Name} | Calls={component.CallCount} | " +
                $"Total={component.TotalDuration.TotalMilliseconds:F2} ms | " +
                $"Avg={component.TotalDuration.TotalMilliseconds / Math.Max(1, component.CallCount):F3} ms | " +
                $"Min={component.MinimumDuration.TotalMilliseconds:F3} ms | " +
                $"Max={component.MaximumDuration.TotalMilliseconds:F3} ms | " +
                $"Allocated={component.AllocatedBytes} bytes");
        }

        DebugLogger.Log(
            "SECTION ANALYSIS V2 PERF OBJECTS | " +
            $"LocalBeatmaps={profile.LocalBeatmapCount} | " +
            $"LocalHitObjectLists={profile.LocalHitObjectListCount} | " +
            $"LocalHitObjectReferences={profile.LocalHitObjectReferenceCount}");
    }

    public static void IdentityV3(
        BeatInsight.Models.Identity.GameplayIdentityV3Profile profile)
    {
        if (!IdentityV3Enabled)
            return;

        DebugLogger.Log("===== GAMEPLAY IDENTITY V3 DEBUG =====");
        DebugLogger.Log("STRUCTURE");
        DebugLogger.Log($"Jump = {profile.JumpStructureScore:F2}");
        DebugLogger.Log($"Stream = {profile.StreamStructureScore:F2}");
        DebugLogger.Log($"Tech = {profile.TechStructureScore:F2}");
        DebugLogger.Log($"ClassicMixed = {profile.ClassicMixedScore:F2}");
        DebugLogger.Log($"Primary = {profile.PrimaryStructure}");
        DebugLogger.Log($"Secondary = {profile.SecondaryStructure?.ToString() ?? "None"}");
        DebugLogger.Log("MAIN SKILLS");
        DebugLogger.Log(profile.MainSkills.Count == 0
            ? "None"
            : string.Join(", ", profile.MainSkills.Select(skill => $"{skill.Name}:{skill.Score:F1} ({skill.Status}, {skill.Confidence:F2})")));
        DebugLogger.Log("MAIN PATTERNS");
        DebugLogger.Log(profile.MainPatterns.Count == 0
            ? "None"
            : string.Join(", ", profile.MainPatterns.Select(pattern => $"{pattern.Family}/{pattern.Name}:{pattern.Score:F1}")));
        DebugLogger.Log("SECTION CONTEXT");
        DebugLogger.Log($"Jump = {profile.SectionContext.JumpSectionRatio:P1}");
        DebugLogger.Log($"Stream = {profile.SectionContext.StreamSectionRatio:P1}");
        DebugLogger.Log($"Tech = {profile.SectionContext.TechSectionRatio:P1}");
        DebugLogger.Log($"Neutral/Mixed = {profile.SectionContext.MixedSectionRatio:P1}");
        DebugLogger.Log($"Represented structural families = {profile.SectionContext.RepresentedStructuralFamilyCount}");
        DebugLogger.Log($"Real structural diversity = {profile.SectionContext.RealStructuralDiversity:F3}");
        DebugLogger.Log($"Transitions = {profile.SectionContext.GameplayTransitionCount}");
        DebugLogger.Log($"Spikes = {profile.SectionContext.DifficultySpikeCount}");
        DebugLogger.Log($"Timeline = {string.Join(" -> ", profile.SectionContext.CompressedStructureTimeline)}");
        DebugLogger.Log("CONFIDENCE");
        DebugLogger.Log($"Structure = {profile.StructureConfidence:F3}");
        DebugLogger.Log($"Skill = {profile.SkillConfidence:F3}");
        DebugLogger.Log($"Pattern = {profile.PatternConfidence:F3}");
        DebugLogger.Log($"Global = {profile.GlobalIdentityConfidence:F3}");
        DebugLogger.Log($"Flags = {(profile.Flags.Count == 0 ? "None" : string.Join(", ", profile.Flags))}");
    }

    public static void GlobalTripletSpeed(BeatInsight.Models.Speed.GlobalTripletSpeedAnalysis result)
    {
        if (!GlobalTripletSpeedEnabled)
            return;

        DebugLogger.Log("===== GLOBAL TRIPLET SPEED DEBUG =====");
        DebugLogger.Log($"HitObjects = {result.HitObjectCount}");
        DebugLogger.Log($"Triplets = {result.TotalTripletCount}");
        DebugLogger.Log($"Valid = {result.ValidTripletCount}");
        DebugLogger.Log($"Excluded = {result.ExcludedTripletCount}");
        DebugLogger.Log($"Average Interval = {result.AverageTripletIntervalMs:F2} ms");
        DebugLogger.Log($"Median Interval = {result.MedianTripletIntervalMs:F2} ms");
        DebugLogger.Log($"Min Interval = {result.MinTripletIntervalMs:F2} ms");
        DebugLogger.Log($"Max Interval = {result.MaxTripletIntervalMs:F2} ms");
        DebugLogger.Log($"Speed Percent = {result.SpeedPercent:F2}");
        DebugLogger.Log($"Confidence = {result.Confidence:F3}");
        DebugLogger.Log("Mapping:");
        DebugLogger.Log($"SlowInterval = {Analysis.Speed.GlobalTripletSpeedAnalyzer.SlowIntervalMs:F0} ms");
        DebugLogger.Log($"FastInterval = {Analysis.Speed.GlobalTripletSpeedAnalyzer.FastIntervalMs:F0} ms");
        DebugLogger.Log($"GapPolicy = {Analysis.Speed.GlobalTripletSpeedAnalyzer.GapPolicy}");
        DebugLogger.Log($"SpinnerPolicy = {Analysis.Speed.GlobalTripletSpeedAnalyzer.SpinnerPolicy}");
    }

    public static void RatingV2(
        BeatInsight.Models.Rating.BeatInsightRatingV2 rating,
        BeatInsight.Models.GameplayProfile gameplay)
    {
        if (!RatingV2Enabled)
            return;

        DebugLogger.Log("===== BEATINSIGHT RATING V2 DEBUG =====");
        DebugLogger.Log("FINAL");
        DebugLogger.Log($"Score = {rating.Score:F2}");
        DebugLogger.Log($"Confidence = {rating.Confidence:F3}");
        DebugLogger.Log("BASE");
        DebugLogger.Log($"Aim = {gameplay.AimScore:F2}");
        DebugLogger.Log($"Speed = {gameplay.SpeedScore:F2}");
        DebugLogger.Log($"Read = {gameplay.ReadScore:F2}");
        DebugLogger.Log($"Tech = {gameplay.TechScore:F2}");
        DebugLogger.Log($"BaseDifficulty = {rating.BaseDifficulty:F2}");
        DebugLogger.Log("SECTIONS");
        DebugLogger.Log($"Mean = {rating.MeanSectionDifficulty:F2}");
        DebugLogger.Log($"Max = {rating.MaxSectionDifficulty:F2}");
        DebugLogger.Log($"Peak = {rating.PeakDifficulty:F2}");
        DebugLogger.Log($"Sustained = {rating.SustainedDifficulty:F2}");
        DebugLogger.Log($"HighSectionRatio = {rating.HighDifficultySectionRatio:P2}");
        DebugLogger.Log($"HardPassages = {rating.HardPassageCount}");
        DebugLogger.Log($"RepeatedSignal = {rating.RepeatedHardPassageSignal:F3}");
        DebugLogger.Log($"SkillPressure = {rating.SkillPressureContribution:F2}");
        DebugLogger.Log($"Patterns = {rating.PatternContribution:F2}");
        DebugLogger.Log($"Endurance = {rating.EnduranceContribution:F2}");
        DebugLogger.Log($"Specialization = {rating.SpecializationContribution:F2}");
        DebugLogger.Log("TOP CONTRIBUTORS");
        if (rating.TopContributors.Count == 0)
            DebugLogger.Log("None");
        else
            for (int index = 0; index < rating.TopContributors.Count; index++)
            {
                BeatInsight.Models.Rating.RatingV2Contributor item = rating.TopContributors[index];
                DebugLogger.Log($"{index + 1}. {item.Name} | {item.Category} | Value={item.Value:F2} | Contribution={item.Contribution:F2} | Confidence={item.Confidence:F3}");
            }
        DebugLogger.Log("FLAGS");
        DebugLogger.Log(rating.Flags.Count == 0 ? "None" : string.Join(", ", rating.Flags));
    }
}
