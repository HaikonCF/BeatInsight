using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BeatInsight.Controls;
using BeatInsight.Models.Fusion;
using BeatInsight.Models.Sections;
using BeatInsight.ViewModels;
using BeatInsight.Views;
using Xunit.Abstractions;

namespace BeatInsight.Tests.ViewModels;

public sealed class SectionOverviewRenderingTests(ITestOutputHelper output)
{
    [Fact]
    public void DashboardUsesResponsiveSectionGraphWithoutHorizontalScrolling()
    {
        RunSta(() =>
        {
            var app = new App();
            app.InitializeComponent();
            var bindingErrors = new BindingErrorTraceListener();
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
            PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrors);
            var vm = new MainWindowViewModel();
            var view = new DashboardView { DataContext = vm };
            var coordinator = new OverviewLoadCoordinator(vm.Overview);
            var source = OverviewViewModelTests.BuildSnapshot();

            Layout(view, new Size(1400, 900));
            Border dashboardHeader = Descendants(view)
                .OfType<Border>()
                .Single(element => element.Name == "DashboardHeader");
            StackPanel windowControls = Descendants(view)
                .OfType<StackPanel>()
                .Single(element => element.Name == "WindowControlsPanel");
            Assert.Same(
                VisualTreeHelper.GetParent(dashboardHeader),
                VisualTreeHelper.GetParent(windowControls));
            Assert.DoesNotContain(windowControls, Descendants(dashboardHeader));
            Assert.Equal(HorizontalAlignment.Right, windowControls.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Top, windowControls.VerticalAlignment);
            Assert.True(Panel.GetZIndex(windowControls) >= 1000);

            Button[] titleBarButtons = Descendants(windowControls).OfType<Button>().ToArray();
            Assert.Equal(3, titleBarButtons.Length);
            Assert.Contains(titleBarButtons, button => Equals(button.Content, "—"));
            Assert.Contains(titleBarButtons, button => Equals(button.Content, "□"));
            Assert.Contains(titleBarButtons, button => Equals(button.Content, "×"));
            Assert.All(titleBarButtons,
                button => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));

            Button settingsButton = Descendants(dashboardHeader)
                .OfType<Button>()
                .Single(button => button.Name == "SettingsButton");
            Border settingsPanel = Descendants(view)
                .OfType<Border>()
                .Single(element => element.Name == "SettingsPanel");
            Assert.Equal(Visibility.Collapsed, settingsPanel.Visibility);
            settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Visibility.Visible, settingsPanel.Visibility);
            settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Visibility.Collapsed, settingsPanel.Visibility);

            StackPanel navigationPanel = Descendants(dashboardHeader).OfType<StackPanel>()
                .Single(panel => panel.Children.OfType<Button>()
                    .Any(button => Equals(button.Content, "Overview")));
            Assert.Equal(
                ["Overview", "Skills", "Sections", "Patterns", "Identity", "Difficulty"],
                navigationPanel.Children.OfType<Button>()
                    .Select(button => Assert.IsType<string>(button.Content)));
            Grid headerGrid = Assert.IsType<Grid>(VisualTreeHelper.GetParent(navigationPanel));
            Assert.True(navigationPanel.DesiredSize.Width <= headerGrid.ColumnDefinitions[1].ActualWidth + .5);

            var sectionGraph = Descendants(view)
                .OfType<SectionTimelineGraphControl>()
                .Single(graph => graph.MinHeight == 130);
            var sectionsTabGraph = Descendants(view)
                .OfType<SectionTimelineGraphControl>()
                .Single(graph => graph.MinHeight == 210);
            var spikeGraph = Descendants(view).OfType<SpikeGraphControl>().First();
            var noSections = Descendants(view)
                .OfType<TextBlock>()
                .First(text => text.Text == "No section data available");
            Assert.Empty(Ancestors(sectionGraph).OfType<ScrollViewer>());
            Grid sectionsPage = Ancestors(sectionsTabGraph)
                .OfType<Grid>()
                .Single(grid => BindingOperations.GetBindingExpression(
                    grid, UIElement.VisibilityProperty)?.ParentBinding.Path.Path == "IsSectionsTabActive");

            foreach (int count in new[] { 0, 1, 5, 25, 101 })
            {
                GameplaySectionV2Analysis[] sections = Enumerable.Range(0, count)
                    .Select(index => source.SectionAnalysisV2.Sections[0] with
                    {
                        SectionIndex = index,
                        StartTime = index * 5000,
                        EndTime = (index + 1) * 5000,
                        DominantStructure = StructureAt(index),
                        LocalDifficultyScore = 20 + index % 4 * 20,
                        Spike = SectionDifficultySpike.None
                    })
                    .ToArray();
                var snapshot = source with
                {
                    SectionAnalysisV2 = source.SectionAnalysisV2 with
                    {
                        Sections = sections,
                        DifficultySpikeCount = 0
                    }
                };

                long request = coordinator.Begin("test");
                Assert.True(coordinator.LoadSnapshot(request, snapshot, false, TimeSpan.Zero, null));
                Assert.True(coordinator.SetReady(request));
                Assert.True(coordinator.CompleteSecondaryDetails(request, snapshot, false));

                foreach (Size size in new[] { new Size(1400, 900), new Size(1200, 800), new Size(1300, 850) })
                {
                    Layout(view, size);
                    Assert.Equal(count == 0 ? Visibility.Collapsed : Visibility.Visible, sectionGraph.Visibility);
                    Assert.Equal(count == 0 ? Visibility.Visible : Visibility.Collapsed, noSections.Visibility);
                    Assert.Equal(count == 0 ? Visibility.Collapsed : Visibility.Visible, spikeGraph.Visibility);
                    if (count > 0)
                    {
                        Assert.True(sectionGraph.ActualWidth > 100);
                        Assert.True(sectionGraph.ActualWidth <= size.Width);
                        Assert.True(sectionGraph.ActualHeight >= 112);
                        Assert.Equal(count, sectionGraph.GetRenderPointsForTesting().Count);
                    }
                }

                Assert.Equal(
                    count == 0 ? "No section data available" : "No significant difficulty spikes detected",
                    vm.Overview.SpikesSummaryText);
            }

            TextBlock skillsHeading = Descendants(view)
                .OfType<TextBlock>()
                .Single(text => text.Text == "SKILL OVERVIEW");
            Grid skillsPage = Ancestors(skillsHeading)
                .OfType<Grid>()
                .Single(grid => BindingOperations.GetBindingExpression(
                    grid, UIElement.VisibilityProperty)?.ParentBinding.Path.Path == "IsSkillsTabActive");
            Button skillsButton = Descendants(view)
                .OfType<Button>()
                .Single(button => Equals(button.Content, "Skills"));
            Assert.Equal(Visibility.Collapsed, skillsPage.Visibility);

            skillsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout(view, new Size(1400, 900));

            Assert.Equal(NavigationTab.Skills, vm.SelectedTab);
            Assert.True(vm.IsSkillsTabActive);
            Assert.Equal(Visibility.Visible, skillsPage.Visibility);
            Assert.Same(vm.Overview.SkillMetrics,
                Descendants(skillsPage).OfType<ItemsControl>()
                    .Single(control => ReferenceEquals(control.ItemsSource, vm.Overview.SkillMetrics))
                    .ItemsSource);
            Assert.Same(vm.Overview.SkillPressureMetrics,
                Descendants(skillsPage).OfType<ItemsControl>()
                    .Single(control => ReferenceEquals(control.ItemsSource, vm.Overview.SkillPressureMetrics))
                    .ItemsSource);

            TextBlock patternsHeading = Descendants(view)
                .OfType<TextBlock>()
                .Single(text => text.Text == "PATTERN ANALYSIS");
            Grid patternsPage = Ancestors(patternsHeading)
                .OfType<Grid>()
                .Single(grid => BindingOperations.GetBindingExpression(
                    grid, UIElement.VisibilityProperty)?.ParentBinding.Path.Path == "IsPatternsTabActive");
            Button patternsButton = Descendants(view)
                .OfType<Button>()
                .Single(button => Equals(button.Content, "Patterns"));
            Assert.Equal(Visibility.Collapsed, patternsPage.Visibility);

            patternsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout(view, new Size(1400, 900));

            Assert.Equal(NavigationTab.Patterns, vm.SelectedTab);
            Assert.True(vm.IsPatternsTabActive);
            Assert.Equal(Visibility.Visible, patternsPage.Visibility);
            Assert.Same(vm.Overview.Patterns.StreamPatterns,
                Descendants(patternsPage).OfType<ItemsControl>()
                    .Single(control => ReferenceEquals(control.ItemsSource, vm.Overview.Patterns.StreamPatterns))
                    .ItemsSource);

            IdentityViewModel identityBeforeNavigation = vm.Overview.Identity;
            int mainSkillCount = identityBeforeNavigation.MainSkills.Count;
            int mainPatternCount = identityBeforeNavigation.MainPatterns.Count;
            TextBlock identityHeading = Descendants(view)
                .OfType<TextBlock>()
                .Single(text => text.Text == "GAMEPLAY IDENTITY V3"
                    && Ancestors(text).OfType<Grid>().Any(grid =>
                        BindingOperations.GetBindingExpression(
                            grid, UIElement.VisibilityProperty)?.ParentBinding.Path.Path == "IsIdentityTabActive"));
            Grid identityPage = Ancestors(identityHeading)
                .OfType<Grid>()
                .Single(grid => BindingOperations.GetBindingExpression(
                    grid, UIElement.VisibilityProperty)?.ParentBinding.Path.Path == "IsIdentityTabActive");
            Button identityButton = Descendants(view)
                .OfType<Button>()
                .Single(button => Equals(button.Content, "Identity"));
            Assert.Equal(Visibility.Collapsed, identityPage.Visibility);

            identityButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout(view, new Size(1400, 900));

            Assert.Equal(NavigationTab.Identity, vm.SelectedTab);
            Assert.True(vm.IsIdentityTabActive);
            Assert.Equal(Visibility.Visible, identityPage.Visibility);
            Assert.Same(identityBeforeNavigation, vm.Overview.Identity);
            Assert.Equal(mainSkillCount, vm.Overview.Identity.MainSkills.Count);
            Assert.Equal(mainPatternCount, vm.Overview.Identity.MainPatterns.Count);
            Assert.Equal("STREAM", Descendants(identityPage).OfType<TextBlock>()
                .Single(text => text.Name == "IdentityPrimaryStructureText").Text);
            Assert.Equal("JUMP", Descendants(identityPage).OfType<TextBlock>()
                .Single(text => text.Name == "IdentitySecondaryStructureText").Text);
            Assert.Equal("83 %", Descendants(identityPage).OfType<TextBlock>()
                .Single(text => text.Name == "IdentityConfidenceText").Text);
            Assert.Same(vm.Overview.Identity.MainSkills,
                Descendants(identityPage).OfType<ItemsControl>()
                    .Single(control => ReferenceEquals(control.ItemsSource, vm.Overview.Identity.MainSkills))
                    .ItemsSource);
            Assert.Same(vm.Overview.Identity.MainPatterns,
                Descendants(identityPage).OfType<ItemsControl>()
                    .Single(control => ReferenceEquals(control.ItemsSource, vm.Overview.Identity.MainPatterns))
                    .ItemsSource);

            var withoutSecondary = source with
            {
                IdentityV3 = source.IdentityV3 with { SecondaryStructure = null }
            };
            vm.Overview.LoadSnapshot(withoutSecondary);
            Layout(view, new Size(1400, 900));
            Border secondaryCard = Descendants(identityPage)
                .OfType<Border>()
                .Single(element => element.Name == "IdentitySecondaryStructureCard");
            Assert.Equal(Visibility.Collapsed, secondaryCard.Visibility);

            TextBlock difficultyHeading = Descendants(view)
                .OfType<TextBlock>()
                .Single(text => text.Text == "DIFFICULTY ANALYSIS");
            Grid difficultyPage = Ancestors(difficultyHeading)
                .OfType<Grid>()
                .Single(grid => BindingOperations.GetBindingExpression(
                    grid, UIElement.VisibilityProperty)?.ParentBinding.Path.Path == "IsDifficultyTabActive");
            Button difficultyButton = Descendants(view)
                .OfType<Button>()
                .Single(button => Equals(button.Content, "Difficulty"));
            Assert.Equal(Visibility.Collapsed, difficultyPage.Visibility);

            difficultyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout(view, new Size(1400, 900));

            Assert.Equal(NavigationTab.Difficulty, vm.SelectedTab);
            Assert.True(vm.IsDifficultyTabActive);
            Assert.Equal(Visibility.Visible, difficultyPage.Visibility);
            Assert.Equal("5.4", Descendants(difficultyPage).OfType<TextBlock>()
                .Single(text => text.Name == "DifficultyRatingV2Text").Text);
            Assert.Equal("6.50 ★", Descendants(difficultyPage).OfType<TextBlock>()
                .Single(text => text.Name == "DifficultyOsuStarRatingText").Text);
            Assert.Same(vm.Overview.Difficulty.RatingBreakdown,
                Descendants(difficultyPage).OfType<ItemsControl>()
                    .Single(control => ReferenceEquals(control.ItemsSource, vm.Overview.Difficulty.RatingBreakdown))
                    .ItemsSource);
            Assert.Same(vm.Overview.Difficulty.TopSections,
                Descendants(difficultyPage).OfType<ItemsControl>()
                    .Single(control => ReferenceEquals(control.ItemsSource, vm.Overview.Difficulty.TopSections))
                    .ItemsSource);

            Button overviewButton = Descendants(view).OfType<Button>()
                .Single(button => Equals(button.Content, "Overview"));
            Grid contentHost = Assert.IsType<Grid>(VisualTreeHelper.GetParent(sectionsPage));

            overviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout(view, new Size(1400, 900));
            Grid overviewPage = contentHost.Children
                .OfType<Grid>()
                .Single(grid => grid.Visibility == Visibility.Visible);

            (NavigationTab Tab, Button Button, Grid Page)[] navigation =
            [
                (NavigationTab.Overview, overviewButton, overviewPage),
                (NavigationTab.Skills, skillsButton, skillsPage),
                (NavigationTab.Sections, Descendants(view).OfType<Button>().Single(button => Equals(button.Content, "Sections")), sectionsPage),
                (NavigationTab.Patterns, patternsButton, patternsPage),
                (NavigationTab.Identity, identityButton, identityPage),
                (NavigationTab.Difficulty, difficultyButton, difficultyPage)
            ];

            foreach (Size size in new[]
                     {
                         new Size(1400, 900),
                         new Size(1280, 720),
                         new Size(1600, 1000),
                         new Size(1100, 700)
                     })
            {
                foreach ((NavigationTab tab, Button button, Grid page) in navigation)
                {
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Layout(view, size);

                    Assert.Equal(tab, vm.SelectedTab);
                    Assert.Equal(Visibility.Visible, page.Visibility);
                    Assert.Single(navigation, item => item.Page.Visibility == Visibility.Visible);
                    Assert.True(page.ActualWidth > 700);
                    Assert.True(page.ActualHeight > 400);
                    Assert.True(navigationPanel.DesiredSize.Width <= headerGrid.ColumnDefinitions[1].ActualWidth + .5);
                }
            }

            foreach ((NavigationTab tab, Button button, _) in navigation)
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                CaptureIfRequested(view, new Size(1400, 900), $"qa-{tab.ToString().ToLowerInvariant()}-1400x900.png");
                CaptureIfRequested(view, new Size(1280, 720), $"qa-{tab.ToString().ToLowerInvariant()}-1280x720.png");
            }

            vm.SelectedTab = NavigationTab.Overview;
            Layout(view, new Size(1400, 900));

            CaptureIfRequested(view, new Size(1400, 900), "section-graph-1400.png");
            CaptureIfRequested(view, new Size(1200, 800), "section-graph-1200.png");

            PresentationTraceSources.DataBindingSource.Listeners.Remove(bindingErrors);
            Assert.Empty(bindingErrors.Messages);
        });
    }

    [Fact]
    public void GraphHandlesEmptySingleConstantVariableAndLargeTimelines()
    {
        RunSta(() =>
        {
            var sections = new ObservableCollection<SectionTimelineItemViewModel>();
            var control = new SectionTimelineGraphControl { Sections = sections };
            Arrange(control, 600, 150);
            Assert.Empty(control.GetRenderPointsForTesting());
            Assert.Empty(control.GetStructureBandSegmentsForTesting());

            sections.Add(Item(0, GameplayStructureKind.Stream, 42));
            IReadOnlyList<SectionTimelineGraphControl.RenderPoint> single = control.GetRenderPointsForTesting();
            Assert.Single(single);
            Assert.Equal(300, single[0].Position.X, 5);
            Assert.True(IsFinite(single[0].Position));
            SectionTimelineGraphControl.StructureBandSegment singleBand =
                Assert.Single(control.GetStructureBandSegmentsForTesting());
            Assert.Equal("Stream", singleBand.Structure);
            Assert.True(singleBand.ShowsLabel);
            Assert.True(singleBand.Bounds.Width > 500);

            sections.Clear();
            for (int index = 0; index < 5; index++)
                sections.Add(Item(index, StructureAt(index), 50));
            IReadOnlyList<SectionTimelineGraphControl.RenderPoint> constant = control.GetRenderPointsForTesting();
            Assert.Equal(5, constant.Count);
            Assert.Single(constant.Select(point => point.Position.Y).Distinct());
            Assert.True(constant.Zip(constant.Skip(1)).All(pair => pair.First.Position.X < pair.Second.Position.X));
            Assert.Equal(5, control.GetStructureBandSegmentsForTesting().Count);
            Assert.All(control.GetStructureBandSegmentsForTesting(), segment => Assert.True(segment.ShowsLabel));

            sections.Clear();
            for (int index = 0; index < 101; index++)
                sections.Add(Item(index, StructureAt(index), index));
            IReadOnlyList<SectionTimelineGraphControl.RenderPoint> large = control.GetRenderPointsForTesting();
            Assert.Equal(101, large.Count);
            Assert.All(large, point => Assert.True(IsFinite(point.Position)));
            Assert.True(large[0].Position.Y > large[^1].Position.Y);
            Assert.Equal(101, control.GetStructureBandSegmentsForTesting().Count);
            Assert.All(control.GetStructureBandSegmentsForTesting(), segment => Assert.False(segment.ShowsLabel));

            Point beforeResize = large[50].Position;
            Arrange(control, 360, 110);
            Point afterResize = control.GetRenderPointsForTesting()[50].Position;
            Assert.NotEqual(beforeResize, afterResize);
            Assert.InRange(afterResize.X, 0, control.ActualWidth);
            Assert.InRange(afterResize.Y, 0, control.ActualHeight);
        });
    }

    [Fact]
    public void GraphDistributesSectionsProportionallyToTime()
    {
        RunSta(() =>
        {
            var first = Item(0, GameplayStructureKind.Stream, 40);
            first.StartTimeMs = 0;
            first.EndTimeMs = 1000;
            var second = Item(1, GameplayStructureKind.Jump, 50);
            second.StartTimeMs = 1000;
            second.EndTimeMs = 2000;
            var third = Item(2, GameplayStructureKind.Tech, 60);
            third.StartTimeMs = 9000;
            third.EndTimeMs = 10000;
            var control = new SectionTimelineGraphControl { Sections = new[] { first, second, third } };
            Arrange(control, 600, 150);

            IReadOnlyList<SectionTimelineGraphControl.RenderPoint> points = control.GetRenderPointsForTesting();
            IReadOnlyList<Rect> bandRects = control.GetSectionBandRectsForTesting();
            double firstGap = points[1].Position.X - points[0].Position.X;
            double secondGap = points[2].Position.X - points[1].Position.X;
            Assert.True(secondGap > firstGap * 5);
            Assert.True(bandRects[2].Width > bandRects[0].Width * 0.9);
            Assert.True(bandRects[2].Left - bandRects[1].Right > bandRects[0].Width * 5);
        });
    }

    [Fact]
    public void AdjacentIdenticalStructuresMergeOnlyInVisualBand()
    {
        RunSta(() =>
        {
            var sections = new ObservableCollection<SectionTimelineItemViewModel>
            {
                Item(0, GameplayStructureKind.Stream, 30),
                Item(1, GameplayStructureKind.Stream, 45),
                Item(2, GameplayStructureKind.Jump, 55),
                Item(3, GameplayStructureKind.Jump, 65),
                Item(4, GameplayStructureKind.Tech, 70)
            };
            var control = new SectionTimelineGraphControl { Sections = sections };
            Arrange(control, 700, 150);

            IReadOnlyList<SectionTimelineGraphControl.StructureBandSegment> bands =
                control.GetStructureBandSegmentsForTesting();
            Assert.Collection(
                bands,
                segment => { Assert.Equal("Stream", segment.Structure); Assert.Equal((0, 1), (segment.FirstSourceIndex, segment.LastSourceIndex)); },
                segment => { Assert.Equal("Jump", segment.Structure); Assert.Equal((2, 3), (segment.FirstSourceIndex, segment.LastSourceIndex)); },
                segment => { Assert.Equal("Tech", segment.Structure); Assert.Equal((4, 4), (segment.FirstSourceIndex, segment.LastSourceIndex)); });
            Assert.Equal(5, sections.Count);
            Assert.Equal(5, control.GetRenderPointsForTesting().Count);
            Assert.All(bands, segment => Assert.True(segment.ShowsLabel));
        });
    }

    [Fact]
    public void GraphProtectsNonFiniteValuesAndUsesExistingStructurePalette()
    {
        RunSta(() =>
        {
            var sections = new ObservableCollection<SectionTimelineItemViewModel>
            {
                Copy(Item(0, GameplayStructureKind.Stream, double.NaN), startTime: double.NaN),
                Copy(Item(1, GameplayStructureKind.Jump, double.PositiveInfinity), endTime: double.PositiveInfinity),
                Item(2, GameplayStructureKind.Tech, double.NegativeInfinity),
                Item(3, GameplayStructureKind.ClassicMixed, 55)
            };
            var control = new SectionTimelineGraphControl { Sections = sections };
            Arrange(control, 500, 130);

            Assert.All(control.GetRenderPointsForTesting(), point => Assert.True(IsFinite(point.Position)));
            Assert.Equal(Color.FromRgb(0x38, 0xBD, 0xF8), BrushColor(SectionTimelineGraphControl.ResolveStructureBrush("Stream")));
            Assert.Equal(Color.FromRgb(0xF4, 0x3F, 0x5E), BrushColor(SectionTimelineGraphControl.ResolveStructureBrush("Jump")));
            Assert.Equal(Color.FromRgb(0xA8, 0x55, 0xF7), BrushColor(SectionTimelineGraphControl.ResolveStructureBrush("Tech")));
            Assert.Equal(Color.FromRgb(0x10, 0xB9, 0x81), BrushColor(SectionTimelineGraphControl.ResolveStructureBrush("ClassicMixed")));
            Render(control);
        });
    }

    [Fact]
    public void HoverHitTestingSelectsSectionAndShowsPreparedTooltip()
    {
        RunSta(() =>
        {
            var sections = new ObservableCollection<SectionTimelineItemViewModel>
            {
                Item(0, GameplayStructureKind.Stream, 25),
                Item(1, GameplayStructureKind.Jump, 65),
                Item(2, GameplayStructureKind.Tech, 40)
            };
            var control = new SectionTimelineGraphControl { Sections = sections };
            Arrange(control, 480, 140);
            Point target = control.GetRenderPointsForTesting()[1].Position;

            Assert.Equal(1, control.HitTestSectionIndex(target));
            control.UpdateHover(target);
            Assert.Equal(1, control.HoveredSectionIndex);
            Assert.Equal("00:05 – 00:10\nJump\nLocal Difficulty: 65.0", control.ToolTip);
            Assert.Equal(Cursors.Hand, control.Cursor);

            Rect band = control.GetSectionBandRectsForTesting()[2];
            Point bandTarget = new(band.Left + band.Width / 2, band.Top + band.Height / 2);
            byte[] beforeBandHover = Render(control);
            Assert.Equal(2, control.HitTestSectionIndex(bandTarget));
            control.UpdateHover(bandTarget);
            Assert.Equal(2, control.HoveredSectionIndex);
            Assert.Equal("00:10 – 00:15\nTech\nLocal Difficulty: 40.0", control.ToolTip);
            Assert.False(beforeBandHover.SequenceEqual(Render(control)));

            control.UpdateHover(new Point(-100, -100));
            Assert.Equal(-1, control.HoveredSectionIndex);
            Assert.Null(control.ToolTip);
            Assert.Equal(Cursors.Arrow, control.Cursor);
        });
    }

    [Fact]
    public void SelectedSectionHighlightPersistsAfterHoverLeaves()
    {
        RunSta(() =>
        {
            var sections = new ObservableCollection<SectionTimelineItemViewModel>
            {
                Item(4, GameplayStructureKind.Stream, 25),
                Item(9, GameplayStructureKind.Jump, 65)
            };
            var control = new SectionTimelineGraphControl
            {
                Sections = sections,
                SelectedSection = sections[1]
            };
            Arrange(control, 480, 140);

            byte[] selected = Render(control);
            Assert.Equal(1, control.SelectedSectionIndex);
            control.UpdateHover(control.GetRenderPointsForTesting()[0].Position);
            Assert.Equal(0, control.HoveredSectionIndex);
            control.UpdateHover(new Point(-100, -100));
            Assert.Equal(-1, control.HoveredSectionIndex);
            Assert.Equal(1, control.SelectedSectionIndex);
            Assert.True(selected.SequenceEqual(Render(control)));
        });
    }

    [Fact]
    public void CollectionChangesRedrawSectionGraphAndSpikeGraphStillRenders()
    {
        RunSta(() =>
        {
            var sections = new ObservableCollection<SectionTimelineItemViewModel>();
            var sectionGraph = new SectionTimelineGraphControl { Sections = sections };
            Arrange(sectionGraph, 400, 120);
            byte[] empty = Render(sectionGraph);
            sections.Add(Item(0, GameplayStructureKind.Stream, 35));
            byte[] one = Render(sectionGraph);
            sections.Add(Item(1, GameplayStructureKind.Jump, 75));
            byte[] multiple = Render(sectionGraph);
            Assert.False(empty.SequenceEqual(one));
            Assert.False(one.SequenceEqual(multiple));

            var difficultyPoints = new ObservableCollection<DifficultyPointViewModel>();
            var spikeGraph = new SpikeGraphControl { Points = difficultyPoints };
            Arrange(spikeGraph, 400, 120);
            byte[] spikeEmpty = Render(spikeGraph);
            difficultyPoints.Add(new(0, 0, "00:00", 30));
            difficultyPoints.Add(new(1, 5000, "00:05", 80, true));
            Assert.False(spikeEmpty.SequenceEqual(Render(spikeGraph)));
        });
    }

    private static SectionTimelineItemViewModel Item(int index, GameplayStructureKind structure, double difficulty) => new()
    {
        Index = index,
        StartTimeMs = index * 5000,
        EndTimeMs = (index + 1) * 5000,
        TimeRangeDisplay = $"00:{index * 5:00} – 00:{(index + 1) * 5:00}",
        Structure = structure.ToString(),
        StructureColorKey = $"Structure{structure}",
        LocalDifficulty = difficulty,
        LocalDifficultyDisplay = double.IsFinite(difficulty)
            ? difficulty.ToString("F1", CultureInfo.InvariantCulture)
            : "—",
        TooltipText = $"00:{index * 5:00} – 00:{(index + 1) * 5:00}\n{structure}\nLocal Difficulty: {difficulty:F1}"
    };

    private static SectionTimelineItemViewModel Copy(
        SectionTimelineItemViewModel source,
        double? startTime = null,
        double? endTime = null) => new()
    {
        Index = source.Index,
        StartTimeMs = startTime ?? source.StartTimeMs,
        EndTimeMs = endTime ?? source.EndTimeMs,
        TimeRangeDisplay = source.TimeRangeDisplay,
        Structure = source.Structure,
        StructureColorKey = source.StructureColorKey,
        LocalDifficulty = source.LocalDifficulty,
        LocalDifficultyDisplay = source.LocalDifficultyDisplay,
        TooltipText = source.TooltipText
    };

    private static GameplayStructureKind StructureAt(int index) => (index % 4) switch
    {
        0 => GameplayStructureKind.ClassicMixed,
        1 => GameplayStructureKind.Stream,
        2 => GameplayStructureKind.Jump,
        _ => GameplayStructureKind.Tech
    };

    private static void Layout(FrameworkElement element, Size size) => Arrange(element, size.Width, size.Height);

    private static void Arrange(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }

    private static byte[] Render(FrameworkElement element)
    {
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        int width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        byte[] pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    private static bool IsFinite(Point point) => double.IsFinite(point.X) && double.IsFinite(point.Y);

    private static Color BrushColor(Brush brush) => Assert.IsType<SolidColorBrush>(brush).Color;

    private void CaptureIfRequested(FrameworkElement element, Size size, string fileName)
    {
        string? directory = Environment.GetEnvironmentVariable("BEATINSIGHT_UI_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        Arrange(element, size.Width, size.Height);
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string path = Path.Combine(directory, fileName);
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
        output.WriteLine($"Capture: {path}");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject>();
        var pending = new Stack<DependencyObject>();
        pending.Push(root);
        while (pending.TryPop(out DependencyObject? current))
        {
            if (!seen.Add(current)) continue;
            yield return current;
            if (current is Visual)
                for (int index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
                    pending.Push(VisualTreeHelper.GetChild(current, index));
            foreach (DependencyObject child in LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>())
                pending.Push(child);
        }
    }

    private static IEnumerable<DependencyObject> Ancestors(DependencyObject child)
    {
        DependencyObject? current = child;
        while ((current = VisualTreeHelper.GetParent(current)) is not null)
            yield return current;
    }

    private sealed class BindingErrorTraceListener : TraceListener
    {
        private readonly StringBuilder current = new();

        public List<string> Messages { get; } = [];

        public override void Write(string? message)
        {
            current.Append(message);
        }

        public override void WriteLine(string? message)
        {
            current.Append(message);
            string value = current.ToString().Trim();
            if (value.Length > 0)
                Messages.Add(value);
            current.Clear();
        }
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { error = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)), "WPF test timed out");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
