namespace BeatInsight.CommunityOverlays.Poc.Adapter;

/// <summary>
/// Validité d'un champ du contrat v2 (V3.14-COM-09). L'état est porté explicitement :
/// il ne se déduit jamais de la valeur (un 0 n'est pas « inconnu »).
/// </summary>
public enum FieldState
{
    /// <summary>Aucune information : source absente, lecture échouée, périmée ou incohérente.</summary>
    Unknown,

    /// <summary>La donnée n'a pas de sens sur l'écran courant (ex. combo en song select).</summary>
    NotAvailableOnScreen,

    /// <summary>Valeur lue sur l'écran courant, dans la limite d'âge.</summary>
    Known,

    /// <summary>Zéro numérique réellement lu (ex. 0 miss en gameplay).</summary>
    ObservedZero,

    /// <summary>Dernière valeur valide, conservée explicitement par une règle (ex. résultats complets).</summary>
    LastKnown,
}

/// <summary>Origine d'une valeur, pour le diagnostic et la séparation mock / live.</summary>
public enum FieldProvenance
{
    None,

    /// <summary>Source mock du POC. Ne doit jamais apparaître dans un snapshot live.</summary>
    Mock,

    /// <summary><c>GameStateSnapshot</c> (process, beatmap sélectionnée, chemins).</summary>
    GameState,

    /// <summary><c>GameRouteSnapshot</c>.</summary>
    Route,

    /// <summary><c>BeatmapAnalysisSnapshot.Metadata</c> (valeurs de base, sans mods).</summary>
    Analysis,

    /// <summary><c>LiveGameplaySnapshot</c> / <c>GameplayPositionSnapshot</c>.</summary>
    GameplayPosition,

    /// <summary><c>LiveGameplayStatsSnapshot</c>.</summary>
    LiveStats,

    /// <summary><c>LivePerformanceSnapshot</c> (aucun recalcul côté adaptateur).</summary>
    LivePerformance,

    /// <summary>Fichier .osu courant lu par BeatInsight (MD5, en-tête).</summary>
    OsuFile,

    /// <summary>Fichier audio de la beatmap courante lu par BeatInsight (durée, V3.14-COM-11).</summary>
    AudioFile,

    /// <summary>Dérivée de façon déterministe d'autres champs connus (note, AR moddé, max PP de la partie).</summary>
    Derived,

    /// <summary>
    /// V3.14-COM-SB-01 : <b>estimation</b> (pas une mesure native) déduite de l'évolution d'autres champs observés,
    /// selon la même heuristique que tosu (slider breaks : baisse de combo sans nouveau miss).
    /// </summary>
    Estimated,

    /// <summary>
    /// V3.14-COM-PP-03 : PP de la beatmap sélectionnée calculé par BeatInsight (pipe v3 « selection »), mêmes
    /// conventions que <see cref="LivePerformance"/>, pour le processus, la beatmap et les mods courants.
    /// </summary>
    SelectionPerformance,
}

