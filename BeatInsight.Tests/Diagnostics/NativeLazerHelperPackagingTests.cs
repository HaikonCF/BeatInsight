using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace BeatInsight.Tests.Diagnostics;

public sealed class NativeLazerHelperPackagingTests
{
    [Fact]
    public void PackagedHelper_ContainsEveryRuntimeAssemblyDeclaredByDepsFile()
    {
        string repositoryRoot = ResolveRepositoryRoot();
        string configuration = ResolveConfiguration();
        string helperDirectory = Path.Combine(
            repositoryRoot,
            "BeatInsight",
            "bin",
            configuration,
            "net8.0-windows",
            "NativeLazer");
        string depsPath = Path.Combine(helperDirectory, "NativeLazerFastRefreshRunner.deps.json");

        Assert.True(File.Exists(depsPath), $"Packaged helper deps file not found at {depsPath}.");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(depsPath));
        string targetName = document.RootElement
            .GetProperty("runtimeTarget")
            .GetProperty("name")
            .GetString()!;
        JsonElement target = document.RootElement.GetProperty("targets").GetProperty(targetName);

        string[] missing = target.EnumerateObject()
            .Where(library => library.Value.TryGetProperty("runtime", out _))
            .SelectMany(library => library.Value.GetProperty("runtime").EnumerateObject())
            .Select(asset => Path.GetFileName(asset.Name))
            .Where(fileName => !string.Equals(
                fileName,
                "NativeLazerFastRefreshRunner.dll",
                StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(fileName => !File.Exists(Path.Combine(helperDirectory, fileName)))
            .OrderBy(fileName => fileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(missing.Length == 0,
            $"Packaged NativeLazer helper is missing runtime assemblies: {string.Join(", ", missing)}");
    }

    private static string ResolveConfiguration()
    {
        string[] segments = AppContext.BaseDirectory.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        int binIndex = Array.FindLastIndex(
            segments,
            segment => string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase));
        if (binIndex >= 0 && binIndex + 1 < segments.Length)
            return segments[binIndex + 1];

        throw new InvalidOperationException(
            $"Could not determine build configuration from {AppContext.BaseDirectory}.");
    }

    private static string ResolveRepositoryRoot()
    {
        string? current = AppContext.BaseDirectory;
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current, "BeatInsight.slnx")) ||
                File.Exists(Path.Combine(current, "BeatInsight.sln")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new InvalidOperationException(
            "Could not locate repository root containing BeatInsight.slnx.");
    }
}
