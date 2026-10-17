using System.IO;
using System.Diagnostics;
using BeatInsight.CommunityOverlays.Poc.Bridge;
using Xunit;

namespace BeatInsight.CommunityOverlays.Poc.Tests;

/// <summary>Confinement : l'overlay ne doit lire que des fichiers de son dossier.</summary>
public sealed class OverlayFileResolverTests : IDisposable
{
    private readonly string _sandbox;
    private readonly string _root;
    private readonly string _outside;

    public OverlayFileResolverTests()
    {
        _sandbox = Directory.CreateTempSubdirectory("bi-resolver-").FullName;
        _root = Path.Combine(_sandbox, "My Overlay by Someone");
        _outside = Path.Combine(_sandbox, "secret");
        Directory.CreateDirectory(Path.Combine(_root, "js"));
        Directory.CreateDirectory(_outside);
        File.WriteAllText(Path.Combine(_root, "index.html"), "<html></html>");
        File.WriteAllText(Path.Combine(_root, "js", "socket.js"), "//");
        File.WriteAllText(Path.Combine(_root, "%2e%2e"), "literal name");
        File.WriteAllText(Path.Combine(_outside, "secret.txt"), "secret");
    }

    public void Dispose()
    {
        // Supprimer d'abord une éventuelle jonction sans suivre sa cible.
        string junction = Path.Combine(_root, "link");
        if (Directory.Exists(junction)) Directory.Delete(junction);
        Directory.Delete(_sandbox, recursive: true);
    }

    [Theory]
    [InlineData("/", "index.html")]
    [InlineData("/index.html", "index.html")]
    [InlineData("/js/socket.js", "socket.js")]
    public void Serves_files_inside_the_overlay(string urlPath, string expectedFile)
    {
        var result = OverlayFileResolver.Resolve(_root, urlPath);

        Assert.Equal(OverlayFileStatus.Found, result.Status);
        Assert.Equal(expectedFile, Path.GetFileName(result.FullPath));
    }

    [Theory]
    [InlineData("/../secret/secret.txt")]
    [InlineData("/%2e%2e/secret/secret.txt")]
    [InlineData("/%2E%2E%2Fsecret%2Fsecret.txt")]
    [InlineData("/..%5csecret%5csecret.txt")]
    [InlineData("/js/..\\..\\secret\\secret.txt")]
    public void Rejects_traversal(string urlPath)
    {
        Assert.Equal(OverlayFileStatus.Rejected, OverlayFileResolver.Resolve(_root, urlPath).Status);
    }

    [Fact]
    public void Double_encoding_is_decoded_once_only()
    {
        // "%252e%252e" → "%2e%2e" : un nom de fichier littéral, pas une remontée.
        var result = OverlayFileResolver.Resolve(_root, "/%252e%252e");

        Assert.Equal(OverlayFileStatus.Found, result.Status);
        Assert.Equal(Path.Combine(_root, "%2e%2e"), result.FullPath);
    }

    [Theory]
    [InlineData("/C:/Windows/win.ini")]
    [InlineData("/C:%5CWindows%5Cwin.ini")]
    [InlineData("//server/share/file.txt")]
    [InlineData("/index.html:secret")]
    [InlineData("/index.html%3A%3A%24DATA")]
    public void Rejects_absolute_paths_and_streams(string urlPath)
    {
        var result = OverlayFileResolver.Resolve(_root, urlPath);

        Assert.NotEqual(OverlayFileStatus.Found, result.Status);
        Assert.Null(result.FullPath);
    }

    [Theory]
    [InlineData("/NUL")]
    [InlineData("/con.txt")]
    [InlineData("/js/COM1.js")]
    [InlineData("/index.html%00.png")]
    public void Rejects_device_names_and_null_bytes(string urlPath)
    {
        Assert.Equal(OverlayFileStatus.Rejected, OverlayFileResolver.Resolve(_root, urlPath).Status);
    }

    [Fact]
    public void Rejects_junction_pointing_outside()
    {
        string junction = Path.Combine(_root, "link");
        var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{_outside}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;
        mklink.WaitForExit();
        Assert.True(Directory.Exists(junction), "jonction non créée");
        Assert.True(File.Exists(Path.Combine(junction, "secret.txt")), "la jonction doit être traversable par le système");

        var result = OverlayFileResolver.Resolve(_root, "/link/secret.txt");

        Assert.Equal(OverlayFileStatus.Rejected, result.Status);
    }

    [Fact]
    public void Missing_file_is_not_found()
    {
        Assert.Equal(OverlayFileStatus.NotFound, OverlayFileResolver.Resolve(_root, "/favicon.ico").Status);
    }
}
