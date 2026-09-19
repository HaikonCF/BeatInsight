namespace BeatInsight.Analysis;

/// <summary>
/// Deterministic, observation-only decision for one existing Tech V2 window.
/// It is intentionally separate from production Tech scoring and identity.
/// </summary>
internal sealed record TechV2WindowDecision(
    int StartIndex,
    int EndIndex,
    double StartTime,
    double EndTime,
    bool IsTech,
    double GeometryEvidence,
    double MovementEvidence,
    double SliderEvidence,
    double MotifEvidence,
    int EvidenceFamilyCount,
    double RawScore,
    bool IsModerateSupport,
    bool IsSustainedTech)
{
    // Phase 2.2 keeps the original StrongTech decision intact. Support is a
    // separate, moderate-evidence state; sustained support becomes positive
    // only through the deterministic run promotion below.
    internal bool IsStrongTech => IsTech;
    internal bool IsTechSupport => IsModerateSupport;
    internal bool IsDetectedTech => IsStrongTech || IsSustainedTech;
}

/// <summary>
/// A contiguous experimental Tech V2 passage built from StrongTech,
/// SustainedTech, and their bounded neighboring support windows. Times are
/// clamped to playable time.
/// </summary>
internal sealed record TechV2Region(
    double StartTime,
    double EndTime,
    double Duration,
    int StartWindowIndex,
    int EndWindowIndex,
    int WindowCount,
    int StrongTechWindowCount,
    int SustainedTechWindowCount = 0,
    bool LongPropagationAllowed = false,
    int SupportAbsorbedBefore = 0,
    int SupportAbsorbedAfter = 0,
    bool AnchorPropagationLimited = false);

/// <summary>
/// Experimental temporal Tech presence. It is not TechCoverage and is not
/// consumed by GameplayProfile, identity, rating, cache, or UI.
/// </summary>
internal sealed record TechV2PresenceResult(
    IReadOnlyList<TechV2Region> Regions,
    double TotalTechDuration,
    double PlayableDuration,
    double PresenceRatio,
    double PresencePercent,
    int WindowCount,
    int TechWindowCount);

/// <summary>
/// Diagnostic-only explanation for a support run that met the existing
/// length/continuity rule but was not promoted by the Phase 2.4 geometry
/// guard. The source windows remain TechSupport.
/// </summary>
internal sealed record TechV2SustainedRunGeometryRejection(
    double StartTime,
    double EndTime,
    int WindowCount,
    double AverageGeometryEvidence,
    double GuardThreshold,
    bool RejectedByGeometryGuard);

internal sealed record TechV2SustainedPromotionResult(
    IReadOnlyList<TechV2WindowDecision> Decisions,
    IReadOnlyList<TechV2SustainedRunGeometryRejection> RejectedRuns);

/// <summary>
/// Phase 2 deterministic local decision and region grouping for existing
/// TechDetectorV2Experimental output. No ML, annotations, or labels are used.
/// </summary>
internal static class TechV2ExperimentalDecisionEngine
{
    // Family composition is diagnostic-only and deliberately kept alongside
    // the decision thresholds to avoid dispersed, implicit magic numbers.
    private const double GeometryIrregularityWeight = 0.65;
    private const double AngleCoherenceWeight = 0.35;

    private const double RhythmIrregularityWeight = 0.45;
    private const double AwkwardnessWeight = 0.35;
    private const double SpacingVariationWeight = 0.20;

    private const double SliderComplexityWeight = 0.55;
    private const double SliderToSliderWeight = 0.25;
    private const double ComplexSliderRunWeight = 0.20;

    private const double ModerateFamilyThreshold = 0.45;
    private const double StrongFamilyThreshold = 0.60;
    private const double ExceptionalFamilyThreshold = 0.80;

    private const int MinimumSustainedSupportWindows = 3;
    private const double SustainedRunMinimumAverageGeometry = 0.266;

    // Region hysteresis is measured in the existing stride-based window
    // sequence. One intervening non-support window may be bridged only when
    // the natural windows still overlap in time.
    private const int MaximumInterveningWindows = 1;

    internal static IReadOnlyList<TechV2WindowDecision> Decide(
        IReadOnlyList<TechV2ExperimentalWindowResult> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);

