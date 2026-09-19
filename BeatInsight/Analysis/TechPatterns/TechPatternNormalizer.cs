using BeatInsight.Models.TechPatterns;
using System.Numerics;
namespace BeatInsight.Analysis.TechPatterns;
public static class TechPatternNormalizer
{
    public static TechSequence Normalize(TechSequence s)
    {
        var origin=s.Positions[0]; var axis=s.Positions[1]-origin; var scale=axis.Length(); if(scale<=1e-6f) return s;
        var u=axis/scale; var v=new Vector2(-u.Y,u.X); var p=s.Positions.Select(x=>{var q=x-origin; return new Vector2(Vector2.Dot(q,u)/scale,Vector2.Dot(q,v)/scale);}).ToArray();
        var d=new[]{(double)Vector2.Distance(p[0],p[1]),(double)Vector2.Distance(p[1],p[2])}; var intervals=s.Intervals; var r=intervals.Count>0&&intervals[0]>0?intervals.Select(x=>x/intervals[0]).ToArray():intervals.ToArray();
        var c=s.SliderContext; if(c is not null) c=c with { EntryDistance=c.EntryDistance/scale, ExitDistance=c.ExitDistance/scale };
        return s with { Positions=p, Distances=d, Intervals=r, SliderContext=c };
    }
}
