using BeatInsight.Models.Library;
using BeatInsight.Services.Library;
using BeatInsight.Services.Persistence;
using Microsoft.Data.Sqlite;
using System.IO;

namespace BeatInsight.Tests.Library;

/// <summary>
/// Vérifie le scan récursif de bibliothèque : énumération,
/// utilisation du cache, résilience aux échecs, annulation et
/// progression.
///
/// ISOLATION
///
/// Chaque test travaille dans un dossier temporaire dédié contenant
/// de fausses fixtures locales, et une base SQLite temporaire dédiée.
/// La bibliothèque Songs réelle et la base utilisateur
/// (%LOCALAPPDATA%\BeatInsight\beatinsight.db) ne sont jamais
/// touchées.
/// </summary>
public sealed class BeatmapLibraryScannerTests : IDisposable
{
    private readonly string directory;
    private readonly string songsRoot;
    private readonly string databasePath;
    private readonly string failureLogPath;
    private readonly BeatmapAnalysisRepository repository;
    private readonly BeatmapLibraryScanner scanner;

    public BeatmapLibraryScannerTests()
    {
        directory = Path.Combine(
            Path.GetTempPath(),
            "beatinsight-scanner-" + Guid.NewGuid().ToString("N"));

        songsRoot = Path.Combine(directory, "Songs");
        Directory.CreateDirectory(songsRoot);

        databasePath = Path.Combine(directory, "scan.db");
        failureLogPath = Path.Combine(
            directory,
            "Logs",
            "library-scan-failures.log");
        repository = new BeatmapAnalysisRepository(databasePath);

        scanner = new BeatmapLibraryScanner(
            new BeatmapAnalysisCacheService(repository),
            new LibraryScanFailureLogger(failureLogPath));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }


    // ============================================================
    // HELPERS
    // ============================================================

    /// <summary>
    /// Contenu minimal, valide, d'un fichier .osu — suffisant pour
    /// que BeatmapParser.Load() et OsuStarRatingCalculator réussissent
    /// sans lever.
    /// </summary>
    private const string MinimalValidOsu = """
        osu file format v14

        [General]
        AudioFilename: audio.mp3

        [Metadata]
        Title:Scanner Test Map
        Artist:Test Artist
        Creator:Test Creator
        Version:Normal

        [Difficulty]
        ApproachRate:5
        OverallDifficulty:5
        CircleSize:4
        HPDrainRate:5
        SliderMultiplier:1.4
        SliderTickRate:1

        [TimingPoints]
        0,500,4,2,0,50,1,0

        [HitObjects]
        100,100,0,1,0,0:0:0:0:
        200,100,600,1,0,0:0:0:0:
        """;

    /// <summary>
    /// Fichier osu!standard syntaxiquement lisible mais sans objet
    /// jouable. Il doit échouer explicitement au parseur, et non par
    /// un accès d'index hors limites.
    /// </summary>
    private const string EmptyStandardOsu = """
        osu file format v14

        [General]
        Mode: 0

        [Difficulty]
        ApproachRate:5
        OverallDifficulty:5
        CircleSize:4
        HPDrainRate:5
        SliderMultiplier:1.4
        SliderTickRate:1

        [HitObjects]
        """;

    /// <summary>
    /// Map standard avec deux objets simultanés : le parseur doit
    /// ignorer cette transition pour le calcul de vitesse legacy, sans
    /// générer de valeur non finie dans le rating final.
    /// </summary>
    private const string ZeroIntervalOsu = """
        osu file format v14

        [General]
        Mode: 0

        [Metadata]
        Title:Zero Interval
        Artist:Test Artist
        Creator:Test Creator
        Version:Normal

        [Difficulty]
        ApproachRate:5
        OverallDifficulty:5
        CircleSize:4
        HPDrainRate:5
        SliderMultiplier:1.4
        SliderTickRate:1

        [TimingPoints]
        0,500,4,2,0,50,1,0

        [HitObjects]
        100,100,0,1,0,0:0:0:0:
        200,100,0,1,0,0:0:0:0:
        200,100,600,1,0,0:0:0:0:
        """;

