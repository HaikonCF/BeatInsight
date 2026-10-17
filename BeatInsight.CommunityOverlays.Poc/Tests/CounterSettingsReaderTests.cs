using System.IO;
using BeatInsight.CommunityOverlays.Poc.Bridge;
using Xunit;

namespace BeatInsight.CommunityOverlays.Poc.Tests;

public class CounterSettingsReaderTests
{
    [Fact]
    public void Converts_values_like_tosu_counter_get()
    {
        const string settings = """
            [
              { "uniqueID": "title", "type": "header", "value": "Keys" },
              { "uniqueID": "k1Enable", "type": "checkbox", "value": true },
              { "uniqueID": "legacyFlag", "type": "checkbox", "value": "true" },
              { "uniqueID": "bpmInterval", "type": "number", "value": 5 },
              { "uniqueID": "fromText", "type": "number", "value": "3.5" },
              { "uniqueID": "broken", "type": "number", "value": "abc" },
              { "uniqueID": "width", "type": "text", "value": "300px" },
              { "uniqueID": "k1PressColor", "type": "color", "value": "#67b0fe" },
              { "uniqueID": "direction", "type": "options", "value": "right" }
            ]
            """;

        var values = CounterSettingsReader.FromSettingsJson(settings);

        Assert.False(values.ContainsKey("title"));
        Assert.True(values["k1Enable"]!.GetValue<bool>());
        Assert.True(values["legacyFlag"]!.GetValue<bool>());
        Assert.Equal(5, values["bpmInterval"]!.GetValue<double>());
        Assert.Equal(3.5, values["fromText"]!.GetValue<double>());
        Assert.Equal(0, values["broken"]!.GetValue<int>());
        Assert.Equal("300px", values["width"]!.GetValue<string>());
        Assert.Equal("#67b0fe", values["k1PressColor"]!.GetValue<string>());
        Assert.Equal("right", values["direction"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"a\":1}")]
    [InlineData("[1, null, {\"type\":\"text\"}]")]
    public void Invalid_settings_give_empty_values(string json)
    {
        Assert.Empty(CounterSettingsReader.FromSettingsJson(json));
    }

    [Fact]
    public void Missing_settings_file_gives_empty_values()
    {
        string folder = Directory.CreateTempSubdirectory("bi-overlay-").FullName;
        try
        {
            Assert.Empty(CounterSettingsReader.ReadDefaults(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
