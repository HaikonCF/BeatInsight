namespace BeatInsight.Models;

/// <summary>
/// Corroboration externe de l'identité structurelle par les tags osu!.
/// Cette donnée ne participe jamais au calcul de GameplayIdentity.
/// </summary>
public sealed class CommunityIdentityAgreement
{
    /// <summary>
    /// True lorsqu'au moins un tag communautaire Stream, Jump ou Tech
    /// possède un poids de vote positif.
    /// </summary>
    public bool HasStructuralEvidence { get; init; }

    /// <summary>
    /// Part de la masse de vote structurelle qui correspond aux familles
    /// explicitement présentes dans l'identité BeatInsight.
    /// Null lorsqu'aucune preuve structurelle n'est disponible.
    /// </summary>
    public double? Agreement { get; init; }

    /// <summary>
    /// Fiabilité de la preuve communautaire structurelle, normalisée dans
    /// [0, 1] à partir de RelevantVoteMass. Vaut 0 sans preuve structurelle.
    /// </summary>
    public double Reliability { get; init; }

    /// <summary>
    /// Somme brute des votes des tags structurels pertinents.
    /// </summary>
    public int RelevantVotes { get; init; }

    /// <summary>
    /// Masse de preuve structurelle : Σ log10(votes + 1).
    /// </summary>
    public double RelevantVoteMass { get; init; }

    public List<string> MatchedFamilies { get; init; } = [];

    public List<string> ConflictingFamilies { get; init; } = [];

    public List<CommunityIdentityEvidence> Evidence { get; init; } = [];
}

/// <summary>
/// Détail d'un tag structurel pris en compte dans CommunityIdentityAgreement.
/// </summary>
public sealed class CommunityIdentityEvidence
{
    public string Tag { get; init; } = "";

    public int Votes { get; init; }

    public double VoteWeight { get; init; }

    public List<string> Families { get; init; } = [];

    public List<string> MatchedFamilies { get; init; } = [];

    public List<string> ConflictingFamilies { get; init; } = [];
}
