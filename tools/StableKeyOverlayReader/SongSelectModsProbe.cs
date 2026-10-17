using System.Diagnostics;
using BeatInsight.Diagnostics.NativeStable;

namespace BeatInsight.Integration.KeyOverlay;

/// <summary>Validité d'une observation des mods de sélection (V3.14-COM-PP-02, prototype forensic).</summary>
public enum SongSelectModsValidity
{
    /// <summary>Lecture complète, bits tous connus, écran de sélection confirmé.</summary>
    Valid,
    /// <summary>Lecture complète mais l'écran n'est pas la sélection : le masque n'est pas « la sélection actuelle ».</summary>
    NotSongSelect,
    /// <summary>Lecture complète, écran confirmé, mais au moins un bit hors table connue.</summary>
    UnknownBits,
    Unknown,
}

public enum SongSelectModsFailure
{
    None,
    SignatureAbsent,
    SignatureAmbiguous,
    OperandUnreadable,
    GlobalAddressInvalid,
    ReadIncomplete,
    NegativeMask,
    RouteUnavailable,
    ProcessIdentityChanged,
    Stale,
}

/// <summary>Contrat de sortie du diagnostic (COM-PP-02 §4).</summary>
public sealed record SongSelectModsObservation(
    string? ProcessIdentity,
    int? Status,
    int? RawModsMask,
    IReadOnlyList<string> NormalizedMods,
    SongSelectModsValidity Validity,
    DateTimeOffset ObservedAtUtc,
    TimeSpan? DiscoveryDuration,
    SongSelectModsFailure FailureReason);

/// <summary>Résultat d'une découverte de la variable globale des mods de menu.</summary>
internal sealed record SongSelectModsDiscovery(
    SongSelectModsFailure Failure,
    nuint MatchAddress,
    nuint GlobalAddress,
    int MatchCount,
    TimeSpan Duration);

/// <summary>
/// Prototype isolé : lecture des mods sélectionnés en SongSelect d'osu!stable, en lecture mémoire seule.
///
/// Chaîne reprise de tosu (commit a4da331, memory/stable.ts:161-164 et :797, instances/index.ts:145) :
/// <code>
/// match      = signature 'C8 FF ?? ?? ?? ?? ?? 81 0D ?? ?? ?? ?? ?? 08 00 00' (une seule occurrence exigée)
/// operand    = match + 0x9            (opérande de l'instruction « 81 0D [global] 00 08 00 00 » : OR dword ptr [global], 0x800)
/// global     = [operand]              (adresse de la variable statique des mods)
/// mods       = int32 [global]         (masque de mods osu!stable, non chiffré)
/// </code>
/// L'instruction ciblée positionne le bit 0x800 (Autoplay) dans cette variable : c'est la variable des mods
/// courants du client, pas une copie de score. Aucune écriture, aucun hook : uniquement des lectures.
/// </summary>
internal static class SongSelectModsProbe
{
    public const string SignatureText = "C8 FF ?? ?? ?? ?? ?? 81 0D ?? ?? ?? ?? ?? 08 00 00";
    public const int OperandOffset = 0x9;
    public const int SongSelectStatus = 5;

    private static readonly BytePattern Signature = BytePattern.Parse(SignatureText);

    // Masques osu!stable (référence : enum Mods de osu!stable / tosu OsuMods).
    private static readonly (int Bit, string Acronym)[] Known =
    [
        (1 << 0, "NF"), (1 << 1, "EZ"), (1 << 2, "TD"), (1 << 3, "HD"), (1 << 4, "HR"), (1 << 5, "SD"),
        (1 << 6, "DT"), (1 << 7, "RX"), (1 << 8, "HT"), (1 << 9, "NC"), (1 << 10, "FL"), (1 << 11, "AT"),
        (1 << 12, "SO"), (1 << 13, "AP"), (1 << 14, "PF"), (1 << 20, "FI"), (1 << 21, "RD"), (1 << 22, "CN"),
        (1 << 23, "TP"), (1 << 29, "SV2"), (1 << 30, "MR"),
    ];

