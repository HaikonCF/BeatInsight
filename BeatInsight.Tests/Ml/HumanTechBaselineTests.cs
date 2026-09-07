using BeatInsight.Models.Ml;
using BeatInsight.Models.Persistence;
using BeatInsight.Services.Ml;
using System.Text.Json;

namespace BeatInsight.Tests.Ml;

public sealed class HumanTechBaselineTests
{
    [Fact]
    public void Dataset_UsesOnlyValidatedHumanLabels_MapsTechAndIgnoresCommunity()
    {
        MlDatasetSample tech = Sample(
            sampleId: 1,
            label: MlHumanLabel.Tech,
            validated: true,
            featureValue: 10,
            communityEvidence: "{\"label\":\"Stream\"}");
        MlDatasetSample stream = Sample(
            sampleId: 2,
            label: MlHumanLabel.Stream,
            validated: true,
            featureValue: 20,
            communityEvidence: "{\"label\":\"Tech\"}");
        MlDatasetSample jump = Sample(3, MlHumanLabel.Jump, true, 30);
        MlDatasetSample classic = Sample(4, MlHumanLabel.ClassicMixed, true, 40);
        MlDatasetSample unvalidated = Sample(5, MlHumanLabel.Tech, false, 50);
        MlDatasetSample noLabel = Sample(6, null, true, 60);

        HumanStructuralDataset structural = HumanStructuralDataset.Create(
            [tech, stream, jump, classic, unvalidated, noLabel],
            sample => $"set:{sample.SampleId}");
        HumanTechDataset dataset = HumanTechDataset.From(structural);

        Assert.Equal(4, dataset.Samples.Count);
        Assert.Single(dataset.Samples.Where(sample => sample.IsTech));
        Assert.Equal(3, dataset.Samples.Count(sample => !sample.IsTech));
        Assert.Equal(2, structural.ExcludedSampleCount);
    }

    [Fact]
    public void GroupedSplit_IsDeterministicAndNeverLeaksBeatmapSetGroups()
    {
        HumanTechTrainingSample[] samples = CreateBalancedSamples(
            groupsPerClass: 12,
            samplesPerGroup: 2);

        GroupedTechSplit first = GroupedTechSplit.Create(
            samples,
            HumanStructuralBaseline.Seed);
        GroupedTechSplit second = GroupedTechSplit.Create(
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

        HashSet<string> trainGroups = first.Train
            .Select(sample => sample.GroupKey)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> validationGroups = first.Validation
            .Select(sample => sample.GroupKey)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> testGroups = first.Test
            .Select(sample => sample.GroupKey)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Empty(trainGroups.Intersect(validationGroups));
        Assert.Empty(trainGroups.Intersect(testGroups));
        Assert.Empty(validationGroups.Intersect(testGroups));
    }

    [Fact]
    public void Classifier_ProducesDeterministicFiniteTechProbabilities()
    {
        HumanTechTrainingSample[] training =
        [
            TrainingSample(1, true, 10),
            TrainingSample(2, true, 11),
            TrainingSample(3, false, 0),
            TrainingSample(4, false, 1),
        ];

        GaussianNaiveBayesTechClassifier classifier =
            GaussianNaiveBayesTechClassifier.Train(training);

        double first = classifier.PredictTechProbability([10.5]);
        double second = classifier.PredictTechProbability([10.5]);

        Assert.Equal(first, second, precision: 12);
        Assert.InRange(first, 0.0, 1.0);
        Assert.True(first > 0.5);
    }

    [Fact]
    public void Evaluation_CalculatesBinaryConfusionMetricsAtDefaultThreshold()
    {
        BinaryTechEvaluation evaluation = new(
        [
            new HumanTechPrediction(1, true, 0.90),
            new HumanTechPrediction(2, true, 0.40),
            new HumanTechPrediction(3, false, 0.70),
            new HumanTechPrediction(4, false, 0.10),
            new HumanTechPrediction(5, false, 0.20),
        ]);

        BinaryTechMetrics metrics = evaluation.CalculateMetrics(0.50);

        Assert.Equal(1, metrics.TruePositive);
        Assert.Equal(1, metrics.FalsePositive);
        Assert.Equal(2, metrics.TrueNegative);
        Assert.Equal(1, metrics.FalseNegative);
        Assert.Equal(0.60, metrics.Accuracy, precision: 8);
        Assert.Equal(0.50, metrics.TechPrecision, precision: 8);
        Assert.Equal(0.50, metrics.TechRecall, precision: 8);
        Assert.Equal(0.50, metrics.TechF1, precision: 8);
        Assert.Equal(2.0 / 3.0, metrics.NonTechSpecificity, precision: 8);
    }

    [Fact]
    public void Evaluation_ThresholdSweepChangesTechMetricsDeterministically()
    {
        BinaryTechEvaluation evaluation = new(
        [
            new HumanTechPrediction(1, true, 0.90),
            new HumanTechPrediction(2, true, 0.65),
            new HumanTechPrediction(3, false, 0.75),
            new HumanTechPrediction(4, false, 0.40),
        ]);

        BinaryTechMetrics at050 = evaluation.CalculateMetrics(0.50);
        BinaryTechMetrics at070 = evaluation.CalculateMetrics(0.70);

        Assert.Equal(2, at050.TruePositive);
        Assert.Equal(1, at050.FalsePositive);
        Assert.Equal(1.0, at050.TechRecall, precision: 8);
        Assert.Equal(0.80, at050.TechF1, precision: 8);

        Assert.Equal(1, at070.TruePositive);
        Assert.Equal(1, at070.FalsePositive);
        Assert.Equal(0.50, at070.TechRecall, precision: 8);
        Assert.Equal(0.50, at070.TechF1, precision: 8);

        Assert.Equal(
            [0.50, 0.60, 0.70, 0.80],
            HumanTechBaseline.EvaluationThresholds);
    }

    private static HumanTechTrainingSample[] CreateBalancedSamples(
        int groupsPerClass,
        int samplesPerGroup)
    {
        List<HumanTechTrainingSample> samples = [];
        long sampleId = 1;

        for (int isTech = 0; isTech <= 1; isTech++)
        {
            for (int groupIndex = 0; groupIndex < groupsPerClass; groupIndex++)
            {
                for (int sampleIndex = 0; sampleIndex < samplesPerGroup; sampleIndex++)
                {
                    samples.Add(TrainingSample(
                        sampleId++,
                        isTech == 1,
                        (isTech * 100) + groupIndex + (sampleIndex * 0.01),
                        $"set:{isTech}-{groupIndex}"));
                }
            }
        }

        return samples.ToArray();
    }

    private static HumanTechTrainingSample TrainingSample(
        long sampleId,
        bool isTech,
        double value,
        string? groupKey = null) =>
        new(sampleId, groupKey ?? $"set:{sampleId}", isTech, [value]);

    private static MlDatasetSample Sample(
        long sampleId,
        MlHumanLabel? label,
        bool validated,
        double featureValue,
        string? communityEvidence = null) =>
        new()
        {
            SampleId = sampleId,
            SourceFilePath = $"C:\\dataset\\{sampleId}.osu",
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
