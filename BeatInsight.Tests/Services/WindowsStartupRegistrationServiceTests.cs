using BeatInsight.Services.Settings;
using BeatInsight.ViewModels;
using System.IO;

namespace BeatInsight.Tests.Services;

public sealed class WindowsStartupRegistrationServiceTests
{
    private const string CurrentExecutable = @"C:\Program Files\BeatInsight\BeatInsight.exe";

    [Fact]
    public void MissingRegistryValue_IsDisabledByDefault()
    {
        var registry = new FakeStartupRegistry();
        var service = CreateService(registry);

        StartupRegistrationState state = service.GetState();

        Assert.True(state.Success);
        Assert.False(state.IsEnabled);
        Assert.False(service.IsEnabled);
    }

    [Fact]
    public void Enable_WritesCurrentExecutableAsQuotedFullPath()
    {
        var registry = new FakeStartupRegistry();
        var service = CreateService(registry);

        StartupRegistrationState state = service.Enable();

        Assert.True(state.Success);
        Assert.True(state.IsEnabled);
        Assert.Equal($"\"{CurrentExecutable}\"", registry.Value);
    }

    [Fact]
    public void BuildCommand_QuotesPathWithoutSpacesToo()
    {
        string command = WindowsStartupRegistrationService.BuildCommand(
            @"C:\BeatInsight\BeatInsight.exe");

        Assert.Equal("\"C:\\BeatInsight\\BeatInsight.exe\"", command);
    }

    [Fact]
    public void UninstallTarget_RecognizesExecutableInsideInstallRoot()
    {
        Assert.True(WindowsStartupRegistrationService.IsPathInside(
            @"C:\Users\test\AppData\Local\BeatInsight\current\BeatInsight.exe",
            @"C:\Users\test\AppData\Local\BeatInsight"));
    }

