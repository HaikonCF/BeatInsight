using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BeatInsight.Integration.CommunityOverlays.Managed;

/// <summary>
/// FEAT-003 : contrat entre BeatInsight et un compagnon CommunityOverlays lancé en mode géré (<c>--managed-by</c>).
/// Une ligne JSON par message : commandes sur l'entrée standard du compagnon, événements sur sa sortie standard.
/// Fichier partagé avec le compagnon par lien (comme le contrat du Named Pipe) : aucune dépendance vers BeatInsight.
/// Le mode CLI autonome n'utilise jamais ce contrat.
/// </summary>
public static class CommunityOverlayCompanionProtocol
{
    public const string ManagedByArgument = "--managed-by";

    /// <summary>FEAT-003.1 : échelle du contenu (zoom WebView2) en pourcentage, au lancement.</summary>
    public const string ScaleArgument = "--scale";

    public const int DefaultScalePercent = 100;
    public const int MinimumScalePercent = 25;
    public const int MaximumScalePercent = 200;
    public const int ScaleStepPercent = 5;

    /// <summary>Codes de sortie du compagnon en mode géré.</summary>
    public const int ExitOk = 0;
    public const int ExitInitializationFailed = 2;
    public const int ExitDuplicate = 3;

    public const double MinimumSize = 40;
    public const double MaximumSize = 10_000;
    public const double MaximumCoordinate = 100_000;

    /// <summary>Taille maximale d'une ligne acceptée (au-delà : ignorée).</summary>
    public const int MaxLineLength = 4096;

    /// <summary>Nom du mutex local empêchant deux compagnons gérés sur le même overlay.</summary>
    public static string MutexNameFor(string profileName) => $@"Local\BeatInsight.CommunityOverlay.{profileName}";

    public static string Edit(bool enabled) =>
        new JsonObject { ["type"] = "edit", ["enabled"] = enabled }.ToJsonString();

    public static string Bounds(CompanionBounds bounds) => new JsonObject
    {
        ["type"] = "bounds",
        ["x"] = Round(bounds.X),
        ["y"] = Round(bounds.Y),
        ["width"] = Round(bounds.Width),
        ["height"] = Round(bounds.Height),
    }.ToJsonString();

    public static string Close() => new JsonObject { ["type"] = "close" }.ToJsonString();

    public static string Scale(int percent) =>
        new JsonObject { ["type"] = "scale", ["percent"] = NormalizeScale(percent) }.ToJsonString();

    /// <summary>BUG-003 : osu! (l'instance suivie par BeatInsight) est-il au premier plan de Windows.</summary>
    public static string Foreground(bool osuForeground) =>
        new JsonObject { ["type"] = "foreground", ["osu"] = osuForeground }.ToJsonString();

    /// <summary>Ramène une échelle dans [25, 200] au pas de 5 %.</summary>
    public static int NormalizeScale(double percent)
    {
        if (!double.IsFinite(percent))
            return DefaultScalePercent;
        double clamped = Math.Clamp(percent, MinimumScalePercent, MaximumScalePercent);
        return (int)(Math.Round(clamped / ScaleStepPercent, MidpointRounding.AwayFromZero) * ScaleStepPercent);
    }

    /// <summary>Facteur de zoom WebView2 correspondant (100 % = 1.0).</summary>
    public static double ZoomFactorFor(int percent) => NormalizeScale(percent) / 100.0;

    public static string Ready() => new JsonObject { ["type"] = "ready" }.ToJsonString();

    public static string UserClosed() => new JsonObject { ["type"] = "user-closed" }.ToJsonString();

    public static string Duplicate() => new JsonObject { ["type"] = "duplicate" }.ToJsonString();

    public static string Error(string message) =>
        new JsonObject { ["type"] = "error", ["message"] = Truncate(message, 512) }.ToJsonString();

