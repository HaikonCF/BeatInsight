using System.Diagnostics;
using System.IO;
using BeatInsight.CommunityOverlays.Poc.Adapter;
using BeatInsight.CommunityOverlays.Poc.Bridge;
using Xunit;

namespace BeatInsight.CommunityOverlays.Poc.Tests;

/// <summary>/files/beatmap/* : sémantique tosu et confinement à la beatmap courante.</summary>
public sealed class BeatmapAssetProviderTests : IDisposable
{
    private const string FolderA = "900001 Artist - Title";
    private const string FolderB = "900002 Other - Map";

    private readonly string _sandbox;
    private readonly string _songs;
    private readonly string _outside;
    private readonly BeatmapAssetProvider _provider;

    private static readonly CommunityOverlayBeatmapFiles CurrentA = new(FolderA, "map [Insane].osu", "bg.jpg", "audio.mp3");

    public BeatmapAssetProviderTests()
    {
        _sandbox = Directory.CreateTempSubdirectory("bi-assets-").FullName;
        _songs = Path.Combine(_sandbox, "Songs");
        _outside = Path.Combine(_sandbox, "outside");
        Directory.CreateDirectory(Path.Combine(_songs, FolderA, "sb"));
        Directory.CreateDirectory(Path.Combine(_songs, FolderB));
        Directory.CreateDirectory(_outside);

        File.WriteAllBytes(Path.Combine(_songs, FolderA, "bg.jpg"), [0xFF, 0xD8, 0xFF]);
        File.WriteAllBytes(Path.Combine(_songs, FolderA, "audio.mp3"), new byte[100]);
        File.WriteAllText(Path.Combine(_songs, FolderA, "map [Insane].osu"), "osu file format v14");
        File.WriteAllBytes(Path.Combine(_songs, FolderA, "sb", "star.png"), [0x89, 0x50]);
        File.WriteAllText(Path.Combine(_songs, FolderA, "notes.txt"), "x");
        File.WriteAllBytes(Path.Combine(_songs, FolderB, "bg.png"), [0x89, 0x50]);
        File.WriteAllBytes(Path.Combine(_outside, "secret.png"), [0x89, 0x50]);

        _provider = new BeatmapAssetProvider(_songs);
    }

    public void Dispose()
    {
        foreach (string junction in new[] { Path.Combine(_songs, FolderA, "link"), Path.Combine(_songs, "linked map") })
            if (Directory.Exists(junction)) Directory.Delete(junction);
        Directory.Delete(_sandbox, recursive: true);
    }

    [Theory]
    [InlineData("/files/beatmap/background", "bg.jpg", "image/jpeg")]
    [InlineData("/files/beatmap/audio", "audio.mp3", "audio/mpeg")]
    [InlineData("/files/beatmap/file", "map [Insane].osu", "text/plain; charset=utf-8")]
    public void Shortcuts_serve_current_beatmap_files(string url, string expectedFile, string expectedType)
    {
        var result = _provider.Resolve(url, CurrentA);

        Assert.Equal(BeatmapAssetStatus.Found, result.Status);
        Assert.Equal(Path.Combine(_songs, FolderA, expectedFile), result.FullPath);
        Assert.Equal(expectedType, result.ContentType);
    }

    /// <summary>tosu : regex non ancrée sur le pathname ; la query (ex. ?v=checksum) est ignorée par le routeur.</summary>
    [Fact]
    public void Shortcut_matches_like_tosu_regex()
    {
        Assert.Equal(BeatmapAssetStatus.Found, _provider.Resolve("/files/beatmap/background/", CurrentA).Status);
        Assert.Equal(BeatmapAssetStatus.Found, _provider.Resolve("/files/beatmap/backgroundX", CurrentA).Status);
    }

    [Fact]
    public void Repeated_requests_are_stable()
    {
        for (int i = 0; i < 200; i++)
            Assert.Equal(BeatmapAssetStatus.Found, _provider.Resolve("/files/beatmap/background", CurrentA).Status);
    }

    [Fact]
    public void Disabled_provider_always_returns_not_found()
    {
        var disabled = new BeatmapAssetProvider((string?)null);

        Assert.False(disabled.IsEnabled);
        Assert.Equal(BeatmapAssetStatus.NotFound, disabled.Resolve("/files/beatmap/background", CurrentA).Status);
    }

    [Fact]
    public void No_current_beatmap_returns_not_found()
    {
        Assert.Equal(BeatmapAssetStatus.NotFound, _provider.Resolve("/files/beatmap/background", null).Status);
    }

