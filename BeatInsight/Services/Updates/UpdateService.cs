using BeatInsight.Diagnostics;
using System.IO;
using Velopack;
using Velopack.Sources;

namespace BeatInsight.Services.Updates;

internal enum UpdateState
{
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    ReadyToRestart,
    Applying,
    Failed
}

internal sealed record UpdateCandidate(
    string Version,
    long Size,
    object NativeUpdate);

internal sealed record UpdateStatus(
    UpdateState State,
    string CurrentVersion,
    string? TargetVersion = null,
    long? ReleaseSize = null,
    int Progress = 0,
    string? Message = null)
{
    internal static UpdateStatus Idle(string currentVersion) =>
        new(UpdateState.Idle, currentVersion, Message: "Ready");
}

internal interface IUpdateClient
{
    bool IsInstalled { get; }

    string CurrentVersion { get; }

    Task<UpdateCandidate?> CheckForUpdatesAsync();

    Task DownloadAsync(
        UpdateCandidate candidate,
        Action<int> progress,
        CancellationToken cancellationToken);

    void WaitExitThenApplyAndRestart(UpdateCandidate candidate);
}

internal sealed record UpdateFeedConfiguration(
    string Channel,
    string SourceDescription,
    Func<IUpdateSource> CreateSource,
    Func<bool> IsAvailable)
{
    internal const string PublicRepository = "https://github.com/HaikonCF/BeatInsight";
    internal const string DefaultChannel = "stable";
    internal const string LocalFeedEnvironmentVariable = "BEATINSIGHT_UPDATE_FEED";

    internal static UpdateFeedConfiguration CreateDefault()
    {
        string? localFeed = Environment.GetEnvironmentVariable(LocalFeedEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(localFeed))
        {
            string fullPath = Path.GetFullPath(localFeed);
            return new(
                DefaultChannel,
                $"local:{fullPath}",
                () => new SimpleFileSource(new DirectoryInfo(fullPath)),
                () => Directory.Exists(fullPath));
        }

        return new(
            DefaultChannel,
            PublicRepository,
            () => new GithubSource(
                PublicRepository,
                accessToken: null!,
                prerelease: false,
                downloader: new HttpClientFileDownloader()),
            () => true);
    }
}

internal sealed class VelopackUpdateClient : IUpdateClient
{
    private readonly UpdateManager manager;
    private readonly Func<bool> sourceAvailable;

    internal VelopackUpdateClient(UpdateFeedConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        sourceAvailable = configuration.IsAvailable;
        manager = new UpdateManager(
            configuration.CreateSource(),
            new UpdateOptions
            {
                ExplicitChannel = configuration.Channel,
                AllowVersionDowngrade = false
            });
    }

    public bool IsInstalled => manager.IsInstalled;

    public string CurrentVersion => manager.CurrentVersion?.ToString() ?? ReleaseInfo.Version;

    public async Task<UpdateCandidate?> CheckForUpdatesAsync()
    {
        if (!sourceAvailable())
            throw new DirectoryNotFoundException("The configured update feed is unavailable.");

        UpdateInfo? update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
        return update is null
            ? null
            : new UpdateCandidate(
                update.TargetFullRelease.Version.ToString(),
                update.TargetFullRelease.Size,
                update);
    }

    public Task DownloadAsync(
        UpdateCandidate candidate,
        Action<int> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return manager.DownloadUpdatesAsync(
            (UpdateInfo)candidate.NativeUpdate,
            progress,
            cancellationToken);
    }

    public void WaitExitThenApplyAndRestart(UpdateCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        UpdateInfo update = (UpdateInfo)candidate.NativeUpdate;
        manager.WaitExitThenApplyUpdates(
            update.TargetFullRelease,
            silent: false,
            restart: true,
            restartArgs: []);
    }
}

internal sealed class UpdateService : IDisposable
{
    private readonly IUpdateClient client;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly object sync = new();
    private UpdateCandidate? candidate;
    private UpdateStatus status;
    private int applyStarted;
    private int disposed;

