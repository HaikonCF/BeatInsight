using System.Reflection;

namespace BeatInsight.Diagnostics;

internal static class ReleaseInfo
{
    internal static string Version =>
        typeof(ReleaseInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? typeof(ReleaseInfo).Assembly.GetName().Version?.ToString(3)
        ?? "unknown";

    internal static string DisplayName => $"BeatInsight v{Version}";
}
