using Microsoft.Win32;
using System.IO;
using System.Security;

namespace BeatInsight.Services.Settings;

internal readonly record struct StartupRegistrationState(
    bool IsEnabled,
    bool Success,
    string? FailureReason = null);

internal interface IStartupRegistrationService
{
    bool IsEnabled { get; }
    StartupRegistrationState GetState();
    StartupRegistrationState Enable();
    StartupRegistrationState Disable();
    StartupRegistrationState RepairIfNeeded();
}

internal interface IStartupRegistry
{
    string? ReadValue();
    void WriteValue(string command);
    void DeleteValue();
}

internal sealed class WindowsStartupRegistrationService : IStartupRegistrationService
{
    internal const string ValueName = "BeatInsight";
    private readonly IStartupRegistry registry;
    private readonly Func<string?> executablePath;

    public WindowsStartupRegistrationService()
        : this(new CurrentUserRunRegistry(), () => Environment.ProcessPath)
    {
    }

    internal WindowsStartupRegistrationService(
        IStartupRegistry registry,
        Func<string?> executablePath)
    {
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        this.executablePath = executablePath ?? throw new ArgumentNullException(nameof(executablePath));
    }

    public bool IsEnabled => GetState().IsEnabled;

    public StartupRegistrationState GetState()
    {
        try
        {
            return EnabledState(registry.ReadValue());
        }
        catch (Exception exception) when (IsRegistryFailure(exception))
        {
            return new StartupRegistrationState(false, false, FailureReason(exception));
        }
    }

    public StartupRegistrationState Enable()
    {
        if (!TryGetExpectedCommand(out string command))
            return CurrentStateAfterFailure("ExecutablePathUnavailable");

        try
        {
            registry.WriteValue(command);
            return new StartupRegistrationState(true, true);
        }
        catch (Exception exception) when (IsRegistryFailure(exception))
        {
            return CurrentStateAfterFailure(FailureReason(exception));
        }
    }

    public StartupRegistrationState Disable()
    {
        try
        {
            registry.DeleteValue();
            return new StartupRegistrationState(false, true);
        }
        catch (Exception exception) when (IsRegistryFailure(exception))
        {
            return CurrentStateAfterFailure(FailureReason(exception));
        }
    }

    public StartupRegistrationState RepairIfNeeded()
    {
        string? existing;
        try
        {
            existing = registry.ReadValue();
        }
        catch (Exception exception) when (IsRegistryFailure(exception))
        {
            return new StartupRegistrationState(false, false, FailureReason(exception));
        }

        if (existing is null)
            return new StartupRegistrationState(false, true);

        if (!TryGetExpectedCommand(out string expected))
            return new StartupRegistrationState(true, false, "ExecutablePathUnavailable");

        if (string.Equals(existing.Trim(), expected, StringComparison.OrdinalIgnoreCase))
            return new StartupRegistrationState(true, true);

        try
        {
            registry.WriteValue(expected);
            return new StartupRegistrationState(true, true);
        }
        catch (Exception exception) when (IsRegistryFailure(exception))
        {
            return CurrentStateAfterFailure(FailureReason(exception));
        }
    }

    internal static string BuildCommand(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        return $"\"{Path.GetFullPath(executablePath)}\"";
    }

    internal static bool RemoveIfTargetsInstallation(string? installationRoot)
        => RemoveIfTargetsInstallation(new CurrentUserRunRegistry(), installationRoot);

    internal static bool RemoveIfTargetsInstallation(
        IStartupRegistry registry,
        string? installationRoot)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (string.IsNullOrWhiteSpace(installationRoot))
            return false;

        try
        {
            string? command = registry.ReadValue();
            if (!TryExtractExecutablePath(command, out string executablePath) ||
                !IsPathInside(executablePath, installationRoot))
            {
                return false;
            }

            registry.DeleteValue();
            return true;
        }
        catch (Exception exception) when (IsRegistryFailure(exception))
        {
            return false;
        }
    }

    internal static bool TryExtractExecutablePath(string? command, out string executablePath)
    {
        executablePath = string.Empty;
        if (string.IsNullOrWhiteSpace(command))
            return false;

        string trimmed = command.Trim();
        if (trimmed[0] == '"')
        {
            int closingQuote = trimmed.IndexOf('"', 1);
            if (closingQuote <= 1)
                return false;
            executablePath = trimmed[1..closingQuote];
        }
        else
        {
            int separator = trimmed.IndexOf(' ');
            executablePath = separator < 0 ? trimmed : trimmed[..separator];
        }

        try
        {
            executablePath = Path.GetFullPath(executablePath);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            executablePath = string.Empty;
            return false;
        }
    }

    internal static bool IsPathInside(string candidatePath, string directoryPath)
    {
        try
        {
            string candidate = Path.GetFullPath(candidatePath);
            string directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));
            return candidate.StartsWith(directory + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private bool TryGetExpectedCommand(out string command)
    {
        try
        {
            string? path = executablePath();
            if (string.IsNullOrWhiteSpace(path))
            {
                command = string.Empty;
                return false;
            }

            command = BuildCommand(path);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            command = string.Empty;
            return false;
        }
    }

    private StartupRegistrationState CurrentStateAfterFailure(string reason)
    {
        StartupRegistrationState current = GetState();
        return new StartupRegistrationState(
            current.IsEnabled,
            false,
            current.Success ? reason : $"{reason};ReadBack{current.FailureReason}");
    }

    private static StartupRegistrationState EnabledState(string? value) =>
        new(value is not null, true);

    private static bool IsRegistryFailure(Exception exception) =>
        exception is UnauthorizedAccessException or SecurityException or IOException or ArgumentException;

    private static string FailureReason(Exception exception) => exception.GetType().Name;
}

internal sealed class CurrentUserRunRegistry : IStartupRegistry
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? ReadValue()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(WindowsStartupRegistrationService.ValueName) as string;
    }

    public void WriteValue(string command)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new IOException("Current-user Run registry key is unavailable.");
        key.SetValue(WindowsStartupRegistrationService.ValueName, command, RegistryValueKind.String);
    }

    public void DeleteValue()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(WindowsStartupRegistrationService.ValueName, throwOnMissingValue: false);
    }
}
