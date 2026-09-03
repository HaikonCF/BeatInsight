using BeatInsight.Models.Discovery;
using BeatInsight.Models.Persistence;
using BeatInsight.Services.Persistence;

namespace BeatInsight.Services.CommunityDiscovery;

/// <summary>
/// Frontière lecture seule des données locales. Elle évite un scan du dossier
/// Songs par candidat : AlreadyOwned repose uniquement sur l'index runtime
/// déjà persisté, et le dataset ML sur MlDatasetSample.BeatmapId.
/// </summary>
internal interface ICommunityBeatmapLocalStateSource
{
    IReadOnlyDictionary<int, CommunityBeatmapLocalState> GetStates(
        IReadOnlyCollection<int> beatmapIds);
}

internal sealed class RepositoryCommunityBeatmapLocalStateSource :
    ICommunityBeatmapLocalStateSource
{
    private readonly MlDatasetSampleRepository mlDatasetRepository;
    private readonly BeatmapAnalysisRepository analysisRepository;

    internal RepositoryCommunityBeatmapLocalStateSource(
        MlDatasetSampleRepository mlDatasetRepository,
        BeatmapAnalysisRepository analysisRepository)
    {
        ArgumentNullException.ThrowIfNull(mlDatasetRepository);
        ArgumentNullException.ThrowIfNull(analysisRepository);

        this.mlDatasetRepository = mlDatasetRepository;
        this.analysisRepository = analysisRepository;
    }

    public IReadOnlyDictionary<int, CommunityBeatmapLocalState> GetStates(
        IReadOnlyCollection<int> beatmapIds)
    {
        ArgumentNullException.ThrowIfNull(beatmapIds);

        int[] ids = beatmapIds
            .Where(id => id > 0)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        if (ids.Length == 0)
        {
            return new Dictionary<int, CommunityBeatmapLocalState>();
        }

        IReadOnlyList<MlDatasetSample> samples =
            mlDatasetRepository.FindByBeatmapIds(ids);

        HashSet<int> ownedBeatmapIds =
            analysisRepository.FindOwnedBeatmapIds(ids);

        var datasetByBeatmapId = samples
            .Where(sample => sample.BeatmapId is not null)
            .GroupBy(sample => sample.BeatmapId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group.Any(sample => sample.HumanValidated));

        var states = new Dictionary<int, CommunityBeatmapLocalState>();

        foreach (int beatmapId in ids)
        {
            bool alreadyInDataset =
                datasetByBeatmapId.ContainsKey(beatmapId);

            states[beatmapId] = new CommunityBeatmapLocalState(
                AlreadyOwned: ownedBeatmapIds.Contains(beatmapId),
                AlreadyInMlDataset: alreadyInDataset,
                HumanValidated: datasetByBeatmapId.TryGetValue(
                    beatmapId,
                    out bool humanValidated) && humanValidated);
        }

        return states;
    }
}
