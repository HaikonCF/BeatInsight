using System.IO;

namespace BeatInsight.Analysis;

internal enum TechReviewLabelFilter
{
    All,
    UnreviewedOnly,
    Tech,
    NotTech,
    Unsure
}

internal enum TechReviewPriorityFilter
{
    All,
    High,
    Medium,
    Low
}

internal sealed record TechReviewMapProgress(
    string StableBeatmapId,
    string MapName,
    string Difficulty,
    int Reviewed,
    int Total)
{
    internal string DisplayName =>
        $"{MapName} [{Difficulty}] — {Reviewed}/{Total}";
}

internal sealed record TechReviewProgressCounters(
    int Reviewed,
    int Total,
    int Tech,
    int NotTech,
    int Unsure,
    int Remaining);

internal sealed record TechReviewCandidateCursor(
    string StableBeatmapId,
    string MapName,
    string Difficulty,
    int CurrentIndex,
    int TotalCandidates,
    TechReviewCandidate Candidate);

internal sealed class TechReviewSession
{
    private readonly TechReviewStore store;
    private readonly List<TechReviewDocument> documents;
    private readonly List<CandidateLocation> orderedLocations;

    private List<CandidateLocation> filteredLocations;
    private int filteredIndex;

    private TechReviewSession(
        TechReviewStore store,
        List<TechReviewDocument> documents)
    {
        this.store = store;
        this.documents = documents;

        orderedLocations = BuildOrderedLocations(documents);
        filteredLocations = [];

        SelectedMapStableBeatmapId = null;
        LabelFilter = TechReviewLabelFilter.All;
        PriorityFilter = TechReviewPriorityFilter.All;

        RefreshFilteredLocations();
    }

    internal string? SelectedMapStableBeatmapId { get; private set; }

    internal TechReviewLabelFilter LabelFilter { get; private set; }

    internal TechReviewPriorityFilter PriorityFilter { get; private set; }

    internal IReadOnlyList<TechReviewMapProgress> GetMapProgress()
    {
        return documents
            .Select(document => new TechReviewMapProgress(
                document.StableBeatmapId,
                document.MapName,
                document.Difficulty,
                document.Candidates.Count(candidate => candidate.IsReviewed),
                document.Candidates.Count))
            .OrderBy(item => item.MapName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Difficulty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.StableBeatmapId, StringComparer.Ordinal)
            .ToArray();
    }

