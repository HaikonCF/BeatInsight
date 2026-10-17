using BeatInsight.Integration.Game;
using System.Diagnostics;
using System.IO;

namespace BeatInsight.Tests.Integration.Game;

public sealed class LazerGameProviderLiveTests
{
    [Fact]
    public void ManualBackground_MapThenDifficultyTransitions()
    {
        string? marker = Environment.GetEnvironmentVariable(
            "BEATINSIGHT_NATIVE_LAZER_BACKGROUND_TRANSITION_MARKER");
        if (string.IsNullOrWhiteSpace(marker))
            return;

        var provider = new LazerGameProvider();
        GameStateSnapshot baseline = WaitFor(
            provider,
            candidate => candidate.IsAvailable,
            TimeSpan.FromSeconds(120));
        ValidateLiveBackground(baseline);
        WriteBackgroundMarker(marker, "BASE_REQUEST_MAP_CHANGE", baseline);

        GameStateSnapshot changedMap = WaitFor(
            provider,
            candidate => candidate.IsAvailable &&
                         candidate.CurrentBeatmapId != baseline.CurrentBeatmapId,
            TimeSpan.FromMinutes(5));
        ValidateLiveBackground(changedMap);
        WriteBackgroundMarker(marker, "MAP_CHANGED_REQUEST_DIFFICULTY_SAME_SET", changedMap);

        GameStateSnapshot changedDifficulty = WaitFor(
            provider,
            candidate => candidate.IsAvailable &&
                         candidate.CurrentBeatmapId != changedMap.CurrentBeatmapId &&
                         candidate.CurrentBeatmapSetId == changedMap.CurrentBeatmapSetId,
            TimeSpan.FromMinutes(5));
        ValidateLiveBackground(changedDifficulty);
        WriteBackgroundMarker(marker, "DIFFICULTY_VALIDATED", changedDifficulty);
    }

    [Fact]
    public void ManualBackground_CurrentMap()
    {
        string? marker = Environment.GetEnvironmentVariable(
            "BEATINSIGHT_NATIVE_LAZER_BACKGROUND_MARKER");
        if (string.IsNullOrWhiteSpace(marker))
            return;

        var provider = new LazerGameProvider();
        GameStateSnapshot snapshot = WaitFor(
            provider,
            candidate => candidate.IsAvailable,
            TimeSpan.FromSeconds(120));
        string beatmapPath = Assert.IsType<string>(snapshot.CurrentBeatmapPath);
        string? eventFilename = BeatmapBackgroundResolver.ExtractBackgroundFileName(beatmapPath);
        int setId = int.Parse(snapshot.CurrentBeatmapSetId!, System.Globalization.CultureInfo.InvariantCulture);
        IReadOnlyList<LazerNamedFile> namedFiles = new LazerRealmBeatmapSetFileCatalog().ReadFiles(setId);
        File.WriteAllLines(marker,
        [
            $"Process={snapshot.ProcessIdentity}",
            $"Beatmap={snapshot.CurrentBeatmapId}",
            $"Set={snapshot.CurrentBeatmapSetId}",
            $"BeatmapPath={beatmapPath}",
            $"EventFilename={eventFilename ?? "null"}",
            $"BackgroundPath={snapshot.CurrentBackgroundPath ?? "null"}",
            $"NamedFileCount={namedFiles.Count}",
            .. namedFiles.Select(file => $"NamedFile={file.Filename}|{file.Hash ?? "null"}")
        ]);
        string backgroundPath = Assert.IsType<string>(snapshot.CurrentBackgroundPath);
        Assert.True(File.Exists(beatmapPath));
        Assert.True(File.Exists(backgroundPath));
        global::BeatInsight.Models.Beatmap beatmap =
            global::BeatInsight.Parser.BeatmapParser.Load(beatmapPath);
        Assert.NotEmpty(beatmap.HitObjects);
    }

    [Fact]
    public void ManualContinuousB_A_B_A_Restart()
    {
        string? markerPath = Environment.GetEnvironmentVariable(
            "BEATINSIGHT_NATIVE_LAZER_PROVIDER_MARKER");
        if (string.IsNullOrWhiteSpace(markerPath))
            return;

        var provider = new LazerGameProvider();
        GameStateSnapshot b = WaitForBeatmap(provider, "847314", TimeSpan.FromSeconds(120));
        ValidateAnalysisInput(b);
        WriteMarker(markerPath, "B_VALIDATED", b);

        GameStateSnapshot a = WaitForBeatmap(provider, "2431320", TimeSpan.FromMinutes(5));
        ValidateAnalysisInput(a);
        WriteMarker(markerPath, "A_VALIDATED_REQUEST_B", a);

        GameStateSnapshot b2 = WaitForBeatmap(provider, "847314", TimeSpan.FromMinutes(5));
        ValidateAnalysisInput(b2);
        WriteMarker(markerPath, "B2_VALIDATED_REQUEST_A", b2);

        GameStateSnapshot a2 = WaitForBeatmap(provider, "2431320", TimeSpan.FromMinutes(5));
        ValidateAnalysisInput(a2);
        string oldIdentity = Assert.IsType<string>(a2.ProcessIdentity);
        WriteMarker(markerPath, "A2_VALIDATED_REQUEST_EXIT", a2);

        GameStateSnapshot exited = WaitFor(
            provider,
            snapshot => snapshot.Availability == GameStateAvailability.NoProcess,
            TimeSpan.FromMinutes(5));
        Assert.Null(exited.CurrentBeatmapPath);
        WriteMarker(markerPath, "EXIT_VALIDATED_REQUEST_RESTART_A", exited);

        GameStateSnapshot restarted = WaitFor(
            provider,
            snapshot => snapshot.IsAvailable &&
                        snapshot.CurrentBeatmapId == "2431320" &&
                        snapshot.ProcessIdentity != oldIdentity,
            TimeSpan.FromMinutes(5));
        ValidateAnalysisInput(restarted);
        WriteMarker(markerPath, "RESTART_VALIDATED", restarted);
    }

