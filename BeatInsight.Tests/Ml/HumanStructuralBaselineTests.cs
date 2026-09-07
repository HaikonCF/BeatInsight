using BeatInsight.Models.Ml;
using BeatInsight.Models.Persistence;
using BeatInsight.Services.Ml;
using System.IO;
using System.Text.Json;

namespace BeatInsight.Tests.Ml;

public sealed class HumanStructuralBaselineTests
{
    [Fact]
    public void Dataset_UsesOnlyValidatedPrimaryHumanLabels_AndIgnoresCommunity()
    {
        MlDatasetSample included = Sample(
            sampleId: 1,
            label: MlHumanLabel.Stream,
            validated: true,
            featureValue: 1,
            communityEvidence: "{\"label\":\"Tech\"}");
        MlDatasetSample unvalidated = Sample(
            sampleId: 2,
            label: MlHumanLabel.Tech,
            validated: false,
            featureValue: 2,
            communityEvidence: "{\"label\":\"Jump\"}");
        MlDatasetSample noPrimaryLabel = Sample(
            sampleId: 3,
            label: null,
            validated: true,
            featureValue: 3,
            communityEvidence: "{\"label\":\"Classic/Mixed\"}");

        HumanStructuralDataset dataset = HumanStructuralDataset.Create(
            [included, unvalidated, noPrimaryLabel],
            _ => "set:1");

        HumanStructuralTrainingSample result = Assert.Single(dataset.Samples);
        Assert.Equal(MlHumanLabel.Stream, result.Label);
        Assert.Equal(2, dataset.ExcludedSampleCount);
    }

    [Fact]
    public void GroupedSplit_NeverLeaksAGroupAcrossSplits()
    {
        HumanStructuralTrainingSample[] samples = CreateBalancedSamples(
            groupsPerClass: 8,
            samplesPerGroup: 2);

        GroupedStructuralSplit split = GroupedStructuralSplit.Create(
            samples,
            HumanStructuralBaseline.Seed);

        HashSet<string> trainGroups = split.Train
            .Select(sample => sample.GroupKey)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> validationGroups = split.Validation
            .Select(sample => sample.GroupKey)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> testGroups = split.Test
            .Select(sample => sample.GroupKey)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Empty(trainGroups.Intersect(validationGroups));
        Assert.Empty(trainGroups.Intersect(testGroups));
        Assert.Empty(validationGroups.Intersect(testGroups));
    }

    [Fact]
    public void GroupedSplit_IsDeterministicForTheFixedSeed()
    {
        HumanStructuralTrainingSample[] samples = CreateBalancedSamples(
            groupsPerClass: 8,
            samplesPerGroup: 1);

        GroupedStructuralSplit first = GroupedStructuralSplit.Create(
            samples,
            HumanStructuralBaseline.Seed);
        GroupedStructuralSplit second = GroupedStructuralSplit.Create(
            samples,
            HumanStructuralBaseline.Seed);

        Assert.Equal(
            first.Train.Select(sample => sample.SampleId),
            second.Train.Select(sample => sample.SampleId));
        Assert.Equal(
            first.Validation.Select(sample => sample.SampleId),
            second.Validation.Select(sample => sample.SampleId));
        Assert.Equal(
            first.Test.Select(sample => sample.SampleId),
            second.Test.Select(sample => sample.SampleId));
    }

    [Fact]
    public void Baseline_HandlesAllFourHumanClasses()
    {
        MlDatasetSample[] samples = CreateBalancedSamples(
            groupsPerClass: 12,
            samplesPerGroup: 1)
            .Select(sample => Sample(
                sample.SampleId,
                sample.Label,
                validated: true,
                sample.Features[0],
                sourcePath: sample.GroupKey + ".osu"))
            .ToArray();

        HumanStructuralBaselineReport report = HumanStructuralBaseline.TrainAndEvaluate(
            samples,
            sample => sample.SourceFilePath);

        Assert.Equal(48, report.IncludedSampleCount);
        Assert.Equal(0, report.ExcludedSampleCount);

        foreach (MlHumanLabel label in StructuralMlLabels.All)
        {
            Assert.Contains(report.Split.Train, sample => sample.Label == label);
            Assert.Contains(label, report.Validation.F1ByClass.Keys);
            Assert.Contains(label, report.Test.F1ByClass.Keys);
        }
    }

