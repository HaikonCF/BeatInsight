using BeatInsight.Models.TechPatterns;
using System.Globalization;

namespace BeatInsight.Analysis.TechPatterns;

public interface ITechPatternDetector
{
    IReadOnlyList<TechPatternMatch> Detect(LocalPatternContext context);
}

internal static class PatternGrammar
{
    internal const double MinimumMovementRadii = .25;
    internal const double DirectionContrast = Math.PI / 3;
    internal const double ExitContrast = Math.PI * 5 / 12;
    internal const double CoupledTurn = Math.PI / 4;
    internal const double CoupledAngleChange = Math.PI * 5 / 36;
    internal const double ContextRatio = 1.5;

    internal static bool Usable(LocalPatternContext context, GameplayTransition transition) =>
        transition.Available && transition.Direction.HasValue && transition.AvailableMovementTime > 0
        && transition.Distance >= context.CircleRadius * MinimumMovementRadii;

    internal static TechPatternMatch Match(LocalPatternContext c, TechPatternType type, int first, int last,
        double geometry, double rhythm, double spacing, double slider, params string[] reasons) =>
        new(type, first, last, c.Events[first].StartTime, c.Events[last].EffectiveEndTime,
            Math.Clamp(Math.Max(geometry, Math.Max(rhythm, Math.Max(spacing, slider))), 0, 1),
            geometry, rhythm, spacing, slider)
        {
            SupportingEventIndices = Enumerable.Range(first, last - first + 1).ToArray(),
            Reasons = reasons,
        };

    internal static string Degrees(double angle) => (Math.Abs(angle) * 180 / Math.PI).ToString("F1", CultureInfo.InvariantCulture);
    internal static double RelativeChange(double ratio) => ratio > 0 ? 1 - 1 / ratio : 0;
}

/// <summary>ENTRY_C: directional contrast AND corroboration. Curve type is never evidence.</summary>
public sealed class TechnicalSliderEntryDetector : ITechPatternDetector
{
    public IReadOnlyList<TechPatternMatch> Detect(LocalPatternContext context) =>
        SliderGrammarDetector.Detect(context, TechPatternType.TechnicalSliderEntry);
}

public sealed class TechnicalSliderExitDetector : ITechPatternDetector
{
    public IReadOnlyList<TechPatternMatch> Detect(LocalPatternContext context) =>
        SliderGrammarDetector.Detect(context, TechPatternType.TechnicalSliderExit);
}

public sealed class SliderToSliderTransitionDetector : ITechPatternDetector
{
    public IReadOnlyList<TechPatternMatch> Detect(LocalPatternContext context) =>
        SliderGrammarDetector.Detect(context, TechPatternType.SliderToSliderTransition);
}

