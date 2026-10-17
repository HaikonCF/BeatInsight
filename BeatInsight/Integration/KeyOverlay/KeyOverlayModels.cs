namespace BeatInsight.Integration.KeyOverlay;

/// <summary>Raison explicite d'un échantillon <see cref="KeyOverlayReadStatus.Unknown"/>.</summary>
public enum KeyOverlayReadFailureReason
{
    None,
    Disposed,
    /// <summary>COM-KEY-04 : aucune source attachée (pas d'osu!stable, conteneur non résolu, client autre).</summary>
    SourceUnavailable,
    UnsupportedProcessArchitecture,
    ProcessExited,
    ProcessIdentityMismatch,
    StatusUnavailable,
    RouteNotGameplay,
    PlayTimeUnavailable,
    ContainerUnreadable,
    RulesetNull,
    RulesetUnreadable,
    ModeChainUnreadable,
    ModeUnsupported,
    KeyOverlayPointerNull,
    KeyOverlayUnreadable,
    KeyOverlayTypeInvalid,
    ListUnreadable,
    ListTypeInvalid,
    ItemsUnreadable,
    ItemsTypeInvalid,
    ItemsLengthInvalid,
    ListSizeInvalid,
    ElementUnreadable,
    ElementsNotDistinct,
    ElementTypeInvalid,
    ElementTypeMismatch,
    CountOutOfRange,
    PressedNotBoolean,
    ReadIncomplete,
    StructureChangedDuringRead,
    CounterRegression,
    PlayIdentityAmbiguous,
}

public enum KeyOverlayReadStatus
{
    Valid,
    Unknown,
}

/// <summary>État d'un KeyCounter tel que tenu par osu! (compteur d'appuis acceptés, état pressé).</summary>
public readonly record struct KeyCounterState(int Count, bool IsPressed);

/// <summary>
/// Échantillon publié par <see cref="NativeStableKeyOverlayReader"/>.
/// Invariant : <see cref="Keys"/> n'est non nul que si <see cref="Status"/> vaut Valid.
/// Les adresses mémoire ne servent qu'au diagnostic, jamais à l'identité de partie.
/// </summary>
public sealed record KeyOverlaySample(
    KeyOverlayReadStatus Status,
    KeyOverlayReadFailureReason Reason,
    string? ProcessIdentity,
    string? BeatmapIdentity,
    string? PlayIdentity,
    int? PlayTimeMs,
    KeyOverlayKeys? Keys,
    long ObservedTimestamp,
    int MemoryReads,
    ulong DiagnosticRulesetAddress)
{
    public bool IsValid => Status == KeyOverlayReadStatus.Valid;
}

public sealed record KeyOverlayKeys(KeyCounterState K1, KeyCounterState K2, KeyCounterState M1, KeyCounterState M2)
{
    public int[] Counts => [K1.Count, K2.Count, M1.Count, M2.Count];
}