    [Fact]
    public void Evaluation_CalculatesAccuracyMacroF1PerClassAndConfusionMatrix()
    {
        HumanStructuralTrainingSample[] training =
        [
            TrainingSample(1, MlHumanLabel.Stream, 0),
            TrainingSample(2, MlHumanLabel.Jump, 10),
            TrainingSample(3, MlHumanLabel.Tech, 20),
            TrainingSample(4, MlHumanLabel.ClassicMixed, 30),
        ];
        GaussianNaiveBayesStructuralClassifier classifier =
            GaussianNaiveBayesStructuralClassifier.Train(training);
        HumanStructuralTrainingSample[] evaluated =
        [
            TrainingSample(11, MlHumanLabel.Stream, 0),
            TrainingSample(12, MlHumanLabel.Jump, 20),
            TrainingSample(13, MlHumanLabel.Tech, 20),
            TrainingSample(14, MlHumanLabel.ClassicMixed, 30),
        ];

        StructuralMlEvaluation evaluation = StructuralMlEvaluation.Calculate(
            evaluated,
            classifier);

        Assert.Equal(0.75, evaluation.Accuracy, precision: 8);
        Assert.Equal(2.0 / 3.0, evaluation.MacroF1, precision: 8);
        Assert.Equal(1.0, evaluation.F1ByClass[MlHumanLabel.Stream], precision: 8);
        Assert.Equal(0.0, evaluation.F1ByClass[MlHumanLabel.Jump], precision: 8);
        Assert.Equal(2.0 / 3.0, evaluation.F1ByClass[MlHumanLabel.Tech], precision: 8);
        Assert.Equal(1.0, evaluation.F1ByClass[MlHumanLabel.ClassicMixed], precision: 8);
        Assert.Equal(1, evaluation.ConfusionMatrix[MlHumanLabel.Jump][MlHumanLabel.Tech]);
    }

    [Fact]
    public void GroupKeyResolver_UsesBeatmapSetIdBeforeDirectoryFallback()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "beatinsight-group-key-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(directory);
            string filePath = Path.Combine(directory, "map.osu");
            File.WriteAllText(filePath, """
                osu file format v14

                [Metadata]
                BeatmapSetID:2468
                """);

            string key = HumanStructuralGroupKeyResolver.Resolve(Sample(
                sampleId: 99,
                label: MlHumanLabel.Tech,
                validated: true,
                featureValue: 0,
                sourcePath: filePath));

            Assert.Equal("set:2468", key);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static HumanStructuralTrainingSample[] CreateBalancedSamples(
        int groupsPerClass,
        int samplesPerGroup)
    {
        List<HumanStructuralTrainingSample> samples = [];
        long sampleId = 1;

        for (int labelIndex = 0; labelIndex < StructuralMlLabels.All.Count; labelIndex++)
        {
            MlHumanLabel label = StructuralMlLabels.All[labelIndex];

            for (int groupIndex = 0; groupIndex < groupsPerClass; groupIndex++)
            {
                for (int sampleIndex = 0; sampleIndex < samplesPerGroup; sampleIndex++)
                {
                    samples.Add(TrainingSample(
                        sampleId++,
                        label,
                        (labelIndex * 100) + groupIndex + (sampleIndex * 0.01),
                        $"set:{labelIndex}-{groupIndex}"));
                }
            }
        }

        return samples.ToArray();
    }

    private static HumanStructuralTrainingSample TrainingSample(
        long sampleId,
        MlHumanLabel label,
        double value,
        string? groupKey = null) =>
        new(sampleId, groupKey ?? $"set:{sampleId}", label, [value]);

    private static MlDatasetSample Sample(
        long sampleId,
        MlHumanLabel? label,
        bool validated,
        double featureValue,
        string? communityEvidence = null,
        string? sourcePath = null) =>
        new()
        {
            SampleId = sampleId,
            SourceFilePath = sourcePath ?? $"C:\\dataset\\{sampleId}.osu",
            FileSize = 1,
            FileLastWriteUtc = DateTime.UtcNow,
            FeatureSchemaVersion = 1,
            AnalyzerVersion = 1,
            CapturedAtUtc = DateTime.UtcNow,
            RawFeaturesJson = JsonSerializer.Serialize(new MlRawFeatures
            {
                StreamRatio = featureValue,
            }),
            PrimaryHumanLabel = label,
            HumanValidated = validated,
            CommunityEvidenceJson = communityEvidence,
        };
}
