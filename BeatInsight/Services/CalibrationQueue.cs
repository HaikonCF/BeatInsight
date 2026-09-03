using BeatInsight.Models.Persistence;

namespace BeatInsight.Services;

/// <summary>
/// Ordonne et parcourt les échantillons correspondant à un pack de
/// calibration, sans dépendance à MainWindow ni au repository : cette
/// classe ne fait que réordonner/filtrer une liste déjà chargée.
///
/// Le bucket du pack (voir <see cref="CalibrationPackBucket"/>) ne
/// participe à rien ici au-delà de l'ordre d'affichage : aucune
/// méthode de cette classe ne lit ni n'écrit
/// <see cref="MlDatasetSample.PrimaryHumanLabel"/> ou
/// <see cref="MlDatasetSample.SecondaryHumanLabel"/>.
/// </summary>
internal static class CalibrationQueue
{
    /// <summary>
    /// Réordonne <paramref name="matches"/> (dans un ordre quelconque,
    /// typiquement SampleId croissant depuis le repository) pour suivre
    /// l'ordre déterministe du pack. Un Beatmap ID du pack absent de
    /// <paramref name="matches"/> est simplement ignoré ; un doublon de
    /// BeatmapId dans <paramref name="matches"/> ne conserve que la
    /// première occurrence rencontrée.
    /// </summary>
    internal static IReadOnlyList<MlDatasetSample> OrderByPackSequence(
        IReadOnlyList<CalibrationPackEntry> pack,
        IEnumerable<MlDatasetSample> matches)
    {
        Dictionary<int, MlDatasetSample> byBeatmapId = [];

        foreach (MlDatasetSample sample in matches)
        {
            if (sample.BeatmapId is int beatmapId &&
                !byBeatmapId.ContainsKey(beatmapId))
            {
                byBeatmapId[beatmapId] = sample;
            }
        }

        List<MlDatasetSample> ordered = new(pack.Count);

        foreach (CalibrationPackEntry entry in pack)
        {
            if (byBeatmapId.TryGetValue(entry.BeatmapId, out MlDatasetSample? sample))
            {
                ordered.Add(sample);
            }
        }

        return ordered;
    }

    /// <summary>
    /// Premier échantillon non validé suivant <paramref name="afterSampleId"/>
    /// dans l'ordre du pack (ou le tout premier si null). Symétrique de
    /// <see cref="FindPreviousUnvalidated"/>.
    /// </summary>
    internal static MlDatasetSample? FindNextUnvalidated(
        IReadOnlyList<MlDatasetSample> orderedSamples,
        long? afterSampleId)
    {
        bool pastAnchor = afterSampleId is null;

        foreach (MlDatasetSample sample in orderedSamples)
        {
            if (!pastAnchor)
            {
                if (sample.SampleId == afterSampleId)
                {
                    pastAnchor = true;
                }

                continue;
            }

            if (!sample.HumanValidated)
            {
                return sample;
            }
        }

        return null;
    }

    /// <summary>
    /// Dernier échantillon non validé précédant
    /// <paramref name="beforeSampleId"/> dans l'ordre du pack (ou le
    /// tout dernier si null).
    /// </summary>
    internal static MlDatasetSample? FindPreviousUnvalidated(
        IReadOnlyList<MlDatasetSample> orderedSamples,
        long? beforeSampleId)
    {
        bool pastAnchor = beforeSampleId is null;

        for (int i = orderedSamples.Count - 1; i >= 0; i--)
        {
            MlDatasetSample sample = orderedSamples[i];

            if (!pastAnchor)
            {
                if (sample.SampleId == beforeSampleId)
                {
                    pastAnchor = true;
                }

                continue;
            }

            if (!sample.HumanValidated)
            {
                return sample;
            }
        }

        return null;
    }
}
