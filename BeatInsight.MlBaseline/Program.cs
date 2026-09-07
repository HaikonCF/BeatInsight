using BeatInsight.Models.Persistence;
using BeatInsight.Services.Ml;
using BeatInsight.Services.Persistence;
using System.Globalization;

string databasePath = args.Length > 0
    ? Path.GetFullPath(args[0])
    : MlDatasetSampleRepository.DefaultDatabasePath;

try
{
    HumanStructuralBaselineReport report = HumanStructuralBaseline.TrainAndEvaluate(
        new MlDatasetSampleRepository(databasePath));

    PrintReport(report);
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
