using BeatInsight.Models;

namespace BeatInsight.Analysis;

internal sealed record TechWindowAnnotationDatasetBuildResult(
    TechWindowAnnotationLoadResult AnnotationLoad,
    TechWindowDatasetBuildResult Dataset,
    IReadOnlyList<TechWindowAnnotationIssue> UnresolvedBeatmaps);

/// <summary>
/// Converts the manually maintained annotation corpus into dataset rows. The
/// caller resolves annotations to local Beatmaps; no network, cache, or
/// likelihood-derived labels are involved in this pipeline.
/// </summary>
internal sealed class TechWindowAnnotationDatasetPipeline
{
    private readonly TechWindowAnnotationStore _annotationStore;

    public TechWindowAnnotationDatasetPipeline(
        TechWindowAnnotationStore annotationStore)
    {
        _annotationStore = annotationStore;
    }

    public TechWindowAnnotationDatasetBuildResult Build(
        Func<TechWindowAnnotationDocument, Beatmap?> resolveBeatmap)
    {
        ArgumentNullException.ThrowIfNull(resolveBeatmap);

        var annotationLoad = _annotationStore.LoadAll();
        var unresolved = new List<TechWindowAnnotationIssue>();
        var sources = new List<TechWindowDatasetSource>();

        foreach (var annotation in annotationLoad.Annotations)
        {
            var beatmap = resolveBeatmap(annotation);
            if (beatmap is null)
            {
                unresolved.Add(new TechWindowAnnotationIssue(
                    TechWindowAnnotationIssueKind.UnresolvedBeatmap,
                    "<resolver>",
                    annotation.StableBeatmapId,
                    "No local Beatmap was supplied for this annotation."));
                continue;
            }

            sources.Add(new TechWindowDatasetSource(
                beatmap,
                annotation.StableBeatmapId,
                BeatmapId: null,
                BeatmapSetId: null,
                annotation.MapName,
                annotation.Difficulty,
                new TechAnnotationSet(
                    annotation.AnnotationComplete,
                    annotation.TechRegions
                        .Select(region => new TechAnnotationRegion(
                            region.StartTime,
                            region.EndTime,
                            IsTech: true))
                        .Concat(
                            annotation.NotTechRegions.Select(region =>
                                new TechAnnotationRegion(
                                    region.StartTime,
                                    region.EndTime,
                                    IsTech: false)))
                        .ToList())));
        }

        var dataset = TechWindowDatasetBuilder.Build(sources);
        return new TechWindowAnnotationDatasetBuildResult(annotationLoad, dataset, unresolved);
    }

    public void ExportCsv(
        TechWindowAnnotationDatasetBuildResult result,
        string trainingCsvPath,
        string auditCsvPath)
    {
        ArgumentNullException.ThrowIfNull(result);
        TechWindowDatasetCsvExporter.ExportTrainingCsv(trainingCsvPath, result.Dataset.Rows);
        TechWindowDatasetCsvExporter.ExportAuditCsv(auditCsvPath, result.Dataset.Rows);
    }
}
