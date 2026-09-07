using BeatInsight.Models.Ml;
using BeatInsight.Models.Persistence;
using BeatInsight.Services.Persistence;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace BeatInsight.Services.Ml;

/// <summary>
/// Pipeline expérimental Human-only pour le premier baseline structurel.
/// Il lit des features déjà extraites et des labels explicitement validés,
/// sans écrire dans SQLite, sans appeler Community et sans toucher à
/// GameplayIdentity.
/// </summary>
internal static class HumanStructuralBaseline
{
    internal const int Seed = 20250907;

    internal static HumanStructuralBaselineReport TrainAndEvaluate(
        IEnumerable<MlDatasetSample> samples,
        Func<MlDatasetSample, string>? groupKeyResolver = null)
    {
        HumanStructuralDataset dataset = HumanStructuralDataset.Create(
            samples,
            groupKeyResolver);

        GroupedStructuralSplit split = GroupedStructuralSplit.Create(
            dataset.Samples,
            Seed);

        GaussianNaiveBayesStructuralClassifier model =
            GaussianNaiveBayesStructuralClassifier.Train(split.Train);

        return new HumanStructuralBaselineReport(
            dataset.Samples.Count,
            dataset.ExcludedSampleCount,
            split,
            StructuralMlEvaluation.Calculate(split.Validation, model),
            StructuralMlEvaluation.Calculate(split.Test, model),
            GaussianNaiveBayesStructuralClassifier.SettingsDescription);
    }

    internal static HumanStructuralBaselineReport TrainAndEvaluate(
        MlDatasetSampleRepository repository,
        Func<MlDatasetSample, string>? groupKeyResolver = null)
    {
        ArgumentNullException.ThrowIfNull(repository);

        return TrainAndEvaluate(repository.List(), groupKeyResolver);
    }
}

/// <summary>
/// Echantillon d'entraînement issu exclusivement d'un label humain primaire
/// validé. Les annotations Community ne sont jamais lues par ce type.
/// </summary>
internal sealed record HumanStructuralTrainingSample(
    long SampleId,
    string GroupKey,
    MlHumanLabel Label,
    double[] Features);

internal sealed record HumanStructuralDataset(
    IReadOnlyList<HumanStructuralTrainingSample> Samples,
    int ExcludedSampleCount)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    internal static HumanStructuralDataset Create(
        IEnumerable<MlDatasetSample> source,
        Func<MlDatasetSample, string>? groupKeyResolver = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        Func<MlDatasetSample, string> resolveGroup = groupKeyResolver
            ?? HumanStructuralGroupKeyResolver.Resolve;

        List<HumanStructuralTrainingSample> samples = [];
        int excluded = 0;

        foreach (MlDatasetSample sample in source.OrderBy(sample => sample.SampleId))
        {
            // La règle de sélection est volontairement stricte : aucune
            // annotation Community, secondaire ou Identity n'intervient ici.
            if (!sample.HumanValidated ||
                sample.PrimaryHumanLabel is not MlHumanLabel label ||
                !Enum.IsDefined(label) ||
                string.IsNullOrWhiteSpace(sample.RawFeaturesJson))
            {
                excluded++;
                continue;
            }

            try
            {
                MlRawFeatures? rawFeatures = JsonSerializer.Deserialize<MlRawFeatures>(
                    sample.RawFeaturesJson,
                    JsonOptions);

                if (rawFeatures is null ||
                    !MlRawFeatureVectorizer.TryCreate(rawFeatures, out double[] vector))
                {
                    excluded++;
                    continue;
                }

                string groupKey = resolveGroup(sample);

                if (string.IsNullOrWhiteSpace(groupKey))
                {
                    excluded++;
                    continue;
                }

                samples.Add(new HumanStructuralTrainingSample(
                    sample.SampleId,
                    groupKey,
                    label,
                    vector));
            }
            catch (JsonException)
            {
                excluded++;
            }
        }

        return new HumanStructuralDataset(samples, excluded);
    }
}