    [Fact]
    public void ManualSingleCapture_SayGoodbye()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("BEATINSIGHT_NATIVE_LAZER_PROVIDER_LIVE"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var provider = new LazerGameProvider();
        var firstTimer = Stopwatch.StartNew();
        GameStateSnapshot snapshot = provider.Refresh();
        firstTimer.Stop();

        Assert.True(firstTimer.Elapsed < TimeSpan.FromSeconds(1));
        Assert.Equal(GameStateAvailability.ProviderUnavailable, snapshot.Availability);
        Assert.Equal("NativeLazer discovery in progress.", snapshot.DiagnosticReason);

        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(120))
        {
            Thread.Sleep(250);
            snapshot = provider.Refresh();
            if (snapshot.IsAvailable)
                break;
            Assert.NotEqual(GameStateAvailability.NoProcess, snapshot.Availability);
            Assert.Null(snapshot.CurrentBeatmapPath);
        }

        Assert.Equal(GameStateAvailability.Available, snapshot.Availability);
        Assert.Equal("2431320", snapshot.CurrentBeatmapId);
        Assert.Equal("1049899", snapshot.CurrentBeatmapSetId);
        Assert.NotNull(snapshot.CurrentBeatmapPath);
        Assert.True(File.Exists(snapshot.CurrentBeatmapPath));
        Assert.NotNull(snapshot.CurrentBackgroundPath);
        Assert.True(File.Exists(snapshot.CurrentBackgroundPath));
        string? backgroundMarker = Environment.GetEnvironmentVariable(
            "BEATINSIGHT_NATIVE_LAZER_BACKGROUND_MARKER");
        if (!string.IsNullOrWhiteSpace(backgroundMarker))
        {
            File.WriteAllText(backgroundMarker,
                $"{snapshot.ProcessIdentity}|{snapshot.CurrentBeatmapId}|" +
                $"{snapshot.CurrentBeatmapSetId}|{snapshot.CurrentBackgroundPath}");
        }
    }

    private static GameStateSnapshot WaitForBeatmap(
        LazerGameProvider provider,
        string beatmapId,
        TimeSpan timeout) => WaitFor(
            provider,
            snapshot => snapshot.IsAvailable && snapshot.CurrentBeatmapId == beatmapId,
            timeout);

    private static GameStateSnapshot WaitFor(
        LazerGameProvider provider,
        Func<GameStateSnapshot, bool> predicate,
        TimeSpan timeout)
    {
        var timer = Stopwatch.StartNew();
        GameStateSnapshot snapshot = provider.Refresh();
        while (!predicate(snapshot) && timer.Elapsed < timeout)
        {
            if (!snapshot.IsAvailable)
            {
                Assert.Null(snapshot.CurrentBeatmapPath);
                Assert.Null(snapshot.CurrentBeatmapId);
            }
            Thread.Sleep(100);
            snapshot = provider.Refresh();
        }

        Assert.True(predicate(snapshot),
            $"Timed out after {timeout} with {snapshot.Availability}: {snapshot.DiagnosticReason}");
        return snapshot;
    }

    private static void ValidateAnalysisInput(GameStateSnapshot snapshot)
    {
        string path = Assert.IsType<string>(snapshot.CurrentBeatmapPath);
        Assert.True(File.Exists(path));
        global::BeatInsight.Models.Beatmap beatmap =
            global::BeatInsight.Parser.BeatmapParser.Load(path);
        Assert.NotEmpty(beatmap.HitObjects);
        Assert.NotNull(beatmap.GameplayProfile);
    }

    private static void ValidateLiveBackground(GameStateSnapshot snapshot)
    {
        string beatmapPath = Assert.IsType<string>(snapshot.CurrentBeatmapPath);
        string backgroundPath = Assert.IsType<string>(snapshot.CurrentBackgroundPath);
        int setId = int.Parse(snapshot.CurrentBeatmapSetId!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(File.Exists(beatmapPath));
        Assert.True(File.Exists(backgroundPath));
        string? independentlyResolved = new LazerBackgroundResolver().Resolve(beatmapPath, setId);
        Assert.Equal(independentlyResolved, backgroundPath);
    }

    private static void WriteBackgroundMarker(
        string markerPath,
        string phase,
        GameStateSnapshot snapshot) => File.WriteAllText(
            markerPath,
            $"{phase}|{snapshot.ProcessIdentity}|{snapshot.CurrentBeatmapId}|" +
            $"{snapshot.CurrentBeatmapSetId}|{snapshot.CurrentBackgroundPath}");

    private static void WriteMarker(
        string markerPath,
        string phase,
        GameStateSnapshot snapshot)
    {
        File.WriteAllText(markerPath,
            $"{phase}|{snapshot.ProcessIdentity ?? "none"}|{snapshot.CurrentBeatmapId ?? "none"}|" +
            $"{snapshot.Availability}|{snapshot.CurrentBeatmapPath ?? "none"}");
    }
}
