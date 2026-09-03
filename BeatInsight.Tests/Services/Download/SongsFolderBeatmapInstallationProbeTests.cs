using BeatInsight.Services.Download;
using System.IO;

namespace BeatInsight.Tests.Services.Download;

/// <summary>
/// V2.4.3a : ce probe ne doit jamais dépendre de BeatmapAnalysisRepository
/// (voir BeatmapImportServiceTests.HasNoDependencyOnBeatmapAnalysisRepository)
/// ni invoquer BeatmapParser/GameplayAnalyzer. Chaque test utilise un
/// dossier Songs temporaire jetable.
/// </summary>
public sealed class SongsFolderBeatmapInstallationProbeTests : IDisposable
{
    private readonly string songsFolder;

    public SongsFolderBeatmapInstallationProbeTests()
    {
        songsFolder = Path.Combine(
            Path.GetTempPath(),
            "beatinsight-songs-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(songsFolder);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(songsFolder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string CreateSetFolder(int beatmapSetId, string suffix = "Artist - Title")
    {
        string folder = Path.Combine(songsFolder, $"{beatmapSetId} {suffix}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void WriteOsuFile(
        string folder,
        string fileName,
        int beatmapId,
        // HitObjects volontairement absents/invalides : un vrai
        // BeatmapParser.Load planterait dessus ("no hit objects"). Le
        // probe ne doit jamais l'invoquer.
        bool includeGarbageHitObjectsSection = true)
    {
        string content = $"""
            osu file format v14

            [Metadata]
            Title:Title
            Artist:Artist
            Creator:Mapper
            Version:Normal
            BeatmapID:{beatmapId}
            BeatmapSetID:999

            """;

        if (includeGarbageHitObjectsSection)
        {
            content += """

                [HitObjects]
                not,a,valid,hitobject,line

                """;
        }

        File.WriteAllText(Path.Combine(folder, fileName), content);
    }

    [Fact]
    public void InstalledSetFolder_WithoutAnalysisIndex_IsConfirmed()
    {
        // "Importé dans Songs mais absent de BeatmapAnalysisRepository" :
        // ce probe ne référence même pas ce repository, donc ce scénario
        // n'a besoin d'aucune configuration particulière pour réussir.
        string folder = CreateSetFolder(100_001);
        WriteOsuFile(folder, "Normal.osu", beatmapId: 500_001);

        var probe = new SongsFolderBeatmapInstallationProbe(() => songsFolder);

        bool installed = probe.IsInstalledLocally(
            beatmapId: 500_001,
            beatmapSetId: 100_001,
            cancellationToken: default);

        Assert.True(installed);
    }

    [Fact]
    public void MapAbsentFromSongs_ReturnsFalse()
    {
        var probe = new SongsFolderBeatmapInstallationProbe(() => songsFolder);

        bool installed = probe.IsInstalledLocally(
            beatmapId: 999_999,
            beatmapSetId: 999_999,
            cancellationToken: default);

        Assert.False(installed);
    }

    [Fact]
    public void MultipleDifficultiesInSet_ConfirmedViaBeatmapSetId()
    {
        string folder = CreateSetFolder(100_002);
        WriteOsuFile(folder, "Easy.osu", beatmapId: 500_010);
        WriteOsuFile(folder, "Normal.osu", beatmapId: 500_011);
        WriteOsuFile(folder, "Insane.osu", beatmapId: 500_012);

        var probe = new SongsFolderBeatmapInstallationProbe(() => songsFolder);

        // On demande confirmation pour une difficulté (BeatmapId) qui
        // n'est même pas celle vérifiée ici : le SetId doit suffire, un
        // .osz importé installant toujours toutes ses difficultés d'un
        // coup dans le même dossier.
        bool installed = probe.IsInstalledLocally(
            beatmapId: 500_099,
            beatmapSetId: 100_002,
            cancellationToken: default);

        Assert.True(installed);
    }

    [Fact]
    public void Cancellation_ThrowsBeforeCompletingEnumeration()
    {
        for (int i = 0; i < 5; i++)
        {
            CreateSetFolder(200_000 + i);
        }

        var probe = new SongsFolderBeatmapInstallationProbe(() => songsFolder);

        using CancellationTokenSource cts = new();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            probe.IsInstalledLocally(
                beatmapId: 1,
                beatmapSetId: 999_888,
                cancellationToken: cts.Token));
    }

    [Fact]
    public void BeatmapIdOnlyFallback_FindsMatchWithoutSetId()
    {
        string folder = CreateSetFolder(100_003);
        WriteOsuFile(folder, "Normal.osu", beatmapId: 500_020);

        var probe = new SongsFolderBeatmapInstallationProbe(() => songsFolder);

        bool installed = probe.IsInstalledLocally(
            beatmapId: 500_020,
            beatmapSetId: null,
            cancellationToken: default);

        Assert.True(installed);
    }

    [Fact]
    public void MissingSongsFolder_ReturnsFalseWithoutThrowing()
    {
        var probe = new SongsFolderBeatmapInstallationProbe(
            () => Path.Combine(songsFolder, "does-not-exist"));

        bool installed = probe.IsInstalledLocally(
            beatmapId: 1,
            beatmapSetId: 1,
            cancellationToken: default);

        Assert.False(installed);
    }

    [Fact]
    public void DoesNotThrowOnGarbageHitObjects_ProvingNoFullParse()
    {
        // Un vrai BeatmapParser.Load lèverait sur ce fichier (HitObjects
        // invalides / absents). Le probe ne doit jamais atteindre ce
        // code : il ne lit que [Metadata].
        string folder = CreateSetFolder(100_004);
        WriteOsuFile(
            folder,
            "Broken.osu",
            beatmapId: 500_030,
            includeGarbageHitObjectsSection: true);

        var probe = new SongsFolderBeatmapInstallationProbe(() => songsFolder);

        bool installed = probe.IsInstalledLocally(
            beatmapId: 500_030,
            beatmapSetId: null,
            cancellationToken: default);

        Assert.True(installed);
    }
}
