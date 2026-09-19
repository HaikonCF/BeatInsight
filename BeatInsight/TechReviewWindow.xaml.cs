using BeatInsight.Analysis;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace BeatInsight;

public partial class TechReviewWindow : Window
{
    private readonly TechReviewSession session;

    private bool suppressFilterEvents;

    public TechReviewWindow()
    {
        InitializeComponent();

        session = TechReviewSession.Load(new TechReviewStore());

        InitializeFilters();
        RefreshView();
    }

    private sealed record FilterOption<T>(
        string Label,
        T Value)
    {
        public override string ToString() => Label;
    }

    private void InitializeFilters()
    {
        suppressFilterEvents = true;

        try
        {
            MapFilterComboBox.Items.Clear();
            MapFilterComboBox.Items.Add(new FilterOption<string?>("All maps", null));

            foreach (TechReviewMapProgress map in session.GetMapProgress())
            {
                MapFilterComboBox.Items.Add(new FilterOption<string?>(
                    map.DisplayName,
                    map.StableBeatmapId));
            }

            MapFilterComboBox.SelectedIndex = 0;

            LabelFilterComboBox.ItemsSource = new object[]
            {
                new FilterOption<TechReviewLabelFilter>("All", TechReviewLabelFilter.All),
                new FilterOption<TechReviewLabelFilter>("Unreviewed only", TechReviewLabelFilter.UnreviewedOnly),
                new FilterOption<TechReviewLabelFilter>("Tech", TechReviewLabelFilter.Tech),
                new FilterOption<TechReviewLabelFilter>("NotTech", TechReviewLabelFilter.NotTech),
                new FilterOption<TechReviewLabelFilter>("Unsure", TechReviewLabelFilter.Unsure)
            };
            LabelFilterComboBox.SelectedIndex = 0;

            PriorityFilterComboBox.ItemsSource = new object[]
            {
                new FilterOption<TechReviewPriorityFilter>("All", TechReviewPriorityFilter.All),
                new FilterOption<TechReviewPriorityFilter>("High", TechReviewPriorityFilter.High),
                new FilterOption<TechReviewPriorityFilter>("Medium", TechReviewPriorityFilter.Medium),
                new FilterOption<TechReviewPriorityFilter>("Low", TechReviewPriorityFilter.Low)
            };
            PriorityFilterComboBox.SelectedIndex = 0;
        }
        finally
        {
            suppressFilterEvents = false;
        }
    }

    private void RefreshView()
    {
        UpdateProgressCounters();

        TechReviewCandidateCursor? cursor = session.GetCurrentCandidate();

        if (cursor is null)
        {
            MapHeaderText.Text = "No review candidates found";
            CandidateIndexText.Text = "Candidate 0 / 0";
            TimeRangeText.Text = "--:--.--- → --:--.---";
            PriorityText.Text = "Priority: -";
            WindowCountText.Text = "Windows merged: 0";
            CurrentLabelText.Text = "Current label: Unreviewed";
            DebugScoreText.Text = "Debug score: hidden";
            DebugScoreText.Visibility = ShowDebugScoreCheckBox.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;
            SetActionButtonsEnabled(false);
            return;
        }

        MapHeaderText.Text = $"{cursor.MapName}\n{cursor.Difficulty}";
        CandidateIndexText.Text =
            $"Candidate {cursor.CurrentIndex} / {cursor.TotalCandidates}";

        TimeRangeText.Text =
            $"{FormatTime(cursor.Candidate.StartTime)} → {FormatTime(cursor.Candidate.EndTime)}";

        PriorityText.Text = $"Priority: {cursor.Candidate.Priority}";
        WindowCountText.Text = $"Windows merged: {cursor.Candidate.WindowCount}";
        CurrentLabelText.Text = $"Current label: {cursor.Candidate.HumanLabel}";

        DebugScoreText.Text =
            $"Debug score: {cursor.Candidate.RepresentativeScore.ToString("F4", CultureInfo.InvariantCulture)}";
        DebugScoreText.Visibility = ShowDebugScoreCheckBox.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;

        SetActionButtonsEnabled(true);
    }

