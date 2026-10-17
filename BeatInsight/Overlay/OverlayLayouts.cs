using System.IO;
using System.Text.Json;

namespace BeatInsight.Overlay;

public static class OverlayLayoutIds
{
    public const string Minimal = "Minimal";
    public const string Compact = "Compact";
    public const string Analysis = "Analysis";
    public const string Timeline = "Timeline";
    public const string Custom = "Custom";
}

public sealed record OverlayWidgetPlacement(
    string WidgetId,
    bool IsVisible,
    double X,
    double Y,
    double Width,
    double Height,
    int ZIndex,
    bool HasCustomSize = false,
    IReadOnlyDictionary<string, JsonElement>? Configuration = null);

public sealed record OverlayLayout(
    string Id,
    IReadOnlyList<OverlayWidgetPlacement> Widgets);

public sealed record OverlayLoadResult<T>(
    T Value,
    bool UsedFallback,
    string? Diagnostic);

public static class OverlayLayoutCatalog
{
    private static readonly IReadOnlyDictionary<string, OverlayLayout> layouts =
        new Dictionary<string, OverlayLayout>(StringComparer.Ordinal)
        {
            [OverlayLayoutIds.Minimal] = Layout(OverlayLayoutIds.Minimal,
                Place(OverlayWidgetIds.MapOverview, 24, 24, 360, 220, 0),
                Place(OverlayWidgetIds.Skills, 24, 256, 360, 264, 1),
                Place(OverlayWidgetIds.GameplayHud, 24, 24, 360, 560, 2)),
            [OverlayLayoutIds.Compact] = Layout(OverlayLayoutIds.Compact,
                Place(OverlayWidgetIds.MapOverview, 24, 24, 360, 160, 0),
                Place(OverlayWidgetIds.Skills, 24, 196, 360, 180, 1)),
            [OverlayLayoutIds.Analysis] = Layout(OverlayLayoutIds.Analysis,
                Place(OverlayWidgetIds.MapOverview, 24, 24, 360, 160, 0),
                Place(OverlayWidgetIds.Skills, 24, 196, 360, 180, 1),
                Place(OverlayWidgetIds.GameplayHud, 396, 24, 360, 560, 2)),
            [OverlayLayoutIds.Timeline] = Layout(OverlayLayoutIds.Timeline,
                Place(OverlayWidgetIds.MapOverview, 24, 24, 360, 220, 0),
                Place(OverlayWidgetIds.Skills, 24, 256, 360, 264, 1),
                Place(OverlayWidgetIds.GameplayHud, 24, 24, 360, 560, 2)),
            [OverlayLayoutIds.Custom] = Layout(OverlayLayoutIds.Custom)
        };

    public static IReadOnlyCollection<string> Ids => layouts.Keys.ToArray();

    public static IReadOnlyList<string> ExposedIds { get; } =
    [
        OverlayLayoutIds.Minimal,
        OverlayLayoutIds.Analysis,
        OverlayLayoutIds.Timeline
    ];

    public static OverlayLayout Get(string id) =>
        layouts.TryGetValue(id, out OverlayLayout? layout)
            ? layout
            : throw new KeyNotFoundException($"Unknown overlay layout '{id}'.");

    public static bool TryGet(string id, out OverlayLayout? layout) => layouts.TryGetValue(id, out layout);

    private static OverlayLayout Layout(string id, params OverlayWidgetPlacement[] widgets) => new(id, widgets);

    private static OverlayWidgetPlacement Place(
        string widgetId,
        double x,
        double y,
        double width,
        double height,
        int zIndex) => new(widgetId, true, x, y, width, height, zIndex);
}

public readonly record struct OverlayVirtualScreen(
    double Left,
    double Top,
    double Width,
    double Height)
{
    public bool IsValid =>
        double.IsFinite(Left) && double.IsFinite(Top) &&
        double.IsFinite(Width) && double.IsFinite(Height) &&
        Width > 0 && Height > 0;
}

public static class OverlayPlacementPolicy
{
    public static (double Width, double Height) MinimumSize(string widgetId) => widgetId switch
    {
        OverlayWidgetIds.MapOverview => (240, 120),
        OverlayWidgetIds.Skills => (260, 180),
        OverlayWidgetIds.GameplayHud => (280, 180),
        _ => (120, 80)
    };