    /// <summary>
    /// Un mode non-standard volontairement incomplet : s'il atteignait
    /// le cache ou le parseur, il échouerait. Son statut skipped prouve
    /// donc que le filtrage intervient avant le pipeline d'analyse.
    /// </summary>
    private const string UnsupportedModeOsu = """
        osu file format v14

        [General]
        Mode: 3
        """;

    /// <summary>
    /// Fichier .osu dont le contenu fait échouer le parsing d'une
    /// valeur numérique : reproduit une map réellement corrompue,
    /// sans dépendre d'une simplification artificielle du parser.
    /// </summary>
    private const string InvalidOsu = """
        osu file format v14

        [Difficulty]
        ApproachRate:not-a-number
        """;

    private string WriteMap(
        string relativePath,
        string content = MinimalValidOsu)
    {
        string fullPath = Path.Combine(songsRoot, relativePath);
        string? parent = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllText(fullPath, content);

        return fullPath;
    }


    // ============================================================
    // ÉNUMÉRATION
    // ============================================================

    [Fact]
    public void Scan_EmptyFolder_ReturnsZeroTotals()
    {
        LibraryScanResult result = scanner.Scan(songsRoot);

        Assert.Equal(0, result.TotalFiles);
        Assert.Equal(0, result.ProcessedFiles);
        Assert.Equal(0, result.AnalyzedFiles);
        Assert.Equal(0, result.SkippedUpToDateFiles);
        Assert.Equal(0, result.SkippedUnsupportedFiles);
        Assert.Equal(0, result.FailedFiles);
        Assert.False(result.WasCancelled);
    }

    [Fact]
    public void Scan_RecursiveSubfolders_FindsAllOsuFiles()
    {
        WriteMap("mapset-a/map-a1.osu");
        WriteMap("mapset-a/map-a2.osu");
        WriteMap("nested/deep/mapset-b/map-b1.osu");

        LibraryScanResult result = scanner.Scan(songsRoot);

        Assert.Equal(3, result.TotalFiles);
        Assert.Equal(3, result.ProcessedFiles);
    }

    [Fact]
    public void Scan_IgnoresNonOsuFiles()
    {
        WriteMap("mapset/map.osu");

        File.WriteAllText(
            Path.Combine(songsRoot, "mapset", "audio.mp3"),
            "not an audio file, just test content");
        File.WriteAllText(
            Path.Combine(songsRoot, "mapset", "background.jpg"),
            "not a real image");
        File.WriteAllText(
            Path.Combine(songsRoot, "readme.txt"),
            "readme");

        LibraryScanResult result = scanner.Scan(songsRoot);

        Assert.Equal(1, result.TotalFiles);
    }


    // ============================================================
    // MISS / HIT
    // ============================================================

    [Fact]
    public void FirstScan_AllFilesAreAnalyzed()
    {
        WriteMap("a.osu");
        WriteMap("b.osu");

        LibraryScanResult result = scanner.Scan(songsRoot);

        Assert.Equal(2, result.AnalyzedFiles);
        Assert.Equal(0, result.SkippedUpToDateFiles);
        Assert.Equal(0, result.FailedFiles);
    }

    [Fact]
    public void SecondScan_UnmodifiedFiles_AreSkipped()
    {
        WriteMap("a.osu");
        WriteMap("b.osu");

        scanner.Scan(songsRoot);
        LibraryScanResult second = scanner.Scan(songsRoot);

        Assert.Equal(0, second.AnalyzedFiles);
        Assert.Equal(2, second.SkippedUpToDateFiles);
        Assert.Equal(0, second.FailedFiles);
    }

    [Fact]
    public void OneModifiedFile_OnlyThatFileIsReanalyzed()
    {
        string a = WriteMap("a.osu");
        WriteMap("b.osu");

        scanner.Scan(songsRoot);

        // Édition réelle du fichier : taille et date changent.
        File.AppendAllText(a, Environment.NewLine);

        LibraryScanResult second = scanner.Scan(songsRoot);

        Assert.Equal(1, second.AnalyzedFiles);
        Assert.Equal(1, second.SkippedUpToDateFiles);
    }


