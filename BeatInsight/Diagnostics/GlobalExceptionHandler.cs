using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace BeatInsight.Diagnostics;

internal static class GlobalExceptionHandler
{
    private static readonly ConcurrentDictionary<Exception, byte>
        ReportedExceptions = new(ReferenceEqualityComparer.Instance);

    internal static void Register(Application application)
    {
        application.DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    internal static bool IsExpectedCancellation(Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            IReadOnlyCollection<Exception> leaves = aggregate.Flatten().InnerExceptions;
            return leaves.Count > 0 && leaves.All(IsExpectedCancellation);
        }

        return exception is OperationCanceledException;
    }

    internal static string FormatCrashReport(
        Exception exception,
        string source,
        bool isFatal)
    {
        var report = new StringBuilder();
        report.AppendLine("BeatInsight crash report");
        report.AppendLine($"Version: {ReleaseInfo.Version}");
        report.AppendLine($"UTC: {DateTimeOffset.UtcNow:O}");
        report.AppendLine($"Source: {source}");
        report.AppendLine($"Fatal: {isFatal}");
        report.AppendLine($"OS: {RuntimeInformation.OSDescription}");
        report.AppendLine($"Framework: {RuntimeInformation.FrameworkDescription}");
        report.AppendLine(
            $"Architecture: process={RuntimeInformation.ProcessArchitecture}, "
            + $"OS={RuntimeInformation.OSArchitecture}");

        AppendException(report, exception, 0);
        return DebugLogger.RedactSensitiveData(report.ToString());
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        if (IsExpectedCancellation(e.Exception))
        {
            e.Handled = true;
            return;
        }

        if (TryReport(e.Exception, "WPF dispatcher", isFatal: true))
        {
            ShowFatalDialog();
        }

        e.Handled = false;
    }

    private static void OnAppDomainUnhandledException(
        object sender,
        UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is not Exception exception
            || IsExpectedCancellation(exception))
        {
            return;
        }

        if (TryReport(exception, "AppDomain", isFatal: e.IsTerminating))
        {
            ShowFatalDialog();
        }
    }

    private static void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        if (!IsExpectedCancellation(e.Exception))
        {
            TryReport(e.Exception, "Unobserved task", isFatal: false);
        }

        e.SetObserved();
    }

    private static bool TryReport(
        Exception exception,
        string source,
        bool isFatal)
    {
        if (!ReportedExceptions.TryAdd(exception, 0))
        {
            return false;
        }

        try
        {
            DebugLogger.Error(FormatCrashReport(exception, source, isFatal));
        }
        catch
        {
            // A crash handler must never replace the original failure.
        }

        return true;
    }

    private static void ShowFatalDialog()
    {
        try
        {
            string logLocation = DebugLogger.CurrentLogFilePath
                ?? DebugLogger.DefaultLogDirectory;
            MessageBox.Show(
                "BeatInsight encountered an unexpected error and must close."
                + Environment.NewLine
                + Environment.NewLine
                + "A local diagnostic log was written to:"
                + Environment.NewLine
                + logLocation,
                "BeatInsight",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // Do not mask the fatal exception if the dialog cannot be shown.
        }
    }

    private static void AppendException(
        StringBuilder report,
        Exception exception,
        int depth)
    {
        report.AppendLine();
        report.AppendLine(depth == 0 ? "Exception:" : $"Inner exception {depth}:");
        report.AppendLine($"Type: {exception.GetType().FullName}");
        report.AppendLine($"Message: {exception.Message}");
        report.AppendLine("Stack trace:");
        report.AppendLine(exception.StackTrace ?? "(not available)");

        if (exception.InnerException is not null && depth < 8)
        {
            AppendException(report, exception.InnerException, depth + 1);
        }
    }
}
