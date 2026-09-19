using BeatInsight.Models;
using System.Numerics;

namespace BeatInsight.Analysis.TechPatterns;

public sealed record GameplayEvent(
    int Index, double StartTime, double EffectiveEndTime,
    Vector2 StartPosition, Vector2 EffectiveEndPosition, int ObjectType,
    int SliderRepeats, double? SliderDuration, bool EndpointReliable,
    Vector2? InitialDirection, Vector2? TerminalDirection)
{
    public bool IsSlider => (ObjectType & 2) != 0;
    public bool IsSpinner => (ObjectType & 8) != 0;
}

/// <summary>Immutable facts; no labels, scores or modifications of the beatmap.</summary>
public static class GameplayEventBuilder
{
    public static IReadOnlyList<GameplayEvent> Build(Beatmap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var timing = map.TimingPoints.OrderBy(t => t.Time).ToArray();
        int timingIndex = 0;
        double beatLength = 0, velocity = 1;
        var result = new List<GameplayEvent>(map.HitObjects.Count);
        for (int i = 0; i < map.HitObjects.Count; i++)
        {
            var obj = map.HitObjects[i];
            while (timingIndex < timing.Length && timing[timingIndex].Time <= obj.Time)
            {
                var point = timing[timingIndex++];
                if (point.Uninherited && point.BeatLength > 0)
                {
                    beatLength = point.BeatLength;
                    velocity = 1;
                }
                else if (!point.Uninherited && point.BeatLength < 0)
                    velocity = Math.Clamp(-100 / point.BeatLength, .1, 10);
            }

            bool slider = GameplayAnalyzer.IsSlider(obj);
            var start = new Vector2(obj.X, obj.Y);
            var parsedEnd = GameplayAnalyzer.GetObjectEndPosition(obj);
            var end = slider ? new Vector2((float)parsedEnd.X, (float)parsedEnd.Y) : start;
            double? duration = null;
            if (slider && beatLength > 0 && map.SliderMultiplier > 0 && obj.Length > 0 && obj.Slides > 0)
            {
                double value = obj.Length * obj.Slides * beatLength / (100 * map.SliderMultiplier * velocity);
                if (double.IsFinite(value)) duration = value;
            }

            // The parser retains control vertices, not the flattened playable curve.
            // L/B endpoint tangents use those vertices (B endpoint remains an approximation).
            // Three-point P arcs have analytic tangents; the type itself never adds evidence.
            Vector2? initial = null, terminal = null;
            var points = new[] { start }.Concat(obj.SliderControlPoints.Select(p => new Vector2(p.X, p.Y))).ToArray();
            bool reliable = !slider;
            if (slider && points.Length > 1 && obj.SliderCurveType is "L" or "B")
            {
                var first = points.Skip(1).FirstOrDefault(p => Vector2.DistanceSquared(p, start) > 1e-8f, start);
                var last = points[^1];
                var before = points.Reverse().Skip(1).FirstOrDefault(p => Vector2.DistanceSquared(p, last) > 1e-8f, last);
                double chord = Vector2.Distance(start, last);
                double polygon = points.Zip(points.Skip(1), Vector2.Distance).Sum();
                // Length below the chord or beyond the polygon cannot end at the last control point.
                reliable = obj.Length > 0 && obj.Length >= chord - .01 && obj.Length <= polygon + .01;
                if (reliable)
                {
                    initial = Unit(first - start);
                    terminal = Unit(last - before);
                    if (obj.Slides > 0 && obj.Slides % 2 == 0)
                        terminal = initial is { } direction ? -direction : null;
                }
            }
            else if (slider && obj.SliderCurveType == "P" && points.Length == 3)
            {
                (initial, terminal) = ArcTangents(points[0], points[1], points[2], obj.Length);
                reliable = initial.HasValue && terminal.HasValue;
                if (obj.Slides > 0 && obj.Slides % 2 == 0)
                    terminal = initial is { } direction ? -direction : null;
            }
            result.Add(new(i, obj.Time, obj.Time + (duration ?? 0), start, end, obj.Type,
                slider ? Math.Max(0, obj.Slides - 1) : 0, duration, reliable, initial, terminal));
        }
        return result.ToArray();
    }

