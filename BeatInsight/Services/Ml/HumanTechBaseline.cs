using BeatInsight.Models.Persistence;
using BeatInsight.Services.Persistence;

namespace BeatInsight.Services.Ml;

/// <summary>
/// Baseline binaire Human-only destiné à estimer la probabilité qu'une map
/// soit réellement Tech. Les autres classes humaines sont regroupées dans
/// Non-Tech. Cette expérience ne produit ni écriture SQLite, ni prédiction
/// consommée par l'application.
/// </summary>
internal static class HumanTechBaseline
{
    internal static readonly double[] EvaluationThresholds =
    [
        0.50,
        0.60,
        0.70,
        0.80,
    ];

    internal static HumanTechBaselineReport TrainAndEvaluate(
        IEnumerable<MlDatasetSample> samples,
        Func<MlDatasetSample, string>? groupKeyResolver = null)
    {
        HumanStructuralDataset structuralDataset = HumanStructuralDataset.Create(
            samples,
            groupKeyResolver);
        HumanTechDataset dataset = HumanTechDataset.From(structuralDataset);
        GroupedTechSplit split = GroupedTechSplit.Create(
            dataset.Samples,
            HumanStructuralBaseline.Seed);
        GaussianNaiveBayesTechClassifier classifier =
            GaussianNaiveBayesTechClassifier.Train(split.Train);

        return new HumanTechBaselineReport(
            dataset.Samples.Count,
            structuralDataset.ExcludedSampleCount,
            split,
            BinaryTechEvaluation.Create(split.Validation, classifier),
            BinaryTechEvaluation.Create(split.Test, classifier),
            GaussianNaiveBayesTechClassifier.SettingsDescription);
    }

    internal static HumanTechBaselineReport TrainAndEvaluate(
        MlDatasetSampleRepository repository,
        Func<MlDatasetSample, string>? groupKeyResolver = null)
    {
        ArgumentNullException.ThrowIfNull(repository);

        return TrainAndEvaluate(repository.List(), groupKeyResolver);
    }
}

/// <summary>
/// Cible binaire dérivée exclusivement du PrimaryHumanLabel validé : Tech est
/// positif, Stream/Jump/ClassicMixed sont négatifs. CommunityEvidenceJson et
/// SecondaryHumanLabel sont volontairement absents de cette conversion.
/// </summary>
internal sealed record HumanTechTrainingSample(
    long SampleId,
    string GroupKey,
    bool IsTech,
    double[] Features);

internal sealed record HumanTechDataset(
    IReadOnlyList<HumanTechTrainingSample> Samples)
{
    internal static HumanTechDataset From(HumanStructuralDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        return new HumanTechDataset(dataset.Samples
            .Select(sample => new HumanTechTrainingSample(
                sample.SampleId,
                sample.GroupKey,
                sample.Label == MlHumanLabel.Tech,
                sample.Features))
            .ToArray());
    }
}

