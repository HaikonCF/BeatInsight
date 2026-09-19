using BeatInsight.Models.Library;
using System.IO;

namespace BeatInsight.ViewModels;

public sealed class LibraryPanelViewModel : ViewModelBase
{
    private string songsFolderText = "Not set";
    private bool isScanning;
    private bool canChangeFolder = true;
    private bool canScan = true;
    private bool canCancel;
    private bool hasProgress;
    private double progressValue;
    private string statusText = "Ready";
    private string progressPercentText = "0.0%";
    private string processedText = "Processed: 0 / 0";
    private string analyzedText = "Analyzed: 0";
    private string upToDateText = "Up to date: 0";
    private string unsupportedText = "Unsupported: 0";
    private string failedText = "Failed: 0";
    private string currentFileText = "Current file: —";
    private string? currentFileToolTip;

    public string SongsFolderText
    {
        get => songsFolderText;
        private set => SetProperty(ref songsFolderText, value);
    }

    public bool IsScanning
    {
        get => isScanning;
        private set => SetProperty(ref isScanning, value);
    }

    public bool CanChangeFolder
    {
        get => canChangeFolder;
        private set => SetProperty(ref canChangeFolder, value);
    }

    public bool CanScan
    {
        get => canScan;
        private set => SetProperty(ref canScan, value);
    }

    public bool CanCancel
    {
        get => canCancel;
        private set => SetProperty(ref canCancel, value);
    }

    public bool HasProgress
    {
        get => hasProgress;
        private set => SetProperty(ref hasProgress, value);
    }

    public double ProgressValue
    {
        get => progressValue;
        private set => SetProperty(ref progressValue, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public string ProgressPercentText
    {
        get => progressPercentText;
        private set => SetProperty(ref progressPercentText, value);
    }

    public string ProcessedText
    {
        get => processedText;
        private set => SetProperty(ref processedText, value);
    }

    public string AnalyzedText
    {
        get => analyzedText;
        private set => SetProperty(ref analyzedText, value);
    }

    public string UpToDateText
    {
        get => upToDateText;
        private set => SetProperty(ref upToDateText, value);
    }

    public string UnsupportedText
    {
        get => unsupportedText;
        private set => SetProperty(ref unsupportedText, value);
    }

    public string FailedText
    {
        get => failedText;
        private set => SetProperty(ref failedText, value);
    }

    public string CurrentFileText
    {
        get => currentFileText;
        private set => SetProperty(ref currentFileText, value);
    }

    public string? CurrentFileToolTip
    {
        get => currentFileToolTip;
        private set => SetProperty(ref currentFileToolTip, value);
    }

    internal void SetSongsFolder(string? path)
    {
        SongsFolderText = string.IsNullOrWhiteSpace(path)
            ? "Not set"
            : path;
    }

    internal void SetControls(bool isScanning, bool otherWorkRunning)
    {
        IsScanning = isScanning;
        CanChangeFolder = !isScanning && !otherWorkRunning;
        CanScan = !isScanning && !otherWorkRunning;
        CanCancel = isScanning;
    }

    internal void ShowPreparing()
    {
        HasProgress = true;
        StatusText = "Scanning library...";
        ProgressValue = 0;
        ProgressPercentText = "0.0%";
        ProcessedText = "Processed: 0 / 0";
        AnalyzedText = "Analyzed: 0";
        UpToDateText = "Up to date: 0";
        UnsupportedText = "Unsupported: 0";
        FailedText = "Failed: 0";
        CurrentFileText = "Current file: —";
        CurrentFileToolTip = null;
    }

    internal void UpdateProgress(LibraryScanProgress progress)
    {
        HasProgress = true;
        StatusText = "Scanning library...";
        ProgressValue = SafePercent(progress.Percent);
        ProgressPercentText = $"{ProgressValue:0.0}%";
        ProcessedText = $"Processed: {progress.ProcessedFiles} / {progress.TotalFiles}";
        AnalyzedText = $"Analyzed: {progress.AnalyzedFiles}";
        UpToDateText = $"Up to date: {progress.SkippedUpToDateFiles}";
        UnsupportedText = $"Unsupported: {progress.SkippedUnsupportedFiles}";
        FailedText = $"Failed: {progress.FailedFiles}";

        string currentFile = string.IsNullOrWhiteSpace(progress.CurrentFile)
            ? "—"
            : Path.GetFileName(progress.CurrentFile);

        CurrentFileText = $"Current file: {currentFile}";
        CurrentFileToolTip = progress.CurrentFile;
    }

    internal void ShowSummary(LibraryScanResult result)
    {
        double percent = result.TotalFiles == 0
            ? 0.0
            : result.ProcessedFiles * 100.0 / result.TotalFiles;

        HasProgress = true;
        StatusText = result.WasCancelled
            ? "Library scan cancelled"
            : "Library scan complete";
        ProgressValue = SafePercent(percent);
        ProgressPercentText = $"{ProgressValue:0.0}%";
        ProcessedText = $"Processed: {result.ProcessedFiles} / {result.TotalFiles}";
        AnalyzedText = $"Analyzed: {result.AnalyzedFiles}";
        UpToDateText = $"Up to date: {result.SkippedUpToDateFiles}";
        UnsupportedText = $"Unsupported: {result.SkippedUnsupportedFiles}";
        FailedText = $"Failed: {result.FailedFiles}";
        CurrentFileText = result.WasCancelled
            ? $"Cancelled after {result.Elapsed:mm\\:ss}."
            : $"Completed in {result.Elapsed:mm\\:ss}.";
        CurrentFileToolTip = null;
    }

    internal void ShowCancelling()
    {
        StatusText = "Cancelling scan...";
        CanCancel = false;
    }

    internal void ShowCancelledWithoutResult()
    {
        HasProgress = true;
        StatusText = "Library scan cancelled";
        CurrentFileText = "Cancelled before a final summary was available.";
        CurrentFileToolTip = null;
    }

    internal void ShowFailure(Exception exception)
    {
        HasProgress = true;
        StatusText = "Library scan failed";
        CurrentFileText = $"Error: {exception.Message}";
        CurrentFileToolTip = exception.ToString();
    }

    private static double SafePercent(double value) =>
        double.IsFinite(value)
            ? Math.Clamp(value, 0.0, 100.0)
            : 0.0;
}
