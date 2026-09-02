using BeatInsight.Diagnostics;
using BeatInsight.Models;
using BeatInsight.Parser;
using System.IO;
using Xunit;

namespace BeatInsight.Tests;

internal static class FixtureLoader
{
    public static Beatmap Load(string fixtureName)
    {
        DebugLogger.DebugMode = false;
        DebugLogger.DetailedDebug = false;

        string fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Maps",
            fixtureName);

        Assert.True(
            File.Exists(fixturePath),
            $"Fixture not found: {fixturePath}");

        return BeatmapParser.Load(fixturePath);
    }

    public static void AssertFinite(double value, string name)
    {
        Assert.False(double.IsNaN(value), $"{name} must not be NaN.");
        Assert.False(double.IsInfinity(value), $"{name} must not be infinite.");
    }
}