internal static class SliderGrammarDetector
{
    internal static IReadOnlyList<TechPatternMatch> Detect(LocalPatternContext c, TechPatternType type)
    {
        var matches = new List<TechPatternMatch>();
        for (int i = 1; i + 1 < c.Events.Count; i++)
        {
            var slider = c.Events[i];
            var next = c.Events[i + 1];
            var incoming = c.Transitions[i - 1];
            var outgoing = c.Transitions[i];
            if (!slider.IsSlider || !slider.EndpointReliable || !PatternGrammar.Usable(c, incoming)
                || !PatternGrammar.Usable(c, outgoing)) continue;

            double? entry = LocalPatternContext.Angle(incoming.Direction, slider.InitialDirection);
            double? exit = LocalPatternContext.Angle(slider.TerminalDirection, outgoing.Direction);
            double? handoff = LocalPatternContext.Angle(outgoing.Direction, next.InitialDirection);
            double spacing = LocalPatternContext.Ratio(incoming.Distance, outgoing.Distance);
            double rhythm = LocalPatternContext.Ratio(incoming.Interval, outgoing.Interval);
            bool directionalEntry = entry.HasValue && Math.Abs(entry.Value) >= PatternGrammar.DirectionContrast;
            bool directionalExit = exit.HasValue && Math.Abs(exit.Value) >= PatternGrammar.ExitContrast;
            bool redirectedHandoff = next.IsSlider && next.EndpointReliable && handoff.HasValue
                && Math.Abs(handoff.Value) >= PatternGrammar.DirectionContrast;
            bool corroboration = spacing >= PatternGrammar.ContextRatio || rhythm >= PatternGrammar.ContextRatio
                || redirectedHandoff;
            bool accepted = type switch
            {
                TechPatternType.TechnicalSliderEntry => directionalEntry && corroboration,
                TechPatternType.TechnicalSliderExit => directionalExit && (directionalEntry || corroboration),
                TechPatternType.SliderToSliderTransition => directionalExit && redirectedHandoff,
                _ => false,
            };
            if (!accepted) continue;
            double angle = type == TechPatternType.TechnicalSliderEntry ? entry!.Value : exit!.Value;
            var evidence = new List<TechInternalEvidence>();
            // Recovery provenance does not change detector acceptance. Reliability, physical
            // displacement and valid times are prerequisites, not independent Tech votes.
            if (type is TechPatternType.TechnicalSliderExit or TechPatternType.SliderToSliderTransition
                && slider.SliderDuration is > 0)
            {
                evidence.Add(new(TechEvidenceFamily.Direction, new[] { i, i + 1 },
                    $"terminal-to-departure redirect {PatternGrammar.Degrees(exit!.Value)} degrees"));
                if (spacing >= PatternGrammar.ContextRatio)
                    evidence.Add(new(TechEvidenceFamily.Spacing, new[] { i - 1, i, i + 1 },
                        FormattableString.Invariant($"entry/exit distance contrast {spacing:F3}x")));
                if (rhythm >= PatternGrammar.ContextRatio)
                    evidence.Add(new(TechEvidenceFamily.Timing, new[] { i - 1, i, i + 1 },
                        FormattableString.Invariant($"onset interval contrast {rhythm:F3}x")));
                double? traversal = LocalPatternContext.Angle(slider.InitialDirection, slider.TerminalDirection);
                if (traversal.HasValue && Math.Abs(traversal.Value) >= PatternGrammar.DirectionContrast
                    && System.Numerics.Vector2.Distance(slider.StartPosition, slider.EffectiveEndPosition)
                        >= c.CircleRadius * PatternGrammar.MinimumMovementRadii)
                    evidence.Add(new(TechEvidenceFamily.SliderTraversal, new[] { i },
                        $"within-slider initial-to-terminal turn {PatternGrammar.Degrees(traversal.Value)} degrees"));
                if (redirectedHandoff && next.SliderDuration is > 0)
                    evidence.Add(new(TechEvidenceFamily.Handoff, new[] { i, i + 1 },
                        $"separate next-entry redirect {PatternGrammar.Degrees(handoff!.Value)} degrees"));
            }
            matches.Add(PatternGrammar.Match(c, type, i - 1, i + 1, Math.Abs(angle) / Math.PI,
                PatternGrammar.RelativeChange(rhythm), PatternGrammar.RelativeChange(spacing), Math.Abs(angle) / Math.PI,
                $"{type}: directional contrast {PatternGrammar.Degrees(angle)} degrees",
                FormattableString.Invariant($"spacing ratio {spacing:F2}x; timing ratio {rhythm:F2}x"),
                redirectedHandoff ? $"next slider entry redirects {PatternGrammar.Degrees(handoff!.Value)} degrees" : "local entry/exit context")
                with { InternalEvidence = evidence.ToArray() });
        }
        return matches;
    }
}

public sealed class IrregularRhythmTransitionDetector : ITechPatternDetector
{
    public IReadOnlyList<TechPatternMatch> Detect(LocalPatternContext c)
    {
        var result = new List<TechPatternMatch>();
        for (int i = 0; i + 1 < c.Transitions.Count; i++)
        {
            var a = c.Transitions[i]; var b = c.Transitions[i + 1];
            if (!PatternGrammar.Usable(c, a) || !PatternGrammar.Usable(c, b)) continue;
            double ratio = LocalPatternContext.Ratio(a.Interval, b.Interval);
            if (ratio < PatternGrammar.ContextRatio) continue;
            result.Add(PatternGrammar.Match(c, TechPatternType.IrregularRhythmTransition, i, i + 2,
                0, PatternGrammar.RelativeChange(ratio), 0, 0,
                FormattableString.Invariant($"ordered timing change {a.Interval:F1} -> {b.Interval:F1} ms ({ratio:F2}x); support only")));
        }
        return result;
    }
}