internal sealed record GroupedTechSplit(
    IReadOnlyList<HumanTechTrainingSample> Train,
    IReadOnlyList<HumanTechTrainingSample> Validation,
    IReadOnlyList<HumanTechTrainingSample> Test)
{
    private static readonly StructuralSplit[] Splits =
    [
        StructuralSplit.Train,
        StructuralSplit.Validation,
        StructuralSplit.Test,
    ];

    private static readonly IReadOnlyDictionary<StructuralSplit, double> Ratios =
        new Dictionary<StructuralSplit, double>
        {
            [StructuralSplit.Train] = 0.70,
            [StructuralSplit.Validation] = 0.15,
            [StructuralSplit.Test] = 0.15,
        };

    internal static GroupedTechSplit Create(
        IReadOnlyList<HumanTechTrainingSample> samples,
        int seed)
    {
        ArgumentNullException.ThrowIfNull(samples);

        if (samples.Count == 0)
        {
            throw new InvalidOperationException(
                "The Tech baseline requires at least one validated Human sample.");
        }

        Dictionary<string, List<HumanTechTrainingSample>> grouped = samples
            .GroupBy(sample => sample.GroupKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(sample => sample.SampleId).ToList(),
                StringComparer.Ordinal);
        int techTotal = samples.Count(sample => sample.IsTech);
        int nonTechTotal = samples.Count - techTotal;

        if (techTotal == 0 || nonTechTotal == 0)
        {
            throw new InvalidOperationException(
                "The Tech baseline requires validated Human samples from both Tech and Non-Tech.");
        }

        Dictionary<StructuralSplit, SplitAllocation> allocations = Splits
            .ToDictionary(split => split, _ => new SplitAllocation());

        foreach (KeyValuePair<string, List<HumanTechTrainingSample>> group in grouped
            .OrderByDescending(pair => pair.Value.Count)
            .ThenBy(pair => StableHash(pair.Key, seed))
            .ThenBy(pair => pair.Key, StringComparer.Ordinal))
        {
            StructuralSplit selected = Splits
                .OrderBy(split => AllocationCost(
                    allocations,
                    split,
                    group.Value,
                    samples.Count,
                    techTotal,
                    nonTechTotal))
                .ThenBy(split => split)
                .First();

            allocations[selected].Add(group.Value);
        }

        if (allocations[StructuralSplit.Train].TechCount == 0 ||
            allocations[StructuralSplit.Train].NonTechCount == 0)
        {
            throw new InvalidOperationException(
                "The grouped training split does not contain both Tech and Non-Tech samples.");
        }

        return new GroupedTechSplit(
            allocations[StructuralSplit.Train].Samples,
            allocations[StructuralSplit.Validation].Samples,
            allocations[StructuralSplit.Test].Samples);
    }

    private static double AllocationCost(
        IReadOnlyDictionary<StructuralSplit, SplitAllocation> allocations,
        StructuralSplit candidateSplit,
        IReadOnlyList<HumanTechTrainingSample> candidateGroup,
        int totalSamples,
        int totalTech,
        int totalNonTech)
    {
        int groupTech = candidateGroup.Count(sample => sample.IsTech);
        int groupNonTech = candidateGroup.Count - groupTech;
        double cost = 0;

        foreach (StructuralSplit split in Splits)
        {
            SplitAllocation allocation = allocations[split];
            int count = allocation.Samples.Count +
                (split == candidateSplit ? candidateGroup.Count : 0);
            int tech = allocation.TechCount +
                (split == candidateSplit ? groupTech : 0);
            int nonTech = allocation.NonTechCount +
                (split == candidateSplit ? groupNonTech : 0);
            double ratio = Ratios[split];

            cost += Math.Pow(
                (count - (totalSamples * ratio)) /
                Math.Max(1.0, totalSamples * ratio),
                2);
            // La classe positive étant minoritaire, les deux distributions
            // reçoivent la même importance dans l'affectation gloutonne.
            cost += 2.0 * Math.Pow(
                (tech - (totalTech * ratio)) / Math.Max(1.0, totalTech * ratio),
                2);
            cost += 2.0 * Math.Pow(
                (nonTech - (totalNonTech * ratio)) /
                Math.Max(1.0, totalNonTech * ratio),
                2);
        }

        return cost;
    }

    private static uint StableHash(string value, int seed)
    {
        uint hash = 2166136261u ^ (uint)seed;

        foreach (char character in value)
        {
            hash ^= character;
            hash *= 16777619u;
        }

        return hash;
    }

    private sealed class SplitAllocation
    {
        internal List<HumanTechTrainingSample> Samples { get; } = [];

        internal int TechCount => Samples.Count(sample => sample.IsTech);
        internal int NonTechCount => Samples.Count - TechCount;

        internal void Add(IEnumerable<HumanTechTrainingSample> samples) =>
            Samples.AddRange(samples);
    }
}

/// <summary>
/// Gaussian Naive Bayes binaire sur les MlRawFeatures. Les probabilités sont
/// dérivées des deux log-vraisemblances avec une sigmoïde numériquement stable.
/// </summary>
internal sealed class GaussianNaiveBayesTechClassifier
{
    private const double VarianceFloor = 1e-6;

