using BeatInsight.Services;

namespace BeatInsight.Tests.Services;

public sealed class CalibrationPackTests
{
    [Fact]
    public void Pack1_HasExactlyOneHundredEntries()
    {
        Assert.Equal(100, CalibrationPack.Pack1.Count);
    }

    [Fact]
    public void Pack1_HasNoDuplicateBeatmapIds()
    {
        int distinctCount = CalibrationPack.Pack1
            .Select(entry => entry.BeatmapId)
            .Distinct()
            .Count();

        Assert.Equal(CalibrationPack.Pack1.Count, distinctCount);
    }

    [Fact]
    public void Pack1_OrderIsDeterministicAcrossCalls()
    {
        List<int> first = CalibrationPack.Pack1.Select(e => e.BeatmapId).ToList();
        List<int> second = CalibrationPack.Pack1.Select(e => e.BeatmapId).ToList();

        Assert.Equal(first, second);
    }

    [Fact]
    public void Pack1_GroupsEntriesByBucketInOrder()
    {
        // L'ordre du pack (Aim, Stream, Alternate, Reading, TechControl)
        // doit rester contigu par bucket : c'est la source de vérité de
        // l'ordre déterministe de la Calibration Queue.
        List<CalibrationPackBucket> seenBuckets = [];

        foreach (CalibrationPackEntry entry in CalibrationPack.Pack1)
        {
            if (seenBuckets.Count == 0 || seenBuckets[^1] != entry.Bucket)
            {
                Assert.DoesNotContain(entry.Bucket, seenBuckets);
                seenBuckets.Add(entry.Bucket);
            }
        }

        Assert.Equal(
            [
                CalibrationPackBucket.Aim,
                CalibrationPackBucket.Stream,
                CalibrationPackBucket.Alternate,
                CalibrationPackBucket.Reading,
                CalibrationPackBucket.TechControl,
            ],
            seenBuckets);
    }
}
