using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using BeatInsight.Analysis;
using Xunit;

namespace BeatInsight.Tests.Architecture;

public sealed class RuntimeMlIsolationTests
{
    private static readonly string RepositoryRoot = ResolveRepositoryRoot();

    [Fact]
    public void RuntimeAssembly_MustNotReferenceMlAssemblies()
    {
        Assembly runtimeAssembly = typeof(GameplayAnalyzer).Assembly;
        AssemblyName[] referencedAssemblies = runtimeAssembly.GetReferencedAssemblies();

        string[] forbiddenNames =
        [
            "BeatInsight.ML",
            "BeatInsight.ML.Tests",
            "BeatInsight.MlBaseline",
            "Microsoft.ML",
            "Microsoft.ML.Core",
            "Microsoft.ML.Data",
            "Microsoft.ML.FastTree",
            "LightGBM",
            "TorchSharp",
            "TensorFlow.NET",
            "Microsoft.ML.OnnxRuntime"
        ];

        foreach (AssemblyName referenced in referencedAssemblies)
        {
            Assert.DoesNotContain(
                forbiddenNames,
                forbidden => string.Equals(referenced.Name, forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void RuntimeCsproj_MustNotReferenceMlProjectsOrPackages()
    {
        string csprojPath = Path.Combine(RepositoryRoot, "BeatInsight", "BeatInsight.csproj");
        Assert.True(File.Exists(csprojPath), $"Csproj not found at {csprojPath}");

        XDocument doc = XDocument.Load(csprojPath);

        string[] projectReferences = doc.Descendants("ProjectReference")
            .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
            .ToArray();

        foreach (string pr in projectReferences)
        {
            Assert.DoesNotContain("BeatInsight.ML", pr, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("BeatInsight.MlBaseline", pr, StringComparison.OrdinalIgnoreCase);
        }

        string[] packageReferences = doc.Descendants("PackageReference")
            .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
            .ToArray();

        string[] forbiddenPackages =
        [
            "Microsoft.ML",
            "LightGBM",
            "TorchSharp",
            "TensorFlow",
            "Microsoft.ML.OnnxRuntime"
        ];

        foreach (string pkg in packageReferences)
        {
            Assert.DoesNotContain(
                forbiddenPackages,
                forbidden => pkg.StartsWith(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void RuntimeSourceFiles_MustNotImportBeatInsightMlNamespace()
    {
        string beatInsightDir = Path.Combine(RepositoryRoot, "BeatInsight");
        Assert.True(Directory.Exists(beatInsightDir), $"Directory not found at {beatInsightDir}");

        string[] csFiles = Directory.GetFiles(beatInsightDir, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase) &&
                           !path.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (string file in csFiles)
        {
            string content = File.ReadAllText(file);
            Assert.False(
                content.Contains("using BeatInsight.ML", StringComparison.Ordinal),
                $"File {file} illegally references 'using BeatInsight.ML'");
        }
    }

    private static string ResolveRepositoryRoot()
    {
        string? current = AppDomain.CurrentDomain.BaseDirectory;
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current, "BeatInsight.slnx")) ||
                File.Exists(Path.Combine(current, "BeatInsight.sln")))
            {
                return current;
            }
            current = Directory.GetParent(current)?.FullName;
        }

        throw new InvalidOperationException("Could not locate repository root containing BeatInsight.slnx.");
    }
}
