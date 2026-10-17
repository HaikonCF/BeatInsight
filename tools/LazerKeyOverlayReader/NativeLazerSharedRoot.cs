using System.Globalization;

namespace BeatInsight.Diagnostics.NativeLazer;

public enum NativeLazerSharedRootStatus
{
    /// <summary>Racine publiée par la découverte NativeLazer et revalidée dans ce processus.</summary>
    Validated,

    /// <summary>Aucun indice publié (le helper n'a pas encore terminé sa découverte).</summary>
    HintAbsent,

    /// <summary>Indice d'une autre build, architecture ou structure.</summary>
    HintMismatch,

    /// <summary>Indice de la même build mais racine invalide dans ce processus (indice d'un processus précédent).</summary>
    RootValidationFailed,

    /// <summary>Lecture impossible (fichier, layout, processus).</summary>
    Failed,
}

/// <summary>
/// V3.14-COM-LAZER-KEY-03 : racine OsuGame partagée, en lecture seule.
/// <see cref="RootAddress"/> est une adresse virtuelle <b>du processus osu!lazer</b> (jamais du helper), utilisable
/// uniquement avec un handle sur le processus <see cref="ProcessId"/> démarré à <see cref="ProcessStartIdentity"/>.
/// </summary>
public sealed record NativeLazerSharedRoot(
    string ProcessIdentity,
    int ProcessId,
    DateTime ProcessStartIdentity,
    ulong RootAddress,
    string DiscoveryGeneration,
    DateTimeOffset ValidatedAtUtc,
    NativeLazerSharedRootStatus Status,
    string? Detail);

/// <summary>
/// Source unique de vérité : la découverte NativeLazer de production (helper <c>NativeLazerFastRefreshRunner</c>), qui
/// publie la racine validée dans <c>%LOCALAPPDATA%\BeatInsight\NativeLazer\root-hint-v1.json</c> après chaque découverte.
/// Ce lecteur ne fait que <b>lire</b> cet indice et le <b>revalider</b> dans le processus courant
/// (<c>NativeLazerPersistentHintResolver</c> : identité de build, de modules et de structure, puis empreinte de la racine).
/// Il n'écrit ni n'efface jamais l'indice et ne lance aucune découverte complète : sans indice valide, la racine est
/// simplement indisponible et sera relue plus tard.
/// </summary>
internal static class NativeLazerSharedRootSource
{
    public static NativeLazerSharedRoot Acquire(
        NativeLazerProcessCandidate process,
        NativeLazerProcessMemory memory,
        NativeLazerGenerationIdentity generation,
        string? hintPath = null)
    {
        string identity = generation.Display;
        NativeLazerSharedRoot Result(NativeLazerSharedRootStatus status, ulong root, string discovery, string? detail) =>
            new(identity, process.ProcessId, process.StartTimeUtc, root, discovery, DateTimeOffset.UtcNow, status, detail);

        if (!memory.IsCurrentGeneration(generation))
            return Result(NativeLazerSharedRootStatus.Failed, 0, "", "processus changé");
        string? directory = Path.GetDirectoryName(process.ExecutablePath);
        if (string.IsNullOrWhiteSpace(directory))
            return Result(NativeLazerSharedRootStatus.Failed, 0, "", "dossier d'installation inconnu");

        NativeLazerFastLayout layout;
        NativeLazerPersistentHintContext context;
        try
        {
            (layout, _) = NativeLazerFastLayoutDiscovery.Derive(directory, process.ProductVersion);
            context = NativeLazerPersistentHintIdentity.Create(process.ProductVersion ?? string.Empty, process.ExecutablePath, directory, layout);
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or TypeLoadException or MissingFieldException or InvalidOperationException)
        {
            return Result(NativeLazerSharedRootStatus.Failed, 0, "", $"layout : {ex.GetType().Name}");
        }

        var store = new NativeLazerPersistentRootHintStore(hintPath);
        NativeLazerPersistentRootHint? hint = store.Load(out NativeLazerPersistentHintMissReason loadMiss);
        if (hint is null)
            return Result(loadMiss == NativeLazerPersistentHintMissReason.Absent ? NativeLazerSharedRootStatus.HintAbsent : NativeLazerSharedRootStatus.Failed,
                0, "", loadMiss.ToString());
        string discovery = string.Create(CultureInfo.InvariantCulture, $"{HintWriteTicks(hintPath):X}:{hint.RootAddress:X}");

        bool accepted = NativeLazerPersistentHintResolver.TryResolve(
            hint, context, generation, layout, new NativeLazerCountingMemory(memory),
            out NativeLazerRootCacheEntry? entry, out _, out NativeLazerPersistentHintMissReason miss);
        if (!accepted || entry is null || entry.RootAddress != hint.RootAddress || entry.GenerationIdentity != generation)
        {
            NativeLazerSharedRootStatus status = miss == NativeLazerPersistentHintMissReason.RootValidationFailed
                ? NativeLazerSharedRootStatus.RootValidationFailed
                : NativeLazerSharedRootStatus.HintMismatch;
            return Result(status, 0, discovery, miss.ToString());
        }
        if (!memory.IsCurrentGeneration(generation))
            return Result(NativeLazerSharedRootStatus.Failed, 0, discovery, "processus changé pendant la validation");
        return Result(NativeLazerSharedRootStatus.Validated, entry.RootAddress, discovery, null);
    }

    private static long HintWriteTicks(string? hintPath)
    {
        try
        {
            string path = hintPath ?? Path.Combine(
                Environment.GetEnvironmentVariable("BEATINSIGHT_NATIVE_LAZER_HINT_PATH") is { Length: > 0 } overridden
                    ? Path.GetDirectoryName(Path.GetFullPath(overridden))!
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BeatInsight", "NativeLazer"),
                Environment.GetEnvironmentVariable("BEATINSIGHT_NATIVE_LAZER_HINT_PATH") is { Length: > 0 } o ? Path.GetFileName(o) : "root-hint-v1.json");
            return File.GetLastWriteTimeUtc(path).Ticks;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return 0;
        }
    }
}
