using BeatInsight.Overlay;
using BeatInsight.Overlay.GameplayHud;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BeatInsight.Tests.Overlay;

public sealed class OverlayEditModeTests
{
    [Fact]
    public void EnterEditCreatesIndependentWorkingAndEntryLayouts()
    {
        var engine = Engine();

        Assert.True(engine.BeginEdit());

        Assert.True(engine.IsOverlayEditMode);
        Assert.NotSame(engine.EntryLayoutSnapshot, engine.WorkingLayout);
        AssertLayoutEqual(engine.EntryLayoutSnapshot!, engine.WorkingLayout!);
    }

    [Fact]
    public void MoveSurvivesProductionTickAndCancelRestoresEntry()
    {
        var engine = Engine();
        OverlayLayout entry = engine.CurrentLayout;
        Assert.True(engine.BeginEdit());

        Assert.True(engine.UpdateWorkingPlacement(
            OverlayWidgetIds.MapOverview, 444, 333, 360, 220, resized: false));
        engine.PublishSnapshot(null);

        Assert.Equal(444, Placement(engine.WorkingLayout!, OverlayWidgetIds.MapOverview).X);
        Assert.True(engine.CancelEdit());
        AssertLayoutEqual(entry, engine.CurrentLayout);
        Assert.False(engine.IsOverlayEditMode);
    }

    [Fact]
    public void ResizeSetsCustomSizeAndClampsMinimum()
    {
        var engine = Engine();
        Assert.True(engine.BeginEdit());

        Assert.True(engine.UpdateWorkingPlacement(
            OverlayWidgetIds.GameplayHud, 24, 24, 10, 20, resized: true));

        OverlayWidgetPlacement placement = Placement(
            engine.WorkingLayout!, OverlayWidgetIds.GameplayHud);
        Assert.True(placement.HasCustomSize);
        Assert.Equal(280, placement.Width);
        Assert.Equal(180, placement.Height);
    }

    [Fact]
    public void NonFiniteResizeIsRejectedWithoutDestroyingWorkingPlacement()
    {
        var engine = Engine();
        Assert.True(engine.BeginEdit());
        OverlayWidgetPlacement before = Placement(
            engine.WorkingLayout!, OverlayWidgetIds.MapOverview);

        Assert.True(engine.UpdateWorkingPlacement(
            OverlayWidgetIds.MapOverview, 24, 24, double.NaN, 100, resized: true));

        Assert.Equal(before, Placement(engine.WorkingLayout!, OverlayWidgetIds.MapOverview));
    }

    [Fact]
    public void ResetUsesBuiltInButCancelRestoresPreviousCustomLayout()
    {
        OverlayLayout builtIn = OverlayLayoutCatalog.Get(OverlayLayoutIds.Minimal);
        OverlayWidgetPlacement customHud = Placement(builtIn, OverlayWidgetIds.GameplayHud) with
        {
            X = 700,
            Y = 300,
            Width = 500,
            Height = 640,
            HasCustomSize = true
        };
        var custom = builtIn with
        {
            Widgets = builtIn.Widgets.Select(item =>
                item.WidgetId == OverlayWidgetIds.GameplayHud ? customHud : item).ToArray()
        };
        var engine = new OverlayEngine(custom);
        Assert.True(engine.BeginEdit());

        Assert.True(engine.ResetWorkingLayout(builtIn));
        Assert.False(Placement(engine.WorkingLayout!, OverlayWidgetIds.GameplayHud).HasCustomSize);
        Assert.True(engine.CancelEdit());

        Assert.Equal(customHud, Placement(engine.CurrentLayout, OverlayWidgetIds.GameplayHud));
    }

    [Fact]
    public void SaveClampsOnlyEnoughToKeepCenterOnVirtualScreen()
    {
        var engine = Engine();
        Assert.True(engine.BeginEdit());
        Assert.True(engine.UpdateWorkingPlacement(
            OverlayWidgetIds.MapOverview, 9000, -9000, 500, 300, resized: true));

        OverlayLayout saved = Assert.IsType<OverlayLayout>(
            engine.PrepareWorkingLayoutForSave(new(0, 0, 1920, 1080)));
        OverlayWidgetPlacement placement = Placement(saved, OverlayWidgetIds.MapOverview);

        Assert.InRange(placement.X + placement.Width / 2, 0, 1920);
        Assert.InRange(placement.Y + placement.Height / 2, 0, 1080);
        Assert.Equal(500, placement.Width);
        Assert.Equal(300, placement.Height);
    }

    [Fact]
    public void ExclusiveFullscreenRefusesEdit()
    {
        var engine = Engine();
        engine.SetDisplayCompatibility(OverlayDisplayCompatibility.FullscreenExclusive);

        Assert.False(engine.BeginEdit());
        Assert.False(engine.IsOverlayEditMode);
    }

