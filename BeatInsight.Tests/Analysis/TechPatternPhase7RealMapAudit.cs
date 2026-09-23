using BeatInsight.Analysis.TechPatterns;
using BeatInsight.Models;
using BeatInsight.Models.TechPatterns;
using BeatInsight.Parser;
using System.Globalization;
using System.Text;
using System.IO;

namespace BeatInsight.Tests.Analysis;

internal sealed record Phase7Map(string Name, string Category, string? Path);
internal static class TechPatternPhase7RealMapAudit
{
    private static readonly (string Name,string Category,string? Path)[] Targets =
    [
        ("Silhouette","HARD_NEGATIVE",null),("GLORIOUS DAYS","EXPECTED_TECH",null),("Kira Killer","EXPECTED_TECH","Kira Killer [Mocaotic's Insane].osu"),("The World Ends Now","EXPECTED_TECH",null),("Frozen","EXPECTED_TECH","Frozen [Collab Insane].osu"),("Move That Body","MIXED / LIGHT_TECH",null),("Premonition","MIXED / LIGHT_TECH",null),("Connect","MIXED / LIGHT_TECH",null),("Road of Resistance","HARD_NEGATIVE",null),("Tower of Heaven","HARD_NEGATIVE","Tower Of Heaven [Extra].osu"),("Wings of Courage","HARD_NEGATIVE",null),("(can you) understand me?","HARD_NEGATIVE","(can you) understand me [uhh].osu")
    ];
    internal static string Run()
    {
        var sb=new StringBuilder("TECH PATTERN RECOGNITION PHASE 7 REAL-MAP AUDIT (TEST-ONLY)\n"); var all=new List<(string Cat,int Obj,IReadOnlyList<TechPatternMatch> M)>();
        foreach(var t in Targets){string? p=Resolve(t.Name,t.Path); if(p is null){sb.AppendLine($"{t.Name} | MAP_UNAVAILABLE");continue;} Beatmap m=BeatmapParser.Load(p); var matches=TechPatternRecognitionEngine.FindMatches(m.HitObjects); all.Add((t.Category,m.HitObjects.Count,matches)); sb.AppendLine($"\nMAP: {t.Name} | Difficulty: {m.Version} | ObjectCount: {m.HitObjects.Count} | PlayableDuration: {(m.HitObjects.LastOrDefault()?.Time-(m.HitObjects.FirstOrDefault()?.Time??0))/1000.0:F2}s"); foreach(var type in Enum.GetValues<TechPatternType>()){var q=matches.Where(x=>x.Type==type).ToArray(); sb.AppendLine($"{type,-28} {q.Length,5} { (q.Length>0?q.Average(x=>x.Similarity):0),10:F3} {(q.Length>0?q.Max(x=>x.Similarity):0),10:F3} | per100={(q.Length*100.0/m.HitObjects.Count):F2}"); foreach(var x in q.OrderByDescending(x=>x.Similarity).Take(5)) sb.AppendLine($"  {x.StartTime:F0}-{x.EndTime:F0} [{x.StartIndex}-{x.EndIndex}] sim={x.Similarity:F3} geo={x.GeometrySimilarity:F3} rhythm={x.RhythmSimilarity:F3} space={x.SpacingSimilarity:F3} slider={x.SliderContextSimilarity:F3}");} var groups=matches.GroupBy(x=>$"{x.StartIndex}-{x.EndIndex}").GroupBy(g=>g.Count()).ToDictionary(g=>g.Key,g=>g.Count()); sb.AppendLine($"OVERLAP exactly1={groups.GetValueOrDefault(1)} exactly2={groups.GetValueOrDefault(2)} 3+={groups.Where(x=>x.Key>=3).Sum(x=>x.Value)}"); }
        sb.AppendLine("\nSYNTHESIS (average matches per 100 objects)"); foreach(var cat in new[]{"EXPECTED_TECH","MIXED / LIGHT_TECH","HARD_NEGATIVE"}){var rows=all.Where(x=>x.Cat==cat).ToArray(); sb.AppendLine($"{cat}: total={(rows.Length>0?rows.Average(x=>x.M.Count*100.0/x.Obj):0):F2} " + string.Join(" ",Enum.GetValues<TechPatternType>().Select(t=>$"{t}={(rows.Length>0?rows.Average(x=>x.M.Count(m=>m.Type==t)*100.0/x.Obj):0):F2}")));} return sb.ToString();
    }
    private static string? Resolve(string name,string? fixture){if(fixture is not null){var p=Path.Combine(AppContext.BaseDirectory,"Fixtures","Maps",fixture);if(File.Exists(p))return p;} var known=TechV2SliderDensityCalibrationAudit.Corpus.FirstOrDefault(x=>x.Name.Contains(name,StringComparison.OrdinalIgnoreCase)); return !string.IsNullOrEmpty(known.Path)&&File.Exists(known.Path)?known.Path:null;}
}
