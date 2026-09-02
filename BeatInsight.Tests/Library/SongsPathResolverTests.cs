using BeatInsight.Services.Library;
using System.IO;

namespace BeatInsight.Tests.Library;

/// <summary>
/// Vérifie la résolution du dossier Songs et la persistance de la
/// préférence manuelle.
///
/// ISOLATION
///
/// Chaque test utilise un fichier de préférence temporaire dédié
/// (FileSongsPathPreferenceStore pointant sous %TEMP%). La préférence
/// réelle de l'utilisateur, sous
/// %LOCALAPPDATA%\BeatInsight\songs-path.txt, n'est jamais touchée.
/// </summary>
public sealed class SongsPathResolverTests : IDisposable
{
    private readonly string directory;
    private readonly string preferenceFilePath;
    private readonly string validSavedFolder;
    private readonly string validTosuFolder;

    public SongsPathResolverTests()
    {
        directory = Path.Combine(
            Path.GetTempPath(),
            "beatinsight-songspath-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        preferenceFilePath = Path.Combine(
            directory,
            "songs-path.txt");

        validSavedFolder = Path.Combine(directory, "saved-songs");
        Directory.CreateDirectory(validSavedFolder);

        validTosuFolder = Path.Combine(directory, "tosu-songs");
        Directory.CreateDirectory(validTosuFolder);
    }