    [Fact]
    public void UninstallTarget_RejectsSiblingPathWithSamePrefix()
    {
        Assert.False(WindowsStartupRegistrationService.IsPathInside(
            @"C:\Users\test\AppData\Local\BeatInsight-Other\BeatInsight.exe",
            @"C:\Users\test\AppData\Local\BeatInsight"));
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\BeatInsight\\BeatInsight.exe\"")]
    [InlineData("\"C:\\Program Files\\BeatInsight\\BeatInsight.exe\" --hidden")]
    public void StartupCommand_ExtractsQuotedExecutable(string command)
    {
        Assert.True(WindowsStartupRegistrationService.TryExtractExecutablePath(
            command, out string path));
        Assert.Equal(@"C:\Program Files\BeatInsight\BeatInsight.exe", path);
    }

    [Fact]
    public void UninstallCleanup_RemovesOnlyRegistrationInsideInstallRoot()
    {
        var matching = new FakeStartupRegistry
        {
            Value = "\"C:\\Users\\test\\AppData\\Local\\BeatInsight\\current\\BeatInsight.exe\""
        };
        var unrelated = new FakeStartupRegistry
        {
            Value = "\"D:\\Portable\\BeatInsight.exe\""
        };

        Assert.True(WindowsStartupRegistrationService.RemoveIfTargetsInstallation(
            matching, @"C:\Users\test\AppData\Local\BeatInsight"));
        Assert.Null(matching.Value);
        Assert.Equal(1, matching.DeleteCount);

        Assert.False(WindowsStartupRegistrationService.RemoveIfTargetsInstallation(
            unrelated, @"C:\Users\test\AppData\Local\BeatInsight"));
        Assert.Equal("\"D:\\Portable\\BeatInsight.exe\"", unrelated.Value);
        Assert.Equal(0, unrelated.DeleteCount);
    }

    [Fact]
    public void Disable_RemovesOnlyBeatInsightValue()
    {
        var registry = new FakeStartupRegistry { Value = "old command" };
        var service = CreateService(registry);

        StartupRegistrationState state = service.Disable();

        Assert.True(state.Success);
        Assert.False(state.IsEnabled);
        Assert.Equal(1, registry.DeleteCount);
        Assert.Null(registry.Value);
    }

    [Fact]
    public void Disable_IsIdempotent()
    {
        var registry = new FakeStartupRegistry();
        var service = CreateService(registry);

        Assert.True(service.Disable().Success);
        Assert.True(service.Disable().Success);
        Assert.False(service.IsEnabled);
    }

    [Fact]
    public void RepairIfNeeded_ReplacesOldExecutablePath()
    {
        var registry = new FakeStartupRegistry
        {
            Value = "\"C:\\Old Folder\\BeatInsight.exe\""
        };
        var service = CreateService(registry);

        StartupRegistrationState state = service.RepairIfNeeded();

        Assert.True(state.Success);
        Assert.True(state.IsEnabled);
        Assert.Equal($"\"{CurrentExecutable}\"", registry.Value);
        Assert.Equal(1, registry.WriteCount);
    }

    [Fact]
    public void RepairIfNeeded_LeavesCorrectCommandUntouched()
    {
        var registry = new FakeStartupRegistry
        {
            Value = $"\"{CurrentExecutable}\""
        };
        var service = CreateService(registry);

        StartupRegistrationState state = service.RepairIfNeeded();

        Assert.True(state.Success);
        Assert.True(state.IsEnabled);
        Assert.Equal(0, registry.WriteCount);
    }

    [Fact]
    public void RepairIfNeeded_DoesNotEnableMissingRegistration()
    {
        var registry = new FakeStartupRegistry();
        var service = CreateService(registry);

        StartupRegistrationState state = service.RepairIfNeeded();

        Assert.True(state.Success);
        Assert.False(state.IsEnabled);
        Assert.Equal(0, registry.WriteCount);
    }

    [Fact]
    public void RepairIfNeeded_RepairsPresentButEmptyValue()
    {
        var registry = new FakeStartupRegistry { Value = string.Empty };
        var service = CreateService(registry);

        StartupRegistrationState state = service.RepairIfNeeded();

        Assert.True(state.Success);
        Assert.True(state.IsEnabled);
        Assert.Equal($"\"{CurrentExecutable}\"", registry.Value);
    }

    [Fact]
    public void Enable_WithUnavailableExecutable_FailsClosedWithoutWrite()
    {
        var registry = new FakeStartupRegistry();
        var service = new WindowsStartupRegistrationService(registry, () => null);

        StartupRegistrationState state = service.Enable();

        Assert.False(state.Success);
        Assert.False(state.IsEnabled);
        Assert.Equal("ExecutablePathUnavailable", state.FailureReason);
        Assert.Equal(0, registry.WriteCount);
    }

    [Fact]
    public void Enable_WriteFailure_ReturnsActualDisabledState()
    {
        var registry = new FakeStartupRegistry { WriteException = new UnauthorizedAccessException() };
        var service = CreateService(registry);

        StartupRegistrationState state = service.Enable();

        Assert.False(state.Success);
        Assert.False(state.IsEnabled);
        Assert.Equal("UnauthorizedAccessException", state.FailureReason);
    }

    [Fact]
    public void Disable_DeleteFailure_ReturnsActualEnabledState()
    {
        var registry = new FakeStartupRegistry
        {
            Value = "old command",
            DeleteException = new UnauthorizedAccessException()
        };
        var service = CreateService(registry);

        StartupRegistrationState state = service.Disable();

        Assert.False(state.Success);
        Assert.True(state.IsEnabled);
    }

    [Fact]
    public void ReadFailure_DoesNotThrowAndReportsFailure()
    {
        var registry = new FakeStartupRegistry { ReadException = new IOException() };
        var service = CreateService(registry);

        StartupRegistrationState state = service.GetState();

        Assert.False(state.Success);
        Assert.False(state.IsEnabled);
        Assert.Equal("IOException", state.FailureReason);
    }

    [Fact]
    public void Settings_DefaultStartMinimized_IsFalse()
    {
        Assert.False(WindowLifecycleSettings.Default.StartMinimized);
    }

    [Fact]
    public void SettingsStore_RoundTripsStartMinimizedIndependently()
    {
        using var directory = new TemporaryDirectory();
        var store = new WindowLifecycleSettingsStore(
            System.IO.Path.Combine(directory.Path, "window.json"));

        Assert.True(store.Save(new WindowLifecycleSettings(1, false, true)));
        WindowLifecycleSettings loaded = store.Load();

        Assert.False(loaded.CloseToTray);
        Assert.True(loaded.StartMinimized);
    }

    [Fact]
    public void SettingsStore_LegacyDocumentDefaultsStartMinimizedToFalse()
    {
        using var directory = new TemporaryDirectory();
        string path = System.IO.Path.Combine(directory.Path, "window.json");
        System.IO.File.WriteAllText(path, "{\"version\":1,\"closeToTray\":false}");
        var store = new WindowLifecycleSettingsStore(path);

        WindowLifecycleSettings loaded = store.Load();

        Assert.False(loaded.CloseToTray);
        Assert.False(loaded.StartMinimized);
    }

    [Fact]
    public void ViewModel_StartWithWindows_DoesNotChangeLocalSettings()
    {
        var viewModel = new WindowLifecycleSettingsViewModel();
        int settingsChanges = 0;
        bool? requested = null;
        viewModel.SettingsChanged += _ => settingsChanges++;
        viewModel.StartWithWindowsChangeRequested += value => requested = value;

        viewModel.StartWithWindows = true;

        Assert.True(requested);
        Assert.Equal(0, settingsChanges);
        Assert.False(viewModel.Current.StartMinimized);
    }

    [Fact]
    public void ViewModel_RegistryRollback_DoesNotRequestAnotherWrite()
    {
        var viewModel = new WindowLifecycleSettingsViewModel();
        int requests = 0;
        viewModel.StartWithWindowsChangeRequested += _ => requests++;
        viewModel.StartWithWindows = true;

        viewModel.ApplyStartupRegistration(false);

        Assert.False(viewModel.StartWithWindows);
        Assert.Equal(1, requests);
    }

    [Fact]
    public void ViewModel_StartMinimized_PersistsWithoutChangingStartupRegistration()
    {
        var viewModel = new WindowLifecycleSettingsViewModel();
        int registrationRequests = 0;
        WindowLifecycleSettings? changed = null;
        viewModel.StartWithWindowsChangeRequested += _ => registrationRequests++;
        viewModel.SettingsChanged += settings => changed = settings;

        viewModel.StartMinimized = true;

        Assert.NotNull(changed);
        Assert.True(changed!.StartMinimized);
        Assert.Equal(0, registrationRequests);
    }

    private static WindowsStartupRegistrationService CreateService(FakeStartupRegistry registry) =>
        new(registry, () => CurrentExecutable);

    private sealed class FakeStartupRegistry : IStartupRegistry
    {
        public string? Value { get; set; }
        public Exception? ReadException { get; init; }
        public Exception? WriteException { get; init; }
        public Exception? DeleteException { get; init; }
        public int WriteCount { get; private set; }
        public int DeleteCount { get; private set; }

        public string? ReadValue()
        {
            if (ReadException is not null)
                throw ReadException;
            return Value;
        }

        public void WriteValue(string command)
        {
            if (WriteException is not null)
                throw WriteException;
            Value = command;
            WriteCount++;
        }

        public void DeleteValue()
        {
            if (DeleteException is not null)
                throw DeleteException;
            Value = null;
            DeleteCount++;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "BeatInsightStartupTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
