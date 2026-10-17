using System.Text.Json;
using System.Text.Json.Nodes;

namespace BeatInsight.CommunityOverlays.Poc.Bridge;

/// <summary>
/// Traitement des messages texte envoyés par un overlay sur une socket tosu,
/// calqué sur tosu <c>handleSocketCommands</c> (packages/server/utils/commands.ts).
/// Retourne la réponse à renvoyer à la seule socket émettrice, ou null.
/// </summary>
public sealed class TosuCommandHandler
{
    private readonly string _counterPath;
    private readonly Func<JsonObject> _settingsProvider;

    /// <param name="counterPath">Nom du dossier de l'overlay (<c>window.COUNTER_PATH</c>).</param>
    public TosuCommandHandler(string counterPath, Func<JsonObject> settingsProvider)
    {
        _counterPath = counterPath;
        _settingsProvider = settingsProvider;
    }

    /// <summary>Derniers filtres reçus par canal (non appliqués : le POC envoie toujours le payload complet).</summary>
    public Dictionary<string, string> ReceivedFilters { get; } = [];

    /// <param name="channel">Canal du bridge (v2, v1, precise, commands).</param>
    /// <param name="socketUrl">URL de la socket côté overlay, pour le paramètre <c>?l=</c>.</param>
    /// <param name="data">Texte envoyé par l'overlay.</param>
    public string? Handle(string channel, string socketUrl, string data)
    {
        if (!data.Contains(':'))
            return null;

        int first = data.IndexOf(':');
        int second = data.IndexOf(':', first + 1);

        string command = data[..first];
        string overlayName = SafeDecode(second == -1 ? data[(first + 1)..] : data[(first + 1)..second]);
        string legacyPayload = data[(first + 1)..];
        string payload = data[(second + 1)..];
        string overlayFrom = SafeDecode(QueryValue(socketUrl, "l") ?? "");

        JsonNode? message;
        switch (command)
        {
            case "getSettings":
                message = overlayName == overlayFrom && overlayFrom == _counterPath
                    ? _settingsProvider()
                    : new JsonObject { ["error"] = "Wrong overlay" };
                break;

            case "getOverlays":
            case "getCounters":
                // Réservé à l'overlay in-game de tosu.
                message = new JsonObject { ["error"] = "Wrong overlay" };
                break;

            case "applyFilters":
                ReceivedFilters[channel] = payload.StartsWith('[') ? payload : legacyPayload;
                return null; // tosu ne répond pas

            case "saveSettings":
                return null; // tosu ne répond pas ; le POC ne persiste rien

            case "updateSettings":
                // tosu renvoie le JSON reçu (aperçu depuis son tableau de bord).
                try
                {
                    message = JsonNode.Parse(payload);
                }
                catch (JsonException)
                {
                    return null;
                }
                break;

            default:
                message = null; // tosu : JSON.stringify omet message: undefined
                break;
        }

        var reply = new JsonObject { ["command"] = command };
        if (message is not null)
            reply["message"] = message;
        return reply.ToJsonString();
    }

    private static string SafeDecode(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return value;
        }
    }

    private static string? QueryValue(string url, string key)
    {
        int q = url.IndexOf('?');
        if (q < 0) return null;

        foreach (string pair in url[(q + 1)..].Split('&'))
        {
            int eq = pair.IndexOf('=');
            string name = eq < 0 ? pair : pair[..eq];
            if (name == key)
                return eq < 0 ? "" : pair[(eq + 1)..];
        }

        return null;
    }
}