/// <summary>
/// Résout le groupe anti-fuite. BeatmapSetID est lu directement depuis le
/// fichier .osu lorsque disponible. Le dossier parent est un repli déterministe
/// qui garde normalement ensemble les difficultés locales d'un même set.
/// </summary>
internal static class HumanStructuralGroupKeyResolver
{
    internal static string Resolve(MlDatasetSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        int? beatmapSetId = TryReadBeatmapSetId(sample.SourceFilePath);

        if (beatmapSetId is > 0)
        {
            return $"set:{beatmapSetId.Value.ToString(CultureInfo.InvariantCulture)}";
        }

        string? parentDirectory = Path.GetDirectoryName(sample.SourceFilePath);

        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            return $"path:{Path.GetFullPath(parentDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToUpperInvariant()}";
        }

        return $"sample:{sample.SampleId.ToString(CultureInfo.InvariantCulture)}";
    }

    private static int? TryReadBeatmapSetId(string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath))
        {
            return null;
        }

        try
        {
            using StreamReader reader = new(sourceFilePath);

            bool inMetadata = false;
            string? line;

            while ((line = reader.ReadLine()) is not null)
            {
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    if (inMetadata)
                    {
                        break;
                    }

                    inMetadata = string.Equals(
                        line.Trim('[', ']'),
                        "Metadata",
                        StringComparison.Ordinal);
                    continue;
                }

                if (!inMetadata ||
                    !line.StartsWith("BeatmapSetID:", StringComparison.Ordinal))
                {
                    continue;
                }

                return int.TryParse(
                    line["BeatmapSetID:".Length..].Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int beatmapSetId) && beatmapSetId > 0
                    ? beatmapSetId
                    : null;
            }
        }
        catch (IOException)
        {
            // Le repli dossier reste valable si le fichier a été déplacé.
        }
        catch (UnauthorizedAccessException)
        {
            // Le repli dossier reste valable si le fichier est inaccessible.
        }

        return null;
    }
}

internal static class MlRawFeatureVectorizer
{
    private static readonly PropertyInfo[] NumericProperties = typeof(MlRawFeatures)
        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(property => property.PropertyType == typeof(int) ||
            property.PropertyType == typeof(double))
        .OrderBy(property => property.Name, StringComparer.Ordinal)
        .ToArray();

    internal static IReadOnlyList<string> FeatureNames =>
        NumericProperties.Select(property => property.Name).ToArray();

    internal static bool TryCreate(MlRawFeatures rawFeatures, out double[] vector)
    {
        ArgumentNullException.ThrowIfNull(rawFeatures);

        vector = new double[NumericProperties.Length];

        for (int index = 0; index < NumericProperties.Length; index++)
        {
            object? value = NumericProperties[index].GetValue(rawFeatures);
            double numeric = value switch
            {
                int integer => integer,
                double floating => floating,
                _ => double.NaN,
            };

            if (!double.IsFinite(numeric))
            {
                vector = [];
                return false;
            }

            vector[index] = numeric;
        }

        return true;
    }
}

internal enum StructuralSplit
{
    Train,
    Validation,
    Test,
}