    internal static Vector2? Unit(Vector2 vector) => vector.LengthSquared() <= 1e-8f
        ? null : Vector2.Normalize(vector);

    private static (Vector2? Initial, Vector2? Terminal) ArcTangents(Vector2 a, Vector2 b, Vector2 c, double declaredLength)
    {
        // Translation-invariant circumcentre. Degenerate arcs provide no invented direction.
        var u = b - a; var v = c - a;
        double cross = (double)u.X * v.Y - (double)u.Y * v.X;
        if (Math.Abs(cross) <= 1e-8 || declaredLength <= 0) return (null, null);
        double u2 = u.LengthSquared(), v2 = v.LengthSquared();
        var centre = new Vector2((float)((u2 * v.Y - v2 * u.Y) / (2 * cross)),
            (float)((v2 * u.X - u2 * v.X) / (2 * cross)));
        var startRadius = -centre; var endRadius = v - centre;
        double sweep = Math.Atan2((double)startRadius.X * endRadius.Y - (double)startRadius.Y * endRadius.X,
            Vector2.Dot(startRadius, endRadius));
        if (cross > 0 && sweep < 0) sweep += 2 * Math.PI;
        if (cross < 0 && sweep > 0) sweep -= 2 * Math.PI;
        double arcLength = Math.Abs(sweep) * startRadius.Length();
        // Only accept an untrimmed arc; the parsed endpoint is not valid after length adjustment.
        const double LengthRoundingTolerancePixels = .5;
        if (Math.Abs(arcLength - declaredLength) > LengthRoundingTolerancePixels) return (null, null);
        float sign = Math.Sign(cross);
        return (Unit(sign * new Vector2(-startRadius.Y, startRadius.X)),
            Unit(sign * new Vector2(-endRadius.Y, endRadius.X)));
    }
}

public sealed record GameplayTransition(int FromIndex, int ToIndex, Vector2 Displacement,
    double Distance, double Interval, double AvailableMovementTime, Vector2? Direction, bool Available);

public sealed class LocalPatternContext
{
    public IReadOnlyList<GameplayEvent> Events { get; }
    public IReadOnlyList<GameplayTransition> Transitions { get; }
    public double CircleRadius { get; }
    public IReadOnlyList<double?> SignedTurns { get; }

    public LocalPatternContext(Beatmap map)
    {
        Events = GameplayEventBuilder.Build(map);
        CircleRadius = Math.Max(1, 54.4 - 4.48 * Math.Clamp(map.CS, 0, 10));
        Transitions = Events.Zip(Events.Skip(1), (a, b) =>
        {
            Vector2 displacement = b.StartPosition - a.EffectiveEndPosition;
            double interval = b.StartTime - a.StartTime;
            return new GameplayTransition(a.Index, b.Index, displacement, displacement.Length(), interval,
                b.StartTime - a.EffectiveEndTime, GameplayEventBuilder.Unit(displacement),
                !a.IsSpinner && !b.IsSpinner && a.EndpointReliable && interval > 0);
        }).ToArray();
        SignedTurns = Transitions.Zip(Transitions.Skip(1), (a, b) =>
            a.Available && b.Available ? Angle(a.Direction, b.Direction) : null).ToArray();
    }

    public static double? Angle(Vector2? first, Vector2? second) => first is { } a && second is { } b
        ? Math.Atan2(a.X * b.Y - a.Y * b.X, Vector2.Dot(a, b)) : null;

    public static double Ratio(double a, double b) => a > 0 && b > 0 ? Math.Max(a, b) / Math.Min(a, b) : 0;
}
