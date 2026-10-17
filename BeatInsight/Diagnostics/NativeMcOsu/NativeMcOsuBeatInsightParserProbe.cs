using BeatInsight.Parser;

namespace BeatInsight.Diagnostics.NativeMcOsu;

internal static class NativeMcOsuBeatInsightParserProbe
{
    internal static bool CanParse(string path)
    {
        try
        {
            _ = BeatmapParser.Load(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