    public static OverlayWidgetPlacement Resize(
        OverlayWidgetPlacement placement,
        double width,
        double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height))
            return placement;

        (double minimumWidth, double minimumHeight) = MinimumSize(placement.WidgetId);
        return placement with
        {
            Width = Math.Max(minimumWidth, width),
            Height = Math.Max(minimumHeight, height),
            HasCustomSize = true
        };
    }

    public static OverlayLayout ValidateForSave(
        OverlayLayout layout,
        OverlayVirtualScreen virtualScreen)
    {
        if (!virtualScreen.IsValid)
            throw new ArgumentException("A finite, positive virtual screen is required.", nameof(virtualScreen));

        OverlayWidgetPlacement[] widgets = layout.Widgets
            .Select(item => ClampForSave(item, virtualScreen))
            .ToArray();
        return layout with { Widgets = widgets };
    }

    private static OverlayWidgetPlacement ClampForSave(
        OverlayWidgetPlacement placement,
        OverlayVirtualScreen screen)
    {
        (double minimumWidth, double minimumHeight) = MinimumSize(placement.WidgetId);
        double width = Math.Clamp(
            placement.Width,
            minimumWidth,
            Math.Max(minimumWidth, screen.Width * 2));
        double height = Math.Clamp(
            placement.Height,
            minimumHeight,
            Math.Max(minimumHeight, screen.Height * 2));
        double x = Math.Clamp(
            placement.X,
            screen.Left - width / 2,
            screen.Left + screen.Width - width / 2);
        double y = Math.Clamp(
            placement.Y,
            screen.Top - height / 2,
            screen.Top + screen.Height - height / 2);
        return placement with { X = x, Y = y, Width = width, Height = height };
    }
}

public static class OverlayLayoutResolver
{
    private static readonly JsonSerializerOptions options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static OverlayLoadResult<OverlayLayout> ResolveJson(
        string? json,
        OverlayLayout? fallback = null)
    {
        fallback ??= OverlayLayoutCatalog.Get(OverlayLayoutIds.Minimal);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new(fallback, true, "Layout JSON was empty.");
        }

        try
        {
            OverlayLayout? layout = JsonSerializer.Deserialize<OverlayLayout>(json, options);
            if (layout is null)
            {
                return new(fallback, true, "Layout JSON produced no layout.");
            }

            if (!TryValidate(layout, out string? diagnostic))
            {
                return new(fallback, true, diagnostic);
            }

            return new(layout, false, null);
        }
        catch (JsonException exception)
        {
            return new(fallback, true, $"Invalid layout JSON: {exception.GetType().Name}.");
        }
        catch (NotSupportedException exception)
        {
            return new(fallback, true, $"Unsupported layout JSON: {exception.GetType().Name}.");
        }
    }

    public static OverlayLoadResult<OverlayLayout> ResolveFile(
        string path,
        OverlayLayout? fallback = null)
    {
        try
        {
            return ResolveJson(File.ReadAllText(path), fallback);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            fallback ??= OverlayLayoutCatalog.Get(OverlayLayoutIds.Minimal);
            return new(fallback, true, $"Layout file unavailable: {exception.GetType().Name}.");
        }
    }

    public static bool TryValidate(OverlayLayout layout, out string? diagnostic)
    {
        if (string.IsNullOrWhiteSpace(layout.Id) || layout.Widgets is null)
        {
            diagnostic = "Layout ID and widget collection are required.";
            return false;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (OverlayWidgetPlacement placement in layout.Widgets)
        {
            if (string.IsNullOrWhiteSpace(placement.WidgetId) ||
                !ids.Add(placement.WidgetId) ||
                !double.IsFinite(placement.X) ||
                !double.IsFinite(placement.Y) ||
                !double.IsFinite(placement.Width) ||
                !double.IsFinite(placement.Height) ||
                placement.Width <= 0 ||
                placement.Height <= 0)
            {
                diagnostic = "Layout contains an invalid or duplicate widget placement.";
                return false;
            }
        }

        diagnostic = null;
        return true;
    }
}