public sealed class DirectionObservationDetector : ITechPatternDetector
{
    public IReadOnlyList<TechPatternMatch> Detect(LocalPatternContext c)
    {
        var result = new List<TechPatternMatch>();
        for (int i = 0; i < c.SignedTurns.Count; i++)
        {
            if (c.SignedTurns[i] is not { } angle || !PatternGrammar.Usable(c, c.Transitions[i])
                || !PatternGrammar.Usable(c, c.Transitions[i + 1]) || Math.Abs(angle) < PatternGrammar.DirectionContrast) continue;
            result.Add(PatternGrammar.Match(c, TechPatternType.SharpDirectionChange, i, i + 2,
                Math.Abs(angle) / Math.PI, 0, 0, 0, $"signed turn {angle.ToString("F3", CultureInfo.InvariantCulture)} radians; observation only"));
            if (Math.Abs(angle) >= Math.PI - PatternGrammar.DirectionContrast / 2)
                result.Add(PatternGrammar.Match(c, TechPatternType.DirectionReversal, i, i + 2,
                    Math.Abs(angle) / Math.PI, 0, 0, 0, "direction reversal; observation only"));
        }
        return result;
    }
}

/// <summary>Two consecutive pivot constraints, never a bag of window averages.</summary>
public sealed class CoupledAimControlDetector : ITechPatternDetector
{
    public IReadOnlyList<TechPatternMatch> Detect(LocalPatternContext c)
    {
        var result = new List<TechPatternMatch>();
        for (int i = 0; i + 2 < c.Transitions.Count; i++)
        {
            var a = c.Transitions[i]; var b = c.Transitions[i + 1]; var d = c.Transitions[i + 2];
            if (!PatternGrammar.Usable(c, a) || !PatternGrammar.Usable(c, b) || !PatternGrammar.Usable(c, d)
                || c.SignedTurns[i] is not { } first || c.SignedTurns[i + 1] is not { } second) continue;
            double r1 = LocalPatternContext.Ratio(a.Distance, b.Distance);
            double r2 = LocalPatternContext.Ratio(b.Distance, d.Distance);
            bool oppositeTurns = first * second < 0;
            bool expansionThenContraction = (b.Distance - a.Distance) * (d.Distance - b.Distance) < 0;
            bool twoCoupledPivots = Math.Abs(first) >= PatternGrammar.CoupledTurn && Math.Abs(second) >= PatternGrammar.CoupledTurn
                && r1 >= PatternGrammar.ContextRatio && r2 >= PatternGrammar.ContextRatio;
            double timing = Math.Min(LocalPatternContext.Ratio(a.Interval, b.Interval), LocalPatternContext.Ratio(b.Interval, d.Interval));
            bool additionalConstraint = Math.Abs(Math.Abs(first) - Math.Abs(second)) >= PatternGrammar.CoupledAngleChange
                || timing >= PatternGrammar.ContextRatio;
            // Ordinary equal-spacing zigzags and regular spaced streams fail the coupled pivot grammar.
            if (!oppositeTurns || !expansionThenContraction || !twoCoupledPivots || !additionalConstraint) continue;
            result.Add(PatternGrammar.Match(c, TechPatternType.CoupledAimControl, i, i + 3,
                Math.Min(Math.Abs(first), Math.Abs(second)) / Math.PI, PatternGrammar.RelativeChange(timing),
                PatternGrammar.RelativeChange(Math.Min(r1, r2)), 0,
                $"two coupled signed direction changes: {PatternGrammar.Degrees(first)} / {PatternGrammar.Degrees(second)} degrees",
                FormattableString.Invariant($"ordered spacing contraction/expansion {r1:F2}x then {r2:F2}x"),
                "unequal turn magnitudes or two coupled timing changes; complete four-object grammar")
                with
                {
                    InternalEvidence = new TechInternalEvidence[]
                    {
                        new(TechEvidenceFamily.Direction, new[] { i, i + 1, i + 2, i + 3 }, "two opposite signed turns"),
                        new(TechEvidenceFamily.Spacing, new[] { i, i + 1, i + 2, i + 3 }, "two expansion/contraction distance relations"),
                        new(TechEvidenceFamily.CoupledSequence, new[] { i, i + 1, i + 2, i + 3 }, "complete ordered two-pivot grammar with angle/timing corroboration"),
                    },
                });
        }
        return result;
    }
}
