using System.Globalization;
using System.IO;
using System.Text;

namespace BeatInsight.Analysis;

internal static class TechWindowDatasetCsvExporter
{
    private static readonly string[] Columns =
    [
        "SampleId",
        "StableBeatmapId",
        "BeatmapId",
        "BeatmapSetId",
        "MapName",
        "Difficulty",
        "StartIndex",
        "EndIndex",
        "StartTime",
        "EndTime",
        "GeometryIrregularity",
        "RhythmIrregularity",
        "SliderComplexity",
        "Awkwardness",
        "SpacingVariation",
        "AngleCoherenceWithSupport",
        "SliderToSliderRatio",
        "SliderToCircleRatio",
        "CircleToSliderRatio",
        "CircleToCircleRatio",
        "ComplexSliderRunLength",
        "CoupledTechnicalMotifContinuity",
        "TechOverlapRatio",
        "IsBoundaryWindow",
        "IsTech",
    ];

    internal static void ExportTrainingCsv(
        string path,
        IEnumerable<TechWindowDatasetRow> rows) =>
        ExportCsv(path, rows, includeUnknown: false);

    internal static void ExportAuditCsv(
        string path,
        IEnumerable<TechWindowDatasetRow> rows) =>
        ExportCsv(path, rows, includeUnknown: true);

    internal static string CreateCsv(
        IEnumerable<TechWindowDatasetRow> rows,
        bool includeUnknown)
    {
        ArgumentNullException.ThrowIfNull(rows);

        using StringWriter writer = new(CultureInfo.InvariantCulture);
        WriteCsv(writer, rows, includeUnknown);
        return writer.ToString();
    }

    private static void ExportCsv(
        string path,
        IEnumerable<TechWindowDatasetRow> rows,
        bool includeUnknown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(rows);

        using StreamWriter writer = new(
            path,
            append: false,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        WriteCsv(writer, rows, includeUnknown);
    }

    private static void WriteCsv(
        TextWriter writer,
        IEnumerable<TechWindowDatasetRow> rows,
        bool includeUnknown)
    {
        writer.WriteLine(string.Join(',', Columns));

        foreach (TechWindowDatasetRow row in rows
            .Where(row => includeUnknown || row.Label != TechWindowLabel.Unknown)
            .OrderBy(row => row.SampleId, StringComparer.Ordinal))
        {
            TechV2ExperimentalWindowResult features = row.Window;

            WriteRow(writer,
            [
                row.SampleId,
                row.StableBeatmapId,
                NullableToString(row.BeatmapId),
                NullableToString(row.BeatmapSetId),
                row.MapName,
                row.Difficulty,
                row.StartIndex.ToString(CultureInfo.InvariantCulture),
                row.EndIndex.ToString(CultureInfo.InvariantCulture),
                FormatDouble(row.StartTime),
                FormatDouble(row.EndTime),
                FormatDouble(features.GeometryIrregularity),
                FormatDouble(features.RhythmIrregularity),
                FormatDouble(features.SliderComplexity),
                FormatDouble(features.Awkwardness),
                FormatDouble(features.SpacingVariation),
                FormatDouble(features.AngleCoherenceWithSupport),
                FormatDouble(features.ComplexSliderTransitionTopology.SliderToSliderRatio),
                FormatDouble(features.ComplexSliderTransitionTopology.SliderToCircleRatio),
                FormatDouble(features.ComplexSliderTransitionTopology.CircleToSliderRatio),
                FormatDouble(features.ComplexSliderTransitionTopology.CircleToCircleRatio),
                features.ComplexSliderRunLength.ToString(CultureInfo.InvariantCulture),
                FormatDouble(features.CoupledTechnicalMotifContinuity),
                FormatDouble(row.TechOverlapRatio),
                row.IsBoundaryWindow ? "true" : "false",
                row.IsTech switch { true => "true", false => "false", _ => "" },
            ]);
        }
    }

    private static void WriteRow(TextWriter writer, IReadOnlyList<string> values) =>
        writer.WriteLine(string.Join(',', values.Select(Escape)));

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\r') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    private static string FormatDouble(double value) =>
        value.ToString("G17", CultureInfo.InvariantCulture);

    private static string NullableToString(long? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
}