    private readonly ClassParameters tech;
    private readonly ClassParameters nonTech;

    private GaussianNaiveBayesTechClassifier(
        ClassParameters tech,
        ClassParameters nonTech)
    {
        this.tech = tech;
        this.nonTech = nonTech;
    }

    internal static string SettingsDescription =>
        "Binary Gaussian Naive Bayes | numeric MlRawFeatures | variance floor 1e-6 | seed 20250907";

    internal static GaussianNaiveBayesTechClassifier Train(
        IReadOnlyList<HumanTechTrainingSample> trainingSamples)
    {
        ArgumentNullException.ThrowIfNull(trainingSamples);

        if (trainingSamples.Count == 0)
        {
            throw new InvalidOperationException("Training split is empty.");
        }

        HumanTechTrainingSample[] techSamples = trainingSamples
            .Where(sample => sample.IsTech)
            .ToArray();
        HumanTechTrainingSample[] nonTechSamples = trainingSamples
            .Where(sample => !sample.IsTech)
            .ToArray();

        if (techSamples.Length == 0 || nonTechSamples.Length == 0)
        {
            throw new InvalidOperationException(
                "Training requires both Tech and Non-Tech samples.");
        }

        return new GaussianNaiveBayesTechClassifier(
            CalculateParameters(techSamples, trainingSamples.Count),
            CalculateParameters(nonTechSamples, trainingSamples.Count));
    }

    internal double PredictTechProbability(double[] features)
    {
        ArgumentNullException.ThrowIfNull(features);

        double difference = CalculateLogLikelihood(features, tech) -
            CalculateLogLikelihood(features, nonTech);

        if (difference >= 0.0)
        {
            return 1.0 / (1.0 + Math.Exp(-difference));
        }

        double exponent = Math.Exp(difference);
        return exponent / (1.0 + exponent);
    }

    private static ClassParameters CalculateParameters(
        IReadOnlyList<HumanTechTrainingSample> samples,
        int totalSampleCount)
    {
        int featureCount = samples[0].Features.Length;
        double[] mean = new double[featureCount];

        foreach (HumanTechTrainingSample sample in samples)
        {
            ValidateFeatureLength(sample, featureCount);

            for (int index = 0; index < featureCount; index++)
            {
                mean[index] += sample.Features[index];
            }
        }

        for (int index = 0; index < featureCount; index++)
        {
            mean[index] /= samples.Count;
        }

        double[] variance = new double[featureCount];

        foreach (HumanTechTrainingSample sample in samples)
        {
            for (int index = 0; index < featureCount; index++)
            {
                double delta = sample.Features[index] - mean[index];
                variance[index] += delta * delta;
            }
        }

        for (int index = 0; index < featureCount; index++)
        {
            variance[index] = Math.Max(
                variance[index] / samples.Count,
                VarianceFloor);
        }

        return new ClassParameters(
            Math.Log((double)samples.Count / totalSampleCount),
            mean,
            variance);
    }

    private static double CalculateLogLikelihood(
        IReadOnlyList<double> features,
        ClassParameters parameters)
    {
        if (features.Count != parameters.Mean.Length)
        {
            throw new ArgumentException(
                "Feature vector length does not match the trained model.",
                nameof(features));
        }

        double score = parameters.LogPrior;

        for (int index = 0; index < features.Count; index++)
        {
            if (!double.IsFinite(features[index]))
            {
                throw new ArgumentException(
                    "Feature vectors must not contain NaN or Infinity.",
                    nameof(features));
            }

            double delta = features[index] - parameters.Mean[index];
            double variance = parameters.Variance[index];
            score += -0.5 * (Math.Log(2.0 * Math.PI * variance) +
                ((delta * delta) / variance));
        }

        return score;
    }

