using BeatInsight.Models;
using BeatInsight.Models.TechPatterns;
using System.Numerics;
namespace BeatInsight.Analysis.TechPatterns;
public static class TechSequenceExtractor
{
    public static IReadOnlyList<TechSequence> Extract(IReadOnlyList<HitObject> objects, Func<HitObject,bool>? isSpinner = null)
    {
        var result = new List<TechSequence>();
        for (int i=0; i+2<objects.Count; i++)
        {
            var trio = objects.Skip(i).Take(3).ToArray();
            if (isSpinner is not null && trio.Any(isSpinner)) continue;
            var p = trio.Select(o => new Vector2(o.X,o.Y)).ToArray();
            var d = new[] { (double)Vector2.Distance(p[0],p[1]), (double)Vector2.Distance(p[1],p[2]) };
            if (d[0] <= 1e-6f) continue;
            var a = new[] { Math.Atan2(p[1].Y-p[0].Y,p[1].X-p[0].X), Math.Atan2(p[2].Y-p[1].Y,p[2].X-p[1].X) };
            SliderContext? context = null;
            if (GameplayAnalyzer.IsSlider(trio[1]))
            {
                var end = GameplayAnalyzer.GetObjectEndPosition(trio[1]);
                var endPoint = new Vector2((float)end.X, (float)end.Y);
                double incoming = Math.Atan2(p[1].Y-p[0].Y,p[1].X-p[0].X);
                bool reliableStart = trio[1].SliderControlPoints.Count > 0;
                var firstCp = reliableStart ? trio[1].SliderControlPoints[0] : trio[1].SliderEndPosition;
                double initial = Math.Atan2(firstCp.Y-trio[1].Y, firstCp.X-trio[1].X);
                context = new SliderContext(trio[1].SliderCurveType, trio[1].Length, trio[1].Slides, trio[1].SliderControlPoints.Count,
                    d[0], Vector2.Distance(endPoint, p[2]), trio[1].Time-trio[0].Time, trio[2].Time-trio[1].Time,
                    NormalizeAngle(Math.Atan2(p[2].Y-endPoint.Y,p[2].X-endPoint.X)-Math.Atan2(endPoint.Y-p[0].Y,endPoint.X-p[0].X)), true, NormalizeAngle(initial), NormalizeAngle(initial-incoming), reliableStart);
            }
            result.Add(new TechSequence(i,i+2,trio[0].Time,trio[2].Time,p,d,new[]{NormalizeAngle(a[1]-a[0])},new[]{(double)(trio[1].Time-trio[0].Time),(double)(trio[2].Time-trio[1].Time)},trio.Select(GameplayAnalyzer.IsSlider).ToArray(),context));
        }
        return result;
    }
    private static double NormalizeAngle(double a) => Math.Atan2(Math.Sin(a),Math.Cos(a));
}