internal sealed record GroupedStructuralSplit(
    IReadOnlyList<HumanStructuralTrainingSample> Train,
    IReadOnlyList<HumanStructuralTrainingSample> Validation,
    IReadOnlyList<HumanStructuralTrainingSample> Test)
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

    internal static GroupedStructuralSplit Create(
        IReadOnlyList<HumanStructuralTrainingSample> samples,
        int seed)
    {
        ArgumentNullException.ThrowIfNull(samples);

        if (samples.Count == 0)
        {
            throw new InvalidOperationException(
                "The Human-only ML baseline requires at least one validated sample.");
        }

        Dictionary<string, List<HumanStructuralTrainingSample>> grouped = samples
            .GroupBy(sample => sample.GroupKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(sample => sample.SampleId).ToList(),
                StringComparer.Ordinal);

        Dictionary<MlHumanLabel, int> totalByClass = StructuralMlLabels.All
            .ToDictionary(
                label => label,
                label => samples.Count(sample => sample.Label == label));
        Dictionary<StructuralSplit, SplitAllocation> allocations = Splits
            .ToDictionary(split => split, _ => new SplitAllocation());

        IEnumerable<KeyValuePair<string, List<HumanStructuralTrainingSample>>> orderedGroups =
            grouped
                .OrderByDescending(group => group.Value.Count)
                .ThenBy(group => StableHash(group.Key, seed))
                .ThenBy(group => group.Key, StringComparer.Ordinal);

        foreach ((string _, List<HumanStructuralTrainingSample> group) in orderedGroups)
        {
            StructuralSplit selected = Splits
                .OrderBy(split => AllocationCost(
                    allocations,
                    split,
                    group,
                    samples.Count,
                    totalByClass))
                .ThenBy(split => split)
                .First();

            allocations[selected].Add(group);
        }

        return new GroupedStructuralSplit(
            allocations[StructuralSplit.Train].Samples,
            allocations[StructuralSplit.Validation].Samples,
            allocations[StructuralSplit.Test].Samples);
    }

    private static double AllocationCost(
        IReadOnlyDictionary<StructuralSplit, SplitAllocation> allocations,
        StructuralSplit candidateSplit,
        IReadOnlyList<HumanStructuralTrainingSample> candidateGroup,
        int totalSamples,
        IReadOnlyDictionary<MlHumanLabel, int> totalByClass)
    {
        double cost = 0;

        foreach (StructuralSplit split in Splits)
        {
            SplitAllocation allocation = allocations[split];
            int count = allocation.Samples.Count +
                (split == candidateSplit ? candidateGroup.Count : 0);
            double targetCount = totalSamples * Ratios[split];
            cost += Math.Pow((count - targetCount) / Math.Max(1.0, targetCount), 2);

            foreach (MlHumanLabel label in StructuralMlLabels.All)
            {
                int labelCount = allocation.Count(label) +
                    (split == candidateSplit
                        ? candidateGroup.Count(sample => sample.Label == label)
                        : 0);
                double targetLabelCount = totalByClass[label] * Ratios[split];
                cost += 2.0 * Math.Pow(
                    (labelCount - targetLabelCount) /
                    Math.Max(1.0, targetLabelCount),
                    2);
            }
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
        internal List<HumanStructuralTrainingSample> Samples { get; } = [];

        internal void Add(IEnumerable<HumanStructuralTrainingSample> samples) =>
            Samples.AddRange(samples);

        internal int Count(MlHumanLabel label) =>
            Samples.Count(sample => sample.Label == label);
    }
}

internal sealed class GaussianNaiveBayesStructuralClassifier
{
    private const double VarianceFloor = 1e-6;

    private readonly IReadOnlyDictionary<MlHumanLabel, ClassParameters> parameters;

    private GaussianNaiveBayesStructuralClassifier(
        IReadOnlyDictionary<MlHumanLabel, ClassParameters> parameters)
    {
        this.parameters = parameters;
    }

    internal static string SettingsDescription =>
        "Gaussian Naive Bayes | numeric MlRawFeatures | variance floor 1e-6 | seed 20250907";

    internal static GaussianNaiveBayesStructuralClassifier Train(
        IReadOnlyList<HumanStructuralTrainingSample> trainingSamples)
    {
        ArgumentNullException.ThrowIfNull(trainingSamples);

        if (trainingSamples.Count == 0)
        {
            throw new InvalidOperationException("Training split is empty.");
        }

        int featureCount = trainingSamples[0].Features.Length;
        Dictionary<MlHumanLabel, ClassParameters> parameters = [];

        foreach (MlHumanLabel label in StructuralMlLabels.All)
        {
            HumanStructuralTrainingSample[] classSamples = trainingSamples
                .Where(sample => sample.Label == label)
                .ToArray();

            if (classSamples.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Training split has no validated Human label for {label}.");
            }

            double[] mean = new double[featureCount];

            foreach (HumanStructuralTrainingSample sample in classSamples)
            {
                ValidateFeatureLength(sample, featureCount);

                for (int index = 0; index < featureCount; index++)
                {
                    mean[index] += sample.Features[index];
                }
            }

            for (int index = 0; index < featureCount; index++)
            {
                mean[index] /= classSamples.Length;
            }

            double[] variance = new double[featureCount];

            foreach (HumanStructuralTrainingSample sample in classSamples)
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
                    variance[index] / classSamples.Length,
                    VarianceFloor);
            }