    /// <summary>Lecture tolérante : une ligne invalide, inconnue ou hors bornes donne null (jamais d'exception).</summary>
    public static CompanionMessage? Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length > MaxLineLength)
            return null;

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(line) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }

        if (root?["type"] is not JsonValue typeValue || !typeValue.TryGetValue(out string? type))
            return null;

        switch (type)
        {
            case "edit":
                return root["enabled"] is JsonValue e && e.TryGetValue(out bool enabled)
                    ? new CompanionMessage(CompanionMessageKind.Edit, EditEnabled: enabled)
                    : null;
            case "bounds":
                return TryReadBounds(root, out CompanionBounds bounds)
                    ? new CompanionMessage(CompanionMessageKind.Bounds, Bounds: bounds)
                    : null;
            case "close":
                return new CompanionMessage(CompanionMessageKind.Close);
            case "scale":
                return TryNumber(root["percent"], out double percent) && double.IsFinite(percent)
                    ? new CompanionMessage(CompanionMessageKind.Scale, ScalePercent: NormalizeScale(percent))
                    : null;
            case "foreground":
                return root["osu"] is JsonValue f && f.TryGetValue(out bool osuForeground)
                    ? new CompanionMessage(CompanionMessageKind.Foreground, OsuForeground: osuForeground)
                    : null;
            case "ready":
                return new CompanionMessage(CompanionMessageKind.Ready);
            case "user-closed":
                return new CompanionMessage(CompanionMessageKind.UserClosed);
            case "duplicate":
                return new CompanionMessage(CompanionMessageKind.Duplicate);
            case "error":
                return new CompanionMessage(CompanionMessageKind.Error,
                    Message: root["message"] is JsonValue m && m.TryGetValue(out string? text) ? Truncate(text, 512) : "");
            default:
                return null;
        }
    }

    /// <summary>Bornes acceptées : nombres finis, taille entre <see cref="MinimumSize"/> et <see cref="MaximumSize"/>.</summary>
    public static bool IsValid(CompanionBounds bounds) =>
        double.IsFinite(bounds.X) && double.IsFinite(bounds.Y) &&
        double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height) &&
        Math.Abs(bounds.X) <= MaximumCoordinate && Math.Abs(bounds.Y) <= MaximumCoordinate &&
        bounds.Width is >= MinimumSize and <= MaximumSize &&
        bounds.Height is >= MinimumSize and <= MaximumSize;

    /// <summary>Argument <c>--window</c> du compagnon (culture invariante).</summary>
    public static string WindowArgument(CompanionBounds bounds) => string.Join(',',
        new[] { bounds.X, bounds.Y, bounds.Width, bounds.Height }
            .Select(v => Round(v).ToString(CultureInfo.InvariantCulture)));

    private static bool TryReadBounds(JsonObject root, out CompanionBounds bounds)
    {
        bounds = default;
        if (!TryNumber(root["x"], out double x) || !TryNumber(root["y"], out double y) ||
            !TryNumber(root["width"], out double width) || !TryNumber(root["height"], out double height))
            return false;

        bounds = new CompanionBounds(x, y, width, height);
        return IsValid(bounds);
    }

    private static bool TryNumber(JsonNode? node, out double value)
    {
        value = 0;
        return node is JsonValue v && v.TryGetValue(out value);
    }

    private static double Round(double value) => Math.Round(value, 1);

    private static string Truncate(string? text, int max) =>
        string.IsNullOrEmpty(text) ? "" : text.Length <= max ? text : text[..max];
}

public readonly record struct CompanionBounds(double X, double Y, double Width, double Height);

public enum CompanionMessageKind
{
    Edit,
    Bounds,
    Close,
    Scale,
    Foreground,
    Ready,
    UserClosed,
    Duplicate,
    Error,
}

public sealed record CompanionMessage(
    CompanionMessageKind Kind,
    bool EditEnabled = false,
    CompanionBounds? Bounds = null,
    string? Message = null,
    int ScalePercent = CommunityOverlayCompanionProtocol.DefaultScalePercent,
    bool OsuForeground = false);
