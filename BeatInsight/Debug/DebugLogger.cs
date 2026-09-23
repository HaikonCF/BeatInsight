using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace BeatInsight.Diagnostics;

public static class DebugLogger
{
#if DEBUG
    public static bool DebugMode = true;
#else
    public static bool DebugMode = false;
#endif

    public static bool DetailedDebug = false;

    public static bool IdentityEnabled = false;
    public static bool TechEnabled = true;
    public static bool TechV2ExperimentalEnabled = false;
    public static bool TechV2RegionExperimentalEnabled = false;
    public static bool ReadEnabled = true;
    public static bool SpeedEnabled = false;
    public static bool AimEnabled = false;
    public static bool SummaryEnabled = false;

    private static readonly ReleaseLogWriter Writer = new(
        DefaultLogDirectory,
        maxLogFiles: 8,
        maxFileBytes: 5 * 1024 * 1024);

    internal static string DefaultLogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BeatInsight",
        "Logs");

    public static string? CurrentLogFilePath => Writer.LogFilePath;

    public static void Initialize() => Writer.Initialize();

    public static void Info(string message) => Writer.Write("INFO", message);

    public static void Warning(string message) => Writer.Write("WARN", message);

    public static void Error(string message) => Writer.Write("ERROR", message);

    public static void Log(string message)
    {
        if (!DebugMode)
        {
            return;
        }

        Writer.Write("DEBUG", message);
    }

    public static void Detailed(string message)
    {
        if (!DebugMode || !DetailedDebug)
        {
            return;
        }

        Writer.Write("TRACE", message);
    }

    public static void Section(string title)
    {
        if (!DebugMode)
        {
            return;
        }

        Log(string.Empty);
        Log($"===== {title} =====");
    }

    public static void NewMap(string map, string difficulty)
    {
        if (!DebugMode)
        {
            return;
        }

        Log(string.Empty);
        Log("============================================================");
        Log("NEW MAP");
        Log($"MAP = {map}");
        Log($"DIFFICULTY = {difficulty}");
        Log("============================================================");
    }

    internal static string RedactSensitiveData(string message) =>
        LogPrivacy.Redact(message);
}

internal sealed class ReleaseLogWriter
{
    private readonly object writeLock = new();
    private readonly string directory;
    private readonly int maxLogFiles;
    private readonly long maxFileBytes;
    private readonly Func<DateTimeOffset> utcNow;
    private bool initialized;

    internal ReleaseLogWriter(
        string directory,
        int maxLogFiles,
        long maxFileBytes,
        Func<DateTimeOffset>? utcNow = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        this.directory = directory;
        this.maxLogFiles = Math.Max(1, maxLogFiles);
        this.maxFileBytes = Math.Max(1, maxFileBytes);
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    internal string? LogFilePath { get; private set; }

    internal void Initialize()
    {
        lock (writeLock)
        {
            InitializeCore();
        }
    }

    internal void Write(string level, string message)
    {
        try
        {
            lock (writeLock)
            {
                InitializeCore();

                if (LogFilePath is null)
                {
                    return;
                }

                string safeMessage = LogPrivacy.Redact(message ?? string.Empty);
                string line = string.Create(
                    CultureInfo.InvariantCulture,
                    $"[{utcNow():O}] [{level}] {safeMessage}{Environment.NewLine}");
                byte[] bytes = Encoding.UTF8.GetBytes(line);
                long currentLength = new FileInfo(LogFilePath).Length;

                if (currentLength + bytes.Length > maxFileBytes)
                {
                    return;
                }

                using var stream = new FileStream(
                    LogFilePath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite);
                stream.Write(bytes);
                Debug.WriteLine(line.TrimEnd());
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[DebugLogger] File logging failed: {ex.GetType().Name}");
        }
    }

    private void InitializeCore()
    {
        if (initialized)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(directory);
            string timestamp = utcNow().ToString(
                "yyyyMMdd-HHmmssfff",
                CultureInfo.InvariantCulture);
            LogFilePath = Path.Combine(
                directory,
                $"beatinsight-{timestamp}-{Environment.ProcessId}-{Guid.NewGuid():N}.log");

            using (new FileStream(
                LogFilePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.ReadWrite))
            {
            }

            PruneOldLogs();
        }
        catch (Exception ex)
        {
            LogFilePath = null;
            Debug.WriteLine(
                $"[DebugLogger] Initialization failed: {ex.GetType().Name}");
        }
        finally
        {
            initialized = true;
        }
    }

    private void PruneOldLogs()
    {
        FileInfo[] logs = new DirectoryInfo(directory)
            .EnumerateFiles("beatinsight-*.log", SearchOption.TopDirectoryOnly)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .ToArray();

        foreach (FileInfo oldLog in logs.Skip(maxLogFiles))
        {
            try
            {
                oldLog.Delete();
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

internal static partial class LogPrivacy
{
    [GeneratedRegex(
        @"(?i)(?<prefix>Authorization\s*(?:=|:)\s*Bearer\s+)(?<value>[^\s,;]+)")]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(
        "(?i)(?<prefix>[\\\"']?(?:client[_\\s-]?secret|access[_\\s-]?token|refresh[_\\s-]?token|password|protectedsecret|BEATINSIGHT_OSU_CLIENT_SECRET)[\\\"']?\\s*(?:=|:)\\s*)(?<value>\\\"[^\\\"]*\\\"|'[^']*'|[^\\s,&;]+)")]
    private static partial Regex SensitiveAssignmentRegex();

    internal static string Redact(string value)
    {
        string redacted = BearerTokenRegex().Replace(
            value,
            "${prefix}[REDACTED]");
        redacted = SensitiveAssignmentRegex().Replace(
            redacted,
            "${prefix}[REDACTED]");

        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        string userProfile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);

        redacted = ReplacePath(
            redacted,
            localAppData,
            "%LOCALAPPDATA%");
        return ReplacePath(redacted, userProfile, "%USERPROFILE%");
    }

    private static string ReplacePath(
        string value,
        string path,
        string replacement) => string.IsNullOrWhiteSpace(path)
            ? value
            : Regex.Replace(
                value,
                Regex.Escape(path),
                replacement.Replace("$", "$$"),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
