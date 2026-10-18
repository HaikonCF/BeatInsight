using BeatInsight.Diagnostics.NativeStable;
using BeatInsight.Integration.Game;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace BeatInsight.Tests.Integration.Game;

// BUG-004 : une installation neuve doit utiliser le profil NativeStable embarqué.
public sealed class NativeStableGameProfileLoaderTests
{
    private const string SimilarityLine = "\"MinimumNeighborhoodSimilarity\": 0.8";

    [Fact]
    public void EmbeddedResource_IsPresentInProductAssembly()
    {
        Assert.Contains(
            NativeStableGameProfileLoader.EmbeddedResourceName,
            typeof(NativeStableGameProvider).Assembly.GetManifestResourceNames());
    }

    [Fact]
    public void EmbeddedResource_IsValidProfile()
    {
        NativeStableProfileResolution resolution =
            NativeStableGameProfileLoader.Resolve(localPath: null);

        Assert.Equal(NativeStableProfileSource.Embedded, resolution.Source);
        Assert.Equal(NativeStableProfileLoadState.Loaded, resolution.EmbeddedState);
        Assert.NotNull(resolution.Profile);
        Assert.Equal(1, resolution.Profile.SchemaVersion);
        Assert.Equal(0.8, resolution.Profile.MinimumNeighborhoodSimilarity);
    }