    public static readonly int KnownMask = Known.Aggregate(0, (mask, item) => mask | item.Bit);

    /// <summary>
    /// Une seule découverte par processus : recherche de la signature (exactement une occurrence),
    /// lecture de l'opérande, validation de l'adresse globale (alignée, dans une région lisible).
    /// </summary>
    public static SongSelectModsDiscovery Discover(
        INativeStableMemoryReader memory, IReadOnlyList<NativeStableMemoryRegion> searchRegions, RegionIndex readable)
    {
        var watch = Stopwatch.StartNew();
        IReadOnlyList<nuint> matches = NativeStableProcessPatternSearch.FindAll(memory, searchRegions, Signature, maximumMatches: 2);
        if (matches.Count == 0)
            return new(SongSelectModsFailure.SignatureAbsent, 0, 0, 0, watch.Elapsed);
        if (matches.Count > 1)
            return new(SongSelectModsFailure.SignatureAmbiguous, 0, 0, matches.Count, watch.Elapsed);

        nuint match = matches[0];
        NativeStableMemoryReadResult operand = memory.ReadBytes(match + OperandOffset, 4);
        if (!operand.Complete)
            return new(SongSelectModsFailure.OperandUnreadable, match, 0, 1, watch.Elapsed);
        nuint global = BitConverter.ToUInt32(operand.Data, 0);
        if (!readable.IsReadableAligned(global, 4))
            return new(SongSelectModsFailure.GlobalAddressInvalid, match, global, 1, watch.Elapsed);
        return new(SongSelectModsFailure.None, match, global, 1, watch.Elapsed);
    }

    /// <summary>Lit le masque et qualifie l'observation. Jamais de NM supposé : seulement une lecture valide en SongSelect.</summary>
    public static SongSelectModsObservation Read(
        INativeStableMemoryReader memory, SongSelectModsDiscovery discovery, string processIdentity,
        int? status, DateTimeOffset now)
    {
        if (discovery.Failure != SongSelectModsFailure.None)
            return Unknown(processIdentity, status, now, discovery.Duration, discovery.Failure);
        NativeStableMemoryReadResult read = memory.ReadBytes(discovery.GlobalAddress, 4);
        if (!read.Complete)
            return Unknown(processIdentity, status, now, discovery.Duration, SongSelectModsFailure.ReadIncomplete);
        int mask = BitConverter.ToInt32(read.Data, 0);
        if (mask < 0)
            return Unknown(processIdentity, status, now, discovery.Duration, SongSelectModsFailure.NegativeMask) with { RawModsMask = mask };
        if (status is null)
            return Unknown(processIdentity, status, now, discovery.Duration, SongSelectModsFailure.RouteUnavailable) with { RawModsMask = mask };

        SongSelectModsValidity validity =
            status != SongSelectStatus ? SongSelectModsValidity.NotSongSelect :
            (mask & ~KnownMask) != 0 ? SongSelectModsValidity.UnknownBits :
            SongSelectModsValidity.Valid;
        return new(processIdentity, status, mask, Normalize(mask), validity, now, discovery.Duration, SongSelectModsFailure.None);
    }

    /// <summary>
    /// Acronymes normalisés : NC implique DT (le bit DT n'est pas répété), PF implique SD.
    /// Les bits inconnus sont signalés par « ?0x… ».
    /// </summary>
    public static IReadOnlyList<string> Normalize(int mask)
    {
        var result = new List<string>();
        foreach ((int bit, string acronym) in Known)
        {
            if ((mask & bit) == 0) continue;
            if (acronym == "DT" && (mask & (1 << 9)) != 0) continue;
            if (acronym == "SD" && (mask & (1 << 14)) != 0) continue;
            result.Add(acronym);
        }
        int unknown = mask & ~KnownMask;
        if (unknown != 0) result.Add($"?0x{unknown:X}");
        return result;
    }

    private static SongSelectModsObservation Unknown(string? identity, int? status, DateTimeOffset now, TimeSpan? duration, SongSelectModsFailure failure) =>
        new(identity, status, null, [], SongSelectModsValidity.Unknown, now, duration, failure);
}
