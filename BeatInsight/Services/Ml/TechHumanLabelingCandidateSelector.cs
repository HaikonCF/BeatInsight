using BeatInsight.Models.Persistence;
using BeatInsight.Services.Library;
using BeatInsight.Services.Persistence;
using System.IO;

namespace BeatInsight.Services.Ml;

/// <summary>
/// Sélection locale, déterministe et en lecture seule des candidats à
/// annotation humaine Tech. Le dataset conserve les labels humains ; le cache
/// runtime fournit seulement l'Identity actuelle et sa confidence pour
/// prioriser la revue. Aucun résultat ne devient un label automatiquement.
/// </summary>
internal sealed class TechHumanLabelingCandidateSelector
{
    private const string TechPrimary = "Tech";

    private readonly MlDatasetSampleRepository sampleRepository;
    private readonly BeatmapAnalysisRepository analysisRepository;

    internal TechHumanLabelingCandidateSelector(
        MlDatasetSampleRepository sampleRepository,
        BeatmapAnalysisRepository analysisRepository)
    {
        ArgumentNullException.ThrowIfNull(sampleRepository);
        ArgumentNullException.ThrowIfNull(analysisRepository);

        this.sampleRepository = sampleRepository;
        this.analysisRepository = analysisRepository;
    }

    /// <summary>
    /// Retourne le prochain candidat non validé après l'ancre courante. Si
    /// celle-ci a disparu de la sélection (par exemple après validation), le
    /// meilleur candidat restant est retourné. Une fois la dernière entrée
    /// atteinte, null signale explicitement la fin de la file Tech.
    /// </summary>
    internal TechHumanLabelingCandidate? FindNext(long? currentSampleId)
    {
        IReadOnlyList<TechHumanLabelingCandidate> candidates = ListCandidates();

        if (candidates.Count == 0)
        {
            return null;
        }

        if (!currentSampleId.HasValue)
        {
            return candidates[0];
        }

        int currentIndex = candidates
            .Select((candidate, index) => new { candidate, index })
            .Where(item => item.candidate.Sample.SampleId == currentSampleId)
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .Single();

        return currentIndex < 0
            ? candidates[0]
            : candidates.ElementAtOrDefault(currentIndex + 1);
    }

    /// <summary>
    /// Exposé pour les tests et les futures vues de file, sans modifier aucun
    /// sample. Les candidats sont déjà dédoublonnés et triés.
    /// </summary>
    internal IReadOnlyList<TechHumanLabelingCandidate> ListCandidates()
    {
        sampleRepository.EnsureSchema();
        analysisRepository.EnsureSchema();

        List<TechHumanLabelingCandidate> matching = [];

        foreach (MlDatasetSample sample in sampleRepository.List())
        {
            if (sample.HumanValidated || !IsStandardLocalSample(sample))
            {
                continue;
            }

            BeatmapAnalysisRecord? analysis = analysisRepository.Find(
                sample.SourceFilePath);

            if (analysis is null
                || !string.Equals(
                    analysis.Profile.IdentityPrimary,
                    TechPrimary,
                    StringComparison.Ordinal))
            {
                continue;
            }

            matching.Add(new TechHumanLabelingCandidate
            {
                Sample = sample,
                IdentityConfidence = analysis.Profile.IdentityConfidence,
            });
        }

        // Un même BeatmapId ne doit pas être présenté deux fois. Pour les
        // difficultés locales appartenant au même beatmapset, le dossier
        // source est la clé stable déjà disponible sans changer le schéma ML.
        return matching
            .GroupBy(candidate => BeatmapKey(candidate.Sample),
                StringComparer.OrdinalIgnoreCase)
            .Select(ChooseBest)
            .GroupBy(candidate => BeatmapSetKey(candidate.Sample),
                StringComparer.OrdinalIgnoreCase)
            .Select(ChooseBest)
            .OrderByDescending(candidate => candidate.IdentityConfidence)
            .ThenBy(candidate => candidate.Sample.SampleId)
            .ThenBy(candidate => candidate.Sample.SourceFilePath,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static TechHumanLabelingCandidate ChooseBest(
        IGrouping<string, TechHumanLabelingCandidate> group) => group
            .OrderByDescending(candidate => candidate.IdentityConfidence)
            .ThenBy(candidate => candidate.Sample.SampleId)
            .ThenBy(candidate => candidate.Sample.SourceFilePath,
                StringComparer.OrdinalIgnoreCase)
            .First();

    private static bool IsStandardLocalSample(MlDatasetSample sample)
    {
        try
        {
            return File.Exists(sample.SourceFilePath)
                   && BeatmapGameModeReader.IsSupportedForAnalysis(
                       sample.SourceFilePath);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string BeatmapKey(MlDatasetSample sample) =>
        sample.BeatmapId is int beatmapId and > 0
            ? $"id:{beatmapId}"
            : $"path:{sample.SourceFilePath}";

    private static string BeatmapSetKey(MlDatasetSample sample)
    {
        string? directory = Path.GetDirectoryName(sample.SourceFilePath);

        return string.IsNullOrWhiteSpace(directory)
            ? $"path:{sample.SourceFilePath}"
            : $"directory:{directory}";
    }
}

internal sealed class TechHumanLabelingCandidate
{
    internal required MlDatasetSample Sample { get; init; }
    internal required double IdentityConfidence { get; init; }
}
