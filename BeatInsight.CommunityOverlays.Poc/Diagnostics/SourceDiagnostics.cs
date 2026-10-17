using System.Text.Json.Nodes;
using BeatInsight.CommunityOverlays.Poc.Adapter;
using BeatInsight.CommunityOverlays.Poc.Adapter.Live;
using BeatInsight.CommunityOverlays.Poc.Bridge;

namespace BeatInsight.CommunityOverlays.Poc.Diagnostics;

/// <summary>
/// V3.14-COM-10 : état de la source au moment d'un échantillon smoke (identités, états des champs,
/// pipe, et sous-ensemble du JSON tosu émis), pour comparer les valeurs affichées aux snapshots source.
/// Aucun chemin absolu n'est écrit.
/// </summary>
internal static class SourceDiagnostics
{
    public static JsonObject Describe(ICommunityOverlayDataSource source)
    {
        var s = source.Current;
        var v1 = TosuPayloadBuilder.BuildV1(s);
        var states = new JsonObject();
        foreach (var (field, state) in LiveCommunityOverlayAdapter.FieldStates(s))
            states[field] = state.ToString();

        var result = new JsonObject
        {
            ["kind"] = s.Source.ToString(),
            ["playId"] = s.Identity.PlayId,
            ["fieldStates"] = states,
            ["tosu"] = new JsonObject
            {
                ["state"] = v1["menu"]!["state"]!.DeepClone(),
                ["md5"] = v1["menu"]!["bm"]!["md5"]!.DeepClone(),
                ["title"] = v1["menu"]!["bm"]!["metadata"]!["title"]!.DeepClone(),
                ["difficulty"] = v1["menu"]!["bm"]!["metadata"]!["difficulty"]!.DeepClone(),
                ["time"] = v1["menu"]!["bm"]!["time"]!["current"]!.DeepClone(),
                ["mp3"] = v1["menu"]!["bm"]!["time"]!["mp3"]!.DeepClone(),
                ["mods"] = v1["menu"]!["mods"]!["str"]!.DeepClone(),
                ["pp100"] = v1["menu"]!["pp"]!["100"]!.DeepClone(),
                ["hits"] = v1["gameplay"]!["hits"]!.DeepClone(),
                ["combo"] = v1["gameplay"]!["combo"]!.DeepClone(),
                ["accuracy"] = v1["gameplay"]!["accuracy"]!.DeepClone(),
                ["pp"] = v1["gameplay"]!["pp"]!.DeepClone(),
                ["folder"] = v1["menu"]!["bm"]!["path"]!["folder"]!.DeepClone(),
                ["bg"] = v1["menu"]!["bm"]!["path"]!["bg"]!.DeepClone(),
                ["audio"] = v1["menu"]!["bm"]!["path"]!["audio"]!.DeepClone(),
            },
        };

        switch (source)
        {
            case PipeLiveCommunityOverlayDataSource pipe:
                result["pipe"] = new JsonObject
                {
                    ["state"] = pipe.Client.State.ToString(),
                    ["received"] = pipe.Client.Received,
                    ["rejected"] = pipe.Client.Rejected,
                    ["connections"] = pipe.Client.Connections,
                    ["lastRejection"] = pipe.Client.LastRejection,
                    ["lastSequence"] = pipe.Client.Latest?.Envelope.Sequence,
                    ["pipeName"] = pipe.Client.ConnectedPipeName,
                    ["version"] = pipe.Client.Latest?.Envelope.Version,
                    ["messageAgeMs"] = pipe.Client.Latest is { } r
                        ? Math.Round((DateTimeOffset.UtcNow - r.Envelope.SentAtUtc).TotalMilliseconds, 1)
                        : null,
                    // Valeurs brutes reçues de BeatInsight, pour comparaison avec le JSON émis.
                    ["raw"] = pipe.Client.Latest is { Envelope.Snapshot: var raw }
                        ? new JsonObject
                        {
                            ["availability"] = raw.Game?.Availability.ToString(),
                            ["route"] = raw.Route?.Route.ToString(),
                            ["beatmapId"] = raw.Game?.CurrentBeatmapId,
                            ["md5"] = raw.OsuFile?.Md5,
                            ["suspension"] = raw.Suspension.ToString(),
                            ["timeMs"] = raw.LiveGameplay?.CurrentTimeMs ?? raw.Stats?.CurrentTimeMs,
                            ["hits"] = raw.Stats is { } st ? $"{st.Count300}/{st.Count100}/{st.Count50}/{st.CountMiss}" : null,
                            ["combo"] = raw.Stats is { } st2 ? $"{st2.CurrentCombo}/{st2.MaxComboReached}" : null,
                            ["accuracy"] = raw.Stats?.Accuracy,
                            ["mods"] = raw.Stats is { } st3 ? string.Join("", st3.Mods) : null,
                            ["pp"] = raw.Performance is { } p ? $"{p.CurrentPp:F2}/{p.FcPp:F2}/{p.SsPp:F2}" : null,
                            ["audio"] = raw.Audio is { } a ? $"{a.Status}/{a.Format}/{a.DurationMs}" : null,
                            ["statsAgeMs"] =raw.Stats is { } st4 ? Math.Round((DateTimeOffset.UtcNow - st4.ObservedAtUtc).TotalMilliseconds) : null,
                        }
                        : null,
                };
                result["events"] = new JsonArray(pipe.LastEvents.Select(e => (JsonNode)e.ToString()).ToArray());
                // V3.14-COM-PP-03 : canal selection (opt-in).
                if (pipe.Selection is { } sel)
                {
                    result["selection"] = new JsonObject
                    {
                        ["connected"] = sel.IsConnected,
                        ["received"] = sel.Received,
                        ["rejected"] = sel.Rejected,
                        ["stale"] = sel.Stale,
                        ["lastRejection"] = sel.LastRejection,
                        ["applied"] = pipe.LastSelectionStatus,
                        ["validity"] = sel.Latest?.Envelope.Validity.ToString(),
                        ["generation"] = sel.Latest?.Envelope.SelectionGeneration,
                        ["mods"] = sel.Latest?.Envelope.ModsKey,
                        ["ss"] = sel.Latest?.Envelope.Performance?.SsPp,
                        ["pp95"] = sel.Latest?.Envelope.Performance?.Accuracy95Pp,
                    };
                }
                break;
            case SyntheticLiveCommunityOverlayDataSource synthetic:
                result["events"] = new JsonArray(synthetic.LastEvents.Select(e => (JsonNode)e.ToString()).ToArray());
                break;
        }
        return result;
    }
}
