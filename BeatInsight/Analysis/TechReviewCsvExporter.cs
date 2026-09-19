using System.Globalization;
using System.IO;
using System.Text;

namespace BeatInsight.Analysis;

internal static class TechReviewCsvExporter
{
    private static readonly string[] Columns =
    [
        "StableBeatmapId",
        "MapName",
        "Difficulty",
        "CandidateId",
        "StartTime",
        "EndTime",
        "Priority",
        "RepresentativeScore",
        "WindowCount",
        "HumanLabel"
    ];

    /// <summary>
    /// By default exports only passages that still require human review.
    /// Reviewed passages can be included explicitly for audit/export purposes.
    /// </summary>
    internal static string CreateCsv(
        TechReviewDocument document,
        bool includeReviewed = false)
    {
        ArgumentNullException.ThrowIfNull(document);

        using StringWriter writer = new(CultureInfo.InvariantCulture);
        writer.WriteLine(string.Join(',', Columns));

        foreach (TechReviewCandidate candidate in document.Candidates
            .Where(candidate => includeReviewed || !candidate.IsReviewed)
            .OrderBy(candidate => candidate.StartTime)
            .ThenBy(candidate => candidate.EndTime)
            .ThenBy(candidate => candidate.CandidateId, StringComparer.Ordinal))
        {
            writer.WriteLine(string.Join(',',
            [
                Escape(document.StableBeatmapId),
                Escape(document.MapName),
                Escape(document.Difficulty),
                Escape(candidate.CandidateId),
                Format(candidate.StartTime),
                Format(candidate.EndTime),
                candidate.Priority.ToString(),
                Format(candidate.RepresentativeScore),
                candidate.WindowCount.ToString(CultureInfo.InvariantCulture),
                candidate.HumanLabel.ToString()
            ]));
        }

        return writer.ToString();
    }

    internal static void ExportCsv(
        string path,
        TechReviewDocument document,
        bool includeReviewed = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllText(
            path,
            CreateCsv(document, includeReviewed),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string Format(double value) =>
        value.ToString("G17", CultureInfo.InvariantCulture);

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\r') || value.Contains('\n')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
}
