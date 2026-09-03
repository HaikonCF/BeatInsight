using BeatInsight.Services.Download;
using System.IO;

namespace BeatInsight.Tests.Services.Download;

public sealed class BeatmapImportServiceTests : IDisposable
{
    private readonly string tempOszPath;

    public BeatmapImportServiceTests()
    {
        tempOszPath = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N") + ".osz");
        File.WriteAllBytes(tempOszPath, [0x50, 0x4B, 0x03, 0x04]);
    }

    public void Dispose()
    {
        try
        {
            File.Delete(tempOszPath);
        }
        catch (IOException)
        {
        }
    }

    private sealed class FakeImportShell : IBeatmapImportShell
    {
        internal string? OpenedPath { get; private set; }
        internal bool ShouldSucceed { get; set; } = true;
        internal string? FailureReason { get; set; }

        public bool TryOpen(string filePath, out string? failureReason)
        {
            OpenedPath = filePath;
            failureReason = ShouldSucceed ? null : FailureReason ?? "shell error";
            return ShouldSucceed;
        }
    }

    /// <summary>
    /// Simule le résultat du sondage à chaque tentative, sans jamais
    /// toucher au système de fichiers ni à GameplayAnalyzer.
    /// </summary>
    private sealed class FakeInstallationProbe : IBeatmapInstallationProbe
    {
        private readonly Queue<bool> results;

        internal int CallCount { get; private set; }

        internal FakeInstallationProbe(params bool[] results)
        {
            this.results = new Queue<bool>(results);
        }

        public bool IsInstalledLocally(
            int beatmapId,
            int? beatmapSetId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return results.Count > 0 && results.Dequeue();
        }
    }

    [Fact]
    public void LaunchImport_OpensOszThroughShellAbstraction()
    {
        var shell = new FakeImportShell();
        var service = new BeatmapImportService(shell, new FakeInstallationProbe());

        BeatmapImportResult result = service.LaunchImport(tempOszPath);

        Assert.Equal(
            BeatmapImportOutcome.LaunchedWaitingForConfirmation,
            result.Outcome);
        Assert.Equal(tempOszPath, shell.OpenedPath);
    }

    [Fact]
    public void LaunchImport_MissingFile_FailsWithoutCallingShell()
    {
        var shell = new FakeImportShell();
        var service = new BeatmapImportService(shell, new FakeInstallationProbe());

        BeatmapImportResult result = service.LaunchImport(
            Path.Combine(Path.GetTempPath(), "does-not-exist.osz"));

        Assert.Equal(BeatmapImportOutcome.ImportLaunchFailed, result.Outcome);
        Assert.Null(shell.OpenedPath);
    }

    [Fact]
    public void LaunchImport_ShellFailure_IsReportedTyped()
    {
        var shell = new FakeImportShell
        {
            ShouldSucceed = false,
            FailureReason = "No application associated with .osz files.",
        };
        var service = new BeatmapImportService(shell, new FakeInstallationProbe());

        BeatmapImportResult result = service.LaunchImport(tempOszPath);

        Assert.Equal(BeatmapImportOutcome.ImportLaunchFailed, result.Outcome);
        Assert.Equal(
            "No application associated with .osz files.",
            result.FailureReason);
    }

    [Fact]
    public async Task WaitForImportConfirmation_ConfirmsAsSoonAsInstalled()
    {
        var probe = new FakeInstallationProbe(false, false, true);
        var service = new BeatmapImportService(new FakeImportShell(), probe);

        int delayCalls = 0;

        BeatmapImportResult result =
            await service.WaitForImportConfirmationAsync(
                beatmapId: 42,
                beatmapSetId: 100,
                maxAttempts: 5,
                delayBetweenAttempts: _ =>
                {
                    delayCalls++;
                    return Task.CompletedTask;
                },
                cancellationToken: default);

        Assert.Equal(BeatmapImportOutcome.Confirmed, result.Outcome);
        Assert.Equal(2, delayCalls);
        Assert.Equal(3, probe.CallCount);
    }

    [Fact]
    public async Task WaitForImportConfirmation_MapAbsentFromSongs_TimesOut()
    {
        var service = new BeatmapImportService(
            new FakeImportShell(),
            new FakeInstallationProbe(false, false, false));

        BeatmapImportResult result =
            await service.WaitForImportConfirmationAsync(
                beatmapId: 42,
                beatmapSetId: 100,
                maxAttempts: 3,
                delayBetweenAttempts: _ => Task.CompletedTask,
                cancellationToken: default);

        Assert.Equal(BeatmapImportOutcome.ImportNotConfirmed, result.Outcome);
    }

    [Fact]
    public async Task WaitForImportConfirmation_Cancellation_StopsImmediately()
    {
        var probe = new FakeInstallationProbe(false, false, false);
        var service = new BeatmapImportService(new FakeImportShell(), probe);

        using CancellationTokenSource cts = new();
        cts.Cancel();

        BeatmapImportResult result =
            await service.WaitForImportConfirmationAsync(
                beatmapId: 42,
                beatmapSetId: 100,
                maxAttempts: 5,
                delayBetweenAttempts: _ => Task.CompletedTask,
                cancellationToken: cts.Token);

        Assert.Equal(BeatmapImportOutcome.Cancelled, result.Outcome);
        Assert.Equal(0, probe.CallCount);
    }

    [Fact]
    public void HasNoDependencyOnHumanLabelStorage()
    {
        System.Reflection.ConstructorInfo ctor =
            typeof(BeatmapImportService).GetConstructors(
                System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                .Single();

        Assert.DoesNotContain(
            ctor.GetParameters(),
            parameter => parameter.ParameterType.Name.Contains(
                "MlDatasetSampleRepository",
                StringComparison.Ordinal));
    }

    [Fact]
    public void HasNoDependencyOnBeatmapAnalysisRepository()
    {
        // V2.4.3a : confirmer un import via BeatmapAnalysisRepository
        // produisait un faux négatif permanent (un import osu! réussi ne
        // l'alimente pas). Ce test documente que la dépendance a bien été
        // remplacée par IBeatmapInstallationProbe.
        System.Reflection.ConstructorInfo ctor =
            typeof(BeatmapImportService).GetConstructors(
                System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                .Single();

        Assert.DoesNotContain(
            ctor.GetParameters(),
            parameter => parameter.ParameterType.Name.Contains(
                "BeatmapAnalysisRepository",
                StringComparison.Ordinal));
        Assert.Contains(
            ctor.GetParameters(),
            parameter => parameter.ParameterType == typeof(IBeatmapInstallationProbe));
    }
}