    internal UpdateService(IUpdateClient client)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        status = UpdateStatus.Idle(client.CurrentVersion);
    }

    internal event Action<UpdateStatus>? StatusChanged;

    internal UpdateStatus Status
    {
        get
        {
            lock (sync)
                return status;
        }
    }

    internal bool IsInstalled => client.IsInstalled;

    internal async Task<bool> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!client.IsInstalled
            || Volatile.Read(ref disposed) != 0
            || cancellationToken.IsCancellationRequested)
            return false;

        bool entered;
        try
        {
            entered = await operationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        if (!entered)
            return false;

        try
        {
            candidate = null;
            Publish(new UpdateStatus(
                UpdateState.Checking,
                client.CurrentVersion,
                Message: "Checking for updates..."));
            UpdateCandidate? found = await client.CheckForUpdatesAsync().ConfigureAwait(false);
            candidate = found;
            if (found is null)
            {
                Publish(new UpdateStatus(
                    UpdateState.UpToDate,
                    client.CurrentVersion,
                    Message: "Up to date"));
            }
            else
            {
                Publish(new UpdateStatus(
                    UpdateState.Available,
                    client.CurrentVersion,
                    found.Version,
                    found.Size,
                    Message: $"BeatInsight {found.Version} is available"));
            }

            return true;
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested || lifetimeCancellation.IsCancellationRequested)
        {
            Publish(UpdateStatus.Idle(client.CurrentVersion));
            return false;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            DebugLogger.Warning($"UPDATE CHECK failed | {exception.GetType().Name}");
            Publish(new UpdateStatus(
                UpdateState.Failed,
                client.CurrentVersion,
                Message: "Unable to check for updates"));
            return false;
        }
        finally
        {
            operationGate.Release();
        }
    }

    internal async Task<bool> DownloadAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref disposed) != 0
            || cancellationToken.IsCancellationRequested
            || candidate is null
            || Status.State != UpdateState.Available)
            return false;

        bool entered;
        try
        {
            entered = await operationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        if (!entered)
            return false;

        UpdateCandidate target = candidate;
        try
        {
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
            Publish(new UpdateStatus(
                UpdateState.Downloading,
                client.CurrentVersion,
                target.Version,
                target.Size,
                Message: "Downloading update..."));
            await client.DownloadAsync(
                target,
                progress => Publish(new UpdateStatus(
                    UpdateState.Downloading,
                    client.CurrentVersion,
                    target.Version,
                    target.Size,
                    Math.Clamp(progress, 0, 100),
                    $"Downloading update... {Math.Clamp(progress, 0, 100)}%")),
                linked.Token).ConfigureAwait(false);
            Publish(new UpdateStatus(
                UpdateState.ReadyToRestart,
                client.CurrentVersion,
                target.Version,
                target.Size,
                100,
                "Update ready. Restart BeatInsight to update."));
            return true;
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested || lifetimeCancellation.IsCancellationRequested)
        {
            Publish(new UpdateStatus(
                UpdateState.Available,
                client.CurrentVersion,
                target.Version,
                target.Size,
                Message: $"BeatInsight {target.Version} is available"));
            return false;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            DebugLogger.Warning($"UPDATE DOWNLOAD failed | {exception.GetType().Name}");
            Publish(new UpdateStatus(
                UpdateState.Failed,
                client.CurrentVersion,
                target.Version,
                target.Size,
                Message: "Unable to update"));
            return false;
        }
        finally
        {
            operationGate.Release();
        }
    }

    internal bool TryBeginApply(out UpdateCandidate? update)
    {
        update = null;
        if (Volatile.Read(ref disposed) != 0
            || Status.State != UpdateState.ReadyToRestart
            || candidate is null
            || Interlocked.CompareExchange(ref applyStarted, 1, 0) != 0)
        {
            return false;
        }

        update = candidate;
        Publish(new UpdateStatus(
            UpdateState.Applying,
            client.CurrentVersion,
            candidate.Version,
            candidate.Size,
            100,
            "Restarting to apply update..."));
        return true;
    }

    internal void WaitExitThenApplyAndRestart(UpdateCandidate update) =>
        client.WaitExitThenApplyAndRestart(update);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        lifetimeCancellation.Cancel();
        lifetimeCancellation.Dispose();
    }

    private void Publish(UpdateStatus next)
    {
        lock (sync)
            status = next;
        StatusChanged?.Invoke(next);
    }
}
