namespace BeatInsight.Overlay;

public sealed record ResolvedOverlayWidget(
    OverlayWidgetRegistration Registration,
    OverlayWidgetPlacement Placement,
    OverlayTheme Theme,
    OverlaySnapshot? Snapshot);

public sealed class OverlayEngine
{
    private readonly object gate = new();
    private readonly Dictionary<string, OverlayWidgetRegistration> widgets = new(StringComparer.Ordinal);
    private readonly HashSet<string> faultedWidgets = new(StringComparer.Ordinal);
    private OverlaySnapshot? currentSnapshot;
    private OverlaySnapshot? latestProductionSnapshot;
    private OverlayGameState currentGameState = OverlayGameState.Unknown;
    private OverlayDisplayCompatibility displayCompatibility =
        OverlayDisplayCompatibility.Supported(OverlayDisplayMode.Windowed);
    private bool osuIsForeground;
    private OverlayLayout? entryLayoutSnapshot;
    private OverlayLayout? workingLayout;

    public event EventHandler? EditModeChanged;

    public OverlayEngine(
        OverlayLayout? initialLayout = null,
        OverlayTheme? initialTheme = null)
    {
        CurrentLayout = initialLayout ?? OverlayLayoutCatalog.Get(OverlayLayoutIds.Minimal);
        CurrentTheme = initialTheme ?? OverlayThemeCatalog.Get(OverlayThemeIds.Default);
        EnsureValid(CurrentLayout);
        EnsureValid(CurrentTheme);
    }

    public OverlayLayout CurrentLayout { get; private set; }

    public bool IsOverlayEditMode
    {
        get
        {
            lock (gate)
            {
                return workingLayout is not null;
            }
        }
    }

    public OverlayLayout? EntryLayoutSnapshot
    {
        get
        {
            lock (gate)
            {
                return entryLayoutSnapshot;
            }
        }
    }

    public OverlayLayout? WorkingLayout
    {
        get
        {
            lock (gate)
            {
                return workingLayout;
            }
        }
    }

    public OverlayTheme CurrentTheme { get; private set; }

    public OverlayDisplayCompatibility DisplayCompatibility
    {
        get
        {
            lock (gate)
            {
                return displayCompatibility;
            }
        }
    }

    public bool OverlaySupported => DisplayCompatibility.OverlaySupported;

    public bool OsuIsForeground
    {
        get
        {
            lock (gate)
            {
                return osuIsForeground;
            }
        }
    }

    public bool OverlayVisible
    {
        get
        {
            lock (gate)
            {
                return IsOverlayVisible();
            }
        }
    }

    public OverlaySnapshot? CurrentSnapshot
    {
        get
        {
            lock (gate)
            {
                return currentSnapshot;
            }
        }
    }

    public OverlayGameState CurrentGameState
    {
        get
        {
            lock (gate)
            {
                return currentGameState;
            }
        }
    }

    public IReadOnlyCollection<OverlayWidgetRegistration> Widgets
    {
        get
        {
            lock (gate)
            {
                return widgets.Values.ToArray();
            }
        }
    }

    public void Register(OverlayWidgetRegistration widget)
    {
        ArgumentNullException.ThrowIfNull(widget);
        lock (gate)
        {
            if (!widgets.TryAdd(widget.Definition.Id, widget))
            {
                throw new InvalidOperationException(
                    $"Overlay widget '{widget.Definition.Id}' is already registered.");
            }

            PublishToWidget(widget, currentSnapshot);
        }
    }

