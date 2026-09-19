using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.JumpPatterns;

namespace BeatInsight.Analysis.JumpPatterns;

public static partial class JumpPatternAnalyzer
{
    // No prior Jump Pattern spacing threshold exists. 150px is deliberately the
    // midpoint of the initial 140-160px calibration range, well above Jump's 30px gate.
    private const double MinimumSpacedJumpDistance = 150;
    private const int MinimumSpacedLinks = 3;
    private const double MinimumSpacedDistanceConsistency = .45;
    private const int SpacedConfidenceFullLength = 8;
    private const double SpacedConfidenceFullDistance = 300;

    public static SpacedJumpAnalysis AnalyzeSpaced(IReadOnlyList<HitObject> objects, GameplayProfile profile)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(profile);

        var streamObjects = BuildStreamMask(objects.Count, profile.StreamSequences);
        var matches = new List<JumpPatternMatch>();
        var transitions = new List<SpacedJumpTransition>();
        var pending = new List<SpacedJumpTransition>();
        var involved = new HashSet<int>();
        int longest = 0;
        int sequenceId = 0;

        void FinishSequence()
        {
            if (pending.Count >= MinimumSpacedLinks)
            {
                double consistency = Consistency(pending.Select(transition => transition.Distance));
                if (consistency >= MinimumSpacedDistanceConsistency)
                {
                    int id = sequenceId++;
                    var retained = pending.Select(transition => transition with { SequenceId = id }).ToArray();
                    int objectCount = retained.Length + 1;
                    matches.Add(new(JumpPatternType.Spaced, retained[0].StartObjectIndex,
                        retained[^1].EndObjectIndex, retained[0].StartTime, retained[^1].EndTime,
                        SpacedConfidence(retained, consistency, objects.Count)));
                    transitions.AddRange(retained);
                    foreach (var transition in retained)
                    {
                        involved.Add(transition.StartObjectIndex);
                        involved.Add(transition.EndObjectIndex);
                    }
                    longest = Math.Max(longest, objectCount);
                }
            }
            pending.Clear();
        }

        for (int i = 0; i + 1 < objects.Count; i++)
        {
            var from = objects[i];
            var to = objects[i + 1];
            if ((from.Type & 1) == 0 || (to.Type & 1) == 0
                || streamObjects[i] || streamObjects[i + 1]
                || !GameplayAnalyzer.IsJumpLink(from, to, out double distance)
                || distance < MinimumSpacedJumpDistance)
            {
                FinishSequence();
                continue;
            }

            pending.Add(new(i, i + 1, from.Time, to.Time, from.X, from.Y, to.X, to.Y, distance, -1));
        }
        FinishSequence();

        double average = transitions.Count == 0 ? 0 : transitions.Average(transition => transition.Distance);
        double minimum = transitions.Count == 0 ? 0 : transitions.Min(transition => transition.Distance);
        double maximum = transitions.Count == 0 ? 0 : transitions.Max(transition => transition.Distance);
        double consistencyResult = transitions.Count == 0 ? 0 : Consistency(transitions.Select(transition => transition.Distance));
        var result = new SpacedJumpAnalysis(objects.Count, involved.Count, longest, average, minimum,
            maximum, consistencyResult, SpacedConfidence(transitions, consistencyResult, objects.Count),
            matches.ToArray(), transitions.ToArray());
        GameplayDebug.SpacedJump(result, GameplayDebug.SpacedJumpEnabled, GameplayDebug.SpacedJumpDetailedEnabled);
        return result;
    }

    private static double SpacedConfidence(IReadOnlyList<SpacedJumpTransition> transitions,
        double consistency, int totalObjects)
    {
        if (transitions.Count == 0 || totalObjects == 0) return 0;
        double length = Math.Min(1, (double)(transitions.Count + 1) / SpacedConfidenceFullLength);
        double distance = Math.Clamp((transitions.Average(transition => transition.Distance) - MinimumSpacedJumpDistance)
            / (SpacedConfidenceFullDistance - MinimumSpacedJumpDistance), 0, 1);
        double frequency = (double)transitions.Count / Math.Max(1, totalObjects - 1);
        return Math.Clamp((length + distance + consistency + frequency) / 4, 0, 1);
    }
}
