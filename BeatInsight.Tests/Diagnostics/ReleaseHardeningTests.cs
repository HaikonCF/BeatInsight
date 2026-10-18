using BeatInsight.Diagnostics;
using BeatInsight.Services.Library;
using BeatInsight.ViewModels;
using System.IO;
using System.Reflection;

namespace BeatInsight.Tests.Diagnostics;

public sealed class ReleaseHardeningTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"BeatInsight-release-hardening-{Guid.NewGuid():N}");

#if !DEBUG
    [Fact]
    public void ReleaseBuild_DisablesVerboseDebugLoggingByDefault()
    {
        Assert.False(DebugLogger.DebugMode);
        Assert.False(DebugLogger.DetailedDebug);
    }
#endif

    [Fact]
    public void ReleaseInfo_UsesInformationalVersion()
    {
        string? informationalVersion = typeof(App).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        Assert.Equal("3.0.0", informationalVersion);
        Assert.Equal("3.0.0", ReleaseInfo.Version);
        Assert.Equal("BeatInsight v3.0.0", ReleaseInfo.DisplayName);
        Assert.Equal(
            "BeatInsight v3.0.0",
            new MainWindowViewModel().AppVersionText);
    }

    [Fact]
    public void DefaultLogDirectory_IsUnderLocalApplicationData()
    {
        string localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(
            Path.GetFullPath(localApplicationData),
            Path.GetFullPath(DebugLogger.DefaultLogDirectory),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Writer_RetainsOnlyConfiguredNumberOfSessionLogs()
    {
        Directory.CreateDirectory(directory);
        DateTime now = DateTime.UtcNow;

        for (int index = 0; index < 12; index++)
        {
            string path = Path.Combine(
                directory,
                $"beatinsight-old-{index:D2}.log");
            File.WriteAllText(path, "old");
            File.SetLastWriteTimeUtc(path, now.AddMinutes(-index - 1));
        }

        var writer = new ReleaseLogWriter(
            directory,
            maxLogFiles: 8,
            maxFileBytes: 1024);

        writer.Initialize();

        Assert.Equal(
            8,
            Directory.EnumerateFiles(directory, "beatinsight-*.log").Count());
    }

    [Fact]
    public void Writer_RedactsCredentialsAndPersonalPath()
    {
        const string secret = "test-client-secret-should-not-appear";
        const string token = "test-access-token-should-not-appear";
        var writer = new ReleaseLogWriter(
            directory,
            maxLogFiles: 8,
            maxFileBytes: 4096);

        writer.Write(
            "ERROR",
            $"client_secret={secret} Authorization: Bearer {token} "
            + $"json={{\"access_token\":\"{token}\"}} "
            + $"path={Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}\\Maps");

        string log = File.ReadAllText(Assert.IsType<string>(writer.LogFilePath));
        Assert.DoesNotContain(secret, log, StringComparison.Ordinal);
        Assert.DoesNotContain(token, log, StringComparison.Ordinal);
        Assert.DoesNotContain(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            log,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[REDACTED]", log, StringComparison.Ordinal);
        Assert.Contains("%USERPROFILE%", log, StringComparison.Ordinal);
    }

    [Fact]
    public void Writer_StopsBeforeConfiguredSizeCap()
    {
        var writer = new ReleaseLogWriter(
            directory,
            maxLogFiles: 8,
            maxFileBytes: 256);

        for (int index = 0; index < 100; index++)
        {
            writer.Write("INFO", new string('x', 80));
        }

        var log = new FileInfo(Assert.IsType<string>(writer.LogFilePath));
        Assert.InRange(log.Length, 1, 256);
    }

    [Fact]
    public void LibraryFailureLog_RedactsCredentialShapedExceptionMessage()
    {
        const string secret = "test-library-secret-should-not-appear";
        string logPath = Path.Combine(directory, "library-failures.log");
        var logger = new LibraryScanFailureLogger(logPath);

        logger.LogFailure(
            "broken.osu",
            new InvalidOperationException($"client_secret={secret}"));

        string log = File.ReadAllText(logPath);
        Assert.DoesNotContain(secret, log, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", log, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpectedCancellation_IsNotTreatedAsCrash()
    {
        Assert.True(GlobalExceptionHandler.IsExpectedCancellation(
            new OperationCanceledException()));
        Assert.True(GlobalExceptionHandler.IsExpectedCancellation(
            new TaskCanceledException()));
        Assert.True(GlobalExceptionHandler.IsExpectedCancellation(
            new AggregateException(
                new OperationCanceledException(),
                new TaskCanceledException())));
        Assert.False(GlobalExceptionHandler.IsExpectedCancellation(
            new AggregateException(
                new OperationCanceledException(),
                new InvalidOperationException())));
    }

    [Fact]
    public void CrashReport_ContainsContextAndRedactsSecret()
    {
        const string secret = "test-crash-secret-should-not-appear";
        var exception = new InvalidOperationException(
            $"client_secret={secret}",
            new ArgumentException("inner failure"));

        string report = GlobalExceptionHandler.FormatCrashReport(
            exception,
            "test",
            isFatal: true);

        Assert.Contains($"Version: 3.0.0{Environment.NewLine}", report, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException", report, StringComparison.Ordinal);
        Assert.Contains("Inner exception 1", report, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, report, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", report, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