/// <summary>
/// Valeur d'un champ du contrat v2 : valeur nullable, état, horodatage et provenance.
/// Invariants garantis par les fabriques (le constructeur est privé) :
/// <list type="bullet">
/// <item><see cref="FieldState.Unknown"/> et <see cref="FieldState.NotAvailableOnScreen"/> n'ont jamais de valeur.</item>
/// <item><see cref="FieldState.ObservedZero"/> porte toujours un zéro numérique ; un zéro numérique lu est toujours <see cref="FieldState.ObservedZero"/>, jamais <see cref="FieldState.Known"/>.</item>
/// <item><see cref="FieldState.Known"/>, <see cref="FieldState.ObservedZero"/> et <see cref="FieldState.LastKnown"/> ont toujours une valeur non nulle.</item>
/// </list>
/// </summary>
public readonly struct FieldValue<T> : IEquatable<FieldValue<T>>
{
    private readonly T? _value;

    private FieldValue(T? value, FieldState state, DateTimeOffset? observedAtUtc, FieldProvenance provenance)
    {
        _value = value;
        State = state;
        ObservedAtUtc = observedAtUtc;
        Provenance = provenance;
    }

    public FieldState State { get; }

    public DateTimeOffset? ObservedAtUtc { get; }

    public FieldProvenance Provenance { get; }

    /// <summary>Vrai si une valeur peut être émise (connue, zéro observé ou dernière valeur autorisée).</summary>
    public bool HasValue => State is FieldState.Known or FieldState.ObservedZero or FieldState.LastKnown;

    /// <summary>La valeur ; lève si <see cref="HasValue"/> est faux, pour qu'un inconnu ne soit jamais lu par erreur.</summary>
    public T Value => HasValue
        ? _value!
        : throw new InvalidOperationException($"Champ sans valeur ({State}).");

    /// <summary>Valeur lue. Un zéro numérique devient <see cref="FieldState.ObservedZero"/>.</summary>
    public static FieldValue<T> Observed(T value, DateTimeOffset? observedAtUtc, FieldProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (provenance == FieldProvenance.None)
            throw new ArgumentException("Une valeur lue doit avoir une provenance.", nameof(provenance));
        return new(value, IsNumericZero(value) ? FieldState.ObservedZero : FieldState.Known, observedAtUtc, provenance);
    }

    /// <summary>Dernière valeur valide, conservée par une règle explicite ; garde l'horodatage d'origine.</summary>
    public static FieldValue<T> LastKnown(T value, DateTimeOffset? observedAtUtc, FieldProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (provenance == FieldProvenance.None)
            throw new ArgumentException("Une valeur conservée doit avoir une provenance.", nameof(provenance));
        return new(value, FieldState.LastKnown, observedAtUtc, provenance);
    }

    public static FieldValue<T> Unknown { get; } = new(default, FieldState.Unknown, null, FieldProvenance.None);

    public static FieldValue<T> NotAvailableOnScreen { get; } =
        new(default, FieldState.NotAvailableOnScreen, null, FieldProvenance.None);

    /// <summary>Valeur à émettre : la valeur si elle existe, sinon la valeur neutre fournie.</summary>
    public T Or(T neutral) => HasValue ? _value! : neutral;

    /// <summary>Repasse une valeur lue en « dernière valeur autorisée ».</summary>
    public FieldValue<T> AsLastKnown() => HasValue ? LastKnown(_value!, ObservedAtUtc, Provenance) : this;

    public FieldValue<TOut> Map<TOut>(Func<T, TOut> map, FieldProvenance provenance = FieldProvenance.Derived) => State switch
    {
        FieldState.Known or FieldState.ObservedZero => FieldValue<TOut>.Observed(map(_value!), ObservedAtUtc, provenance),
        FieldState.LastKnown => FieldValue<TOut>.LastKnown(map(_value!), ObservedAtUtc, provenance),
        FieldState.NotAvailableOnScreen => FieldValue<TOut>.NotAvailableOnScreen,
        _ => FieldValue<TOut>.Unknown,
    };

    private static bool IsNumericZero(T value) => value switch
    {
        int i => i == 0,
        long l => l == 0,
        double d => d == 0,
        float f => f == 0,
        decimal m => m == 0,
        _ => false,
    };

    public bool Equals(FieldValue<T> other) =>
        State == other.State && Provenance == other.Provenance && ObservedAtUtc == other.ObservedAtUtc &&
        EqualityComparer<T?>.Default.Equals(_value, other._value);

    public override bool Equals(object? obj) => obj is FieldValue<T> other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(State, Provenance, ObservedAtUtc, _value);

    public override string ToString() => HasValue ? $"{State}({_value})" : State.ToString();
}

/// <summary>Raccourcis pour la source mock : toutes les valeurs sont marquées <see cref="FieldProvenance.Mock"/>.</summary>
public static class MockField
{
    public static FieldValue<T> Of<T>(T value) => FieldValue<T>.Observed(value, null, FieldProvenance.Mock);
}
