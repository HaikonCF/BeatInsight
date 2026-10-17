namespace BeatInsight.Integration.KeyOverlay;

/// <summary>Observation brute d'un échantillon, avant attribution d'une identité de partie.</summary>
internal readonly record struct KeyOverlayObservation(
    string ProcessIdentity,
    string? BeatmapIdentity,
    int? Status,
    int? PlayTimeMs,
    KeyOverlayReadFailureReason ChainFailure,
    KeyOverlayKeys? Keys);

/// <summary>
/// Attribution déterministe de l'identité de partie. Identités séparées : processus, beatmap,
/// partie (numéro de séquence local). L'adresse mémoire du ruleset n'intervient jamais.
///
/// Nouvelle partie si : nouveau processus, entrée en gameplay (status 2) depuis un autre état,
/// ou recul du temps de jeu (retry). L'identité de beatmap (COM-KEY-04, issue de GameStateService)
/// est portée par l'échantillon mais ne crée pas de partie à elle seule : sous osu!stable, changer
/// de map impose de quitter le gameplay, et l'identité publiée par GameStateService peut arriver avec
/// un rafraîchissement de retard. Elle est adoptée telle quelle et sert à qualifier la transition.
/// Une indisponibilité momentanée du KeyCounter ne vaut pas remise à zéro : la partie continue
/// si les compteurs reviennent cohérents (non décroissants). Une décroissance sans signal de
/// nouvelle partie rend l'identité ambiguë → Unknown jusqu'au prochain signal certain.
/// Seule exception : un retour de tous les compteurs à 0 alors qu'aucun incrément n'a encore été
/// observé dans la partie courante (KeyOverlay neuf juste après un retry) re-cale la référence.
/// </summary>
internal sealed class KeyOverlayPlayTracker
{
    public const int Gameplay = 2;
    public const int PlayTimeBackToleranceMs = 50;

    private string? processIdentity;
    private string? beatmapIdentity;
    private long playSequence;
    private bool inPlay;
    private bool ambiguous;
    private bool incrementObserved;
    private int? lastPlayTime;
    private int[]? baseline;

    public long PlaySequence => playSequence;

    public (KeyOverlayReadStatus Status, KeyOverlayReadFailureReason Reason, string? PlayIdentity, string? Beatmap)
        Observe(in KeyOverlayObservation o)
    {
        if (!string.Equals(processIdentity, o.ProcessIdentity, StringComparison.Ordinal))
        {
            processIdentity = o.ProcessIdentity;
            beatmapIdentity = null;
            EndPlay();
            lastPlayTime = null;
        }

        if (o.Status is null)
            return Unknown(KeyOverlayReadFailureReason.StatusUnavailable);
        if (o.Status != Gameplay)
        {
            EndPlay();
            lastPlayTime = null;
            return Unknown(KeyOverlayReadFailureReason.RouteNotGameplay);
        }
        if (o.PlayTimeMs is not int playTime)
            return Unknown(KeyOverlayReadFailureReason.PlayTimeUnavailable);

        bool timeWentBack = lastPlayTime is int last && playTime + PlayTimeBackToleranceMs < last;
        if (!inPlay || timeWentBack)
            BeginPlay();
        if (o.BeatmapIdentity is not null)
            beatmapIdentity = o.BeatmapIdentity;
        lastPlayTime = playTime;

        if (o.ChainFailure != KeyOverlayReadFailureReason.None || o.Keys is null)
            return Unknown(o.ChainFailure == KeyOverlayReadFailureReason.None
                ? KeyOverlayReadFailureReason.ReadIncomplete
                : o.ChainFailure);

        int[] counts = o.Keys.Counts;
        if (baseline is not null && Regressed(counts, baseline))
        {
            if (!incrementObserved && counts.All(c => c == 0))
            {
                baseline = counts; // KeyOverlay neuf de la même partie, pas encore d'appui compté
            }
            else
            {
                ambiguous = true;
                return Unknown(KeyOverlayReadFailureReason.CounterRegression);
            }
        }
        if (ambiguous)
            return Unknown(KeyOverlayReadFailureReason.PlayIdentityAmbiguous);

        if (baseline is not null && Increased(counts, baseline))
            incrementObserved = true;
        baseline = counts;
        return (KeyOverlayReadStatus.Valid, KeyOverlayReadFailureReason.None, PlayIdentity, beatmapIdentity);
    }

    private string PlayIdentity => $"{processIdentity}#{playSequence}";

    private void BeginPlay()
    {
        playSequence++;
        inPlay = true;
        ambiguous = false;
        incrementObserved = false;
        baseline = null;
    }

    private void EndPlay()
    {
        inPlay = false;
        ambiguous = false;
        incrementObserved = false;
        baseline = null;
    }

    private (KeyOverlayReadStatus, KeyOverlayReadFailureReason, string?, string?) Unknown(KeyOverlayReadFailureReason reason) =>
        (KeyOverlayReadStatus.Unknown, reason, inPlay ? PlayIdentity : null, beatmapIdentity);

    private static bool Regressed(int[] counts, int[] reference)
    {
        for (int i = 0; i < counts.Length; i++)
            if (counts[i] < reference[i]) return true;
        return false;
    }

    private static bool Increased(int[] counts, int[] reference)
    {
        for (int i = 0; i < counts.Length; i++)
            if (counts[i] > reference[i]) return true;
        return false;
    }
}