    private static void ValidateFeatureLength(
        HumanTechTrainingSample sample,
        int expectedLength)
    {
        if (sample.Features.Length != expectedLength)
        {
            throw new ArgumentException(
                "All training samples must use the same ML feature schema.",
                nameof(sample));
        }
    }

    private sealed record ClassParameters(
        double LogPrior,
        double[] Mean,
        double[] Variance);
}

internal sealed record HumanTechPrediction(
    long SampleId,
    bool IsActuallyTech,
    double TechProbability);

internal sealed record BinaryTechMetrics(
    int SampleCount,
    double Threshold,
    double Accuracy,
    double TechPrecision,
    double TechRecall,
    double TechF1,
    double NonTechSpecificity,
    int TruePositive,
    int FalsePositive,
    int TrueNegative,
    int FalseNegative);

internal sealed record BinaryTechEvaluation(
    IReadOnlyList<HumanTechPrediction> Predictions)
{
    internal static BinaryTechEvaluation Create(
        IReadOnlyList<HumanTechTrainingSample> samples,
        GaussianNaiveBayesTechClassifier classifier)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(classifier);

        return new BinaryTechEvaluation(samples
            .Select(sample => new HumanTechPrediction(
                sample.SampleId,
                sample.IsTech,
                classifier.PredictTechProbability(sample.Features)))
            .ToArray());
    }

    internal BinaryTechMetrics CalculateMetrics(double threshold)
    {
        if (threshold is < 0.0 or > 1.0 || !double.IsFinite(threshold))
        {
            throw new ArgumentOutOfRangeException(
                nameof(threshold),
                "A binary Tech threshold must be finite and between 0 and 1.");
        }

        int truePositive = 0;
        int falsePositive = 0;
        int trueNegative = 0;
        int falseNegative = 0;

        foreach (HumanTechPrediction prediction in Predictions)
        {
            bool predictedTech = prediction.TechProbability >= threshold;

            if (prediction.IsActuallyTech && predictedTech)
            {
                truePositive++;
            }
            else if (!prediction.IsActuallyTech && predictedTech)
            {
                falsePositive++;
            }
            else if (!prediction.IsActuallyTech)
            {
                trueNegative++;
            }
            else
            {
                falseNegative++;
            }
        }

        int count = Predictions.Count;
        double precision = SafeDivide(truePositive, truePositive + falsePositive);
        double recall = SafeDivide(truePositive, truePositive + falseNegative);
        double f1 = SafeDivide(2.0 * precision * recall, precision + recall);

        return new BinaryTechMetrics(
            count,
            threshold,
            SafeDivide(truePositive + trueNegative, count),
            precision,
            recall,
            f1,
            SafeDivide(trueNegative, trueNegative + falsePositive),
            truePositive,
            falsePositive,
            trueNegative,
            falseNegative);
    }

    internal double MeanProbabilityForActualTech => MeanProbability(true);
    internal double MeanProbabilityForActualNonTech => MeanProbability(false);

    private double MeanProbability(bool actualTech)
    {
        double[] probabilities = Predictions
            .Where(prediction => prediction.IsActuallyTech == actualTech)
            .Select(prediction => prediction.TechProbability)
            .ToArray();

        return probabilities.Length == 0 ? 0.0 : probabilities.Average();
    }

    private static double SafeDivide(double numerator, double denominator) =>
        denominator <= 0.0 ? 0.0 : numerator / denominator;
}

internal sealed record HumanTechBaselineReport(
    int IncludedSampleCount,
    int ExcludedSampleCount,
    GroupedTechSplit Split,
    BinaryTechEvaluation Validation,
    BinaryTechEvaluation Test,
    string ModelSettings)
{
    internal IReadOnlyDictionary<StructuralSplit, IReadOnlyList<HumanTechTrainingSample>>
        SamplesBySplit =>
        new Dictionary<StructuralSplit, IReadOnlyList<HumanTechTrainingSample>>
        {
            [StructuralSplit.Train] = Split.Train,
            [StructuralSplit.Validation] = Split.Validation,
            [StructuralSplit.Test] = Split.Test,
        };
}
