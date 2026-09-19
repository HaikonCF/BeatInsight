using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Models.JumpPatterns;

namespace BeatInsight.Analysis.JumpPatterns;

public static partial class JumpPatternAnalyzer
{
    private const float EdgeMargin = 64f;
    private const double PlayfieldWidth = 512;
    private const double PlayfieldHeight = 384;
    private const double MinimumEdgeDistance = 120;
    private const int MinimumEdgeObjects = 3;
    private const int EdgeConfidenceFullLength = 8;

    public static EdgeJumpAnalysis AnalyzeEdge(IReadOnlyList<HitObject> objects, GameplayProfile profile)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(profile);
        var streamObjects = BuildStreamMask(objects.Count, profile.StreamSequences);

        var matches = new List<JumpPatternMatch>();
        var transitions = new List<EdgeJumpTransition>();
        var pending = new List<EdgeJumpTransition>();
        var participatingObjects = new HashSet<int>();
        int longest = 0;

        void FinishSequence()
        {
            int count = pending.Count + 1;
            if (count >= MinimumEdgeObjects)
            {
                var first = pending[0];
                var last = pending[^1];
                matches.Add(new(JumpPatternType.Edge, first.StartObjectIndex, last.EndObjectIndex,
                    first.StartTime, last.EndTime, EdgeConfidence(pending.Count(t => t.Strong), pending.Count, count)));
                transitions.AddRange(pending);
                foreach (var transition in pending)
                {
                    participatingObjects.Add(transition.StartObjectIndex);
                    participatingObjects.Add(transition.EndObjectIndex);
                }
                longest = Math.Max(longest, count);
            }
            pending.Clear();
        }

        for (int i = 0; i + 1 < objects.Count; i++)
        {
            var from = objects[i];
            var to = objects[i + 1];
            var fromZone = GetEdgeZone(from);
            var toZone = GetEdgeZone(to);
            if ((from.Type & 1) == 0 || (to.Type & 1) == 0
                || streamObjects[i] || streamObjects[i + 1]
                || fromZone == EdgeZone.None || toZone == EdgeZone.None
                || !GameplayAnalyzer.IsJumpLink(from, to, out double distance)
                || distance < MinimumEdgeDistance)
            {
                FinishSequence();
                continue;
            }
            pending.Add(new(i, i + 1, from.Time, to.Time, fromZone, toZone,
                distance, IsStrongEdgeTransition(from, to)));
        }
        FinishSequence();
        var result = new EdgeJumpAnalysis(objects.Count, participatingObjects.Count, longest,
            EdgeConfidence(transitions.Count(t => t.Strong), transitions.Count, longest),
            matches.ToArray(), transitions.ToArray());
        GameplayDebug.EdgeJump(result, GameplayDebug.EdgeJumpEnabled, GameplayDebug.EdgeJumpDetailedEnabled);
        return result;
    }

    internal static EdgeZone GetEdgeZone(HitObject obj)
    {
        bool left = obj.X <= EdgeMargin, right = obj.X >= PlayfieldWidth - EdgeMargin;
        bool top = obj.Y <= EdgeMargin, bottom = obj.Y >= PlayfieldHeight - EdgeMargin;
        if (top && left) return EdgeZone.TopLeft;
        if (top && right) return EdgeZone.TopRight;
        if (bottom && left) return EdgeZone.BottomLeft;
        if (bottom && right) return EdgeZone.BottomRight;
        if (left) return EdgeZone.Left;
        if (right) return EdgeZone.Right;
        if (top) return EdgeZone.Top;
        if (bottom) return EdgeZone.Bottom;
        return EdgeZone.None;
    }

    // Corners retain both memberships, including corner-to-opposite-edge traversals.
    private static bool IsStrongEdgeTransition(HitObject a, HitObject b) =>
        (a.X <= EdgeMargin && b.X >= PlayfieldWidth - EdgeMargin)
        || (b.X <= EdgeMargin && a.X >= PlayfieldWidth - EdgeMargin)
        || (a.Y <= EdgeMargin && b.Y >= PlayfieldHeight - EdgeMargin)
        || (b.Y <= EdgeMargin && a.Y >= PlayfieldHeight - EdgeMargin);

    // Equal contribution of crossing strength and sequence length; diagnostic only.
    private static double EdgeConfidence(int strong, int transitions, int longest) =>
        transitions == 0 ? 0 : Math.Clamp(
            ((double)strong / transitions + Math.Min(1, (double)longest / EdgeConfidenceFullLength)) / 2, 0, 1);
}
