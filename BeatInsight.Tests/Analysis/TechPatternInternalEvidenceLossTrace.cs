using BeatInsight.Analysis.TechPatterns;
using BeatInsight.Models.TechPatterns;
using BeatInsight.Parser;
using System.Text;
using System.IO;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// End-to-end trace of InternalEvidence population to identify where evidence is lost.
/// Instruments SliderGrammarDetector logic flow for TechnicalSliderExit pattern.
/// </summary>
internal static class TechPatternInternalEvidenceLossTrace
{
    internal static string Run()
    {
        var sb = new StringBuilder("TECH PATTERN INTERNAL EVIDENCE LOSS TRACE\n\n");

        var corpus = TechV2SliderDensityCalibrationAudit.Corpus;
        var glorious = corpus.FirstOrDefault(x => x.Name.Contains("GLORIOUS", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(glorious.Path) || !File.Exists(glorious.Path))
        {
            sb.AppendLine("GLORIOUS DAYS not found");
            return sb.ToString();
        }

        var beatmap = BeatmapParser.Load(glorious.Path);
        var context = new LocalPatternContext(beatmap);

        sb.AppendLine($"LocalPatternContext created:");
        sb.AppendLine($"  Events: {context.Events.Count}");
        sb.AppendLine($"  Transitions: {context.Transitions.Count}");
        sb.AppendLine($"  CircleRadius: {context.CircleRadius:F2}");
        sb.AppendLine();

        // Manually trace SliderGrammarDetector logic for TechnicalSliderExit
        int exitCandidates = 0;
        int exitAccepted = 0;
        int exitWithEvidence = 0;

        for (int i = 1; i + 1 < context.Events.Count; i++)
        {
            var slider = context.Events[i];
            if (!slider.IsSlider) continue;

            var next = context.Events[i + 1];
            var incoming = context.Transitions[i - 1];
            var outgoing = context.Transitions[i];

            // Check prerequisites
            if (!slider.EndpointReliable) continue;
            if (!PatternGrammarUsable(context, incoming)) continue;
            if (!PatternGrammarUsable(context, outgoing)) continue;

            exitCandidates++;

            // Calculate geometry
            double? entry = LocalPatternContext.Angle(incoming.Direction, slider.InitialDirection);
            double? exit = LocalPatternContext.Angle(slider.TerminalDirection, outgoing.Direction);
            double? handoff = LocalPatternContext.Angle(outgoing.Direction, next.InitialDirection);
            double spacing = LocalPatternContext.Ratio(incoming.Distance, outgoing.Distance);
            double rhythm = LocalPatternContext.Ratio(incoming.Interval, outgoing.Interval);

            bool directionalEntry = entry.HasValue && Math.Abs(entry.Value) >= Math.PI / 3;
            bool directionalExit = exit.HasValue && Math.Abs(exit.Value) >= Math.PI * 5 / 12;
            bool redirectedHandoff = next.IsSlider && next.EndpointReliable && handoff.HasValue
                && Math.Abs(handoff.Value) >= Math.PI / 3;
            bool corroboration = spacing >= 1.5 || rhythm >= 1.5 || redirectedHandoff;

            // TechnicalSliderExit acceptance
            bool accepted = directionalExit && (directionalEntry || corroboration);

            if (!accepted) continue;
            exitAccepted++;

            // Trace evidence construction
            var evidenceItems = new List<string>();
            int evidenceCount = 0;

            // Check evidence population conditions
            bool sliderDurationPositive = slider.SliderDuration.HasValue && slider.SliderDuration.Value > 0;

            if (sliderDurationPositive)
            {
                // Direction evidence
                evidenceItems.Add($"Direction: terminal-to-departure redirect");
                evidenceCount++;

                // Spacing evidence
                if (spacing >= 1.5)
                {
                    evidenceItems.Add($"Spacing: entry/exit contrast {spacing:F3}x");
                    evidenceCount++;
                }

                // Timing evidence
                if (rhythm >= 1.5)
                {
                    evidenceItems.Add($"Timing: interval contrast {rhythm:F3}x");
                    evidenceCount++;
                }

                // SliderTraversal evidence
                double? traversal = LocalPatternContext.Angle(slider.InitialDirection, slider.TerminalDirection);
                if (traversal.HasValue && Math.Abs(traversal.Value) >= Math.PI / 3)
                {
                    double traversalDist = System.Numerics.Vector2.Distance(slider.StartPosition, slider.EffectiveEndPosition);
                    if (traversalDist >= context.CircleRadius * 0.25)
                    {
                        evidenceItems.Add($"SliderTraversal: initial-to-terminal turn {Math.Abs(traversal.Value) * 180 / Math.PI:F1}°");
                        evidenceCount++;
                    }
                }

                // Handoff evidence
                if (redirectedHandoff && next.SliderDuration.HasValue && next.SliderDuration.Value > 0)
                {
                    evidenceItems.Add($"Handoff: next-entry redirect");
                    evidenceCount++;
                }
            }

            if (evidenceCount > 0)
                exitWithEvidence++;

            // Report first few matches with detailed trace
            if (exitAccepted <= 3)
            {
                sb.AppendLine($"=== Exit Match #{exitAccepted} @ event {i} (time {slider.StartTime:F2}s) ===");
                sb.AppendLine($"  Accepted: YES");
                sb.AppendLine($"  slider.SliderDuration: {slider.SliderDuration?.ToString("F2") ?? "null"}");
                sb.AppendLine($"  SliderDuration > 0: {sliderDurationPositive}");
                sb.AppendLine($"  directionalExit: {directionalExit} (angle: {exit?.ToString("F3") ?? "null"})");
                sb.AppendLine($"  directionalEntry: {directionalEntry}");
                sb.AppendLine($"  corroboration: {corroboration}");
                sb.AppendLine($"  spacing: {spacing:F3}x");
                sb.AppendLine($"  rhythm: {rhythm:F3}x");
                sb.AppendLine($"  redirectedHandoff: {redirectedHandoff}");
                sb.AppendLine($"  Evidence items constructed: {evidenceCount}");
                foreach (var ev in evidenceItems)
                {
                    sb.AppendLine($"    - {ev}");
                }
                sb.AppendLine();
            }
        }

        sb.AppendLine($"SUMMARY:");
        sb.AppendLine($"  Exit candidates (passed prerequisites): {exitCandidates}");
        sb.AppendLine($"  Exit accepted (passed pattern grammar): {exitAccepted}");
        sb.AppendLine($"  Exit with evidence (SliderDuration > 0): {exitWithEvidence}");
        sb.AppendLine();

        if (exitAccepted > exitWithEvidence)
        {
            sb.AppendLine($"INTERNAL_EVIDENCE_LOSS_POINT = SliderDuration condition");
            sb.AppendLine($"  {exitAccepted - exitWithEvidence} accepted matches lack SliderDuration > 0");
        }
        else if (exitAccepted == 0)
        {
            sb.AppendLine($"INTERNAL_EVIDENCE_LOSS_POINT = No matches accepted by detector");
        }
        else
        {
            sb.AppendLine($"INTERNAL_EVIDENCE_LOSS_POINT = Unknown (all accepted matches have SliderDuration > 0)");
        }

        return sb.ToString();
    }

    private static bool PatternGrammarUsable(LocalPatternContext context, GameplayTransition transition)
    {
        return transition.Available && transition.Direction.HasValue && transition.AvailableMovementTime > 0
            && transition.Distance >= context.CircleRadius * 0.25;
    }
}