    public bool Unregister(string widgetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetId);
        lock (gate)
        {
            if (!widgets.Remove(widgetId, out OverlayWidgetRegistration? widget))
            {
                return false;
            }

            faultedWidgets.Remove(widgetId);
            try
            {
                widget.Publish(null);
            }
            catch
            {
                // Widget isolation: removal must still complete.
            }

            return true;
        }
    }

    public bool TryGetWidget(string widgetId, out OverlayWidgetRegistration? widget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetId);
        lock (gate)
        {
            return widgets.TryGetValue(widgetId, out widget);
        }
    }

    public void PublishSnapshot(OverlaySnapshot? snapshot)
    {
        lock (gate)
        {
            latestProductionSnapshot = snapshot;
            if (workingLayout is not null)
                return;

            currentSnapshot = snapshot;
            currentGameState = snapshot?.GameMode ?? OverlayGameState.Unknown;
            faultedWidgets.Clear();
            foreach (OverlayWidgetRegistration widget in widgets.Values)
            {
                PublishToWidget(widget, snapshot);
            }
        }
    }

    public bool SetGameState(OverlayGameState gameState)
    {
        lock (gate)
        {
            if (currentGameState == gameState)
            {
                return false;
            }

            currentGameState = gameState;
            if (currentSnapshot is not null)
            {
                currentSnapshot = currentSnapshot with { GameMode = gameState };
            }

            RefreshWidgets();
            return true;
        }
    }

    public bool SetDisplayCompatibility(OverlayDisplayCompatibility compatibility)
    {
        ArgumentNullException.ThrowIfNull(compatibility);
        lock (gate)
        {
            if (displayCompatibility == compatibility)
            {
                return false;
            }

            displayCompatibility = compatibility;
            RefreshWidgets();

            return true;
        }
    }

    public bool SetForegroundState(bool isOsuForeground)
    {
        lock (gate)
        {
            if (osuIsForeground == isOsuForeground)
            {
                return false;
            }

            osuIsForeground = isOsuForeground;
            RefreshWidgets();
            return true;
        }
    }

    public void ApplyLayout(OverlayLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        EnsureValid(layout);
        lock (gate)
        {
            if (workingLayout is not null)
                return;

            CurrentLayout = layout;
            RefreshWidgets();
        }
    }

    public void ApplyTheme(OverlayTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        EnsureValid(theme);
        lock (gate)
        {
            if (ReferenceEquals(CurrentTheme, theme))
                return;

            CurrentTheme = theme;
            RefreshWidgets();
        }
    }

    public IReadOnlyList<ResolvedOverlayWidget> GetVisibleWidgets()
    {
        lock (gate)
        {
            bool editing = workingLayout is not null;
            if ((!editing && currentSnapshot is null) || !IsOverlayVisible())
            {
                return [];
            }

            var resolved = new List<ResolvedOverlayWidget>();
            foreach (OverlayWidgetPlacement placement in CurrentLayout.Widgets)
            {
                if (!placement.IsVisible ||
                    !widgets.TryGetValue(placement.WidgetId, out OverlayWidgetRegistration? widget) ||
                    faultedWidgets.Contains(placement.WidgetId) ||
                    (!editing && !widget.CanDisplay(currentSnapshot!)))
                {
                    continue;
                }

                if (editing && placement.WidgetId is not (
                    OverlayWidgetIds.MapOverview or
                    OverlayWidgetIds.Skills or
                    OverlayWidgetIds.GameplayHud))
                {
                    continue;
                }

                resolved.Add(new(widget, placement, CurrentTheme, currentSnapshot));
            }

            return resolved
                .OrderBy(item => item.Placement.ZIndex)
                .ThenBy(item => item.Registration.Definition.Id, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public bool BeginEdit()
    {
        lock (gate)
        {
            if (workingLayout is not null || !displayCompatibility.OverlaySupported)
                return false;

            entryLayoutSnapshot = CloneLayout(CurrentLayout);
            workingLayout = CloneLayout(CurrentLayout);
            CurrentLayout = workingLayout;
            RefreshWidgets();
        }

        EditModeChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool UpdateWorkingPlacement(
        string widgetId,
        double x,
        double y,
        double width,
        double height,
        bool resized)
    {
        lock (gate)
        {
            if (workingLayout is null ||
                !double.IsFinite(x) || !double.IsFinite(y))
            {
                return false;
            }

            int index = workingLayout.Widgets
                .Select((item, itemIndex) => (item, itemIndex))
                .FirstOrDefault(pair => pair.item.WidgetId == widgetId)
                .itemIndex;
            if (index < 0 || index >= workingLayout.Widgets.Count ||
                workingLayout.Widgets[index].WidgetId != widgetId)
            {
                return false;
            }

            OverlayWidgetPlacement current = workingLayout.Widgets[index];
            OverlayWidgetPlacement updated = resized
                ? OverlayPlacementPolicy.Resize(current, width, height) with { X = x, Y = y }
                : current with { X = x, Y = y };
            OverlayWidgetPlacement[] placements = workingLayout.Widgets.ToArray();
            placements[index] = updated;
            workingLayout = workingLayout with { Widgets = placements };
            CurrentLayout = workingLayout;
            return true;
        }
    }

    public bool ResetWorkingLayout(OverlayLayout builtInLayout)
    {
        ArgumentNullException.ThrowIfNull(builtInLayout);
        EnsureValid(builtInLayout);
        lock (gate)
        {
            if (workingLayout is null)
                return false;

            workingLayout = CloneLayout(builtInLayout);
            CurrentLayout = workingLayout;
            RefreshWidgets();
            return true;
        }
    }

    public OverlayLayout? PrepareWorkingLayoutForSave(OverlayVirtualScreen virtualScreen)
    {
        lock (gate)
        {
            return workingLayout is null
                ? null
                : OverlayPlacementPolicy.ValidateForSave(workingLayout, virtualScreen);
        }
    }

    public bool CompleteEdit(OverlayLayout savedLayout)
    {
        ArgumentNullException.ThrowIfNull(savedLayout);
        EnsureValid(savedLayout);
        lock (gate)
        {
            if (workingLayout is null)
                return false;

            CurrentLayout = CloneLayout(savedLayout);
            EndEditCore();
        }

        EditModeChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool CancelEdit()
    {
        lock (gate)
        {
            if (workingLayout is null || entryLayoutSnapshot is null)
                return false;

            CurrentLayout = CloneLayout(entryLayoutSnapshot);
            EndEditCore();
        }

        EditModeChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void PublishToWidget(OverlayWidgetRegistration widget, OverlaySnapshot? snapshot)
    {
        try
        {
            widget.Publish(snapshot);
            faultedWidgets.Remove(widget.Definition.Id);
        }
        catch
        {
            faultedWidgets.Add(widget.Definition.Id);
        }
    }

    private bool IsOverlayVisible() =>
        displayCompatibility.OverlaySupported &&
        (workingLayout is not null || osuIsForeground);

    private void EndEditCore()
    {
        workingLayout = null;
        entryLayoutSnapshot = null;
        currentSnapshot = latestProductionSnapshot;
        currentGameState = currentSnapshot?.GameMode ?? OverlayGameState.Unknown;
        RefreshWidgets();
    }

    private static OverlayLayout CloneLayout(OverlayLayout layout) =>
        layout with
        {
            Widgets = layout.Widgets.Select(item => item with { }).ToArray()
        };

    private void RefreshWidgets()
    {
        foreach (OverlayWidgetRegistration widget in widgets.Values)
        {
            PublishToWidget(widget, currentSnapshot);
        }
    }

    private static void EnsureValid(OverlayLayout layout)
    {
        if (!OverlayLayoutResolver.TryValidate(layout, out string? diagnostic))
        {
            throw new ArgumentException(diagnostic, nameof(layout));
        }
    }

    private static void EnsureValid(OverlayTheme theme)
    {
        if (!OverlayThemeResolver.TryValidate(theme, out string? diagnostic))
        {
            throw new ArgumentException(diagnostic, nameof(theme));
        }
    }
}
