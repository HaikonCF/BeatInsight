using BeatInsight.Diagnostics.NativeLazer;
using BeatInsight.Integration.Game;
using System.Diagnostics;
using System.IO;

namespace BeatInsight.Tests.Integration.Game;

public sealed class ShutdownLifecycleTests
{
    [Fact]
    public void HelperLaunch_PrefersSelfContainedExecutableFromApplicationDirectory()
    {
        using var directory = new TemporaryDirectory();
        string helperDirectory = Path.Combine(directory.Path, "NativeLazer");
        Directory.CreateDirectory(helperDirectory);
        string executable = Path.Combine(helperDirectory, "NativeLazerFastRefreshRunner.exe");
        File.WriteAllBytes(executable, []);

        ProcessStartInfo startInfo = NativeLazerHelperClient.CreateStartInfo(directory.Path);

        Assert.Equal(executable, startInfo.FileName);
        Assert.Equal(string.Empty, startInfo.Arguments);
        Assert.Equal(helperDirectory, startInfo.WorkingDirectory);
    }

    [Fact]
    public void HelperLaunch_UsesDotnetOnlyForPackagedDevelopmentDll()
    {
        using var directory = new TemporaryDirectory();
        string helperDirectory = Path.Combine(directory.Path, "NativeLazer");
        Directory.CreateDirectory(helperDirectory);
        string dll = Path.Combine(helperDirectory, "NativeLazerFastRefreshRunner.dll");
        File.WriteAllBytes(dll, []);

        ProcessStartInfo startInfo = NativeLazerHelperClient.CreateStartInfo(directory.Path);

        Assert.Equal("dotnet", startInfo.FileName);
        Assert.Equal($"\"{dll}\"", startInfo.Arguments);
        Assert.Equal(helperDirectory, startInfo.WorkingDirectory);
    }

    [Fact]
    public void HelperLaunch_FailsClosedWhenApplicationPackageIsIncomplete()
    {
        using var directory = new TemporaryDirectory();

        FileNotFoundException exception = Assert.Throws<FileNotFoundException>(
            () => NativeLazerHelperClient.CreateStartInfo(directory.Path));

        Assert.EndsWith(
            Path.Combine("NativeLazer", "NativeLazerFastRefreshRunner.exe"),
            exception.FileName,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GameStateService_DisposesEachProviderExactlyOnce()
    {
        var provider = new DisposableProvider();
        var service = new GameStateService([provider, provider]);

        service.Dispose();
        service.Dispose();

        Assert.Equal(1, provider.DisposeCount);
    }

    [Fact]
    public void GameStateService_RefreshIsIgnoredAfterDispose()
    {
        var provider = new DisposableProvider();
        var service = new GameStateService(provider);
        service.Dispose();

        GameStateSnapshot snapshot = service.Refresh();

        Assert.Equal(0, provider.RefreshCount);
        Assert.False(snapshot.IsAvailable);
        Assert.Equal("Service is shutting down.", snapshot.DiagnosticReason);
    }

    [Fact]
    public void LazerProvider_ShutdownBlocksProbeAndHelperRestart()
    {
        var runtime = new ShutdownAwareRuntime();
        var provider = new LazerGameProvider(runtime);

        provider.Dispose();
        provider.Refresh();

        Assert.Equal(1, runtime.ShutdownCount);
        Assert.Equal(0, runtime.ProbeCount);
        Assert.Equal(0, runtime.RefreshCount);
    }

    [Fact]
    public void HelperShutdown_ClosesStdinOnceAndObservesExit()
    {
        var process = new FakeHelperProcess(waitResult: true);
        var client = new NativeLazerHelperClient(() => process);
        _ = client.Refresh();

        NativeLazerHelperShutdownResult result = client.Shutdown();
        NativeLazerHelperShutdownResult second = client.Shutdown();

        Assert.True(result.HadProcess);
        Assert.True(result.StdinClosed);
        Assert.True(result.Exited);
        Assert.False(result.TimedOut);
        Assert.Equal(1, process.CloseInputCount);
        Assert.Equal(1, process.WaitCount);
        Assert.Equal(1, process.DisposeCount);
        Assert.False(second.HadProcess);
    }

    [Fact]
    public void HelperShutdown_TimeoutIsBoundedAndNeverKills()
    {
        var process = new FakeHelperProcess(waitResult: false);
        var client = new NativeLazerHelperClient(
            () => process,
            TimeSpan.FromMilliseconds(25));
        _ = client.Refresh();
        Stopwatch watch = Stopwatch.StartNew();

        NativeLazerHelperShutdownResult result = client.Shutdown();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1));
        Assert.True(result.TimedOut);
        Assert.False(result.Exited);
        Assert.Equal(1, process.CloseInputCount);
        Assert.Equal(1, process.WaitCount);
        Assert.Equal(1, process.DisposeCount);
    }

    [Fact]
    public void HelperStart_IsBlockedAfterShutdownBegins()
    {
        int starts = 0;
        var client = new NativeLazerHelperClient(() =>
        {
            starts++;
            return new FakeHelperProcess(waitResult: true);
        });
        client.Shutdown();

        Assert.Throws<ObjectDisposedException>(() => client.Refresh());
        Assert.Equal(0, starts);
    }

    private sealed class DisposableProvider : IGameStateProvider, IDisposable
    {
        public string Name => "Disposable";
        public GameStateSnapshot Current { get; private set; } = Unavailable("not refreshed");
        internal int RefreshCount { get; private set; }
        internal int DisposeCount { get; private set; }

        public GameStateSnapshot Refresh()
        {
            RefreshCount++;
            return Current;
        }

        public void Dispose() => DisposeCount++;
    }

    private sealed class ShutdownAwareRuntime : INativeLazerGameRuntime
    {
        internal int ProbeCount { get; private set; }
        internal int RefreshCount { get; private set; }
        internal int ShutdownCount { get; private set; }

        public NativeLazerProcessProbe Probe()
        {
            ProbeCount++;
            return new(NativeLazerProcessProbeStatus.None);
        }

        public NativeLazerServerResponse Refresh()
        {
            RefreshCount++;
            return new();
        }

        public void Shutdown() => ShutdownCount++;
    }

    private sealed class FakeHelperProcess(bool waitResult) : INativeLazerHelperProcess
    {
        public bool HasExited { get; private set; }
        internal int CloseInputCount { get; private set; }
        internal int WaitCount { get; private set; }
        internal int DisposeCount { get; private set; }

        public void WriteCommand(string command)
        {
        }

        public string? ReadResponse() => "{}";

        public void CloseStandardInput() => CloseInputCount++;

        public bool WaitForExit(TimeSpan timeout)
        {
            WaitCount++;
            HasExited = waitResult;
            return waitResult;
        }

        public void Dispose() => DisposeCount++;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"BeatInsight-helper-launch-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private static GameStateSnapshot Unavailable(string reason) => new(
        "test",
        "test",
        null,
        null,
        null,
        null,
        null,
        DateTimeOffset.UtcNow,
        GameStateAvailability.ProviderUnavailable,
        reason);
}
