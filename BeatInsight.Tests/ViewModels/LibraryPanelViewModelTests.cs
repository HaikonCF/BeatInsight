using BeatInsight.Models.Library;
using BeatInsight.ViewModels;

namespace BeatInsight.Tests.ViewModels;

public sealed class LibraryPanelViewModelTests
{
    [Fact]
    public void UpdateProgress_ProjectsScannerCountersWithoutRecalculation()
    {
        LibraryPanelViewModel viewModel = new();
        LibraryScanProgress progress = new()
        {
            TotalFiles = 20,
            ProcessedFiles = 8,
            AnalyzedFiles = 3,
            SkippedUpToDateFiles = 4,
            SkippedUnsupportedFiles = 1,
            FailedFiles = 0,
            CurrentFile = @"D:\osu!\Songs\set\current.osu",
            Percent = 40.0
        };

        viewModel.UpdateProgress(progress);

        Assert.True(viewModel.HasProgress);
        Assert.Equal(40.0, viewModel.ProgressValue);
        Assert.Equal("Processed: 8 / 20", viewModel.ProcessedText);
        Assert.Equal("Analyzed: 3", viewModel.AnalyzedText);
        Assert.Equal("Up to date: 4", viewModel.UpToDateText);
        Assert.Equal("Unsupported: 1", viewModel.UnsupportedText);
        Assert.Equal("Failed: 0", viewModel.FailedText);
        Assert.Equal("Current file: current.osu", viewModel.CurrentFileText);
    }

    [Fact]
    public void ShowSummary_HandlesEmptyLibraryWithoutDivisionByZero()
    {
        LibraryPanelViewModel viewModel = new();
        LibraryScanResult result = new()
        {
            TotalFiles = 0,
            ProcessedFiles = 0,
            AnalyzedFiles = 0,
            SkippedUpToDateFiles = 0,
            SkippedUnsupportedFiles = 0,
            FailedFiles = 0,
            WasCancelled = false,
            Elapsed = TimeSpan.Zero
        };

        viewModel.ShowSummary(result);

        Assert.Equal(0.0, viewModel.ProgressValue);
        Assert.EndsWith("%", viewModel.ProgressPercentText);
        Assert.Equal("Library scan complete", viewModel.StatusText);
    }

    [Fact]
    public void SetControls_ExposesSingleScanAndCancellationState()
    {
        LibraryPanelViewModel viewModel = new();

        viewModel.SetControls(isScanning: true, otherWorkRunning: false);

        Assert.True(viewModel.IsScanning);
        Assert.False(viewModel.CanChangeFolder);
        Assert.False(viewModel.CanScan);
        Assert.True(viewModel.CanCancel);

        viewModel.ShowCancelling();

        Assert.False(viewModel.CanCancel);
        Assert.Equal("Cancelling scan...", viewModel.StatusText);
    }
}
