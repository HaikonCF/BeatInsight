using BeatInsight.Models.Persistence;
using BeatInsight.Services;

namespace BeatInsight.Tests.Services;

/// <summary>
/// Vérifie l'ordonnancement et la navigation de la Calibration Queue
/// indépendamment du repository : les échantillons sont construits à
/// la main, dans un ordre volontairement différent de l'ordre du pack.
/// </summary>
public sealed class CalibrationQueueTests
{
    private static readonly IReadOnlyList<CalibrationPackEntry> Pack =
    [
        new CalibrationPackEntry(100, CalibrationPackBucket.Aim),
        new CalibrationPackEntry(200, CalibrationPackBucket.Stream),
        new CalibrationPackEntry(300, CalibrationPackBucket.Reading),
    ];

    private static MlDatasetSample Sample(
        long sampleId,
        int beatmapId,
        bool humanValidated = false)
    {
        return new MlDatasetSample
        {
            SampleId = sampleId,
            SourceFilePath = $@"C:\Songs\map-{beatmapId}.osu",
            BeatmapId = beatmapId,
            RawFeaturesJson = "{}",
            HumanValidated = humanValidated,
        };
    }

    [Fact]
    public void OrderByPackSequence_FollowsPackOrder_NotInsertionOrder()
    {
        // Les échantillons sont fournis dans l'ordre inverse du pack.
        MlDatasetSample[] matches =
        [
            Sample(3, 300),
            Sample(2, 200),
            Sample(1, 100),
        ];

        IReadOnlyList<MlDatasetSample> ordered =
            CalibrationQueue.OrderByPackSequence(Pack, matches);

        Assert.Equal([100, 200, 300], ordered.Select(s => s.BeatmapId));
    }

    [Fact]
    public void OrderByPackSequence_IgnoresBeatmapIdsAbsentFromMatches()
    {
        MlDatasetSample[] matches = [Sample(1, 100)];

        IReadOnlyList<MlDatasetSample> ordered =
            CalibrationQueue.OrderByPackSequence(Pack, matches);

        MlDatasetSample only = Assert.Single(ordered);
        Assert.Equal(100, only.BeatmapId);
    }

    [Fact]
    public void OrderByPackSequence_KeepsFirstOccurrenceOnDuplicateBeatmapId()
    {
        MlDatasetSample[] matches =
        [
            Sample(1, 100),
            Sample(2, 100),
        ];

        IReadOnlyList<MlDatasetSample> ordered =
            CalibrationQueue.OrderByPackSequence(Pack, matches);

        MlDatasetSample only = Assert.Single(ordered);
        Assert.Equal(1, only.SampleId);
    }

    [Fact]
    public void FindNextUnvalidated_SkipsValidatedSamples()
    {
        IReadOnlyList<MlDatasetSample> ordered =
        [
            Sample(1, 100, humanValidated: true),
            Sample(2, 200, humanValidated: false),
            Sample(3, 300, humanValidated: false),
        ];

        MlDatasetSample? first = CalibrationQueue.FindNextUnvalidated(ordered, null);
        Assert.Equal(200, first?.BeatmapId);

        MlDatasetSample? second = CalibrationQueue.FindNextUnvalidated(
            ordered,
            first!.SampleId);
        Assert.Equal(300, second?.BeatmapId);

        Assert.Null(CalibrationQueue.FindNextUnvalidated(ordered, second!.SampleId));
    }

    [Fact]
    public void FindPreviousUnvalidated_Works()
    {
        IReadOnlyList<MlDatasetSample> ordered =
        [
            Sample(1, 100, humanValidated: false),
            Sample(2, 200, humanValidated: true),
            Sample(3, 300, humanValidated: false),
        ];

        MlDatasetSample? last = CalibrationQueue.FindPreviousUnvalidated(ordered, null);
        Assert.Equal(300, last?.BeatmapId);

        MlDatasetSample? first = CalibrationQueue.FindPreviousUnvalidated(
            ordered,
            last!.SampleId);
        Assert.Equal(100, first?.BeatmapId);

        Assert.Null(CalibrationQueue.FindPreviousUnvalidated(ordered, first!.SampleId));
    }

    [Fact]
    public void FindNextUnvalidated_EmptyQueue_ReturnsNull()
    {
        Assert.Null(CalibrationQueue.FindNextUnvalidated([], null));
    }

    [Fact]
    public void FindNextUnvalidated_AllValidated_ReturnsNull()
    {
        IReadOnlyList<MlDatasetSample> ordered =
        [
            Sample(1, 100, humanValidated: true),
            Sample(2, 200, humanValidated: true),
        ];

        Assert.Null(CalibrationQueue.FindNextUnvalidated(ordered, null));
    }

    [Fact]
    public void Skip_DoesNotAlterSample()
    {
        MlDatasetSample sample = Sample(1, 100, humanValidated: false);
        IReadOnlyList<MlDatasetSample> ordered = [sample, Sample(2, 200)];

        // "Skip" == lecture seule : appeler FindNextUnvalidated ne doit
        // produire aucun effet de bord sur les instances renvoyées.
        CalibrationQueue.FindNextUnvalidated(ordered, null);

        Assert.False(sample.HumanValidated);
        Assert.Null(sample.PrimaryHumanLabel);
        Assert.Null(sample.SecondaryHumanLabel);
    }

    [Fact]
    public void PackBucket_IsNeverExposedAsHumanLabel()
    {
        // CalibrationPackEntry.Bucket n'a pas de relation de type avec
        // MlHumanLabel : ce test documente ce découplage volontaire.
        Assert.IsNotType<MlHumanLabel>(Pack[0].Bucket);
    }
}