    [Theory]
    [InlineData("missing.jpg", BeatmapAssetStatus.NotFound)]
    [InlineData(null, BeatmapAssetStatus.NotFound)]
    [InlineData("", BeatmapAssetStatus.NotFound)]
    [InlineData("notes.txt", BeatmapAssetStatus.Rejected)]   // extension non autorisée pour un fond
    [InlineData("audio.mp3", BeatmapAssetStatus.Rejected)]   // audio servi comme background : refusé
    [InlineData("../../outside/secret.png", BeatmapAssetStatus.Rejected)]
    [InlineData("..\\" + FolderB + "\\bg.png", BeatmapAssetStatus.Rejected)]
    [InlineData("C:\\Windows\\win.ini", BeatmapAssetStatus.Rejected)]
    [InlineData("bg.jpg:stream", BeatmapAssetStatus.Rejected)]
    [InlineData("bg.jpg.", BeatmapAssetStatus.Rejected)]
    public void Background_filename_from_snapshot_is_untrusted(string? background, BeatmapAssetStatus expected)
    {
        var current = CurrentA with { Background = background };

        Assert.Equal(expected, _provider.Resolve("/files/beatmap/background", current).Status);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../outside")]
    [InlineData("C:/Windows")]
    public void Malicious_folder_from_snapshot_is_rejected(string folder)
    {
        var current = CurrentA with { Folder = folder };

        Assert.Equal(BeatmapAssetStatus.Rejected, _provider.Resolve("/files/beatmap/background", current).Status);
    }

    [Theory]
    [InlineData("/files/beatmap/900001%20Artist%20-%20Title/bg.jpg")]
    [InlineData("/files/beatmap/900001%20Artist%20-%20Title%5Cbg.jpg")] // directPath tosu : séparateur "\"
    [InlineData("/files/beatmap/900001%20Artist%20-%20Title/sb/star.png")]
    public void Generic_path_serves_files_of_the_current_beatmap(string url)
    {
        Assert.Equal(BeatmapAssetStatus.Found, _provider.Resolve(url, CurrentA).Status);
    }

    [Theory]
    [InlineData("/files/beatmap/900002%20Other%20-%20Map/bg.png")]           // autre beatmap (tosu l'autorise)
    [InlineData("/files/beatmap/..%2Foutside%2Fsecret.png")]
    [InlineData("/files/beatmap/900001%20Artist%20-%20Title/..%2F..%2Foutside%2Fsecret.png")]
    [InlineData("/files/beatmap/900001%20Artist%20-%20Title/notes.txt")]    // extension non autorisée
    [InlineData("/files/beatmap/900001%20Artist%20-%20Title/")]             // dossier : pas de listing
    [InlineData("/files/beatmap/")]
    [InlineData("/files/beatmap/C:%5CWindows%5Cwin.ini")]
    public void Generic_path_outside_current_beatmap_is_rejected(string url)
    {
        Assert.Equal(BeatmapAssetStatus.Rejected, _provider.Resolve(url, CurrentA).Status);
    }

    [Fact]
    public void Double_encoding_is_not_decoded_twice()
    {
        // %252e%252e → "%2e%2e" littéral : fichier absent, jamais une remontée.
        var result = _provider.Resolve("/files/beatmap/900001%20Artist%20-%20Title/%252e%252e%252fsecret.png", CurrentA);

        Assert.Equal(BeatmapAssetStatus.NotFound, result.Status);
    }

    [Fact]
    public void Missing_generic_file_is_not_found()
    {
        Assert.Equal(BeatmapAssetStatus.NotFound,
            _provider.Resolve("/files/beatmap/900001%20Artist%20-%20Title/missing.png", CurrentA).Status);
    }

    [Fact]
    public void Junction_inside_beatmap_folder_is_rejected()
    {
        CreateJunction(Path.Combine(_songs, FolderA, "link"), _outside);

        Assert.Equal(BeatmapAssetStatus.Rejected,
            _provider.Resolve("/files/beatmap/900001%20Artist%20-%20Title/link/secret.png", CurrentA).Status);
        Assert.Equal(BeatmapAssetStatus.Rejected,
            _provider.Resolve("/files/beatmap/background", CurrentA with { Background = "link/secret.png" }).Status);
    }

    [Fact]
    public void Junction_as_beatmap_folder_is_rejected()
    {
        CreateJunction(Path.Combine(_songs, "linked map"), _outside);
        var current = new CommunityOverlayBeatmapFiles("linked map", null, "secret.png", null);

        Assert.Equal(BeatmapAssetStatus.Rejected, _provider.Resolve("/files/beatmap/background", current).Status);
    }

    private static void CreateJunction(string junction, string target)
    {
        using var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;
        mklink.WaitForExit();
        Assert.True(Directory.Exists(junction), "jonction non créée");
    }
}