    // ============================================================
    // ÉCHECS
    // ============================================================

    [Fact]
    public void InvalidOsuFile_IsCountedAsFailedAndScanContinues()
    {
        WriteMap("valid-before.osu");
        WriteMap("broken.osu", InvalidOsu);
        WriteMap("valid-after.osu");

        LibraryScanResult result = scanner.Scan(songsRoot);

        Assert.Equal(3, result.TotalFiles);
        Assert.Equal(3, result.ProcessedFiles);
        Assert.Equal(1, result.FailedFiles);
        Assert.Equal(2, result.AnalyzedFiles);
    }

    [Fact]
    public void FailedFile_DoesNotPersistAnEntry()
    {
        string broken = WriteMap("broken.osu", InvalidOsu);

        scanner.Scan(songsRoot);

        Assert.Null(repository.Find(broken));
    }

    [Fact]
    public void FailedFile_IsRetriedOnNextScanIfStillInvalid()
    {
        WriteMap("broken.osu", InvalidOsu);

        LibraryScanResult first = scanner.Scan(songsRoot);
        LibraryScanResult second = scanner.Scan(songsRoot);

        Assert.Equal(1, first.FailedFiles);
        Assert.Equal(1, second.FailedFiles);
    }

    [Fact]
    public void InvalidOsuFile_IsRecordedInDedicatedFailureLog()
    {
        string broken = WriteMap("broken.osu", InvalidOsu);

        LibraryScanResult result = scanner.Scan(songsRoot);

        Assert.Equal(1, result.FailedFiles);
        Assert.True(File.Exists(failureLogPath));

        string log = File.ReadAllText(failureLogPath);

        Assert.Contains(broken, log);
        Assert.Contains("Exception: System.FormatException", log);
        Assert.Contains("Message:", log);
        Assert.Matches(@"^\[\d{4}-\d{2}-\d{2}T.*Z\]", log);
        Assert.Equal(
            1,
            log.Split('\n').Count(line => line.StartsWith('[')));
    }

    [Fact]
    public void ValidScan_DoesNotCreateFailureLogEntry()
    {
        WriteMap("valid.osu");

        LibraryScanResult result = scanner.Scan(songsRoot);

        Assert.Equal(0, result.FailedFiles);
        Assert.False(File.Exists(failureLogPath));
    }

    [Fact]
    public void FailureLoggerError_DoesNotStopScan()
    {
        var scannerWithThrowingLogger = new BeatmapLibraryScanner(
            new BeatmapAnalysisCacheService(repository),
            new ThrowingFailureLogger());

        WriteMap("broken.osu", InvalidOsu);
        WriteMap("valid.osu");

        LibraryScanResult result = scannerWithThrowingLogger.Scan(songsRoot);

        Assert.Equal(2, result.ProcessedFiles);
        Assert.Equal(1, result.FailedFiles);
        Assert.Equal(1, result.AnalyzedFiles);
    }

    [Fact]
    public void UnsupportedMode_IsSkippedBeforeCacheAndParser()
    {
        string unsupported = WriteMap(
            "mania.osu",
            UnsupportedModeOsu);

        // Le scanner ne doit pas atteindre le cache pour ce fichier.
        // Le schéma est donc créé ici uniquement pour pouvoir vérifier
        // l'absence d'enregistrement ensuite.
        repository.EnsureSchema();

        LibraryScanResult result = scanner.Scan(songsRoot);

        Assert.Equal(1, result.TotalFiles);
        Assert.Equal(1, result.ProcessedFiles);
        Assert.Equal(0, result.AnalyzedFiles);
        Assert.Equal(0, result.SkippedUpToDateFiles);
        Assert.Equal(1, result.SkippedUnsupportedFiles);
        Assert.Equal(0, result.FailedFiles);
        Assert.Null(repository.Find(unsupported));
        Assert.False(File.Exists(failureLogPath));
    }

