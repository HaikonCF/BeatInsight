using BeatInsight.Models.TechPatterns;
namespace BeatInsight.Analysis.TechPatterns;
public static class TechPatternSimilarity
{
    public static TechPatternMatch Score(TechSequence sequence, TechPatternTemplate template)
    {
        double angle = Math.Abs(sequence.Angles.FirstOrDefault());
        double target = template.Type == TechPatternType.DirectionReversal ? Math.PI : 2.0943951023931953;
        double tolerance = template.Type == TechPatternType.DirectionReversal ? Math.PI / 6 : Math.PI / 3;
        double g = Math.Clamp(1 - Math.Abs(angle - target) / tolerance, 0, 1);
        double r = sequence.Intervals.Count == 2 ? Math.Clamp(1 - Math.Abs(sequence.Intervals[0] - sequence.Intervals[1]) / Math.Max(sequence.Intervals[0], sequence.Intervals[1]), 0, 1) : 0;
        double s = sequence.Distances.Count == 2 ? Math.Clamp(1 - Math.Abs(sequence.Distances[0] - sequence.Distances[1]) / Math.Max(sequence.Distances[0], sequence.Distances[1]), 0, 1) : 0;
        var n = template.NormalizeWeights();
        return new(template.Type, sequence.StartIndex, sequence.EndIndex, sequence.StartTime, sequence.EndTime, Math.Clamp(g*n.GeometryWeight+r*n.RhythmWeight+s*n.SpacingWeight,0,1), g, r, s);
    }
    public static TechPatternMatch ScoreSliderExit(TechSequence sequence, TechPatternTemplate template)
    {
        var c=sequence.SliderContext; double context=c is not null && sequence.SliderFlags.Count>1 && sequence.SliderFlags[1] && c.HasReliableEndPosition ? Math.Clamp((Math.Abs(c.ExitTurnAngle)/(Math.PI))*0.65 + (Math.Abs(c.ExitDistance-c.EntryDistance)/Math.Max(c.EntryDistance,c.ExitDistance))*0.35,0,1) : 0;
        var baseMatch=Score(sequence, template); var n=template.NormalizeWeights(); double final=Math.Clamp(baseMatch.GeometrySimilarity*n.GeometryWeight+baseMatch.RhythmSimilarity*n.RhythmWeight+baseMatch.SpacingSimilarity*n.SpacingWeight+context*n.SliderContextWeight,0,1);
        return baseMatch with { Type=TechPatternType.TechnicalSliderExit, Similarity=final, SliderContextSimilarity=context };
    }
    public static TechPatternMatch ScoreSliderEntry(TechSequence sequence, TechPatternTemplate template)
    {
        var c=sequence.SliderContext; double context=c is not null && sequence.SliderFlags.Count>1 && sequence.SliderFlags[1] && c.HasReliableStartDirection ? Math.Clamp((Math.Abs(c.EntryTurnAngle)/Math.PI)*0.65 + (Math.Abs(c.ExitDistance-c.EntryDistance)/Math.Max(c.EntryDistance,c.ExitDistance))*0.35,0,1) : 0;
        var baseMatch=Score(sequence, template); var n=template.NormalizeWeights(); double final=Math.Clamp(baseMatch.GeometrySimilarity*n.GeometryWeight+baseMatch.RhythmSimilarity*n.RhythmWeight+baseMatch.SpacingSimilarity*n.SpacingWeight+context*n.SliderContextWeight,0,1);
        return baseMatch with { Type=TechPatternType.TechnicalSliderEntry, Similarity=final, SliderContextSimilarity=context };
    }
    public static TechPatternMatch ScoreSliderTransition(TechSequence sequence, TechPatternTemplate template)
    {
        double context = sequence.SliderFlags.Count > 2 && sequence.SliderFlags[1] && sequence.SliderFlags[2] && sequence.SliderContext is not null ? Math.Clamp(Math.Abs(sequence.Angles.FirstOrDefault()) / Math.PI * .7 + Math.Abs(sequence.Distances[0] - sequence.Distances[1]) / Math.Max(sequence.Distances[0], sequence.Distances[1]) * .3, 0, 1) : 0;
        var b=Score(sequence,template); var n=template.NormalizeWeights(); return b with { Type=TechPatternType.SliderToSliderTransition, SliderContextSimilarity=context, Similarity=Math.Clamp(b.GeometrySimilarity*n.GeometryWeight+b.RhythmSimilarity*n.RhythmWeight+b.SpacingSimilarity*n.SpacingWeight+context*n.SliderContextWeight,0,1) };
    }
    public static TechPatternMatch ScoreIrregularRhythm(TechSequence sequence, TechPatternTemplate template)
    {
        double rhythm = sequence.Intervals.Count == 2 ? Math.Clamp(Math.Abs(sequence.Intervals[0] - sequence.Intervals[1]) / 0.75, 0, 1) : 0;
        double spacing = sequence.Distances.Count == 2 ? Math.Clamp(Math.Abs(sequence.Distances[0] - sequence.Distances[1]) / Math.Max(sequence.Distances[0], sequence.Distances[1]), 0, 1) : 0;
        double geometry = Math.Clamp(Math.Abs(sequence.Angles.FirstOrDefault()) / Math.PI, 0, 1);
        double slider = sequence.SliderContext is not null ? 0.5 : 0;
        double context = Math.Max(spacing, Math.Max(geometry, slider));
        var n=template.NormalizeWeights(); double final = context < 0.30 ? 0 : Math.Clamp(rhythm*n.RhythmWeight + geometry*n.GeometryWeight + spacing*n.SpacingWeight + slider*n.SliderContextWeight,0,1);
        return new(TechPatternType.IrregularRhythmTransition, sequence.StartIndex, sequence.EndIndex, sequence.StartTime, sequence.EndTime, final, geometry, rhythm, spacing, slider);
    }
    public static TechPatternMatch Compare(TechSequence a, TechSequence b, TechPatternTemplate t)
    {
        var g=AngleSimilarity(a.Angles.FirstOrDefault(),b.Angles.FirstOrDefault()); var r=RatioSimilarity(a.Intervals,b.Intervals); var s=RatioSimilarity(a.Distances,b.Distances); var n=t.NormalizeWeights(); var f=Math.Clamp(g*n.GeometryWeight+r*n.RhythmWeight+s*n.SpacingWeight,0,1);
        return new(t.Type,a.StartIndex,a.EndIndex,a.StartTime,a.EndTime,f,g,r,s);
    }
    private static double AngleSimilarity(double a,double b)=>Math.Clamp(1-Math.Abs(a-b)/Math.PI,0,1);
    private static double RatioSimilarity(IReadOnlyList<double> a,IReadOnlyList<double> b){if(a.Count!=b.Count||a.Count==0)return 0; return Math.Clamp(1-a.Zip(b,(x,y)=>Math.Abs(x-y)/Math.Max(Math.Max(Math.Abs(x),Math.Abs(y)),1e-9)).Average(),0,1);}
}
