using BeatInsight.Models.Persistence;
using BeatInsight.Services.Ml;
using BeatInsight.Services.Persistence;
using System.Globalization;

bool techOnly = args.Contains("--tech", StringComparer.OrdinalIgnoreCase);
string? suppliedDatabasePath = args.FirstOrDefault(argument =>
    !string.Equals(argument, "--tech", StringComparison.OrdinalIgnoreCase));
string databasePath = suppliedDatabasePath is not null
    ? Path.GetFullPath(suppliedDatabasePath)
    : MlDatasetSampleRepository.DefaultDatabasePath;

try
{
    MlDatasetSampleRepository repository = new(databasePath);

    if (techOnly)
    {
        PrintTechReport(HumanTechBaseline.TrainAndEvaluate(repository));
    }
    else
    {
        PrintReport(HumanStructuralBaseline.TrainAndEvaluate(repository));
    }
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Human-only baseline failed: {exception.Message}");
    return 1;
}

static void PrintReport(HumanStructuralBaselineReport report)
{
    Console.WriteLine("BEATINSIGHT HUMAN-ONLY STRUCTURAL BASELINE");
    Console.WriteLine($"Samples included: {report.IncludedSampleCount}");
    Console.WriteLine($"Samples excluded: {report.ExcludedSampleCount}");
    Console.WriteLine($"Model: {report.ModelSettings}");
    Console.WriteLine();

    Console.WriteLine("SPLITS");

    foreach ((StructuralSplit split, IReadOnlyList<HumanStructuralTrainingSample> samples)
        in report.SamplesBySplit.OrderBy(pair => pair.Key))
    {
        Console.WriteLine($"{split}: {samples.Count}");

        foreach (MlHumanLabel label in StructuralMlLabels.All)
        {
            Console.WriteLine($"  {StructuralMlLabels.Display(label)}: " +
                samples.Count(sample => sample.Label == label));
        }
    }

    PrintEvaluation("VALIDATION", report.Validation);
    PrintEvaluation("TEST", report.Test);
}

static void PrintEvaluation(string name, StructuralMlEvaluation evaluation)
{
    Console.WriteLine();
    Console.WriteLine(name);
    Console.WriteLine($"Accuracy: {Percent(evaluation.Accuracy)}");
    Console.WriteLine($"Macro F1: {Percent(evaluation.MacroF1)}");

    foreach (MlHumanLabel label in StructuralMlLabels.All)
    {
        Console.WriteLine($"F1 {StructuralMlLabels.Display(label)}: " +
            Percent(evaluation.F1ByClass[label]));
    }

    Console.WriteLine("Confusion matrix (actual rows, predicted columns)");
    Console.WriteLine("Actual\\Predicted | " + string.Join(" | ",
        StructuralMlLabels.All.Select(StructuralMlLabels.Display)));

    foreach (MlHumanLabel actual in StructuralMlLabels.All)
    {
        Console.WriteLine($"{StructuralMlLabels.Display(actual),-16} | " +
            string.Join(" | ", StructuralMlLabels.All.Select(predicted =>
                evaluation.ConfusionMatrix[actual][predicted].ToString(
                    CultureInfo.InvariantCulture))));
    }
}

static string Percent(double value) =>
    value.ToString("P1", CultureInfo.InvariantCulture);

static void PrintTechReport(HumanTechBaselineReport report)
{
    Console.WriteLine("BEATINSIGHT HUMAN-ONLY TECH / NON-TECH BASELINE");
    Console.WriteLine($"Samples included: {report.IncludedSampleCount}");
    Console.WriteLine($"Samples excluded: {report.ExcludedSampleCount}");
    Console.WriteLine($"Model: {report.ModelSettings}");
    Console.WriteLine();
    Console.WriteLine("SPLITS");

    foreach ((StructuralSplit split, IReadOnlyList<HumanTechTrainingSample> samples)
        in report.SamplesBySplit.OrderBy(pair => pair.Key))
    {
        Console.WriteLine($"{split}: {samples.Count}");
        Console.WriteLine($"  Tech: {samples.Count(sample => sample.IsTech)}");
        Console.WriteLine($"  Non-Tech: {samples.Count(sample => !sample.IsTech)}");
    }

    PrintTechEvaluation("VALIDATION", report.Validation);
    PrintTechEvaluation("TEST", report.Test);
}

static void PrintTechEvaluation(string name, BinaryTechEvaluation evaluation)
{
    Console.WriteLine();
    Console.WriteLine(name);
    Console.WriteLine($"Mean P(Tech) for actual Tech: " +
        Percent(evaluation.MeanProbabilityForActualTech));
    Console.WriteLine($"Mean P(Tech) for actual Non-Tech: " +
        Percent(evaluation.MeanProbabilityForActualNonTech));
    PrintTechMetrics(evaluation.CalculateMetrics(0.50));
    Console.WriteLine("Threshold sweep");

    foreach (double threshold in HumanTechBaseline.EvaluationThresholds)
    {
        BinaryTechMetrics metrics = evaluation.CalculateMetrics(threshold);
        Console.WriteLine($"  {threshold:F2}: Precision {Percent(metrics.TechPrecision)} | " +
            $"Recall {Percent(metrics.TechRecall)} | F1 {Percent(metrics.TechF1)}");
    }
}

static void PrintTechMetrics(BinaryTechMetrics metrics)
{
    Console.WriteLine($"Threshold: {metrics.Threshold:F2}");
    Console.WriteLine($"Accuracy: {Percent(metrics.Accuracy)}");
    Console.WriteLine($"Tech precision: {Percent(metrics.TechPrecision)}");
    Console.WriteLine($"Tech recall: {Percent(metrics.TechRecall)}");
    Console.WriteLine($"Tech F1: {Percent(metrics.TechF1)}");
    Console.WriteLine($"Non-Tech specificity: {Percent(metrics.NonTechSpecificity)}");
    Console.WriteLine($"Confusion matrix: TP {metrics.TruePositive} | " +
        $"FP {metrics.FalsePositive} | TN {metrics.TrueNegative} | " +
        $"FN {metrics.FalseNegative}");
}