    [Fact]
    public void EmptyStandardMap_IsCountedAsFailedWithExplicitError()
    {
        WriteMap("empty.osu", EmptyStandardOsu);

        LibraryScanResult result = scanner.Scan(songsRoot);

        Assert.Equal(1, result.FailedFiles);
        Assert.Equal(0, result.SkippedUnsupportedFiles);
        Assert.True(File.Exists(failureLogPath));
        Assert.Contains(
            "Beatmap contains no hit objects.",
            File.ReadAllText(failureLogPath));
    }

    [Fact]
    public void ZeroIntervalStandardMap_ProducesFiniteRatings()
    {
        string mapPath = WriteMap("zero-interval.osu", ZeroIntervalOsu);

        LibraryScanResult result = scanner.Scan(songsRoot);

        Assert.Equal(0, result.FailedFiles);

        var record = repository.Find(mapPath);

        Assert.NotNull(record);
        Assert.False(double.IsNaN(record.OsuStarRating));
        Assert.False(double.IsInfinity(record.OsuStarRating));
        Assert.False(double.IsNaN(record.BeatInsightRating));
        Assert.False(double.IsInfinity(record.BeatInsightRating));
    }


    // ============================================================
    // ANNULATION
    // ============================================================

    [Fact]
    public void Cancellation_BeforeAnyFile_ProcessesNothing()
    {
        WriteMap("a.osu");
        WriteMap("b.osu");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        LibraryScanResult result = scanner.Scan(
            songsRoot,
            cancellationToken: cts.Token);

        Assert.True(result.WasCancelled);
        Assert.Equal(0, result.ProcessedFiles);
        Assert.Equal(2, result.TotalFiles);
    }

    [Fact]
    public void Cancellation_MidScan_PreservesAlreadyPersistedEntries()
    {
        string a = WriteMap("a.osu");
        string b = WriteMap("b.osu");
        string c = WriteMap("c.osu");

        using var cts = new CancellationTokenSource();

        // Annule après le premier fichier traité.
        int processed = 0;
        var progress = new Progress<LibraryScanProgress>(p =>
        {
            processed = p.ProcessedFiles;

            if (processed >= 1)
            {
                cts.Cancel();
            }
        });

        LibraryScanResult result = scanner.Scan(
            songsRoot,
            progress,
            cts.Token);

        Assert.True(result.WasCancelled);
        Assert.True(result.ProcessedFiles < result.TotalFiles);

        // Toute map déjà traitée avant l'annulation doit être
        // retrouvable dans le cache, la suite reprendra sans la
        // réanalyser.
        string[] allPaths = [a, b, c];
        int foundCount =
            allPaths.Count(path => repository.Find(path) is not null);

        Assert.Equal(result.ProcessedFiles, foundCount);
        Assert.True(foundCount >= 1);
    }

    [Fact]
    public void Cancellation_DoesNotThrow()
    {
        WriteMap("a.osu");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Le contrat est coopératif : aucune OperationCanceledException
        // ne doit être levée.
        LibraryScanResult result = scanner.Scan(
            songsRoot,
            cancellationToken: cts.Token);

        Assert.True(result.WasCancelled);
    }


    // ============================================================
    // PROGRESSION
    // ============================================================