        return windows
            .OrderBy(window => window.StartIndex)
            .ThenBy(window => window.EndIndex)
            .Select(CreateDecision)
            .ToArray();
    }

    internal static TechV2PresenceResult BuildPresence(
        IReadOnlyList<TechV2WindowDecision> decisions,
        double playableStartTime,
        double playableEndTime)
    {
        ArgumentNullException.ThrowIfNull(decisions);

        double playableDuration = Math.Max(0, playableEndTime - playableStartTime);

        if (decisions.Count == 0 || playableDuration <= 0)
        {
            return new TechV2PresenceResult(
                [],
                0,
                playableDuration,
                0,
                0,
                decisions.Count,
                decisions.Count(decision => decision.IsTech));
        }

        List<TechV2Region> regions = [];
        List<TechV2WindowDecision> active = [];
        int interveningWindowCount = 0;

        foreach (TechV2WindowDecision decision in decisions
            .OrderBy(item => item.StartIndex)
            .ThenBy(item => item.EndIndex))
        {
            if (decision.IsDetectedTech || decision.IsTechSupport)
            {
                if (active.Count == 0)
                {
                    active.Add(decision);
                    interveningWindowCount = 0;
                    continue;
                }

                TechV2WindowDecision previous = active[^1];
                bool directlyContinuous =
                    decision.StartTime <= previous.EndTime
                    && decision.StartIndex
                        <= previous.StartIndex + TechDetectorV2Experimental.Stride;
                bool positiveCanBridgeOneGap = decision.IsDetectedTech
                    && decision.StartTime <= previous.EndTime
                    && decision.StartIndex
                        <= previous.StartIndex
                        + (TechDetectorV2Experimental.Stride
                            * (MaximumInterveningWindows + 1));

                if (directlyContinuous
                    || (positiveCanBridgeOneGap
                        && interveningWindowCount <= MaximumInterveningWindows))
                {
                    active.Add(decision);
                    interveningWindowCount = 0;
                    continue;
                }

                AddRegion(regions, active, playableStartTime, playableEndTime);
                active.Clear();
                active.Add(decision);
                interveningWindowCount = 0;
                continue;
            }

            if (active.Count == 0)
                continue;

            interveningWindowCount++;

            if (interveningWindowCount > MaximumInterveningWindows)
            {
                AddRegion(regions, active, playableStartTime, playableEndTime);
                active.Clear();
                interveningWindowCount = 0;
            }
        }

        if (active.Count > 0)
            AddRegion(regions, active, playableStartTime, playableEndTime);

        IReadOnlyList<TechV2Region> orderedRegions = regions
            .OrderBy(region => region.StartTime)
            .ThenBy(region => region.StartWindowIndex)
            .ToArray();

        double totalTechDuration = orderedRegions.Sum(region => region.Duration);
        double presenceRatio = Math.Clamp(
            totalTechDuration / playableDuration,
            0,
            1);

        return new TechV2PresenceResult(
            orderedRegions,
            totalTechDuration,
            playableDuration,
            presenceRatio,
            presenceRatio * 100,
            decisions.Count,
            decisions.Count(decision => decision.IsDetectedTech));
    }

    internal static TechV2PresenceResult Detect(
        IReadOnlyList<TechV2ExperimentalWindowResult> windows,
        double playableStartTime,
        double playableEndTime) =>
        BuildPresence(
            PromoteSustainedTech(Decide(windows)),
            playableStartTime,
            playableEndTime);

    /// <summary>
    /// Promotes a locally continuous run of at least three TechSupport
    /// windows to SustainedTech. StrongTech stays unchanged and may anchor
    /// shorter neighboring support sequences during region construction.
    /// </summary>
    internal static IReadOnlyList<TechV2WindowDecision> PromoteSustainedTech(
        IReadOnlyList<TechV2WindowDecision> decisions) =>
        PromoteSustainedTechWithDiagnostics(decisions).Decisions;

    /// <summary>
    /// Applies the Phase 2.4 guard only after the existing support-run length
    /// and continuity qualification. This is separate from StrongTech.
    /// </summary>
    internal static TechV2SustainedPromotionResult PromoteSustainedTechWithDiagnostics(
        IReadOnlyList<TechV2WindowDecision> decisions,
        bool applyGeometryGuard = true)
    {
        ArgumentNullException.ThrowIfNull(decisions);

        TechV2WindowDecision[] promoted = decisions
            .OrderBy(decision => decision.StartIndex)
            .ThenBy(decision => decision.EndIndex)
            .ToArray();

        int runStart = -1;
        List<TechV2SustainedRunGeometryRejection> rejectedRuns = [];

        void PromoteRun(int start, int endExclusive)
        {
            if (start < 0 || endExclusive - start < MinimumSustainedSupportWindows)
                return;

            IReadOnlyList<TechV2WindowDecision> run = promoted
                .Skip(start)
                .Take(endExclusive - start)
                .ToArray();
            double averageGeometryEvidence = run.Average(
                decision => decision.GeometryEvidence);

            if (applyGeometryGuard
                && averageGeometryEvidence < SustainedRunMinimumAverageGeometry)
            {
                rejectedRuns.Add(new TechV2SustainedRunGeometryRejection(
                    run[0].StartTime,
                    run[^1].EndTime,
                    run.Count,
                    averageGeometryEvidence,
                    SustainedRunMinimumAverageGeometry,
                    true));
                return;
            }

            for (int index = start; index < endExclusive; index++)
                promoted[index] = promoted[index] with { IsSustainedTech = true };
        }

        for (int index = 0; index < promoted.Length; index++)
        {
            bool continuesSupportRun = runStart >= 0
                && promoted[index].IsTechSupport
                && IsConsecutiveAndOverlapping(promoted[index - 1], promoted[index]);

            if (promoted[index].IsTechSupport && (runStart < 0 || continuesSupportRun))
            {
                if (runStart < 0)
                    runStart = index;

                continue;
            }

            PromoteRun(runStart, index);
            runStart = promoted[index].IsTechSupport ? index : -1;
        }

        PromoteRun(runStart, promoted.Length);
        return new TechV2SustainedPromotionResult(
            promoted,
            rejectedRuns);
    }

    private static TechV2WindowDecision CreateDecision(
        TechV2ExperimentalWindowResult window)
    {
        ArgumentNullException.ThrowIfNull(window);

        double geometryEvidence = Math.Clamp(
            (window.GeometryIrregularity * GeometryIrregularityWeight)
            + (window.AngleCoherenceWithSupport * AngleCoherenceWeight),
            0,
            1);

        double movementEvidence = Math.Clamp(
            (window.RhythmIrregularity * RhythmIrregularityWeight)
            + (window.Awkwardness * AwkwardnessWeight)
            + (window.SpacingVariation * SpacingVariationWeight),
            0,
            1);

        double normalizedComplexSliderRun = Math.Clamp(
            (double)window.ComplexSliderRunLength
            / TechDetectorV2Experimental.WindowSize,
            0,
            1);

        double sliderEvidence = Math.Clamp(
            (window.SliderComplexity * SliderComplexityWeight)
            + (window.ComplexSliderTransitionTopology.SliderToSliderRatio
                * SliderToSliderWeight)
            + (normalizedComplexSliderRun * ComplexSliderRunWeight),
            0,
            1);

        double motifEvidence = Math.Clamp(
            window.CoupledTechnicalMotifContinuity,
            0,
            1);

        double[] evidenceFamilies =
        [
            geometryEvidence,
            movementEvidence,
            sliderEvidence,
            motifEvidence,
        ];

        int moderateFamilyCount = evidenceFamilies.Count(
            evidence => evidence >= ModerateFamilyThreshold);
        int strongFamilyCount = evidenceFamilies.Count(
            evidence => evidence >= StrongFamilyThreshold);
        bool hasExceptionalFamily = evidenceFamilies.Any(
            evidence => evidence >= ExceptionalFamilyThreshold);

        // No single family can decide Tech. An exceptional family merely
        // allows a second, moderate family to provide the required support.
        bool isTech = strongFamilyCount >= 2
            || (hasExceptionalFamily && moderateFamilyCount >= 2);

        return new TechV2WindowDecision(
            window.StartIndex,
            window.EndIndex,
            window.StartTime,
            window.EndTime,
            isTech,
            geometryEvidence,
            movementEvidence,
            sliderEvidence,
            motifEvidence,
            moderateFamilyCount,
            evidenceFamilies.Average(),
            !isTech && moderateFamilyCount >= 2,
            false);
    }

    private static bool IsConsecutiveAndOverlapping(
        TechV2WindowDecision previous,
        TechV2WindowDecision current) =>
        current.StartIndex == previous.StartIndex + TechDetectorV2Experimental.Stride
        && current.StartTime <= previous.EndTime;

    private static void AddRegion(
        ICollection<TechV2Region> regions,
        IReadOnlyList<TechV2WindowDecision> windows,
        double playableStartTime,
        double playableEndTime)
    {
        // A support-only passage is retained only after three continuous
        // supports were promoted to SustainedTech. Otherwise a StrongTech
        // window must anchor the neighboring support sequence.
        if (!windows.Any(window => window.IsDetectedTech))
            return;

        RegionPropagationSelection selection = ApplyAnchorPropagationPolicy(windows);
        IReadOnlyList<TechV2WindowDecision> selectedWindows = selection.Windows;

        // A support-only run is already sustained and must remain a complete
        // region. The policy only limits ordinary support expansion around a
        // single isolated StrongTech anchor.
        if (!selectedWindows.Any(window => window.IsDetectedTech))
            return;

        double startTime = Math.Clamp(
            selectedWindows.Min(window => window.StartTime),
            playableStartTime,
            playableEndTime);
        double endTime = Math.Clamp(
            selectedWindows.Max(window => window.EndTime),
            playableStartTime,
            playableEndTime);

        if (endTime <= startTime)
            return;

        regions.Add(new TechV2Region(
            startTime,
            endTime,
            endTime - startTime,
            selectedWindows.Min(window => window.StartIndex),
            selectedWindows.Max(window => window.EndIndex),
            selectedWindows.Count,
            selectedWindows.Count(window => window.IsStrongTech),
            selectedWindows.Count(window => window.IsSustainedTech),
            selection.LongPropagationAllowed,
            selection.SupportAbsorbedBefore,
            selection.SupportAbsorbedAfter,
            selection.AnchorPropagationLimited));
    }

    /// <summary>
    /// Phase 2.11: a lone StrongTech window can still receive its immediate
    /// local support context, but it cannot pull an arbitrary support chain
    /// into a region. Long propagation requires either multiple StrongTech
    /// anchors or a sustained positive passage in the same continuous group.
    /// </summary>
    private static RegionPropagationSelection ApplyAnchorPropagationPolicy(
        IReadOnlyList<TechV2WindowDecision> windows)
    {
        IReadOnlyList<TechV2WindowDecision> ordered = windows
            .OrderBy(window => window.StartIndex)
            .ThenBy(window => window.EndIndex)
            .ToArray();
        IReadOnlyList<TechV2WindowDecision> strong = ordered
            .Where(window => window.IsStrongTech)
            .ToArray();
        IReadOnlyList<TechV2WindowDecision> sustained = ordered
            .Where(window => window.IsSustainedTech)
            .ToArray();

        // No StrongTech anchor: natural SustainedTech remains fully positive.
        if (strong.Count == 0)
        {
            return new RegionPropagationSelection(
                ordered, false, 0, 0, false);
        }

        bool longPropagationAllowed = strong.Count >= 2 || sustained.Count >= 1;
        if (longPropagationAllowed)
        {
            return new RegionPropagationSelection(
                ordered,
                true,
                CountSupportBeforeFirstStrong(ordered, strong[0]),
                CountSupportAfterLastStrong(ordered, strong[^1]),
                false);
        }

        // Exactly one StrongTech and no SustainedTech: retain only the one
        // directly consecutive, overlapping support window on each side.
        TechV2WindowDecision anchor = strong[0];
        int anchorIndex = Enumerable.Range(0, ordered.Count)
            .Single(index => ReferenceEquals(ordered[index], anchor) || ordered[index] == anchor);
        List<TechV2WindowDecision> local = [anchor];
        int supportsBefore = 0;
        int supportsAfter = 0;

        if (anchorIndex > 0)
        {
            TechV2WindowDecision previous = ordered[anchorIndex - 1];
            if (previous.IsTechSupport
                && IsConsecutiveAndOverlapping(previous, anchor))
            {
                local.Add(previous);
                supportsBefore = 1;
            }
        }

        if (anchorIndex < ordered.Count - 1)
        {
            TechV2WindowDecision next = ordered[anchorIndex + 1];
            if (next.IsTechSupport
                && IsConsecutiveAndOverlapping(anchor, next))
            {
                local.Add(next);
                supportsAfter = 1;
            }
        }

        return new RegionPropagationSelection(
            local.OrderBy(window => window.StartIndex).ToArray(),
            false, supportsBefore, supportsAfter, true);
    }

    private static int CountSupportBeforeFirstStrong(
        IReadOnlyList<TechV2WindowDecision> windows,
        TechV2WindowDecision firstStrong) => windows.Count(window =>
        window.IsTechSupport && window.StartIndex < firstStrong.StartIndex);

    private static int CountSupportAfterLastStrong(
        IReadOnlyList<TechV2WindowDecision> windows,
        TechV2WindowDecision lastStrong) => windows.Count(window =>
        window.IsTechSupport && window.StartIndex > lastStrong.StartIndex);

    private sealed record RegionPropagationSelection(
        IReadOnlyList<TechV2WindowDecision> Windows,
        bool LongPropagationAllowed,
        int SupportAbsorbedBefore,
        int SupportAbsorbedAfter,
        bool AnchorPropagationLimited);
}
