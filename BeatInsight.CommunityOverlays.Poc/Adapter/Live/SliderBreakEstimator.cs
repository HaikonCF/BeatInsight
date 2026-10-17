namespace BeatInsight.CommunityOverlays.Poc.Adapter.Live;

/// <summary>
/// État de l'estimation des slider breaks pour UNE partie (V3.14-COM-SB-01). Valeurs initiales identiques à
/// tosu (<c>Gameplay.init</c> : <c>hitSB = 0</c>, <c>comboPrev = 0</c>, <c>hitMissPrev = 0</c>).
/// </summary>
/// <param name="LastObservedAtUtc">Horodatage de la dernière observation native traitée (null : aucune).</param>
public sealed record SliderBreakState(
    int Count,
    int PreviousCombo,
    int PreviousMisses,
    DateTimeOffset? LastObservedAtUtc,
    long Observations)
{
    public static SliderBreakState Initial { get; } = new(0, 0, 0, null, 0);
}

/// <summary>
/// Slider breaks <b>estimés</b>, avec exactement la logique de tosu (commit a4da331,
/// <c>packages/tosu/src/states/gameplay.ts:244-254</c>) :
/// <code>
/// if (comboPrev &gt; maxCombo) comboPrev = 0;
/// if (combo &lt; comboPrev &amp;&amp; miss == hitMissPrev) hitSB += 1;
/// hitMissPrev = miss; comboPrev = combo;
/// </code>
/// Ce n'est pas une mesure native : osu! n'expose aucun compteur de slider breaks. L'estimation compte une
/// baisse de combo sans nouveau miss entre deux observations (plusieurs ruptures entre deux observations
/// comptent pour une ; une rupture et un miss dans la même fenêtre ne comptent pas).
/// Différence voulue avec tosu : chaque observation native n'est traitée qu'une fois (identifiée par son
/// horodatage d'observation) ; une republication du même échantillon, ou un échantillon plus ancien, est
/// ignorée. Pure et déterministe.
/// </summary>
public static class SliderBreakEstimator
{
    public static SliderBreakState Step(SliderBreakState state, int combo, int maxCombo, int misses, DateTimeOffset observedAtUtc)
    {
        if (state.LastObservedAtUtc is { } last && observedAtUtc <= last)
            return state; // même observation republiée (pipe à 250 ms, tick à 100 ms) ou observation périmée

        int previousCombo = state.PreviousCombo > maxCombo ? 0 : state.PreviousCombo;
        int count = state.Count;
        if (combo < previousCombo && misses == state.PreviousMisses)
            count++;

        return new SliderBreakState(count, combo, misses, observedAtUtc, state.Observations + 1);
    }
}
