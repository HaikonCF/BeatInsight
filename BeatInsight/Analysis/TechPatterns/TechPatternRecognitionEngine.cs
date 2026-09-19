using BeatInsight.Models;
using BeatInsight.Models.TechPatterns;
namespace BeatInsight.Analysis.TechPatterns;
public static class TechPatternRecognitionEngine
{
    public static readonly TechPatternTemplate SharpDirectionChange = new(TechPatternType.SharpDirectionChange,3,3,0.75,0.15,0.10,0.70);
    public static readonly TechPatternTemplate DirectionReversal = new(TechPatternType.DirectionReversal,3,3,0.80,0.12,0.08,0.70);
    public static readonly TechPatternTemplate TechnicalSliderExit = new(TechPatternType.TechnicalSliderExit,3,3,0.45,0.10,0.10,0.70,0.35,true);
    public static readonly TechPatternTemplate TechnicalSliderEntry = new(TechPatternType.TechnicalSliderEntry,3,3,0.45,0.10,0.10,0.70,0.35,true);
    public static readonly TechPatternTemplate SliderToSliderTransition = new(TechPatternType.SliderToSliderTransition,3,3,0.40,0.10,0.10,0.70,0.40,true);
    public static readonly TechPatternTemplate IrregularRhythmTransition = new(TechPatternType.IrregularRhythmTransition,3,3,0.15,0.60,0.20,0.45,0.05,false);
    public static IReadOnlyList<TechPatternMatch> FindMatches(IReadOnlyList<HitObject> objects)
    {
        var sequences=TechSequenceExtractor.Extract(objects,GameplayAnalyzer.IsSpinner).Select(TechPatternNormalizer.Normalize);
        return sequences.SelectMany(s => new[] { TechPatternSimilarity.Score(s, SharpDirectionChange), TechPatternSimilarity.Score(s, DirectionReversal), TechPatternSimilarity.ScoreSliderExit(s, TechnicalSliderExit), TechPatternSimilarity.ScoreSliderEntry(s, TechnicalSliderEntry), TechPatternSimilarity.ScoreSliderTransition(s, SliderToSliderTransition), TechPatternSimilarity.ScoreIrregularRhythm(s, IrregularRhythmTransition) }).Where(m=>m.Similarity>= (m.Type==TechPatternType.DirectionReversal?DirectionReversal.DetectionThreshold:m.Type==TechPatternType.TechnicalSliderExit?TechnicalSliderExit.DetectionThreshold:m.Type==TechPatternType.TechnicalSliderEntry?TechnicalSliderEntry.DetectionThreshold:m.Type==TechPatternType.SliderToSliderTransition?SliderToSliderTransition.DetectionThreshold:m.Type==TechPatternType.IrregularRhythmTransition?IrregularRhythmTransition.DetectionThreshold:SharpDirectionChange.DetectionThreshold)).ToArray();
    }
    public static IReadOnlyList<TechPatternMatch> FindDirectionReversals(IReadOnlyList<HitObject> objects) => FindMatches(objects).Where(m=>m.Type==TechPatternType.DirectionReversal).ToArray();
    public static IReadOnlyList<TechPatternMatch> FindSliderToSliderTransitions(IReadOnlyList<HitObject> objects) => FindMatches(objects).Where(m=>m.Type==TechPatternType.SliderToSliderTransition).ToArray();
    public static IReadOnlyList<TechPatternMatch> FindSharpDirectionChanges(IReadOnlyList<HitObject> objects)
    {
        return FindMatches(objects).Where(m=>m.Type==TechPatternType.SharpDirectionChange).ToArray();
    }
}