    [Fact]
    public void EmbeddedResource_MatchesVersionedSourceFile()
    {
        string source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "BeatInsight", "Integration", "Game", "Profiles",
            NativeStableGameProfileLoader.ProfileFileName));

        Assert.Equal(Normalize(source), Normalize(EmbeddedText()));
    }

    [Fact]
    public void EmbeddedResource_ContainsNoAbsolutePathOrModuleName()
    {
        string json = EmbeddedText();

        Assert.DoesNotMatch(new Regex(@"[A-Za-z]:[\\/]"), json);
        Assert.DoesNotContain("\\\\", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Users", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".exe", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".dll", json, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(new Regex("\"ModuleName\": null"), json);
        Assert.DoesNotMatch(new Regex("\"ModuleName\": \""), json);
    }

    [Fact]
    public void FreshInstallWithoutNativeStableFolder_UsesEmbeddedWithoutCreatingIt()
    {
        string root = Path.Combine(Path.GetTempPath(), $"bi-bug004-{Guid.NewGuid():N}");
        string nativeStable = Path.Combine(root, "BeatInsight", "NativeStable");
        string localPath = Path.Combine(nativeStable, NativeStableGameProfileLoader.ProfileFileName);
        try
        {
            NativeStableProfileResolution resolution =
                NativeStableGameProfileLoader.Resolve(localPath);

            Assert.Equal(NativeStableProfileSource.Embedded, resolution.Source);
            Assert.Equal(NativeStableProfileLoadState.Missing, resolution.LocalState);
            Assert.NotNull(resolution.Profile);
            Assert.False(Directory.Exists(nativeStable));
            Assert.False(Directory.Exists(root));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ValidLocalProfile_TakesPriorityOverEmbedded()
    {
        using var local = new TempProfile(EmbeddedText().Replace(
            SimilarityLine, "\"MinimumNeighborhoodSimilarity\": 0.9", StringComparison.Ordinal));
        int embeddedOpens = 0;

        NativeStableProfileResolution resolution = NativeStableGameProfileLoader.Resolve(
            local.Path,
            () =>
            {
                embeddedOpens++;
                return NativeStableGameProfileLoader.OpenEmbeddedProfile();
            });

        Assert.Equal(NativeStableProfileSource.Local, resolution.Source);
        Assert.Equal(NativeStableProfileLoadState.Loaded, resolution.LocalState);
        Assert.Equal(NativeStableProfileLoadState.NotChecked, resolution.EmbeddedState);
        Assert.Equal(0.9, resolution.Profile!.MinimumNeighborhoodSimilarity);
        Assert.Equal(0, embeddedOpens);
    }

    [Fact]
    public void MissingLocalProfile_FallsBackToEmbedded()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"bi-missing-{Guid.NewGuid():N}.json");

        NativeStableProfileResolution resolution = NativeStableGameProfileLoader.Resolve(missing);

        Assert.Equal(NativeStableProfileSource.Embedded, resolution.Source);
        Assert.Equal(NativeStableProfileLoadState.Missing, resolution.LocalState);
        Assert.Equal(NativeStableProfileLoadState.Loaded, resolution.EmbeddedState);
        Assert.False(File.Exists(missing));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("{\"SchemaVersion\": 2}")]
    public void InvalidLocalProfile_FallsBackToEmbeddedAndIsReported(string json)
    {
        using var local = new TempProfile(json);

        NativeStableProfileResolution resolution = NativeStableGameProfileLoader.Resolve(local.Path);

        Assert.Equal(NativeStableProfileSource.Embedded, resolution.Source);
        Assert.Equal(NativeStableProfileLoadState.Invalid, resolution.LocalState);
        Assert.Equal(NativeStableProfileLoadState.Loaded, resolution.EmbeddedState);
        Assert.NotNull(resolution.Profile);
        Assert.Equal(json, File.ReadAllText(local.Path));
    }

    [Fact]
    public void MissingEmbeddedResource_FailsClosed()
    {
        NativeStableProfileResolution resolution =
            NativeStableGameProfileLoader.Resolve(localPath: null, () => null);

        Assert.Null(resolution.Profile);
        Assert.Equal(NativeStableProfileSource.None, resolution.Source);
        Assert.Equal(NativeStableProfileLoadState.Missing, resolution.EmbeddedState);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{\"SchemaVersion\": 1}")]
    public void InvalidEmbeddedResource_FailsClosed(string json)
    {
        NativeStableProfileResolution resolution = NativeStableGameProfileLoader.Resolve(
            localPath: null,
            () => new MemoryStream(Encoding.UTF8.GetBytes(json)));

        Assert.Null(resolution.Profile);
        Assert.Equal(NativeStableProfileSource.None, resolution.Source);
        Assert.Equal(NativeStableProfileLoadState.Invalid, resolution.EmbeddedState);
    }

    [Fact]
    public void ThrowingEmbeddedResource_FailsClosed()
    {
        NativeStableProfileResolution resolution = NativeStableGameProfileLoader.Resolve(
            localPath: null,
            () => throw new IOException("unavailable"));

        Assert.Null(resolution.Profile);
        Assert.Equal(NativeStableProfileSource.None, resolution.Source);
        Assert.Equal(NativeStableProfileLoadState.Invalid, resolution.EmbeddedState);
    }

    [Fact]
    public void ProviderWithoutAnyProfile_StaysUnavailableWithoutOpeningProcess()
    {
        var runtime = new CountingRuntime();
        var provider = new NativeStableGameProvider(runtime, new ThrowingLocator(), profile: null);

        GameStateSnapshot result = provider.Refresh();

        Assert.False(result.IsAvailable);
        Assert.Equal(GameStateAvailability.ProviderUnavailable, result.Availability);
        Assert.Equal("Native stable profile is unavailable.", result.DiagnosticReason);
        Assert.Null(result.ProcessId);
        Assert.Equal(0, runtime.OpenCount);
    }

    [Fact]
    public void ProviderWithEmbeddedProfile_ProceedsToProcessDiscovery()
    {
        var runtime = new CountingRuntime();
        NativeStableAutonomousQProfile? profile =
            NativeStableGameProfileLoader.Resolve(localPath: null).Profile;
        var provider = new NativeStableGameProvider(runtime, new ThrowingLocator(), profile);

        GameStateSnapshot result = provider.Refresh();

        Assert.Equal(GameStateAvailability.NoProcess, result.Availability);
        Assert.Equal(1, runtime.OpenCount);
    }

    [Fact]
    public void UnavailableNativeStable_DoesNotBlockLazerSelection()
    {
        var native = new NativeStableGameProvider(
            new CountingRuntime(), new ThrowingLocator(), profile: null);
        var lazer = new AvailableProvider(LazerGameProvider.ProviderName);
        var service = new GameStateService([native, lazer]);

        GameStateSnapshot result = service.Refresh();

        Assert.True(result.IsAvailable);
        Assert.Equal(LazerGameProvider.ProviderName, result.ProviderName);
        Assert.Collection(
            result.ProviderAttempts,
            attempt => Assert.Equal(NativeStableGameProvider.ProviderName, attempt.ProviderName),
            attempt => Assert.Equal(LazerGameProvider.ProviderName, attempt.ProviderName));
    }

    [Fact]
    public void LogGate_WritesOnceForSameResolution()
    {
        var gate = new NativeStableProfileLogGate();
        var info = new List<string>();
        var warnings = new List<string>();
        NativeStableProfileResolution resolution =
            NativeStableGameProfileLoader.Resolve(localPath: null);

        Assert.True(gate.TryLog(resolution, info.Add, warnings.Add));
        Assert.False(gate.TryLog(resolution, info.Add, warnings.Add));
        Assert.False(gate.TryLog(resolution, info.Add, warnings.Add));

        string line = Assert.Single(info);
        Assert.Empty(warnings);
        Assert.Equal(
            "NATIVESTABLE | profile source=Embedded local=Missing embedded=Loaded " +
            "file=native-stable-autonomous-q-profile.json",
            line);
    }

    [Fact]
    public void LogGate_WarnsOnFailClosedAndInvalidLocal_WithoutFullPath()
    {
        var gate = new NativeStableProfileLogGate();
        var info = new List<string>();
        var warnings = new List<string>();
        using var local = new TempProfile("{ not json");

        gate.TryLog(
            NativeStableGameProfileLoader.Resolve(local.Path),
            info.Add,
            warnings.Add);
        gate.TryLog(
            NativeStableGameProfileLoader.Resolve(local.Path, () => null),
            info.Add,
            warnings.Add);

        Assert.Empty(info);
        Assert.Equal(2, warnings.Count);
        Assert.Contains("source=Embedded local=Invalid", warnings[0], StringComparison.Ordinal);
        Assert.Contains("source=None local=Invalid embedded=Missing", warnings[1], StringComparison.Ordinal);
        Assert.All(warnings, line =>
        {
            Assert.DoesNotContain(Path.GetTempPath(), line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(local.Path, line, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void LoaderSource_NeverWritesProfileToUserData()
    {
        string source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "BeatInsight", "Integration", "Game",
            "NativeStableGameProvider.cs"));
        int start = source.IndexOf(
            "internal static class NativeStableGameProfileLoader",
            StringComparison.Ordinal);
        Assert.True(start >= 0);
        string loader = source[start..];

        foreach (string api in new[]
        {
            "File.Write", "File.Create", "File.Copy", "File.Move", "File.AppendAll",
            "Directory.CreateDirectory", "FileMode.Create", "FileAccess.Write",
            "StreamWriter", "SaveProfile"
        })
        {
            Assert.DoesNotContain(api, loader, StringComparison.Ordinal);
        }
    }

    private static string EmbeddedText()
    {
        using Stream stream = NativeStableGameProfileLoader.OpenEmbeddedProfile() ??
            throw new InvalidOperationException("Embedded profile is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string Normalize(string text) =>
        text.TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", ".."));

    private sealed class TempProfile : IDisposable
    {
        internal TempProfile(string json)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"bi-profile-{Guid.NewGuid():N}.json");
            File.WriteAllText(Path, json);
        }

        internal string Path { get; }

        public void Dispose() => File.Delete(Path);
    }

    private sealed class CountingRuntime : INativeStableGameRuntime
    {
        internal int OpenCount { get; private set; }

        public NativeStableGameProcessContext? TryOpen()
        {
            OpenCount++;
            return null;
        }

        public string? TryResolveBeatmapPath(
            NativeStableGameProcessContext context,
            string folder,
            string filename) => null;
    }

    private sealed class ThrowingLocator : INativeStableGameLocator
    {
        public NativeStableGameLocatorOutcome Locate(
            NativeStableGameProcessContext context,
            NativeStableAutonomousQProfile profile) =>
            throw new InvalidOperationException("Locator must not run in these tests.");
    }

    private sealed class AvailableProvider(string name) : IGameStateProvider
    {
        public string Name => name;

        public GameStateSnapshot Current { get; private set; } = Snapshot(name);

        public GameStateSnapshot Refresh() => Current = Snapshot(name);

        private static GameStateSnapshot Snapshot(string provider) => new(
            provider,
            "osu!lazer",
            7,
            "7:2026-10-18T00:00:00.0000000Z",
            @"C:\fake\map.osu",
            "1",
            "2",
            DateTimeOffset.UtcNow,
            GameStateAvailability.Available,
            null);
    }
}