    [Fact]
    public void EditPreviewBypassesRouteForegroundAndLiveDataForOnlyProductionSurfaces()
    {
        var engine = Engine();
        foreach (OverlayWidgetRegistration widget in OverlayWidgetCatalog.CreateFoundationWidgets())
            engine.Register(widget);
        engine.SetForegroundState(false);

        Assert.True(engine.BeginEdit());

        Assert.Equal(
            [OverlayWidgetIds.MapOverview, OverlayWidgetIds.Skills, OverlayWidgetIds.GameplayHud],
            engine.GetVisibleWidgets().Select(item => item.Registration.Definition.Id).ToArray());
    }

    [Fact]
    public void LayoutStoreRoundTripsPerPresetAndFallsBackOnInvalidJson()
    {
        string directory = Path.Combine(Path.GetTempPath(), "BeatInsight-edit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "overlay-layout.json");
        try
        {
            var store = new OverlayLayoutStore(path);
            OverlayLayout builtIn = OverlayLayoutCatalog.Get(OverlayLayoutIds.Minimal);
            OverlayLayout custom = builtIn with
            {
                Widgets = builtIn.Widgets.Select(item => item.WidgetId == OverlayWidgetIds.MapOverview
                    ? item with { X = 321, Width = 444, HasCustomSize = true }
                    : item).ToArray()
            };

            Assert.True(store.Save(OverlayLayoutIds.Minimal, custom));
            AssertLayoutEqual(
                custom,
                new OverlayLayoutStore(path).Load(OverlayLayoutIds.Minimal, builtIn));

            File.WriteAllText(path, "{ invalid");
            AssertLayoutEqual(builtIn, store.Load(OverlayLayoutIds.Minimal, builtIn));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void GameplayHudSwitchesExplicitlyBetweenAutoAndCustomSize() => RunSta(() =>
    {
        var window = new GameplayHudWindow();
        OverlayWidgetPlacement builtIn = Placement(
            OverlayLayoutCatalog.Get(OverlayLayoutIds.Minimal), OverlayWidgetIds.GameplayHud);

        window.Apply(OverlayEditPreview.GameplayHud(GameplayHudDisplayOptions.Default), builtIn, Theme());
        Assert.Equal(SizeToContent.Height, window.SizeToContent);

        window.Apply(
            OverlayEditPreview.GameplayHud(GameplayHudDisplayOptions.Default),
            builtIn with { Width = 480, Height = 360, HasCustomSize = true },
            Theme());
        Assert.Equal(SizeToContent.Manual, window.SizeToContent);
        Assert.Equal(480, window.Width);
        Assert.Equal(360, window.Height);

        window.Apply(OverlayEditPreview.GameplayHud(GameplayHudDisplayOptions.Default), builtIn, Theme());
        Assert.Equal(SizeToContent.Height, window.SizeToContent);
        window.Close();
    });

    [Fact]
    public void GameplayHudCustomSizeSurvivesDisplayOptionChanges() => RunSta(() =>
    {
        var window = new GameplayHudWindow();
        OverlayWidgetPlacement placement = Placement(
            OverlayLayoutCatalog.Get(OverlayLayoutIds.Minimal), OverlayWidgetIds.GameplayHud) with
        {
            Width = 470,
            Height = 330,
            HasCustomSize = true
        };
        window.Apply(
            OverlayEditPreview.GameplayHud(new(false, false, false, false, true, true)),
            placement,
            Theme());
        window.Apply(
            OverlayEditPreview.GameplayHud(new(true, true, false, true, false, false)),
            placement,
            Theme());

        Assert.Equal(SizeToContent.Manual, window.SizeToContent);
        Assert.Equal(470, window.Width);
        Assert.Equal(330, window.Height);
        window.Close();
    });

    [Fact]
    public void GameplayHudCreatedDuringEditRemainsHitTestableAfterSourceInitialization() => RunSta(() =>
    {
        var window = new GameplayHudWindow();
        window.SetInteractionMode(OverlayWindowInteractionMode.Editable);

        IntPtr handle = new WindowInteropHelper(window).EnsureHandle();
        int styles = GetWindowLong(handle, GwlExStyle);

        Assert.False(OverlayWindowInteraction.IsTransparentStyleSet(styles));
        window.Close();
    });

    private static OverlayEngine Engine() => new(
        OverlayLayoutCatalog.Get(OverlayLayoutIds.Minimal),
        OverlayThemeCatalog.Get(OverlayThemeIds.Default));

    private static OverlayWidgetPlacement Placement(OverlayLayout layout, string widgetId) =>
        Assert.Single(layout.Widgets, item => item.WidgetId == widgetId);

    private static void AssertLayoutEqual(OverlayLayout expected, OverlayLayout actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Widgets, actual.Widgets);
    }

    private static OverlayTheme Theme() =>
        OverlayThemeCatalog.Get(OverlayThemeIds.Default);

    private const int GwlExStyle = -20;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr windowHandle, int index);

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
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF test timed out");
        if (error is not null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }
}