    private void UpdateProgressCounters()
    {
        TechReviewProgressCounters counters = session.GetProgressCounters();

        ProgressReviewedText.Text = $"Reviewed: {counters.Reviewed} / {counters.Total}";
        ProgressTechText.Text = $"Tech: {counters.Tech}";
        ProgressNotTechText.Text = $"NotTech: {counters.NotTech}";
        ProgressUnsureText.Text = $"Unsure: {counters.Unsure}";
        ProgressRemainingText.Text = $"Remaining: {counters.Remaining}";
    }

    private void SetActionButtonsEnabled(bool enabled)
    {
        LabelTechButton.IsEnabled = enabled;
        LabelNotTechButton.IsEnabled = enabled;
        LabelUnsureButton.IsEnabled = enabled;
        PreviousButton.IsEnabled = enabled;
        NextButton.IsEnabled = enabled;
        SkipButton.IsEnabled = enabled;
        CopyTimestampButton.IsEnabled = enabled;
    }

    private static string FormatTime(double milliseconds)
    {
        return TimeSpan.FromMilliseconds(milliseconds)
            .ToString(@"mm\:ss\.fff", CultureInfo.InvariantCulture);
    }

    private bool IsEditableControlFocused()
    {
        return Keyboard.FocusedElement is TextBoxBase;
    }

    private void ClearError()
    {
        ErrorText.Visibility = Visibility.Collapsed;
        ErrorText.Text = string.Empty;
    }

    private void ShowError(Exception ex)
    {
        ErrorText.Text = "Unable to persist review label: " + ex.Message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void ApplyLabel(TechReviewHumanLabel label)
    {
        try
        {
            ClearError();

            if (!session.ApplyLabel(label))
                return;

            RefreshView();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void MoveNext()
    {
        ClearError();
        session.MoveNext();
        RefreshView();
    }

    private void MovePrevious()
    {
        ClearError();
        session.MovePrevious();
        RefreshView();
    }

    private void Skip()
    {
        try
        {
            ClearError();

            if (!session.SkipCurrent())
                return;

            RefreshView();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        MovePrevious();
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        MoveNext();
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        Skip();
    }

    private void LabelTechButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyLabel(TechReviewHumanLabel.Tech);
    }

    private void LabelNotTechButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyLabel(TechReviewHumanLabel.NotTech);
    }

    private void LabelUnsureButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyLabel(TechReviewHumanLabel.Unsure);
    }

    private void CopyTimestampButton_Click(object sender, RoutedEventArgs e)
    {
        TechReviewCandidateCursor? cursor = session.GetCurrentCandidate();

        if (cursor is null)
            return;

        string text =
            $"{FormatTime(cursor.Candidate.StartTime)} → {FormatTime(cursor.Candidate.EndTime)}";

        Clipboard.SetText(text);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsEditableControlFocused())
            return;

        switch (e.Key)
        {
            case Key.D1:
            case Key.NumPad1:
                ApplyLabel(TechReviewHumanLabel.Tech);
                e.Handled = true;
                break;

            case Key.D2:
            case Key.NumPad2:
                ApplyLabel(TechReviewHumanLabel.NotTech);
                e.Handled = true;
                break;

            case Key.D3:
            case Key.NumPad3:
                ApplyLabel(TechReviewHumanLabel.Unsure);
                e.Handled = true;
                break;

            case Key.Right:
                MoveNext();
                e.Handled = true;
                break;

            case Key.Left:
                MovePrevious();
                e.Handled = true;
                break;
        }
    }

    private void MapFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressFilterEvents)
            return;

        if (MapFilterComboBox.SelectedItem is FilterOption<string?> option)
        {
            session.SetMapFilter(option.Value);
            RefreshView();
        }
    }

    private void LabelFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressFilterEvents)
            return;

        if (LabelFilterComboBox.SelectedItem is FilterOption<TechReviewLabelFilter> option)
        {
            session.SetLabelFilter(option.Value);
            RefreshView();
        }
    }

    private void PriorityFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressFilterEvents)
            return;

        if (PriorityFilterComboBox.SelectedItem is FilterOption<TechReviewPriorityFilter> option)
        {
            session.SetPriorityFilter(option.Value);
            RefreshView();
        }
    }

    private void ShowDebugScoreCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        RefreshView();
    }
}
