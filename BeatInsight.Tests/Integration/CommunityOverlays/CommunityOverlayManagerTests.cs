using System.IO;
using BeatInsight.Integration.CommunityOverlays.Managed;
using BeatInsight.Services.CommunityOverlays;
using BeatInsight.ViewModels;

namespace BeatInsight.Tests.Integration.CommunityOverlays;

/// <summary>
/// FEAT-003 : overlays communautaires gérés depuis Settings. Processus, minuteries et pipes simulés :
/// aucun compagnon réel, aucun WebView2, aucun dossier Songs. Les dossiers d'overlay sont synthétiques (dossier temporaire).
/// </summary>
public sealed class CommunityOverlayManagerTests : IDisposable
{
    private const int OwnerPid = 4242;
    private readonly string root = Path.Combine(Path.GetTempPath(), $"BeatInsight-CommunityOverlays-{Guid.NewGuid():N}");
    private readonly FakeLauncher launcher = new();
    private readonly ManualScheduler scheduler = new();
    private readonly List<bool> pipeChanges = [];
    private int connectedClients;

    public CommunityOverlayManagerTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch (IOException) { }
    }

    private string StorePath => Path.Combine(root, "community-overlays-v1.json");

    private string CreateOverlay(string name, bool withIndex = true)
    {
        string folder = Path.Combine(root, "overlays", name);
        Directory.CreateDirectory(folder);
        if (withIndex) File.WriteAllText(Path.Combine(folder, "index.html"), "<html></html>");
        File.WriteAllText(Path.Combine(folder, "settings.json"), "[]");
        return folder;
    }

    private CommunityOverlayManager CreateManager() => new(
        new CommunityOverlayLibraryStore(StorePath),
        launcher,
        OwnerPid,
        action => action(),
        scheduler,
        () => connectedClients,
        active => pipeChanges.Add(active));

    private static CommunityOverlayStatus Single(CommunityOverlayManager manager) => Assert.Single(manager.Statuses);

    // ---- Import ----

    [Fact]
    public void ValidImportIsAddedDisabledAndPersisted()
    {
        string folder = CreateOverlay("KeyOverlay by cyperdark");
        var manager = CreateManager();

        CommunityOverlayImportResult result = manager.Import(folder);

        Assert.True(result.Success);
        CommunityOverlayStatus status = Single(manager);
        Assert.Equal("KeyOverlay by cyperdark", status.Entry.Name);
        Assert.Equal(folder, status.Entry.FolderPath);
        Assert.False(status.Entry.Enabled);
        Assert.Empty(launcher.Started);
        Assert.Single(new CommunityOverlayLibraryStore(StorePath).Load());
    }

    [Fact]
    public void ImportNeverWritesIntoTheOverlayFolder()
    {
        string folder = CreateOverlay("Simplistic");
        var before = Snapshot(folder);
        var manager = CreateManager();

        manager.Import(folder);
        manager.SetEnabled(Single(manager).Entry.Id, true);
        launcher.Last.Emit(CommunityOverlayCompanionProtocol.Bounds(new CompanionBounds(10, 20, 300, 200)));
        manager.Remove(Single(manager).Entry.Id);

        Assert.Equal(before, Snapshot(folder));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative\\folder")]
    public void InvalidPathIsRejectedWithMessage(string? path)
    {
        var manager = CreateManager();

        CommunityOverlayImportResult result = manager.Import(path);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Empty(manager.Statuses);
    }

    [Fact]
    public void FolderWithoutIndexHtmlIsRejectedExplicitly()
    {
        string folder = CreateOverlay("NoEntry", withIndex: false);
        var manager = CreateManager();

        CommunityOverlayImportResult result = manager.Import(folder);

        Assert.False(result.Success);
        Assert.Contains("index.html", result.Message, StringComparison.Ordinal);
        Assert.Empty(manager.Statuses);
        Assert.False(File.Exists(StorePath));
    }

    [Fact]
    public void MissingFolderIsRejected()
    {
        var manager = CreateManager();

        CommunityOverlayImportResult result = manager.Import(Path.Combine(root, "does-not-exist"));

        Assert.False(result.Success);
        Assert.Empty(manager.Statuses);
    }

    [Fact]
    public void DuplicateImportIsRejectedRegardlessOfCaseAndTrailingSeparator()
    {
        string folder = CreateOverlay("Triangles");
        var manager = CreateManager();
        Assert.True(manager.Import(folder).Success);

        CommunityOverlayImportResult duplicate = manager.Import(folder.ToUpperInvariant() + Path.DirectorySeparatorChar);

        Assert.False(duplicate.Success);
        Assert.Contains("already imported", duplicate.Message, StringComparison.Ordinal);
        Assert.Single(manager.Statuses);
    }

    // ---- Activation, lancement, arrêt ----

    [Fact]
    public void EnablingStartsOneManagedLiveCompanionAndOpensPipes()
    {
        string folder = CreateOverlay("KeyOverlay");
        var manager = CreateManager();
        manager.Import(folder);
        string id = Single(manager).Entry.Id;

        manager.SetEnabled(id, true);

        FakeProcess process = Assert.Single(launcher.Started);
        Assert.Equal(["--overlay", folder, "--source", "live-pipe", "--precise-pipe", "--precise-v2", "--selection-pipe",
            "--managed-by", OwnerPid.ToString()], process.Arguments);
        Assert.Equal([true], pipeChanges);
        Assert.Equal(CommunityOverlayRuntimeState.Starting, Single(manager).State);

        process.Emit(CommunityOverlayCompanionProtocol.Ready());
        Assert.Equal(CommunityOverlayRuntimeState.Running, Single(manager).State);
        Assert.True(new CommunityOverlayLibraryStore(StorePath).Load()[0].Enabled);
    }

    [Fact]
    public void DisablingRequestsCloseAndClosesPipesOnlyAfterExit()
    {
        var manager = CreateManager();
        manager.Import(CreateOverlay("KeyOverlay"));
        string id = Single(manager).Entry.Id;
        manager.SetEnabled(id, true);
        FakeProcess process = launcher.Last;

        manager.SetEnabled(id, false);

        Assert.Contains(CommunityOverlayCompanionProtocol.Close(), process.Sent);
        Assert.True(process.InputClosed);
        Assert.Equal([true], pipeChanges); // le compagnon n'est pas encore sorti

        process.Exit(0);

        Assert.Equal(CommunityOverlayRuntimeState.Stopped, Single(manager).State);
        Assert.Equal([true, false], pipeChanges);
        Assert.False(new CommunityOverlayLibraryStore(StorePath).Load()[0].Enabled);
        Assert.DoesNotContain(scheduler.Pending, p => !p.Cancelled && p.Delay == CommunityOverlayManager.RestartDelay);
    }

    [Fact]
    public void CompanionThatIgnoresCloseIsKilledAfterGracePeriod()
    {
        var manager = CreateManager();
        manager.Import(CreateOverlay("Stubborn"));
        string id = Single(manager).Entry.Id;
        manager.Start(id);

        manager.Stop(id);
        scheduler.RunDue(CommunityOverlayManager.StopGracePeriod);

        Assert.True(launcher.Last.Killed);
    }

    [Fact]
    public void StartingTwiceNeverDuplicatesTheProcess()
    {
        var manager = CreateManager();
        manager.Import(CreateOverlay("KeyOverlay"));
        string id = Single(manager).Entry.Id;

        manager.SetEnabled(id, true);
        manager.Start(id);
        manager.SetEnabled(id, true);

        Assert.Single(launcher.Started);
    }

    [Fact]
    public void DuplicateReportedByCompanionIsNotRestarted()
    {
        var manager = CreateManager();
        manager.Import(CreateOverlay("KeyOverlay"));
        string id = Single(manager).Entry.Id;
        manager.Start(id);

        launcher.Last.Emit(CommunityOverlayCompanionProtocol.Duplicate());
        launcher.Last.Exit(CommunityOverlayCompanionProtocol.ExitDuplicate);
        scheduler.RunDue(TimeSpan.FromMinutes(1));

        Assert.Equal(CommunityOverlayRuntimeState.Duplicate, Single(manager).State);
        Assert.Single(launcher.Started);
    }

    [Fact]
    public void ClosingFromTheOverlayDoesNotRestart()
    {
        var manager = CreateManager();
        manager.Import(CreateOverlay("KeyOverlay"));
        string id = Single(manager).Entry.Id;
        manager.SetEnabled(id, true);

        launcher.Last.Emit(CommunityOverlayCompanionProtocol.UserClosed());
        launcher.Last.Exit(0);
        scheduler.RunDue(TimeSpan.FromMinutes(1));

        CommunityOverlayStatus status = Single(manager);
        Assert.Equal(CommunityOverlayRuntimeState.ClosedByUser, status.State);
        Assert.True(status.Entry.Enabled); // reste activé pour le prochain démarrage
        Assert.Single(launcher.Started);
        Assert.Equal([true, false], pipeChanges);
    }

    [Fact]
    public void UnexpectedExitRestartsThenStopsAfterRepeatedFailures()
    {
        var manager = CreateManager();
        manager.Import(CreateOverlay("Crashy"));
        string id = Single(manager).Entry.Id;
        manager.SetEnabled(id, true);

        launcher.Last.Emit(CommunityOverlayCompanionProtocol.Error("WebView2 runtime missing"));
        launcher.Last.Exit(CommunityOverlayCompanionProtocol.ExitInitializationFailed);
        Assert.Equal(CommunityOverlayRuntimeState.Starting, Single(manager).State);
        Assert.Equal([true], pipeChanges); // pipes conservés pendant la relance

        for (int i = 0; i < CommunityOverlayManager.MaxConsecutiveFailures; i++)
        {
            scheduler.RunDue(CommunityOverlayManager.RestartDelay);
            if (launcher.Last.HasExited) break;
            launcher.Last.Exit(CommunityOverlayCompanionProtocol.ExitInitializationFailed);
        }

        CommunityOverlayStatus status = Single(manager);
        Assert.Equal(CommunityOverlayRuntimeState.Failed, status.State);
        Assert.Equal("WebView2 runtime missing", status.Message);
        Assert.Equal(CommunityOverlayManager.MaxConsecutiveFailures, launcher.Started.Count);
        Assert.Equal([true, false], pipeChanges);
    }

    [Fact]
    public void LaunchExceptionIsReportedAsFailureWithoutPipesLeftOpen()
    {
        launcher.ThrowOnStart = new System.ComponentModel.Win32Exception("access denied");
        var manager = CreateManager();
        manager.Import(CreateOverlay("KeyOverlay"));

        manager.SetEnabled(Single(manager).Entry.Id, true);

        CommunityOverlayStatus status = Single(manager);
        Assert.Equal(CommunityOverlayRuntimeState.Failed, status.State);
        Assert.Contains("access denied", status.Message, StringComparison.Ordinal);
        Assert.Equal([true, false], pipeChanges);
    }

    [Fact]
    public void MissingCompanionLeavesBeatInsightUntouched()
    {
        launcher.ExecutablePath = null;
        var manager = CreateManager();
        manager.Import(CreateOverlay("KeyOverlay"));

        manager.SetEnabled(Single(manager).Entry.Id, true);

        Assert.False(manager.IsCompanionAvailable);
        Assert.Equal(CommunityOverlayRuntimeState.Unavailable, Single(manager).State);
        Assert.Empty(launcher.Started);
        Assert.Empty(pipeChanges);
    }

    [Fact]
    public void PreviewUsesMockSourceWithoutOpeningPipes()
    {
        var manager = CreateManager();
        manager.Import(CreateOverlay("KeyOverlay"));

        manager.Preview(Single(manager).Entry.Id);

        Assert.Equal(["--source", "mock"], launcher.Last.Arguments.Skip(2).Take(2));
        Assert.Contains(CommunityOverlayCompanionProtocol.ManagedByArgument, launcher.Last.Arguments);
        Assert.Empty(pipeChanges);
    }

    [Fact]
    public void SwitchingFromPreviewToLiveWaitsForThePreviewToExit()
    {
        var manager = CreateManager();
        manager.Import(CreateOverlay("KeyOverlay"));
        string id = Single(manager).Entry.Id;
        manager.Preview(id);
        FakeProcess preview = launcher.Last;

        manager.Start(id);
        Assert.Single(launcher.Started);

        preview.Exit(0);
        Assert.Equal(2, launcher.Started.Count);
        Assert.Contains("live-pipe", launcher.Last.Arguments);
    }

    // ---- Limite de clients par pipe ----

    [Fact]
    public void FifthLiveOverlayIsRefusedAtThePipeClientLimit()
    {
        var manager = CreateManager();
        for (int i = 0; i < 5; i++) manager.Import(CreateOverlay($"Overlay{i}"));
        string[] ids = manager.Statuses.Select(s => s.Entry.Id).ToArray();

        foreach (string id in ids) manager.Start(id);

        Assert.Equal(CommunityOverlayManager.MaxPipeClients, launcher.Started.Count);
        Assert.Equal(CommunityOverlayRuntimeState.LimitReached, manager.Statuses[^1].State);
    }

    [Fact]
    public void ManualCompanionsCountTowardsTheLimit()
    {
        var manager = CreateManager();
        manager.Import(CreateOverlay("A"));
        manager.Import(CreateOverlay("B"));
        string[] ids = manager.Statuses.Select(s => s.Entry.Id).ToArray();
        manager.Start(ids[0]);
        connectedClients = CommunityOverlayManager.MaxPipeClients; // 1 géré + 3 compagnons manuels

        manager.Start(ids[1]);

        Assert.Single(launcher.Started);
        Assert.Equal(CommunityOverlayRuntimeState.LimitReached, manager.Statuses[1].State);
    }

    // ---- Position et taille ----

    [Fact]
    public void BoundsFromSettingsArePersistedAppliedAndReusedAtLaunch()
    {
        var manager = CreateManager();
        manager.Import(CreateOverlay("KeyOverlay"));
        string id = Single(manager).Entry.Id;
        manager.Start(id);
        var bounds = new CompanionBounds(120, 80.5, 400, 250);

        Assert.True(manager.SetBounds(id, bounds));

        Assert.Contains(CommunityOverlayCompanionProtocol.Bounds(bounds), launcher.Last.Sent);
        Assert.Equal(bounds, new CommunityOverlayLibraryStore(StorePath).Load()[0].Bounds);

        manager.Stop(id);
        launcher.Last.Exit(0);
        manager.Start(id);
        Assert.Equal("120,80.5,400,250", launcher.Last.Arguments[launcher.Last.Arguments.IndexOf("--window") + 1]);
    }

    [Fact]
    public void BoundsReportedByTheCompanionArePersisted()
    {
        var manager = CreateManager();
        manager.Import(CreateOverlay("KeyOverlay"));
        manager.Start(Single(manager).Entry.Id);

        launcher.Last.Emit(CommunityOverlayCompanionProtocol.Bounds(new CompanionBounds(-1500, 30, 640, 360)));

        Assert.Equal(new CompanionBounds(-1500, 30, 640, 360), new CommunityOverlayLibraryStore(StorePath).Load()[0].Bounds);
    }

    [Theory]
    [InlineData(0, 0, 10, 300)]
    [InlineData(0, 0, 300, 20000)]
    [InlineData(double.NaN, 0, 300, 300)]
    public void InvalidBoundsAreRejected(double x, double y, double width, double height)
    {
        var manager = CreateManager();
        manager.Import(CreateOverlay("KeyOverlay"));

        Assert.False(manager.SetBounds(Single(manager).Entry.Id, new CompanionBounds(x, y, width, height)));
        Assert.Null(Single(manager).Entry.Bounds);
    }

    [Fact]
    public void EditModeIsSentToTheRunningCompanionAndReplayedAfterRestart()
    {
        var manager = CreateManager();
        manager.Import(CreateOverlay("KeyOverlay"));
        string id = Single(manager).Entry.Id;
        manager.Start(id);

        manager.SetEditMode(id, true);
        Assert.Contains(CommunityOverlayCompanionProtocol.Edit(true), launcher.Last.Sent);

        manager.Stop(id);
        launcher.Last.Exit(0);
        manager.Start(id);
        Assert.Contains(CommunityOverlayCompanionProtocol.Edit(true), launcher.Last.Sent);
    }

    // ---- Retrait, dossiers déplacés, redémarrage ----

    [Fact]
    public void RemovingStopsTheCompanionAndKeepsTheFolder()
    {
        string folder = CreateOverlay("KeyOverlay");
        var manager = CreateManager();
        manager.Import(folder);
        string id = Single(manager).Entry.Id;
        manager.SetEnabled(id, true);

        Assert.True(manager.Remove(id));

        Assert.Empty(manager.Statuses);
        Assert.True(launcher.Last.InputClosed);
        Assert.True(File.Exists(Path.Combine(folder, "index.html")));
        Assert.Empty(new CommunityOverlayLibraryStore(StorePath).Load());
    }

    [Fact]
    public void DeletedOrMovedFolderIsReportedAtStartupWithoutLaunching()
    {
        string folder = CreateOverlay("KeyOverlay");
        var first = CreateManager();
        first.Import(folder);
        first.SetEnabled(Single(first).Entry.Id, true);
        launcher.Last.Exit(0);
        first.Dispose();
        launcher.Started.Clear();
        Directory.Move(folder, folder + " (moved)");

        var second = CreateManager();
        second.LoadAndStartEnabled();

        CommunityOverlayStatus status = Single(second);
        Assert.Equal(CommunityOverlayRuntimeState.Missing, status.State);
        Assert.True(status.Entry.Enabled); // la configuration est conservée
        Assert.Empty(launcher.Started);
    }

    [Fact]
    public void BeatInsightRestartRestoresEnabledOverlaysWithTheirBounds()
    {
        var first = CreateManager();
        first.Import(CreateOverlay("Enabled"));
        first.Import(CreateOverlay("Disabled"));
        string enabledId = first.Statuses[0].Entry.Id;
        first.SetEnabled(enabledId, true);
        launcher.Last.Emit(CommunityOverlayCompanionProtocol.Bounds(new CompanionBounds(50, 60, 500, 300)));
        first.Dispose();
        launcher.Started.Clear();

        var second = CreateManager();
        second.LoadAndStartEnabled();

        FakeProcess process = Assert.Single(launcher.Started);
        Assert.Contains(Path.Combine(root, "overlays", "Enabled"), process.Arguments);
        Assert.Equal("50,60,500,300", process.Arguments[process.Arguments.IndexOf("--window") + 1]);
        Assert.Equal(2, second.Statuses.Count);
    }

    [Fact]
    public void ShutdownClosesEveryManagedCompanionAndKillsThoseThatHang()
    {
        var manager = CreateManager();
        manager.Import(CreateOverlay("Polite"));
        manager.Import(CreateOverlay("Hanging"));
        foreach (CommunityOverlayStatus status in manager.Statuses) manager.Start(status.Entry.Id);
        launcher.Started[0].ExitsOnInputClose = true;

        manager.Dispose();

        Assert.All(launcher.Started, p => Assert.Contains(CommunityOverlayCompanionProtocol.Close(), p.Sent));
        Assert.All(launcher.Started, p => Assert.True(p.InputClosed));
        Assert.False(launcher.Started[0].Killed);
        Assert.True(launcher.Started[1].Killed);
        Assert.Equal([true, false], pipeChanges);
    }

    [Fact]
    public void CorruptStoreFallsBackToEmptyLibrary()
    {
        File.WriteAllText(StorePath, "{ not json");
        var manager = CreateManager();

        manager.LoadAndStartEnabled();

        Assert.Empty(manager.Statuses);
    }

    [Fact]
    public void SettingsOverridesAreRoundTrippedWithoutTouchingTheOverlay()
    {
        string folder = CreateOverlay("KeyOverlay");
        var store = new CommunityOverlayLibraryStore(StorePath);
        var overrides = new System.Text.Json.Nodes.JsonObject { ["keyColor"] = "#ff00ff" };
        Assert.True(store.Save([new CommunityOverlayEntry(CommunityOverlayEntry.IdFor(folder), "KeyOverlay", folder, true, null, overrides)]));

        CommunityOverlayEntry loaded = Assert.Single(store.Load());

        Assert.Equal("#ff00ff", loaded.SettingsOverrides?["keyColor"]?.GetValue<string>());
        Assert.Equal("[]", File.ReadAllText(Path.Combine(folder, "settings.json")));
    }

    // ---- ViewModel ----

    [Fact]
    public void ViewModelReflectsStateAndAppliesBoundsFromText()
    {
        var manager = CreateManager();
        var viewModel = new CommunityOverlaysViewModel();
        viewModel.Attach(manager);

        viewModel.Import(CreateOverlay("KeyOverlay"));
        CommunityOverlayItemViewModel item = Assert.Single(viewModel.Overlays);
        Assert.Equal("Stopped", item.StateText);

        item.IsEnabled = true;
        launcher.Last.Emit(CommunityOverlayCompanionProtocol.Ready());
        Assert.Equal("Running", item.StateText);
        Assert.True(item.IsActive);

        item.XText = "10"; item.YText = "20"; item.WidthText = "300,5"; item.HeightText = "200";
        viewModel.ApplyBounds(item);
        Assert.False(viewModel.IsStatusError);
        Assert.Equal(new CompanionBounds(10, 20, 300.5, 200), Single(manager).Entry.Bounds);

        item.WidthText = "abc";
        viewModel.ApplyBounds(item);
        Assert.True(viewModel.IsStatusError);
    }

    [Fact]
    public void ViewModelShowsInvalidImportError()
    {
        var viewModel = new CommunityOverlaysViewModel();
        viewModel.Attach(CreateManager());

        viewModel.Import(CreateOverlay("Broken", withIndex: false));

        Assert.True(viewModel.IsStatusError);
        Assert.Contains("index.html", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.True(viewModel.HasNoOverlays);
    }

    // ---- Contrat stdin/stdout ----

    [Fact]
    public void ProtocolRoundTripsEveryMessage()
    {
        var bounds = new CompanionBounds(1.5, -2, 640, 360);
        Assert.Equal(CompanionMessageKind.Bounds, CommunityOverlayCompanionProtocol.Parse(CommunityOverlayCompanionProtocol.Bounds(bounds))!.Kind);
        Assert.Equal(bounds, CommunityOverlayCompanionProtocol.Parse(CommunityOverlayCompanionProtocol.Bounds(bounds))!.Bounds);
        Assert.True(CommunityOverlayCompanionProtocol.Parse(CommunityOverlayCompanionProtocol.Edit(true))!.EditEnabled);
        Assert.Equal(CompanionMessageKind.Close, CommunityOverlayCompanionProtocol.Parse(CommunityOverlayCompanionProtocol.Close())!.Kind);
        Assert.Equal(CompanionMessageKind.Ready, CommunityOverlayCompanionProtocol.Parse(CommunityOverlayCompanionProtocol.Ready())!.Kind);
        Assert.Equal(CompanionMessageKind.UserClosed, CommunityOverlayCompanionProtocol.Parse(CommunityOverlayCompanionProtocol.UserClosed())!.Kind);
        Assert.Equal("boom", CommunityOverlayCompanionProtocol.Parse(CommunityOverlayCompanionProtocol.Error("boom"))!.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"type\":\"unknown\"}")]
    [InlineData("{\"type\":\"edit\"}")]
    [InlineData("{\"type\":\"bounds\",\"x\":0,\"y\":0,\"width\":5,\"height\":5}")]
    public void ProtocolIgnoresInvalidLines(string line)
    {
        Assert.Null(CommunityOverlayCompanionProtocol.Parse(line));
    }

    // ---- FEAT-003.1 : échelle (zoom WebView2) ----

    [Fact]
    public void ScaleDefaultsTo100AndAddsNoArgument()
    {
        var manager = CreateManager();
        manager.Import(CreateOverlay("KeyOverlay"));

        manager.Start(Single(manager).Entry.Id);

        Assert.Equal(100, Single(manager).Entry.Scale);
        Assert.DoesNotContain(CommunityOverlayCompanionProtocol.ScaleArgument, launcher.Last.Arguments);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(150)]
    public void ScaleIsPersistedAndPassedAtLaunch(int percent)
    {
        var manager = CreateManager();
        manager.Import(CreateOverlay("KeyOverlay"));
        string id = Single(manager).Entry.Id;

        Assert.True(manager.SetScale(id, percent));
        manager.Start(id);

        Assert.Equal(percent, new CommunityOverlayLibraryStore(StorePath).Load()[0].Scale);
        if (percent == 100)
            Assert.DoesNotContain(CommunityOverlayCompanionProtocol.ScaleArgument, launcher.Last.Arguments);
        else
            Assert.Equal(percent.ToString(), launcher.Last.Arguments[launcher.Last.Arguments.IndexOf("--scale") + 1]);
    }

    [Fact]
    public void ScaleIsAppliedHotWithoutChangingWindowBounds()
    {
        var manager = CreateManager();
        manager.Import(CreateOverlay("KeyOverlay"));
        string id = Single(manager).Entry.Id;
        var bounds = new CompanionBounds(100, 100, 640, 360);
        manager.SetBounds(id, bounds);
        manager.Start(id);

        manager.SetScale(id, 75);
        manager.SetScale(id, 75); // inchangé : rien de renvoyé
        manager.SetScale(id, 50);

        Assert.Equal([CommunityOverlayCompanionProtocol.Scale(75), CommunityOverlayCompanionProtocol.Scale(50)],
            launcher.Last.Sent.Where(IsScaleLine));
        Assert.Equal(bounds, Single(manager).Entry.Bounds);
        Assert.Single(launcher.Started); // pas de relance
    }

    [Fact]
    public void ScaleSurvivesBeatInsightRestart()
    {
        var first = CreateManager();
        first.Import(CreateOverlay("KeyOverlay"));
        string id = Single(first).Entry.Id;
        first.SetEnabled(id, true);
        first.SetScale(id, 125);
        first.Dispose();
        launcher.Started.Clear();

        var second = CreateManager();
        second.LoadAndStartEnabled();

        Assert.Equal(125, Single(second).Entry.Scale);
        Assert.Equal("125", launcher.Last.Arguments[launcher.Last.Arguments.IndexOf("--scale") + 1]);
    }

    [Fact]
    public void ScaleIsIndependentPerOverlay()
    {
        var manager = CreateManager();
        manager.Import(CreateOverlay("A"));
        manager.Import(CreateOverlay("B"));
        string a = manager.Statuses[0].Entry.Id;
        string b = manager.Statuses[1].Entry.Id;
        manager.Start(a);
        manager.Start(b);

        manager.SetScale(a, 60);

        Assert.Equal(60, manager.Statuses[0].Entry.Scale);
        Assert.Equal(100, manager.Statuses[1].Entry.Scale);
        Assert.Contains(CommunityOverlayCompanionProtocol.Scale(60), launcher.Started[0].Sent);
        Assert.DoesNotContain(launcher.Started[1].Sent, IsScaleLine);
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(-40, 25)]
    [InlineData(7, 25)]
    [InlineData(72, 70)]
    [InlineData(73, 75)]
    [InlineData(333, 200)]
    public void OutOfRangeOrOffStepScaleIsNormalized(int requested, int expected)
    {
        var manager = CreateManager();
        manager.Import(CreateOverlay("KeyOverlay"));

        manager.SetScale(Single(manager).Entry.Id, requested);

        Assert.Equal(expected, Single(manager).Entry.Scale);
    }

    [Fact]
    public void PreviewUsesTheSavedScaleAndReceivesHotChanges()
    {
        var manager = CreateManager();
        manager.Import(CreateOverlay("KeyOverlay"));
        string id = Single(manager).Entry.Id;
        manager.SetScale(id, 150);

        manager.Preview(id);
        manager.SetScale(id, 125);

        Assert.Contains("mock", launcher.Last.Arguments);
        Assert.Equal("150", launcher.Last.Arguments[launcher.Last.Arguments.IndexOf("--scale") + 1]);
        Assert.Contains(CommunityOverlayCompanionProtocol.Scale(125), launcher.Last.Sent);
        Assert.Empty(pipeChanges);
    }

    [Fact]
    public void MoveResizeStillWorksWithAScale()
    {
        var manager = CreateManager();
        manager.Import(CreateOverlay("KeyOverlay"));
        string id = Single(manager).Entry.Id;
        manager.SetScale(id, 50);
        manager.Start(id);

        manager.SetEditMode(id, true);
        launcher.Last.Emit(CommunityOverlayCompanionProtocol.Bounds(new CompanionBounds(30, 40, 500, 300)));
        Assert.True(manager.SetBounds(id, new CompanionBounds(35, 45, 520, 310)));

        CommunityOverlayEntry saved = new CommunityOverlayLibraryStore(StorePath).Load()[0];
        Assert.Equal(new CompanionBounds(35, 45, 520, 310), saved.Bounds);
        Assert.Equal(50, saved.Scale);
        Assert.Contains(CommunityOverlayCompanionProtocol.Edit(true), launcher.Last.Sent);
    }

    [Fact]
    public void FeatThreeConfigurationWithoutScaleLoadsAt100()
    {
        string folder = CreateOverlay("KeyOverlay");
        string folderJson = System.Text.Json.JsonSerializer.Serialize(folder);
        File.WriteAllText(StorePath,
            "{ \"version\": 1, \"overlays\": [ { \"name\": \"KeyOverlay\", \"folderPath\": " + folderJson +
            ", \"enabled\": true, \"bounds\": { \"x\": 10, \"y\": 20, \"width\": 300, \"height\": 200 } } ] }");

        CommunityOverlayEntry entry = Assert.Single(new CommunityOverlayLibraryStore(StorePath).Load());

        Assert.Equal(100, entry.Scale);
        Assert.True(entry.Enabled);
        Assert.Equal(new CompanionBounds(10, 20, 300, 200), entry.Bounds);
    }

    [Theory]
    [InlineData("\"abc\"", 100)]
    [InlineData("null", 100)]
    [InlineData("true", 100)]
    [InlineData("{}", 100)]
    [InlineData("1000", 200)]
    [InlineData("12.5", 25)]
    [InlineData("88", 90)]
    public void InvalidStoredScaleFallsBackWithoutDroppingTheOverlay(string json, int expected)
    {
        string folder = CreateOverlay("KeyOverlay");
        string folderJson = System.Text.Json.JsonSerializer.Serialize(folder);
        File.WriteAllText(StorePath,
            "{\"version\":1,\"overlays\":[{\"folderPath\":" + folderJson + ",\"enabled\":false,\"scale\":" + json + "}]}");

        CommunityOverlayEntry entry = Assert.Single(new CommunityOverlayLibraryStore(StorePath).Load());

        Assert.Equal(expected, entry.Scale);
    }

    [Fact]
    public void ViewModelSliderAppliesScaleAndResetReturnsTo100()
    {
        var manager = CreateManager();
        var viewModel = new CommunityOverlaysViewModel();
        viewModel.Attach(manager);
        viewModel.Import(CreateOverlay("KeyOverlay"));
        CommunityOverlayItemViewModel item = Assert.Single(viewModel.Overlays);
        Assert.Equal(100, item.ScalePercent);
        Assert.False(item.CanResetScale);

        item.ScalePercent = 74.2; // valeur de Slider non alignée : cran de 5 %
        Assert.Equal(75, item.ScalePercent);
        Assert.Equal("75 %", item.ScaleText);
        Assert.Equal(75, Single(manager).Entry.Scale);
        Assert.True(item.CanResetScale);

        viewModel.ResetScale(item);
        Assert.Equal(100, item.ScalePercent);
        Assert.Equal(100, Single(manager).Entry.Scale);
    }

    [Fact]
    public void ScaleProtocolRoundTripsAndRejectsInvalidPayloads()
    {
        Assert.Equal(150, CommunityOverlayCompanionProtocol.Parse(CommunityOverlayCompanionProtocol.Scale(150))!.ScalePercent);
        Assert.Equal(200, CommunityOverlayCompanionProtocol.Parse("{\"type\":\"scale\",\"percent\":999}")!.ScalePercent);
        Assert.Null(CommunityOverlayCompanionProtocol.Parse("{\"type\":\"scale\"}"));
        Assert.Null(CommunityOverlayCompanionProtocol.Parse("{\"type\":\"scale\",\"percent\":\"big\"}"));
        Assert.Equal(0.5, CommunityOverlayCompanionProtocol.ZoomFactorFor(50));
        Assert.Equal(1.5, CommunityOverlayCompanionProtocol.ZoomFactorFor(150));
        // Les commandes FEAT-003 restent inchangées.
        Assert.Equal("{\"type\":\"close\"}", CommunityOverlayCompanionProtocol.Close());
        Assert.Equal("{\"type\":\"edit\",\"enabled\":true}", CommunityOverlayCompanionProtocol.Edit(true));
    }

    // ---- BUG-003 : premier plan d'osu! ----

    [Fact]
    public void LiveOverlayFollowsOsuForegroundWithoutRestart()
    {
        var manager = CreateManager();
        manager.Import(CreateOverlay("KeyOverlay"));
        string id = Single(manager).Entry.Id;
        manager.SetOsuForeground(true); // osu! au premier plan
        manager.Start(id);

        manager.SetOsuForeground(false); // Alt+Tab vers Opera
        manager.SetOsuForeground(false); // inchangé : rien de renvoyé
        manager.SetOsuForeground(true);  // retour sur osu!

        Assert.Equal(
            [Foreground(true), Foreground(false), Foreground(true)],
            launcher.Last.Sent.Where(IsForegroundLine));
        Assert.Single(launcher.Started); // ni relance ni doublon
        Assert.Equal(CommunityOverlayRuntimeState.Starting, Single(manager).State);
    }

    [Fact]
    public void LiveOverlayStartsHiddenWhileOsuIsAbsentOrClosed()
    {
        var manager = CreateManager();
        manager.Import(CreateOverlay("KeyOverlay"));
        string id = Single(manager).Entry.Id;

        manager.Start(id); // aucun état reçu : fermé par défaut

        Assert.False(manager.OsuForeground);
        Assert.Equal([Foreground(false)], launcher.Last.Sent.Where(IsForegroundLine));

        manager.SetOsuForeground(true);
        manager.SetOsuForeground(false); // processus osu! fermé : la sonde renvoie false
        Assert.Equal(Foreground(false), launcher.Last.Sent.Where(IsForegroundLine).Last());
    }

    [Fact]
    public void RestartedCompanionReceivesCurrentForegroundState()
    {
        var manager = CreateManager();
        manager.Import(CreateOverlay("KeyOverlay"));
        string id = Single(manager).Entry.Id;
        manager.SetEnabled(id, true);
        manager.SetOsuForeground(true);

        launcher.Last.Exit(1);
        manager.SetOsuForeground(false); // pendant la relance : aucun processus à prévenir
        scheduler.RunDue(CommunityOverlayManager.RestartDelay);

        Assert.Equal(2, launcher.Started.Count);
        Assert.Equal([Foreground(false)], launcher.Last.Sent.Where(IsForegroundLine));
    }

    [Fact]
    public void EditModeIsSentIndependentlyOfForeground()
    {
        var manager = CreateManager();
        manager.Import(CreateOverlay("KeyOverlay"));
        string id = Single(manager).Entry.Id;
        manager.SetOsuForeground(true);
        manager.Start(id); // Foreground(true) au lancement, ignoré ci-dessous (Skip)

        manager.SetEditMode(id, true);
        manager.SetOsuForeground(false); // Settings au premier plan pendant l'édition
        manager.SetEditMode(id, false);

        Assert.Equal(
            [CommunityOverlayCompanionProtocol.Edit(true), Foreground(false), CommunityOverlayCompanionProtocol.Edit(false)],
            launcher.Last.Sent.Where(line => IsForegroundLine(line) ||
                CommunityOverlayCompanionProtocol.Parse(line)?.Kind == CompanionMessageKind.Edit).Skip(1));
    }

    [Fact]
    public void PreviewIsNeverGatedByForeground()
    {
        var manager = CreateManager();
        manager.Import(CreateOverlay("KeyOverlay"));
        manager.Preview(Single(manager).Entry.Id);

        manager.SetOsuForeground(true);
        manager.SetOsuForeground(false);

        Assert.DoesNotContain(launcher.Last.Sent, IsForegroundLine);
    }

    [Fact]
    public void ForegroundProtocolRoundTripsAndRejectsInvalidPayloads()
    {
        Assert.Equal("{\"type\":\"foreground\",\"osu\":true}", Foreground(true));
        Assert.True(CommunityOverlayCompanionProtocol.Parse(Foreground(true))!.OsuForeground);
        Assert.False(CommunityOverlayCompanionProtocol.Parse(Foreground(false))!.OsuForeground);
        Assert.Equal(CompanionMessageKind.Foreground, CommunityOverlayCompanionProtocol.Parse(Foreground(false))!.Kind);
        Assert.Null(CommunityOverlayCompanionProtocol.Parse("{\"type\":\"foreground\"}"));
        Assert.Null(CommunityOverlayCompanionProtocol.Parse("{\"type\":\"foreground\",\"osu\":\"yes\"}"));
    }

    private static string Foreground(bool osu) => CommunityOverlayCompanionProtocol.Foreground(osu);

    private static bool IsForegroundLine(string line) =>
        CommunityOverlayCompanionProtocol.Parse(line)?.Kind == CompanionMessageKind.Foreground;

    private static bool IsScaleLine(string line) =>
        CommunityOverlayCompanionProtocol.Parse(line)?.Kind == CompanionMessageKind.Scale;

    private static string[] Snapshot(string folder) =>
        Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .Select(f => $"{Path.GetRelativePath(folder, f)}|{File.ReadAllText(f)}|{File.GetLastWriteTimeUtc(f).Ticks}")
            .Order(StringComparer.Ordinal)
            .ToArray();

    // ---- Doublures ----

    private sealed class FakeLauncher : ICommunityOverlayCompanionLauncher
    {
        public string? ExecutablePath { get; set; } = @"C:\fake\CommunityOverlays\BeatInsight.CommunityOverlays.Poc.exe";
        public Exception? ThrowOnStart { get; set; }
        public List<FakeProcess> Started { get; } = [];
        public FakeProcess Last => Started[^1];

        public ICommunityOverlayCompanionProcess Start(IReadOnlyList<string> arguments)
        {
            if (ThrowOnStart is not null) throw ThrowOnStart;
            var process = new FakeProcess(1000 + Started.Count, arguments.ToList());
            Started.Add(process);
            return process;
        }
    }

    private sealed class FakeProcess(int id, List<string> arguments) : ICommunityOverlayCompanionProcess
    {
        public int ProcessId { get; } = id;
        public List<string> Arguments { get; } = arguments;
        public List<string> Sent { get; } = [];
        public bool InputClosed { get; private set; }
        public bool Killed { get; private set; }
        public bool HasExited { get; private set; }
        public bool ExitsOnInputClose { get; set; }

        public event Action<string>? LineReceived;
        public event Action<int>? Exited;

        public void Emit(string line) => LineReceived?.Invoke(line);

        public void Exit(int code)
        {
            if (HasExited) return;
            HasExited = true;
            Exited?.Invoke(code);
        }

        public bool TrySend(string line)
        {
            if (HasExited || InputClosed) return false;
            Sent.Add(line);
            return true;
        }

        public void CloseInput() => InputClosed = true;

        public bool WaitForExit(TimeSpan timeout) => HasExited || ExitsOnInputClose && InputClosed;

        public void Kill() => Killed = true;

        public void Dispose() { }
    }

    private sealed class ManualScheduler : ICommunityOverlayScheduler
    {
        public List<Scheduled> Pending { get; } = [];

        public IDisposable Schedule(TimeSpan delay, Action action)
        {
            var item = new Scheduled(delay, action);
            Pending.Add(item);
            return item;
        }

        /// <summary>Exécute les actions dont le délai est au plus <paramref name="elapsed"/>.</summary>
        public void RunDue(TimeSpan elapsed)
        {
            foreach (Scheduled item in Pending.ToArray())
            {
                if (item.Cancelled || item.Ran || item.Delay > elapsed) continue;
                item.Ran = true;
                item.Action();
            }
        }

        public sealed class Scheduled(TimeSpan delay, Action action) : IDisposable
        {
            public TimeSpan Delay { get; } = delay;
            public Action Action { get; } = action;
            public bool Cancelled { get; private set; }
            public bool Ran { get; set; }
            public void Dispose() => Cancelled = true;
        }
    }
}