    public void Dispose()
    {
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

    private SongsPathResolver CreateResolver()
    {
        return new SongsPathResolver(
            new FileSongsPathPreferenceStore(preferenceFilePath));
    }


    // ============================================================
    // PRIORITÉ : SAVED > TOSU > NULL
    // ============================================================

    [Fact]
    public void Resolve_SavedValid_BeatsTosu()
    {
        SongsPathResolver resolver = CreateResolver();
        resolver.SaveManualPath(validSavedFolder);

        string? result = resolver.Resolve(validTosuFolder);

        Assert.Equal(validSavedFolder, result);
    }

    [Fact]
    public void Resolve_SavedInvalid_FallsBackToTosu()
    {
        SongsPathResolver resolver = CreateResolver();

        string missingFolder = Path.Combine(directory, "does-not-exist");
        resolver.SaveManualPath(missingFolder);

        string? result = resolver.Resolve(validTosuFolder);

        Assert.Equal(validTosuFolder, result);
    }

    [Fact]
    public void Resolve_NoSavedPath_ReturnsValidTosuPath()
    {
        SongsPathResolver resolver = CreateResolver();

        string? result = resolver.Resolve(validTosuFolder);

        Assert.Equal(validTosuFolder, result);
    }

    [Fact]
    public void Resolve_BothInvalid_ReturnsNull()
    {
        SongsPathResolver resolver = CreateResolver();

        string missingFolder = Path.Combine(directory, "does-not-exist");
        resolver.SaveManualPath(missingFolder);

        string? result = resolver.Resolve(
            Path.Combine(directory, "also-missing"));

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_NoSavedPathAndNullTosuPath_ReturnsNull()
    {
        SongsPathResolver resolver = CreateResolver();

        Assert.Null(resolver.Resolve(null));
    }

    [Fact]
    public void Resolve_SavedInvalidAndTosuNull_ReturnsNull()
    {
        SongsPathResolver resolver = CreateResolver();

        string missingFolder = Path.Combine(directory, "does-not-exist");
        resolver.SaveManualPath(missingFolder);

        Assert.Null(resolver.Resolve(null));
    }

    [Fact]
    public void Resolve_NoSavedPathAndTosuInvalid_ReturnsNull()
    {
        SongsPathResolver resolver = CreateResolver();

        string? result = resolver.Resolve(
            Path.Combine(directory, "missing-tosu-path"));

        Assert.Null(result);
    }


    // ============================================================
    // PERSISTANCE
    // ============================================================

    [Fact]
    public void SaveManualPath_ThenNewResolverInstance_Persists()
    {
        CreateResolver().SaveManualPath(validSavedFolder);

        // Nouvelle instance, même fichier de préférence.
        SongsPathResolver reloaded = CreateResolver();

        Assert.Equal(
            validSavedFolder,
            reloaded.Resolve(validTosuFolder));
    }

    [Fact]
    public void SaveManualPath_WritesPreferenceFile()
    {
        Assert.False(File.Exists(preferenceFilePath));

        CreateResolver().SaveManualPath(validSavedFolder);

        Assert.True(File.Exists(preferenceFilePath));
    }

    [Fact]
    public void SaveManualPath_OverwritesPreviousValue()
    {
        SongsPathResolver resolver = CreateResolver();

        resolver.SaveManualPath(validSavedFolder);
        resolver.SaveManualPath(validTosuFolder);

        Assert.Equal(
            validTosuFolder,
            resolver.Resolve(null));
    }


    // ============================================================
    // CLEAR
    // ============================================================

    [Fact]
    public void ClearManualPath_RemovesPreference()
    {
        SongsPathResolver resolver = CreateResolver();
        resolver.SaveManualPath(validSavedFolder);

        resolver.ClearManualPath();

        // La préférence disparue, tosu redevient la source.
        Assert.Equal(
            validTosuFolder,
            resolver.Resolve(validTosuFolder));
    }

    [Fact]
    public void ClearManualPath_DeletesPreferenceFile()
    {
        SongsPathResolver resolver = CreateResolver();
        resolver.SaveManualPath(validSavedFolder);

        resolver.ClearManualPath();

        Assert.False(File.Exists(preferenceFilePath));
    }

    [Fact]
    public void ClearManualPath_WhenNothingSaved_DoesNotThrow()
    {
        SongsPathResolver resolver = CreateResolver();

        resolver.ClearManualPath();
    }


    // ============================================================
    // NE JAMAIS ÉCRASER AUTOMATIQUEMENT UN CHEMIN MANUEL VALIDE
    // ============================================================

    [Fact]
    public void Resolve_NeverOverwritesSavedPreferenceAutomatically()
    {
        SongsPathResolver resolver = CreateResolver();
        resolver.SaveManualPath(validSavedFolder);

        // Plusieurs résolutions successives avec un chemin tosu
        // différent ne doivent jamais modifier la préférence.
        resolver.Resolve(validTosuFolder);
        resolver.Resolve(validTosuFolder);

        SongsPathResolver reloaded = CreateResolver();

        Assert.Equal(
            validSavedFolder,
            reloaded.Resolve(validTosuFolder));
    }


    // ============================================================
    // AUCUN EMPLACEMENT RÉEL TOUCHÉ
    // ============================================================

    [Fact]
    public void DefaultFilePath_PointsInsideLocalAppDataUnderBeatInsight()
    {
        string expectedRoot = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        string actual = FileSongsPathPreferenceStore.DefaultFilePath;

        Assert.StartsWith(expectedRoot, actual);
        Assert.Contains("BeatInsight", actual);
        Assert.EndsWith("songs-path.txt", actual);
    }

    [Fact]
    public void Tests_NeverTouchRealUserPreferenceFile()
    {
        // Chaque test de cette classe passe un chemin explicite sous
        // %TEMP% au store : jamais celui par défaut, sous
        // %LOCALAPPDATA%\BeatInsight, qui décrit la machine réelle de
        // l'utilisateur.
        Assert.NotEqual(
            FileSongsPathPreferenceStore.DefaultFilePath,
            preferenceFilePath);

        Assert.StartsWith(
            Path.GetTempPath(),
            preferenceFilePath,
            StringComparison.OrdinalIgnoreCase);
    }


    // ============================================================
    // VALIDATION MINIMALE
    // ============================================================

    [Fact]
    public void Resolve_SavedPathIsAFileNotDirectory_IsRejected()
    {
        SongsPathResolver resolver = CreateResolver();

        string filePath = Path.Combine(directory, "not-a-folder.txt");
        File.WriteAllText(filePath, "content");

        resolver.SaveManualPath(filePath);

        Assert.Equal(
            validTosuFolder,
            resolver.Resolve(validTosuFolder));
    }

    [Fact]
    public void Resolve_EmptyStringTosuPath_IsTreatedAsInvalid()
    {
        SongsPathResolver resolver = CreateResolver();

        Assert.Null(resolver.Resolve(string.Empty));
    }
}