    [Fact]
    public void Progress_CountersAreConsistentAcrossReports()
    {
        WriteMap("a.osu");
        WriteMap("broken.osu", InvalidOsu);
        WriteMap("unsupported.osu", UnsupportedModeOsu);
        WriteMap("b.osu");

        var reports = new List<LibraryScanProgress>();
        var progress =
            new Progress<LibraryScanProgress>(reports.Add);

        LibraryScanResult result = scanner.Scan(songsRoot, progress);

        Assert.NotEmpty(reports);

        foreach (LibraryScanProgress report in reports)
        {
            Assert.Equal(4, report.TotalFiles);

            Assert.Equal(
                report.ProcessedFiles,
                report.AnalyzedFiles
                    + report.SkippedUpToDateFiles
                    + report.SkippedUnsupportedFiles
                    + report.FailedFiles);

            Assert.InRange(report.Percent, 0.0, 100.0);
        }

        // Le dernier rapport doit refléter le résultat final et ne
        // plus porter de fichier courant.
        LibraryScanProgress last = reports[^1];

        Assert.Equal(result.ProcessedFiles, last.ProcessedFiles);
        Assert.Equal(result.AnalyzedFiles, last.AnalyzedFiles);
        Assert.Equal(
            result.SkippedUpToDateFiles,
            last.SkippedUpToDateFiles);
        Assert.Equal(
            result.SkippedUnsupportedFiles,
            last.SkippedUnsupportedFiles);
        Assert.Equal(result.FailedFiles, last.FailedFiles);
        Assert.Equal(
            result.TotalFiles,
            result.AnalyzedFiles
                + result.SkippedUpToDateFiles
                + result.SkippedUnsupportedFiles
                + result.FailedFiles);
        Assert.Null(last.CurrentFile);
        Assert.Equal(100.0, last.Percent);
    }

    [Fact]
    public void Progress_ReportsCurrentFileBeforeProcessingIt()
    {
        string a = WriteMap("a.osu");

        var reports = new List<LibraryScanProgress>();
        var progress =
            new Progress<LibraryScanProgress>(reports.Add);

        scanner.Scan(songsRoot, progress);

        LibraryScanProgress firstReport = reports[0];

        Assert.Equal(a, firstReport.CurrentFile);
        Assert.Equal(0, firstReport.ProcessedFiles);
    }

    [Fact]
    public void Progress_EmptyFolder_StillReportsFinalState()
    {
        var reports = new List<LibraryScanProgress>();
        var progress =
            new Progress<LibraryScanProgress>(reports.Add);

        scanner.Scan(songsRoot, progress);

        Assert.Single(reports);
        Assert.Equal(0, reports[0].TotalFiles);
        Assert.Equal(0.0, reports[0].Percent);
    }


    // ============================================================
    // AUCUN CACHE_SERVICE.GETORANALYZE (HITOBJECTS) UTILISÉ EN INTERNE
    //
    // Vérifie indirectement que la distinction hit/miss du scanner ne
    // dépend pas de HitObjects : un snapshot de hit (HitObjects vide)
    // doit être compté comme "skipped", jamais comme "failed" ni
    // "analyzed".
    // ============================================================

    [Fact]
    public void CacheHit_IsNeverCountedAsAnalyzedOrFailed()
    {
        WriteMap("a.osu");

        scanner.Scan(songsRoot);
        LibraryScanResult second = scanner.Scan(songsRoot);

        Assert.Equal(1, second.SkippedUpToDateFiles);
        Assert.Equal(0, second.AnalyzedFiles);
        Assert.Equal(0, second.FailedFiles);
    }


    // ============================================================
    // AUCUNE DB UTILISATEUR TOUCHÉE
    // ============================================================

    [Fact]
    public void Scan_NeverTouchesRealUserDatabase()
    {
        string realDbPath =
            BeatmapAnalysisRepository.DefaultDatabasePath;

        bool existedBefore = File.Exists(realDbPath);
        DateTime? mtimeBefore = existedBefore
            ? File.GetLastWriteTimeUtc(realDbPath)
            : null;

        WriteMap("a.osu");
        scanner.Scan(songsRoot);

        Assert.NotEqual(realDbPath, databasePath);

        bool existsAfter = File.Exists(realDbPath);

        Assert.Equal(existedBefore, existsAfter);

        if (existedBefore && existsAfter)
        {
            Assert.Equal(
                mtimeBefore,
                File.GetLastWriteTimeUtc(realDbPath));
        }
    }

    private sealed class ThrowingFailureLogger : ILibraryScanFailureLogger
    {
        public void LogFailure(string filePath, Exception exception)
        {
            throw new IOException("Test failure logger is unavailable.");
        }
    }
}