    internal static TechReviewSession Load(TechReviewStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        List<TechReviewDocument> documents = [];

        if (Directory.Exists(store.DirectoryPath))
        {
            foreach (string path in Directory
                .EnumerateFiles(store.DirectoryPath, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                string stableBeatmapId = Uri.UnescapeDataString(
                    Path.GetFileNameWithoutExtension(path));

                TechReviewDocument? document = store.Load(stableBeatmapId);

                if (document is not null)
                {
                    documents.Add(document);
                }
            }
        }

        documents = documents
            .OrderBy(document => document.MapName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(document => document.Difficulty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(document => document.StableBeatmapId, StringComparer.Ordinal)
            .ToList();

        return new TechReviewSession(store, documents);
    }

    internal TechReviewProgressCounters GetProgressCounters()
    {
        IEnumerable<TechReviewCandidate> allCandidates = documents.SelectMany(
            document => document.Candidates);

        int total = allCandidates.Count();
        int tech = allCandidates.Count(candidate => candidate.HumanLabel == TechReviewHumanLabel.Tech);
        int notTech = allCandidates.Count(candidate => candidate.HumanLabel == TechReviewHumanLabel.NotTech);
        int unsure = allCandidates.Count(candidate => candidate.HumanLabel == TechReviewHumanLabel.Unsure);
        int reviewed = tech + notTech + unsure;

        return new TechReviewProgressCounters(
            reviewed,
            total,
            tech,
            notTech,
            unsure,
            total - reviewed);
    }

    internal int GetFilteredCandidateCount() => filteredLocations.Count;

    internal TechReviewCandidateCursor? GetCurrentCandidate()
    {
        if (filteredLocations.Count == 0)
            return null;

        CandidateLocation location = filteredLocations[filteredIndex];
        TechReviewDocument document = documents[location.DocumentIndex];
        TechReviewCandidate candidate = document.Candidates[location.CandidateIndex];

        return new TechReviewCandidateCursor(
            document.StableBeatmapId,
            document.MapName,
            document.Difficulty,
            filteredIndex + 1,
            filteredLocations.Count,
            candidate);
    }

    internal void SetMapFilter(string? stableBeatmapId)
    {
        SelectedMapStableBeatmapId = string.IsNullOrWhiteSpace(stableBeatmapId)
            ? null
            : stableBeatmapId;
        RefreshFilteredLocations();
    }

    internal void SetLabelFilter(TechReviewLabelFilter filter)
    {
        LabelFilter = filter;
        RefreshFilteredLocations();
    }

    internal void SetPriorityFilter(TechReviewPriorityFilter filter)
    {
        PriorityFilter = filter;
        RefreshFilteredLocations();
    }

    internal bool MoveNext()
    {
        if (filteredIndex + 1 >= filteredLocations.Count)
            return false;

        filteredIndex++;
        return true;
    }

    internal bool MovePrevious()
    {
        if (filteredIndex <= 0)
            return false;

        filteredIndex--;
        return true;
    }

    internal bool ApplyLabel(TechReviewHumanLabel label)
    {
        if (filteredLocations.Count == 0)
            return false;

        CandidateLocation location = filteredLocations[filteredIndex];
        SetCandidateLabel(location, label);

        CandidateLocation? nextUnreviewed = filteredLocations
            .Skip(filteredIndex)
            .FirstOrDefault(loc =>
                GetCandidate(loc).HumanLabel == TechReviewHumanLabel.Unreviewed);

        if (nextUnreviewed is null)
        {
            nextUnreviewed = filteredLocations.FirstOrDefault(loc =>
                GetCandidate(loc).HumanLabel == TechReviewHumanLabel.Unreviewed);
        }

        if (nextUnreviewed is not null)
        {
            filteredIndex = filteredLocations.FindIndex(nextUnreviewed.Equals);
            if (filteredIndex < 0)
                filteredIndex = 0;
        }
        else if (filteredLocations.Count > 0)
        {
            filteredIndex = Math.Min(filteredIndex, filteredLocations.Count - 1);
        }

        return true;
    }

    internal bool SkipCurrent()
    {
        if (filteredLocations.Count == 0)
            return false;

        CandidateLocation location = filteredLocations[filteredIndex];
        SetCandidateLabel(location, TechReviewHumanLabel.Unreviewed);

        if (filteredLocations.Count > 1)
        {
            filteredIndex = Math.Min(filteredIndex + 1, filteredLocations.Count - 1);
        }

        return true;
    }

    private void SetCandidateLabel(
        CandidateLocation location,
        TechReviewHumanLabel label)
    {
        TechReviewDocument document = documents[location.DocumentIndex];
        TechReviewCandidate existing = document.Candidates[location.CandidateIndex];
        TechReviewCandidate updated = existing with { HumanLabel = label };

        document.Candidates[location.CandidateIndex] = updated;
        store.Save(document);
        RefreshFilteredLocations(location);
    }

    private TechReviewCandidate GetCandidate(CandidateLocation location)
    {
        return documents[location.DocumentIndex].Candidates[location.CandidateIndex];
    }

    private static List<CandidateLocation> BuildOrderedLocations(
        IReadOnlyList<TechReviewDocument> source)
    {
        List<CandidateLocation> locations = [];

        for (int documentIndex = 0; documentIndex < source.Count; documentIndex++)
        {
            TechReviewDocument document = source[documentIndex];

            for (int candidateIndex = 0;
                 candidateIndex < document.Candidates.Count;
                 candidateIndex++)
            {
                locations.Add(new CandidateLocation(documentIndex, candidateIndex));
            }
        }

        return locations;
    }

    private void RefreshFilteredLocations(CandidateLocation? preferred = null)
    {
        CandidateLocation? preserve = preferred;

        if (preserve is null && filteredLocations.Count > 0)
        {
            preserve = filteredLocations[Math.Clamp(filteredIndex, 0, filteredLocations.Count - 1)];
        }

        filteredLocations = orderedLocations
            .Where(MatchesFilters)
            .ToList();

        if (filteredLocations.Count == 0)
        {
            filteredIndex = 0;
            return;
        }

        if (preserve is CandidateLocation target)
        {
            int index = filteredLocations.FindIndex(location => location.Equals(target));

            if (index >= 0)
            {
                filteredIndex = index;
                return;
            }
        }

        filteredIndex = Math.Clamp(filteredIndex, 0, filteredLocations.Count - 1);
    }

    private bool MatchesFilters(CandidateLocation location)
    {
        TechReviewDocument document = documents[location.DocumentIndex];
        TechReviewCandidate candidate = document.Candidates[location.CandidateIndex];

        if (SelectedMapStableBeatmapId is not null
            && !string.Equals(
                document.StableBeatmapId,
                SelectedMapStableBeatmapId,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (PriorityFilter != TechReviewPriorityFilter.All)
        {
            TechReviewPriority expected = PriorityFilter switch
            {
                TechReviewPriorityFilter.High => TechReviewPriority.High,
                TechReviewPriorityFilter.Medium => TechReviewPriority.Medium,
                TechReviewPriorityFilter.Low => TechReviewPriority.Low,
                _ => throw new InvalidOperationException("Invalid priority filter.")
            };

            if (candidate.Priority != expected)
                return false;
        }

        return LabelFilter switch
        {
            TechReviewLabelFilter.All => true,
            TechReviewLabelFilter.UnreviewedOnly => candidate.HumanLabel == TechReviewHumanLabel.Unreviewed,
            TechReviewLabelFilter.Tech => candidate.HumanLabel == TechReviewHumanLabel.Tech,
            TechReviewLabelFilter.NotTech => candidate.HumanLabel == TechReviewHumanLabel.NotTech,
            TechReviewLabelFilter.Unsure => candidate.HumanLabel == TechReviewHumanLabel.Unsure,
            _ => false
        };
    }

    private sealed record CandidateLocation(
        int DocumentIndex,
        int CandidateIndex);
}
