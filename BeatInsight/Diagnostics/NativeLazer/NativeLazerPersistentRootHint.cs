using System.Globalization;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BeatInsight.Diagnostics.NativeLazer;

internal sealed record NativeLazerPersistentHintContext(
    string ProductVersion,
    string Architecture,
    string ModuleIdentity,
    string StructureFingerprint);

internal sealed record NativeLazerPersistentRootHint(
    int SchemaVersion,
    string ProductVersion,
    string Architecture,
    string ModuleIdentity,
    string StructureFingerprint,
    ulong RootAddress)
{
    internal const int CurrentSchemaVersion = 1;

    internal NativeLazerPersistentHintContext Context => new(
        ProductVersion,
        Architecture,
        ModuleIdentity,
        StructureFingerprint);

    internal static NativeLazerPersistentRootHint Create(
        NativeLazerPersistentHintContext context,
        ulong rootAddress) => new(
            CurrentSchemaVersion,
            context.ProductVersion,
            context.Architecture,
            context.ModuleIdentity,
            context.StructureFingerprint,
            rootAddress);
}

internal enum NativeLazerPersistentHintMissReason
{
    None,
    Absent,
    SchemaMismatch,
    VersionMismatch,
    ArchitectureMismatch,
    ModuleIdentityMismatch,
    StructureFingerprintMismatch,
    RootValidationFailed,
    IoFailure
}

internal static class NativeLazerPersistentHintIdentity
{
    internal static NativeLazerPersistentHintContext Create(
        string productVersion,
        string executablePath,
        string installDirectory,
        NativeLazerFastLayout layout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);

        return new(
            productVersion,
            ReadArchitecture(executablePath),
            HashFiles(
                executablePath,
                Path.Combine(installDirectory, "osu.Game.dll"),
                Path.Combine(installDirectory, "osu.Framework.dll")),
            HashLayout(layout));
    }

    internal static string HashLayout(NativeLazerFastLayout layout)
    {
        ulong[] values =
        [
            layout.VersionHash,
            layout.BeatmapManager,
            layout.Api,
            layout.Storage,
            layout.Beatmap,
            layout.Ruleset,
            layout.SelectedMods,
            layout.Realm,
            layout.Dependencies,
            layout.BindableValue,
            layout.BindableDisabled,
            layout.WorkingBeatmapInfo,
            layout.WorkingBeatmapSetInfo,
            layout.BeatmapInfoId,
            layout.BeatmapInfoSet,
            layout.BeatmapInfoOnlineId,
            layout.BeatmapInfoHash,
            layout.BeatmapSetOnlineId
        ];
        string serialized = string.Join(
            ":",
            values.Select(value => value.ToString("X", CultureInfo.InvariantCulture)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serialized)));
    }

    private static string ReadArchitecture(string executablePath)
    {
        using FileStream stream = File.OpenRead(executablePath);
        using var reader = new PEReader(stream);
        return reader.PEHeaders.CoffHeader.Machine.ToString();
    }

    private static string HashFiles(params string[] paths)
    {
        using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string path in paths)
        {
            aggregate.AppendData(Encoding.UTF8.GetBytes(Path.GetFileName(path)));
            using FileStream stream = File.OpenRead(path);
            byte[] buffer = new byte[128 * 1024];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                aggregate.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(aggregate.GetHashAndReset());
    }
}

internal static class NativeLazerPersistentHintResolver
{
    internal static bool TryResolve(
        NativeLazerPersistentRootHint? hint,
        NativeLazerPersistentHintContext context,
        NativeLazerGenerationIdentity generation,
        NativeLazerFastLayout layout,
        INativeLazerFastMemory memory,
        out NativeLazerRootCacheEntry? entry,
        out NativeLazerDiagnosticTrace? trace,
        out NativeLazerPersistentHintMissReason missReason,
        Func<string?, (string? Path, bool Exists)>? blobResolver = null)
    {
        entry = null;
        trace = null;
        missReason = Compare(hint, context);
        if (missReason != NativeLazerPersistentHintMissReason.None || hint is null)
            return false;

        if (!NativeLazerRootFingerprintFactory.TryCreate(
                hint.RootAddress,
                layout,
                memory,
                out NativeLazerRootValidationFingerprint? fingerprint))
        {
            missReason = NativeLazerPersistentHintMissReason.RootValidationFailed;
            return false;
        }

        var candidate = new NativeLazerRootCacheEntry(
            generation,
            hint.RootAddress,
            fingerprint!,
            layout);
        NativeLazerDiagnosticTrace candidateTrace =
            NativeLazerKnownRootTracer.Capture(
                "PersistentHintValidatedPath",
                candidate,
                memory,
                blobResolver);
        if (!candidateTrace.ValidationResult)
        {
            trace = candidateTrace;
            missReason = NativeLazerPersistentHintMissReason.RootValidationFailed;
            return false;
        }

        entry = candidate;
        trace = candidateTrace;
        return true;
    }

    internal static NativeLazerPersistentHintMissReason Compare(
        NativeLazerPersistentRootHint? hint,
        NativeLazerPersistentHintContext context)
    {
        if (hint is null)
            return NativeLazerPersistentHintMissReason.Absent;
        if (hint.SchemaVersion != NativeLazerPersistentRootHint.CurrentSchemaVersion)
            return NativeLazerPersistentHintMissReason.SchemaMismatch;
        if (!string.Equals(hint.ProductVersion, context.ProductVersion, StringComparison.Ordinal))
            return NativeLazerPersistentHintMissReason.VersionMismatch;
        if (!string.Equals(hint.Architecture, context.Architecture, StringComparison.Ordinal))
            return NativeLazerPersistentHintMissReason.ArchitectureMismatch;
        if (!string.Equals(hint.ModuleIdentity, context.ModuleIdentity, StringComparison.Ordinal))
            return NativeLazerPersistentHintMissReason.ModuleIdentityMismatch;
        return string.Equals(
            hint.StructureFingerprint,
            context.StructureFingerprint,
            StringComparison.Ordinal)
            ? NativeLazerPersistentHintMissReason.None
            : NativeLazerPersistentHintMissReason.StructureFingerprintMismatch;
    }
}

internal sealed class NativeLazerPersistentRootHintStore
{
    private readonly string path;

    internal NativeLazerPersistentRootHintStore(string? path = null)
    {
        this.path = path ?? ResolveDefaultPath();
    }

    internal NativeLazerPersistentRootHint? Load(
        out NativeLazerPersistentHintMissReason missReason)
    {
        if (!File.Exists(path))
        {
            missReason = NativeLazerPersistentHintMissReason.Absent;
            return null;
        }

        try
        {
            NativeLazerPersistentRootHint? hint = JsonSerializer.Deserialize<NativeLazerPersistentRootHint>(
                File.ReadAllText(path));
            missReason = hint is null
                ? NativeLazerPersistentHintMissReason.IoFailure
                : NativeLazerPersistentHintMissReason.None;
            return hint;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            missReason = NativeLazerPersistentHintMissReason.IoFailure;
            return null;
        }
    }

    internal bool TrySave(NativeLazerPersistentRootHint hint)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
                return false;
            Directory.CreateDirectory(directory);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(hint));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal void Clear()
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string ResolveDefaultPath()
    {
        string? overridden = Environment.GetEnvironmentVariable(
            "BEATINSIGHT_NATIVE_LAZER_HINT_PATH");
        if (!string.IsNullOrWhiteSpace(overridden))
            return Path.GetFullPath(overridden);

        string local = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(
            local,
            "BeatInsight",
            "NativeLazer",
            "root-hint-v1.json");
    }
}
