using System.Text.Json.Nodes;
using BeatInsight.CommunityOverlays.Poc.Bridge;
using Xunit;

namespace BeatInsight.CommunityOverlays.Poc.Tests;

public class TosuCommandHandlerTests
{
    private const string Counter = "KeyOverlay by cyperdark";
    private const string CommandsUrl = "ws://overlay.beatinsight.invalid/websocket/commands?l=KeyOverlay%20by%20cyperdark";

    private static TosuCommandHandler Handler() =>
        new(Counter, () => new JsonObject { ["k1Enable"] = true, ["width"] = "300px" });

    /// <summary>Régression KeyOverlay : getSettings doit répondre, sinon aucune touche n'est activée.</summary>
    [Fact]
    public void GetSettings_for_own_overlay_returns_values()
    {
        string? reply = Handler().Handle("commands", CommandsUrl, "getSettings:KeyOverlay%20by%20cyperdark");

        var json = JsonNode.Parse(reply!)!;
        Assert.Equal("getSettings", json["command"]!.GetValue<string>());
        Assert.True(json["message"]!["k1Enable"]!.GetValue<bool>());
        Assert.Equal("300px", json["message"]!["width"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("getSettings:Other%20overlay")] // autre overlay
    [InlineData("getSettings:undefined")]       // COUNTER_PATH absent (comportement avant correction)
    public void GetSettings_for_another_overlay_is_refused(string data)
    {
        string? reply = Handler().Handle("commands", CommandsUrl, data);

        Assert.Equal("Wrong overlay", JsonNode.Parse(reply!)!["message"]!["error"]!.GetValue<string>());
    }

    [Fact]
    public void GetSettings_without_l_parameter_is_refused()
    {
        string? reply = Handler().Handle("commands", "ws://overlay.beatinsight.invalid/websocket/commands", "getSettings:KeyOverlay%20by%20cyperdark");

        Assert.Equal("Wrong overlay", JsonNode.Parse(reply!)!["message"]!["error"]!.GetValue<string>());
    }

    [Fact]
    public void ApplyFilters_is_recorded_without_reply()
    {
        var handler = Handler();

        string? reply = handler.Handle("precise", "ws://x/websocket/v2/precise?l=a", "applyFilters:[\"keys\"]");

        Assert.Null(reply);
        Assert.Equal("[\"keys\"]", handler.ReceivedFilters["precise"]);
    }

    [Theory]
    [InlineData("Client Closed!")]               // Simplistic envoie ce texte sans ":"
    [InlineData("saveSettings:a:[{\"x\":1}]")]   // pas de persistance, pas de réponse (comme tosu)
    public void Messages_without_reply(string data)
    {
        Assert.Null(Handler().Handle("v1", CommandsUrl, data));
    }

    [Fact]
    public void Unknown_command_echoes_command_only_like_tosu()
    {
        Assert.Equal("{\"command\":\"ping\"}", Handler().Handle("commands", CommandsUrl, "ping:x"));
    }

    [Fact]
    public void GetCounters_is_reserved_to_tosu_ingame_overlay()
    {
        string? reply = Handler().Handle("commands", CommandsUrl, "getCounters:KeyOverlay%20by%20cyperdark");

        Assert.Equal("Wrong overlay", JsonNode.Parse(reply!)!["message"]!["error"]!.GetValue<string>());
    }
}
