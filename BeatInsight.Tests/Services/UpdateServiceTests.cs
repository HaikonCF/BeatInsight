using BeatInsight.Services.Settings;
using BeatInsight.Services.Updates;
using BeatInsight.ViewModels;
using System.IO;

namespace BeatInsight.Tests.Services;

public sealed class UpdateServiceTests
{
    [Fact]
    public void SettingsDefaultToAutomaticChecksAndRoundTrip()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, "updates.json");
            var store = new UpdateSettingsStore(path);

            Assert.True(store.Load().CheckAutomatically);
            Assert.True(store.Save(new UpdateSettings(
                UpdateSettings.CurrentVersion,
                CheckAutomatically: false)));
            Assert.False(store.Load().CheckAutomatically);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void InvalidSettingsFailClosedToDefault()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, "updates.json");
            File.WriteAllText(path, "not-json");

            Assert.True(new UpdateSettingsStore(path).Load().CheckAutomatically);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LocalFeedConfigurationDistinguishesMissingFromEmptyDirectory()
    {
        string? previous = Environment.GetEnvironmentVariable(
            UpdateFeedConfiguration.LocalFeedEnvironmentVariable);
        string missing = Path.Combine(
            Path.GetTempPath(),
            $"BeatInsight-Missing-Feed-{Guid.NewGuid():N}");
        string empty = CreateTemporaryDirectory();
        try
        {
            Environment.SetEnvironmentVariable(
                UpdateFeedConfiguration.LocalFeedEnvironmentVariable,
                missing);
            Assert.False(UpdateFeedConfiguration.CreateDefault().IsAvailable());

            Environment.SetEnvironmentVariable(
                UpdateFeedConfiguration.LocalFeedEnvironmentVariable,
                empty);
            Assert.True(UpdateFeedConfiguration.CreateDefault().IsAvailable());
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                UpdateFeedConfiguration.LocalFeedEnvironmentVariable,
                previous);
            Directory.Delete(empty, recursive: true);
        }
    }

    [Fact]
    public async Task NoUpdateReportsUpToDate()
    {
        var client = new FakeUpdateClient();
        using var service = new UpdateService(client);

        Assert.True(await service.CheckAsync());

        Assert.Equal(UpdateState.UpToDate, service.Status.State);
        Assert.Equal("Up to date", service.Status.Message);
        Assert.Equal(1, client.CheckCount);
    }

    [Fact]
    public async Task AvailableUpdateReportsVersionAndSize()
    {
        var candidate = Candidate("3.0.0-rc.2", 1234);
        var client = new FakeUpdateClient { CheckResult = candidate };
        using var service = new UpdateService(client);

        Assert.True(await service.CheckAsync());

        Assert.Equal(UpdateState.Available, service.Status.State);
        Assert.Equal(candidate.Version, service.Status.TargetVersion);
        Assert.Equal(candidate.Size, service.Status.ReleaseSize);
    }

    [Fact]
    public async Task CheckFailureIsContainedAndGeneric()
    {
        var client = new FakeUpdateClient { CheckException = new IOException("secret detail") };
        using var service = new UpdateService(client);

        Assert.False(await service.CheckAsync());

        Assert.Equal(UpdateState.Failed, service.Status.State);
        Assert.Equal("Unable to check for updates", service.Status.Message);
        Assert.DoesNotContain("secret detail", service.Status.Message);
    }

    [Fact]
    public async Task DownloadTransitionsToReadyAndReportsProgress()
    {
        var client = new FakeUpdateClient { CheckResult = Candidate("3.0.0-rc.2", 4567) };
        using var service = new UpdateService(client);
        await service.CheckAsync();

        Assert.True(await service.DownloadAsync());

        Assert.Equal(UpdateState.ReadyToRestart, service.Status.State);
        Assert.Equal(100, service.Status.Progress);
        Assert.Equal(1, client.DownloadCount);
    }

    [Fact]
    public async Task DownloadFailureLeavesCurrentVersionRunnable()
    {
        var client = new FakeUpdateClient
        {
            CheckResult = Candidate("3.0.0-rc.2", 4567),
            DownloadException = new IOException("network down")
        };
        using var service = new UpdateService(client);
        await service.CheckAsync();

        Assert.False(await service.DownloadAsync());

        Assert.Equal(UpdateState.Failed, service.Status.State);
        Assert.Equal("3.0.0-rc.1", service.Status.CurrentVersion);
        Assert.Equal(0, client.ApplyCount);
    }

    [Fact]
    public async Task DuplicateChecksAreRejectedWhileFirstIsRunning()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeUpdateClient
        {
            CheckHandler = async () =>
            {
                entered.SetResult();
                await release.Task;
                return null;
            }
        };
        using var service = new UpdateService(client);

        Task<bool> first = service.CheckAsync();
        await entered.Task;
        Assert.False(await service.CheckAsync());
        release.SetResult();
        Assert.True(await first);
        Assert.Equal(1, client.CheckCount);
    }

    [Fact]
    public async Task ApplyCanOnlyBeRequestedOnceAfterDownload()
    {
        var client = new FakeUpdateClient { CheckResult = Candidate("3.0.0-rc.2", 4567) };
        using var service = new UpdateService(client);
        await service.CheckAsync();
        await service.DownloadAsync();

        Assert.True(service.TryBeginApply(out UpdateCandidate? candidate));
        Assert.NotNull(candidate);
        Assert.False(service.TryBeginApply(out _));

        service.Dispose();
        service.WaitExitThenApplyAndRestart(candidate!);
        Assert.Equal(1, client.ApplyCount);
    }

    [Fact]
    public async Task UninstalledBuildDoesNotContactFeed()
    {
        var client = new FakeUpdateClient { IsInstalled = false };
        using var service = new UpdateService(client);

        Assert.False(await service.CheckAsync());
        Assert.Equal(0, client.CheckCount);
        Assert.Equal(UpdateState.Idle, service.Status.State);
    }

    [Fact]
    public void ViewModelPersistsPreferenceChangesOnlyAfterApply()
    {
        var viewModel = new UpdateViewModel();
        var changes = new List<UpdateSettings>();
        viewModel.SettingsChanged += changes.Add;

        viewModel.ApplySettings(new UpdateSettings(
            UpdateSettings.CurrentVersion,
            CheckAutomatically: false));
        Assert.Empty(changes);

        viewModel.CheckAutomatically = true;
        UpdateSettings change = Assert.Single(changes);
        Assert.True(change.CheckAutomatically);
    }

    private static UpdateCandidate Candidate(string version, long size) =>
        new(version, size, new object());

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"BeatInsight-Updates-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeUpdateClient : IUpdateClient
    {
        public bool IsInstalled { get; set; } = true;
        public string CurrentVersion { get; set; } = "3.0.0-rc.1";
        internal UpdateCandidate? CheckResult { get; set; }
        internal Exception? CheckException { get; set; }
        internal Exception? DownloadException { get; set; }
        internal Func<Task<UpdateCandidate?>>? CheckHandler { get; set; }
        internal int CheckCount { get; private set; }
        internal int DownloadCount { get; private set; }
        internal int ApplyCount { get; private set; }

        public async Task<UpdateCandidate?> CheckForUpdatesAsync()
        {
            CheckCount++;
            if (CheckException is not null)
                throw CheckException;
            return CheckHandler is null ? CheckResult : await CheckHandler();
        }

        public Task DownloadAsync(
            UpdateCandidate candidate,
            Action<int> progress,
            CancellationToken cancellationToken)
        {
            DownloadCount++;
            if (DownloadException is not null)
                throw DownloadException;
            progress(25);
            progress(100);
            return Task.CompletedTask;
        }

        public void WaitExitThenApplyAndRestart(UpdateCandidate candidate)
        {
            ApplyCount++;
        }
    }
}
