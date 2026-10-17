namespace BeatInsight.CommunityOverlays.Poc.Adapter;

/// <summary>Décision prise à chaque réveil du minuteur compensé.</summary>
/// <param name="Publish">Vrai si une publication doit avoir lieu maintenant (au plus une par réveil).</param>
/// <param name="NextDelay">Délai jusqu'à la prochaine échéance, à réarmer.</param>
/// <param name="SkippedDeadlines">Échéances dépassées abandonnées (retard), jamais rattrapées.</param>
public readonly record struct ScheduleDecision(bool Publish, TimeSpan NextDelay, long SkippedDeadlines);

/// <summary>
/// Ordonnancement à échéance absolue (V3.14-COM-07) : t_next = t_start + n × période,
/// sur horloge monotone. Logique pure, sans minuteur ni thread, testable.
/// <list type="bullet">
/// <item>Aucune dérive cumulée : chaque échéance est calculée depuis t_start, pas depuis le tick précédent.</item>
/// <item>Réveil précoce (granularité du minuteur) : pas de publication, simple réarmement.</item>
/// <item>Retard (thread UI occupé) : une seule publication, puis saut aux échéances futures — pas de rafale.</item>
/// </list>
/// </summary>
public sealed class CompensatedSchedule
{
    /// <summary>Marge sous laquelle un réveil légèrement en avance compte comme à l'heure.</summary>
    public static readonly TimeSpan EarlyTolerance = TimeSpan.FromMilliseconds(1);

    private long _n = 1;

    public CompensatedSchedule(TimeSpan start, TimeSpan period)
    {
        if (period <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(period));
        Start = start;
        Period = period;
    }

    public TimeSpan Start { get; }

    public TimeSpan Period { get; }

    /// <summary>Prochaine échéance absolue.</summary>
    public TimeSpan NextDeadline => Start + _n * Period;

    public long Published { get; private set; }

    public long SkippedTotal { get; private set; }

    public ScheduleDecision OnWake(TimeSpan now)
    {
        TimeSpan deadline = NextDeadline;
        if (now < deadline - EarlyTolerance)
            return new ScheduleDecision(false, deadline - now, 0);

        // Une publication, puis prochaine échéance strictement future (sans rattrapage).
        long due = (long)Math.Floor((now - Start).Ticks / (double)Period.Ticks);
        long skipped = Math.Max(0, due - _n);
        _n = Math.Max(_n, due) + 1;

        // Après un retard, la prochaine échéance alignée peut tomber juste après cette
        // publication tardive : on la saute si elle est à moins d'une demi-période,
        // pour garantir un espacement minimal (aucune paire de ticks en rafale).
        if (NextDeadline - now < Period / 2)
        {
            _n++;
            skipped++;
        }
        Published++;
        SkippedTotal += skipped;
        return new ScheduleDecision(true, NextDeadline - now, skipped);
    }
}
