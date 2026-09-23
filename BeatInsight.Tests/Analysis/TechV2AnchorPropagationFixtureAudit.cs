using BeatInsight.Analysis;
using BeatInsight.Models;
using BeatInsight.Parser;
using System.Globalization;
using System.Text;

namespace BeatInsight.Tests.Analysis;

/// <summary>
/// Test-only before/after report. The "previous" side mirrors the Phase 2.10
/// unbounded anchor grouping and is never called by production code.
/// </summary>
internal static class TechV2AnchorPropagationFixtureAudit
{
    private const int MaximumInterveningWindows = 1;

    internal static TechV2AnchorPropagationFixtureReport Run(string databasePath)
    {
        TechV2HumanGroundedAuditReport current = TechV2HumanGroundedStrongTechAudit.Run(databasePath);
        IReadOnlyList<TechV2AnchorPropagationFixtureMap> maps = current.Maps.Select(map =>
        {
            if (!map.Resolved || map.Path is null)
                return TechV2AnchorPropagationFixtureMap.Unresolved(map.Reference, map.ResolutionNote);

            Beatmap beatmap = BeatmapParser.Load(map.Path);
            TechV2PresenceResult previous = BuildLegacyPresence(
                map.Decisions,
                beatmap.HitObjects.FirstOrDefault()?.Time ?? 0,
                beatmap.HitObjects.LastOrDefault()?.Time ?? 0);
            return new TechV2AnchorPropagationFixtureMap(
                map.Reference, true, map.ResolutionNote,
                previous, map.Presence,
                map.Decisions.Count(decision => decision.IsStrongTech),
                map.Decisions.Count(decision => decision.IsTechSupport),
                map.Decisions.Count(decision => decision.IsSustainedTech));
        }).ToArray();
        return new TechV2AnchorPropagationFixtureReport(maps);
    }

    internal static string FormatReport(TechV2AnchorPropagationFixtureReport report)
    {
        StringBuilder text = new();
        text.AppendLine("===== TECH V2 PHASE 2.11 ANCHOR PROPAGATION FIXTURE REPORT =====");
        text.AppendLine("Map | HumanLabel | Previous Presence | New Presence | Delta | Previous Regions | New Regions | Strong/Support/Sustained");
        foreach (TechV2AnchorPropagationFixtureMap map in report.Maps)
        {
            if (!map.Resolved)
            {
                text.AppendLine($"  {map.Reference.Key} | {map.Reference.HumanTruth} | unresolved: {map.ResolutionNote}");
                continue;
            }
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  {0} | {1} | {2:F2}% | {3:F2}% | {4:F2}% | {5} | {6} | {7}/{8}/{9}",
                map.Reference.Key, map.Reference.HumanTruth,
                map.Previous.PresencePercent, map.Current.PresencePercent,
                map.Current.PresencePercent - map.Previous.PresencePercent,
                map.Previous.Regions.Count, map.Current.Regions.Count,
                map.StrongTechCount, map.TechSupportCount, map.SustainedTechCount));
        }
        return text.ToString();
    }

    private static TechV2PresenceResult BuildLegacyPresence(
        IReadOnlyList<TechV2WindowDecision> decisions,
        double playableStartTime,
        double playableEndTime)
    {
        double playableDuration = Math.Max(0, playableEndTime - playableStartTime);
        if (decisions.Count == 0 || playableDuration <= 0)
            return new TechV2PresenceResult([], 0, playableDuration, 0, 0, decisions.Count, decisions.Count(decision => decision.IsTech));

        List<TechV2Region> regions = [];
        List<TechV2WindowDecision> active = [];
        int gaps = 0;
        foreach (TechV2WindowDecision decision in decisions.OrderBy(item => item.StartIndex).ThenBy(item => item.EndIndex))
        {
            if (decision.IsDetectedTech || decision.IsTechSupport)
            {
                if (active.Count == 0)
                {
                    active.Add(decision);
                    gaps = 0;
                    continue;
                }
                TechV2WindowDecision previous = active[^1];
                bool direct = decision.StartTime <= previous.EndTime
                    && decision.StartIndex <= previous.StartIndex + TechDetectorV2Experimental.Stride;
                bool bridge = decision.IsDetectedTech
                    && decision.StartTime <= previous.EndTime
                    && decision.StartIndex <= previous.StartIndex + (TechDetectorV2Experimental.Stride * (MaximumInterveningWindows + 1));
                if (direct || (bridge && gaps <= MaximumInterveningWindows))
                {
                    active.Add(decision);
                    gaps = 0;
                    continue;
                }
                AddLegacyRegion(regions, active, playableStartTime, playableEndTime);
                active.Clear();
                active.Add(decision);
                gaps = 0;
                continue;
            }
            if (active.Count == 0)
                continue;
            gaps++;
            if (gaps > MaximumInterveningWindows)
            {
                AddLegacyRegion(regions, active, playableStartTime, playableEndTime);
                active.Clear();
                gaps = 0;
            }
        }
        if (active.Count > 0)
            AddLegacyRegion(regions, active, playableStartTime, playableEndTime);

        double duration = regions.Sum(region => region.Duration);
        double ratio = Math.Clamp(duration / playableDuration, 0, 1);
        return new TechV2PresenceResult(regions, duration, playableDuration, ratio, ratio * 100,
            decisions.Count, decisions.Count(decision => decision.IsDetectedTech));
    }

    private static void AddLegacyRegion(
        ICollection<TechV2Region> regions,
        IReadOnlyList<TechV2WindowDecision> windows,
        double playableStart,
        double playableEnd)
    {
        if (!windows.Any(window => window.IsDetectedTech))
            return;
        double start = Math.Clamp(windows.Min(window => window.StartTime), playableStart, playableEnd);
        double end = Math.Clamp(windows.Max(window => window.EndTime), playableStart, playableEnd);
        if (end <= start)
            return;
        regions.Add(new TechV2Region(start, end, end - start,
            windows.Min(window => window.StartIndex), windows.Max(window => window.EndIndex), windows.Count,
            windows.Count(window => window.IsStrongTech), windows.Count(window => window.IsSustainedTech)));
    }
}

internal sealed record TechV2AnchorPropagationFixtureMap(
    TechV2HumanReference Reference,
    bool Resolved,
    string ResolutionNote,
    TechV2PresenceResult Previous,
    TechV2PresenceResult Current,
    int StrongTechCount,
    int TechSupportCount,
    int SustainedTechCount)
{
    internal static TechV2AnchorPropagationFixtureMap Unresolved(TechV2HumanReference reference, string note) =>
        new(reference, false, note, new([], 0, 0, 0, 0, 0, 0), new([], 0, 0, 0, 0, 0, 0), 0, 0, 0);
}

internal sealed record TechV2AnchorPropagationFixtureReport(IReadOnlyList<TechV2AnchorPropagationFixtureMap> Maps);
