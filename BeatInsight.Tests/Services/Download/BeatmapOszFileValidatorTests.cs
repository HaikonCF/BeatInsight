using BeatInsight.Services.Download;
using System.IO;
using System.Text;

namespace BeatInsight.Tests.Services.Download;

public sealed class BeatmapOszFileValidatorTests : IDisposable
{
    private readonly string directory;

    public BeatmapOszFileValidatorTests()
    {
        directory = Path.Combine(
            Path.GetTempPath(),
            "beatinsight-osz-validator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string WriteFile(byte[] bytes)
    {
        string path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".osz");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void ValidZipSignature_IsAccepted()
    {
        byte[] bytes = [0x50, 0x4B, 0x03, 0x04, 0x01, 0x02, 0x03];
        string path = WriteFile(bytes);

        bool valid = BeatmapOszFileValidator.IsValidOsz(path, out string? reason);

        Assert.True(valid, reason);
        Assert.Null(reason);
    }

    [Fact]
    public void EmptyFile_IsRejected()
    {
        string path = WriteFile([]);

        bool valid = BeatmapOszFileValidator.IsValidOsz(path, out string? reason);

        Assert.False(valid);
        Assert.NotNull(reason);
    }

    [Fact]
    public void HtmlErrorPage_IsRejected()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(
            "<!DOCTYPE html><html><body>Please log in</body></html>");
        string path = WriteFile(bytes);

        bool valid = BeatmapOszFileValidator.IsValidOsz(path, out string? reason);

        Assert.False(valid);
        Assert.Contains("HTML", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnrecognizedBinary_IsRejected()
    {
        byte[] bytes = [0x00, 0x01, 0x02, 0x03, 0x04];
        string path = WriteFile(bytes);

        bool valid = BeatmapOszFileValidator.IsValidOsz(path, out string? reason);

        Assert.False(valid);
    }

    [Fact]
    public void MissingFile_IsRejected()
    {
        string path = Path.Combine(directory, "does-not-exist.osz");

        bool valid = BeatmapOszFileValidator.IsValidOsz(path, out string? reason);

        Assert.False(valid);
        Assert.NotNull(reason);
    }
}
