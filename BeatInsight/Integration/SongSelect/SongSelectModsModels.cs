namespace BeatInsight.Integration.SongSelect;

/// <summary>Validité d'une observation des mods de la sélection des maps (V3.14-COM-PP-03).</summary>
public enum SongSelectModsValidity
{
    /// <summary>Lecture complète, bits tous connus, écran de sélection confirmé (statut 5).</summary>
    Valid,

    /// <summary>Lecture complète, mais l'écran n'est pas la sélection : ce n'est pas « la sélection actuelle ».</summary>
    NotSongSelect,

    /// <summary>Lecture complète, écran confirmé, mais au moins un bit hors table connue.</summary>
    UnknownBits,

    Unknown,
}

public enum SongSelectModsFailure
{
    None,
    Disposed,
    SourceUnavailable,
    Discovering,
    SignatureAbsent,
    SignatureAmbiguous,
    OperandUnreadable,
    GlobalAddressInvalid,
    ReadIncomplete,
    NegativeMask,
    RouteUnavailable,
    ProcessExited,
}

/// <summary>
/// Mods de la sélection des maps, rattachés au processus qui les a produits. <see cref="ModsKey"/> suit la
/// convention de <c>OsuLivePerformanceCalculator</c> (acronymes triés, joints par « + », « NM » sans mod) et
/// n'est renseignée que pour une observation <see cref="SongSelectModsValidity.Valid"/>.
/// </summary>
public sealed record SongSelectModsSnapshot(
    string? ProcessIdentity,
    int? Status,
    int? RawModsMask,
    IReadOnlyList<string> NormalizedMods,
    string? ModsKey,
    SongSelectModsValidity Validity,
    DateTimeOffset ObservedAtUtc,
    SongSelectModsFailure FailureReason)
{
    public bool IsValid => Validity == SongSelectModsValidity.Valid;

    public static SongSelectModsSnapshot Unknown(string? identity, int? status, DateTimeOffset now, SongSelectModsFailure failure) =>
        new(identity, status, null, [], null, SongSelectModsValidity.Unknown, now, failure);
}

/// <summary>Masque de mods osu!stable : table des bits connus et normalisation (reprise du prototype COM-PP-02).</summary>
public static class StableModsMask
{
    public const int SongSelectStatus = 5;

    private const int NightcoreBit = 1 << 9;
    private const int PerfectBit = 1 << 14;

    // Masques osu!stable (référence : enum Mods d'osu!stable / tosu OsuMods).
    private static readonly (int Bit, string Acronym)[] Known =
    [
        (1 << 0, "NF"), (1 << 1, "EZ"), (1 << 2, "TD"), (1 << 3, "HD"), (1 << 4, "HR"), (1 << 5, "SD"),
        (1 << 6, "DT"), (1 << 7, "RX"), (1 << 8, "HT"), (1 << 9, "NC"), (1 << 10, "FL"), (1 << 11, "AT"),
        (1 << 12, "SO"), (1 << 13, "AP"), (1 << 14, "PF"), (1 << 20, "FI"), (1 << 21, "RD"), (1 << 22, "CN"),
        (1 << 23, "TP"), (1 << 29, "SV2"), (1 << 30, "MR"),
    ];

    public static readonly int KnownMask = Known.Aggregate(0, (mask, item) => mask | item.Bit);

    /// <summary>
    /// Acronymes normalisés : NC implique DT (osu!stable stocke NC avec le bit DT ; DT n'est pas répété),
    /// PF implique SD. Les bits inconnus sont signalés par « ?0x… » et ne sont jamais valides.
    /// </summary>
    public static IReadOnlyList<string> Normalize(int mask)
    {
        var result = new List<string>();
        foreach ((int bit, string acronym) in Known)
        {
            if ((mask & bit) == 0) continue;
            if (acronym == "DT" && (mask & NightcoreBit) != 0) continue;
            if (acronym == "SD" && (mask & PerfectBit) != 0) continue;
            result.Add(acronym);
        }
        int unknown = mask & ~KnownMask;
        if (unknown != 0) result.Add($"?0x{unknown:X}");
        return result;
    }

    /// <summary>Clé de mods au format du calculateur : acronymes triés (ordinal) joints par « + », « NM » si vide.</summary>
    public static string Key(IEnumerable<string> acronyms)
    {
        string[] sorted = acronyms
            .Select(item => item.Trim().ToUpperInvariant())
            .Where(item => item.Length > 0 && item != "NM")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return sorted.Length == 0 ? "NM" : string.Join('+', sorted);
    }
}
