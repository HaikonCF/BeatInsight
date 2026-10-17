namespace BeatInsight.Diagnostics.NativeLazer;

/// <summary>Validité d'un échantillon KeyOverlay Lazer (V3.14-COM-LAZER-KEY-02).</summary>
public enum LazerKeyValidity
{
    /// <summary>Chaîne complète et cohérente, osu!standard, SoloPlayer : compteurs publiables.</summary>
    Valid,

    /// <summary>Pas en partie (sélection, résultats, menus, chargement) : aucun compteur à publier.</summary>
    NotGameplay,

    /// <summary>En partie mais non prise en charge (replay, autoplay, spectate, autre ruleset).</summary>
    Unsupported,

    /// <summary>Doute quelconque : rien n'est publié.</summary>
    Unknown,
}

public enum LazerKeyFailure
{
    None,
    Disposed,
    NoProcess,
    AmbiguousProcess,
    Attaching,
    AttachFailed,
    ProcessExited,
    ProcessIdentityChanged,
    RootUnavailable,
    RootMoved,
    MethodTablesUnavailable,
    ScreenStackUnavailable,
    NotInGameplay,
    PlayerLoading,
    UnsupportedPlayer,
    UnsupportedRuleset,
    HudUnavailable,
    TriggersNotReady,
    TriggerCollectionInvalid,
    TriggerTypeInvalid,
    TriggerActionInvalid,
    TriggerActionDuplicated,
    TriggerActionMissing,
    CountOutOfRange,
    ChainChangedDuringRead,
    PressedKeysUnavailable,
}

/// <summary>
/// Disponibilité d'un compteur. <see cref="Unavailable"/> signifie « n'existe pas dans Lazer » : ce n'est jamais un zéro.
/// </summary>
public enum LazerCounterAvailability
{
    /// <summary>Compteur natif du ruleset, lu en mémoire.</summary>
    Native,

    /// <summary>Lazer ne tient pas ce compteur (M1/M2 : clavier et souris d'une action sont fusionnés).</summary>
    Unavailable,

    /// <summary>Compteur qui existe mais n'a pas pu être lu de façon fiable pour cet échantillon.</summary>
    Unknown,
}

/// <summary>
/// Compteur d'une action osu!standard (LeftButton ou RightButton). <see cref="Count"/> inclut <b>tous</b> les appuis
/// reconnus pour cette action : clavier, souris et stylet confondus. Ce n'est pas un compteur « clavier ».
/// </summary>
public sealed record LazerActionCounter(
    string Action,
    LazerCounterAvailability Availability,
    int? Count,
    bool? IsActive,
    bool? IsCounting);

/// <summary>
/// États pressés par entrée physique, lus dans <c>KeyBindingContainer.pressedInputKeys</c> et classés avec les liaisons
/// réelles du ruleset : K = touche clavier liée à l'action, M = bouton de souris lié à l'action. Null = inconnu.
/// </summary>
public sealed record LazerPressedStates(bool? K1, bool? K2, bool? M1, bool? M2, IReadOnlyList<string> RawKeys);

/// <summary>
/// Résultat d'une lecture (contrat COM-LAZER-KEY-02, option A).
/// K1.Count = action gauche native, K2.Count = action droite native, M1/M2 = <see cref="LazerCounterAvailability.Unavailable"/>.
/// </summary>
public sealed record LazerKeyOverlaySample(
    string Source,
    string? ProcessIdentity,
    string? BeatmapIdentity,
    string? PlayIdentity,
    LazerPlayTransition Transition,
    DateTimeOffset ObservedAtUtc,
    LazerKeyValidity Validity,
    LazerKeyFailure FailureReason,
    LazerActionCounter LeftAction,
    LazerActionCounter RightAction,
    LazerCounterAvailability M1Count,
    LazerCounterAvailability M2Count,
    LazerPressedStates? Pressed,
    int? RestartCount,
    string? PlayerType,
    int Reads,
    double ReadMs)
{
    public const string LazerSource = "Lazer";

    public bool IsValid => Validity == LazerKeyValidity.Valid;

    public static LazerActionCounter UnknownCounter(string action) => new(action, LazerCounterAvailability.Unknown, null, null, null);
}

/// <summary>Comment la partie courante a commencé, selon le cycle de vie observé.</summary>
public enum LazerPlayTransition
{
    None,
    FirstPlay,
    NewGameplay,
    Retry,
    ProcessChanged,
}