            parameters[label] = new ClassParameters(
                Math.Log((double)classSamples.Length / trainingSamples.Count),
                mean,
                variance);
        }

        return new GaussianNaiveBayesStructuralClassifier(parameters);
    }

    internal MlHumanLabel Predict(double[] features)
    {
        ArgumentNullException.ThrowIfNull(features);

        MlHumanLabel? bestLabel = null;
        double bestScore = double.NegativeInfinity;

        foreach (MlHumanLabel label in StructuralMlLabels.All)
        {
            ClassParameters classParameters = parameters[label];

            if (classParameters.Mean.Length != features.Length)
            {
                throw new ArgumentException(
                    "Feature vector length does not match the trained model.",
                    nameof(features));
            }

            double score = classParameters.LogPrior;

            for (int index = 0; index < features.Length; index++)
            {
                double delta = features[index] - classParameters.Mean[index];
                double variance = classParameters.Variance[index];
                score += -0.5 * (Math.Log(2.0 * Math.PI * variance) +
                    ((delta * delta) / variance));
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestLabel = label;
            }
        }

        return bestLabel ?? throw new InvalidOperationException(
            "The structural classifier has no configured classes.");
    }

    private static void ValidateFeatureLength(
        HumanStructuralTrainingSample sample,
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

internal sealed record StructuralMlEvaluation(
    int SampleCount,
    double Accuracy,
    double MacroF1,
    IReadOnlyDictionary<MlHumanLabel, double> F1ByClass,
    IReadOnlyDictionary<MlHumanLabel, IReadOnlyDictionary<MlHumanLabel, int>>
        ConfusionMatrix)
{
    internal static StructuralMlEvaluation Calculate(
        IReadOnlyList<HumanStructuralTrainingSample> samples,
        GaussianNaiveBayesStructuralClassifier classifier)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(classifier);

        Dictionary<MlHumanLabel, Dictionary<MlHumanLabel, int>> matrix =
            StructuralMlLabels.All.ToDictionary(
                actual => actual,
                _ => StructuralMlLabels.All.ToDictionary(predicted => predicted, _ => 0));
        int correct = 0;

        foreach (HumanStructuralTrainingSample sample in samples)
        {
            MlHumanLabel predicted = classifier.Predict(sample.Features);
            matrix[sample.Label][predicted]++;

            if (predicted == sample.Label)
            {
                correct++;
            }
        }

        Dictionary<MlHumanLabel, double> f1ByClass = [];

        foreach (MlHumanLabel label in StructuralMlLabels.All)
        {
            int truePositive = matrix[label][label];
            int falsePositive = StructuralMlLabels.All
                .Where(actual => actual != label)
                .Sum(actual => matrix[actual][label]);
            int falseNegative = StructuralMlLabels.All
                .Where(predicted => predicted != label)
                .Sum(predicted => matrix[label][predicted]);
            double denominator = (2.0 * truePositive) + falsePositive + falseNegative;
            f1ByClass[label] = denominator <= 0
                ? 0.0
                : (2.0 * truePositive) / denominator;
        }

        IReadOnlyDictionary<MlHumanLabel, IReadOnlyDictionary<MlHumanLabel, int>>
            readonlyMatrix = matrix.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyDictionary<MlHumanLabel, int>)pair.Value);

        return new StructuralMlEvaluation(
            samples.Count,
            samples.Count == 0 ? 0.0 : (double)correct / samples.Count,
            f1ByClass.Values.Average(),
            f1ByClass,
            readonlyMatrix);
    }
}

internal sealed record HumanStructuralBaselineReport(
    int IncludedSampleCount,
    int ExcludedSampleCount,
    GroupedStructuralSplit Split,
    StructuralMlEvaluation Validation,
    StructuralMlEvaluation Test,
    string ModelSettings)
{
    internal IReadOnlyDictionary<StructuralSplit, IReadOnlyList<HumanStructuralTrainingSample>>
        SamplesBySplit =>
        new Dictionary<StructuralSplit, IReadOnlyList<HumanStructuralTrainingSample>>
        {
            [StructuralSplit.Train] = Split.Train,
            [StructuralSplit.Validation] = Split.Validation,
            [StructuralSplit.Test] = Split.Test,
        };
}

internal static class StructuralMlLabels
{
    internal static IReadOnlyList<MlHumanLabel> All { get; } =
    [
        MlHumanLabel.Stream,
        MlHumanLabel.Jump,
        MlHumanLabel.Tech,
        MlHumanLabel.ClassicMixed,
    ];

    internal static string Display(MlHumanLabel label) => label switch
    {
        MlHumanLabel.Stream => "Stream",
        MlHumanLabel.Jump => "Jump",
        MlHumanLabel.Tech => "Tech",
        MlHumanLabel.ClassicMixed => "Classic/Mixed",
        _ => throw new ArgumentOutOfRangeException(nameof(label), label, null),
    };
}
