namespace BeatInsight.Models.TechPatterns;
public sealed record TechPatternTemplate(TechPatternType Type, int MinObjects, int MaxObjects, double GeometryWeight, double RhythmWeight, double SpacingWeight, double DetectionThreshold, double SliderContextWeight = 0, bool RequiresSlider = false)
{
    public TechPatternTemplate NormalizeWeights() { var s = GeometryWeight + RhythmWeight + SpacingWeight + SliderContextWeight; return s <= 0 ? this : this with { GeometryWeight = GeometryWeight / s, RhythmWeight = RhythmWeight / s, SpacingWeight = SpacingWeight / s, SliderContextWeight = SliderContextWeight / s }; }
}
