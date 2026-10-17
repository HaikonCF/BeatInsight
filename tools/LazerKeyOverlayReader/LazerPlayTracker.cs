namespace BeatInsight.Diagnostics.NativeLazer;

/// <summary>
/// V3.14-COM-LAZER-KEY-02 : identité de partie selon le cycle de vie réel, jamais d'après la seule adresse du Player
/// (le GC la déplace). Une nouvelle partie commence quand :
/// <list type="bullet">
/// <item>le processus change (redémarrage d'osu!lazer) ;</item>
/// <item>le gameplay reprend après un autre écran (sélection, chargement, résultats, replay) ;</item>
/// <item><c>RestartCount</c> change (retry) ;</item>
/// <item>un compteur natif recule (nouveaux triggers à zéro).</item>
/// </list>
/// Un changement d'adresse du Player sans aucun de ces signaux est un déplacement par le GC : même partie.
/// Une lecture Unknown ne décide rien et n'est jamais rattachée à une partie. La transition qui a ouvert la partie est
/// répétée sur tous ses échantillons (un message perdu ne la fait pas disparaître).
/// </summary>
internal sealed class LazerPlayTracker
{
    private string? process;
    private bool hadPreviousProcess;
    private long sequence;
    private int playsInProcess;
    private string? play;
    private bool gameplayOpen;
    private int? restart;
    private int? lastPlayRestart;
    private int left, right;
    private ulong? player;
    private LazerPlayTransition transition;

    /// <summary>Changements d'adresse du Player sans changement de partie (déplacements GC).</summary>
    public long PlayerMoves { get; private set; }

    public (string? PlayIdentity, LazerPlayTransition Transition) Observe(string? processIdentity, LazerChainRead read)
    {
        if (!string.Equals(processIdentity, process, StringComparison.Ordinal))
        {
            hadPreviousProcess |= process is not null;
            process = processIdentity;
            playsInProcess = 0;
            play = null;
            gameplayOpen = false;
            restart = lastPlayRestart = null;
            player = null;
        }

        switch (read.Validity)
        {
            case LazerKeyValidity.NotGameplay or LazerKeyValidity.Unsupported:
                gameplayOpen = false;
                return (null, LazerPlayTransition.None);
            case LazerKeyValidity.Unknown:
                return (null, LazerPlayTransition.None);
        }

        int l = read.Left.Count!.Value, r = read.Right.Count!.Value;
        bool newPlay = play is null || !gameplayOpen || read.RestartCount != restart || l < left || r < right;
        if (newPlay)
        {
            sequence++;
            playsInProcess++;
            play = $"{process}#{sequence}";
            transition =
                playsInProcess == 1 ? (hadPreviousProcess ? LazerPlayTransition.ProcessChanged : LazerPlayTransition.FirstPlay) :
                read.RestartCount > lastPlayRestart ? LazerPlayTransition.Retry :
                LazerPlayTransition.NewGameplay;
            lastPlayRestart = read.RestartCount;
        }
        else if (read.Player != player)
        {
            PlayerMoves++;
        }
        gameplayOpen = true;
        restart = read.RestartCount;
        left = l;
        right = r;
        player = read.Player;
        return (play, transition);
    }
}
