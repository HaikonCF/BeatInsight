using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BeatInsight.CommunityOverlays.Poc.Bridge;

/// <summary>
/// Valeurs de réglages d'un overlay, calculées comme tosu
/// (<c>parseCounterSettings(..., 'counter/get')</c>) à partir du
/// <c>settings.json</c> de l'overlay. Lecture seule : les valeurs personnalisées
/// enregistrées par tosu ne sont pas lues et rien n'est écrit (pas de
/// <c>settings.values.json</c>, contrairement à tosu).
/// </summary>
public static class CounterSettingsReader
{
    private const long MaxSettingsBytes = 1024 * 1024;

    public static JsonObject ReadDefaults(string overlayRoot)
    {
        var resolution = OverlayFileResolver.Resolve(overlayRoot, "/settings.json");
        if (resolution.Status != OverlayFileStatus.Found || resolution.FullPath is null)
            return new JsonObject();

        if (new FileInfo(resolution.FullPath).Length > MaxSettingsBytes)
            return new JsonObject();

        try
        {
            return FromSettingsJson(File.ReadAllText(resolution.FullPath));
        }
        catch (IOException)
        {
            return new JsonObject();
        }
    }

    /// <summary>Même conversion que tosu : number → nombre (NaN → 0), checkbox → booléen, header retiré.</summary>
    public static JsonObject FromSettingsJson(string settingsJson)
    {
        var values = new JsonObject();

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(settingsJson);
        }
        catch (JsonException)
        {
            return values; // tosu : JsonSafeParse → [] par défaut
        }

        if (root is not JsonArray settings)
            return values;

        foreach (JsonNode? item in settings)
        {
            if (item is not JsonObject setting)
                continue;
            if (setting["uniqueID"] is not JsonValue idValue || !idValue.TryGetValue(out string? id) || string.IsNullOrEmpty(id))
                continue;

            string type = setting["type"] is JsonValue t && t.TryGetValue(out string? typeName) ? typeName : "";
            JsonNode? value = setting["value"]?.DeepClone();

            switch (type)
            {
                case "header":
                    values.Remove(id);
                    break;
                case "number":
                    values[id] = ToNumber(value);
                    break;
                case "checkbox":
                    values[id] = value is JsonValue v && v.TryGetValue(out string? text) ? text == "true" : value;
                    break;
                default:
                    values[id] = value;
                    break;
            }
        }

        return values;
    }

    private static JsonNode ToNumber(JsonNode? value)
    {
        if (value is JsonValue v)
        {
            if (v.TryGetValue(out double number)) return JsonValue.Create(number);
            if (v.TryGetValue(out bool flag)) return JsonValue.Create(flag ? 1 : 0);
            if (v.TryGetValue(out string? text))
            {
                // JS : +"" === 0 ; isNaN("abc") → 0.
                if (string.IsNullOrWhiteSpace(text)) return JsonValue.Create(0);
                return double.TryParse(text, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double parsed)
                    ? JsonValue.Create(parsed)
                    : JsonValue.Create(0);
            }
        }

        return JsonValue.Create(0);
    }
}
