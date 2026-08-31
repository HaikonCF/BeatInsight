using System.Collections.Generic;
using System.Linq;

namespace BeatInsight.Models;

/// <summary>
/// Représente l'identité gameplay structurelle globale détectée pour une beatmap.
///
/// Cette classe ne réalise aucun calcul.
/// Elle stocke uniquement le résultat produit par GameplayAnalyzer.
///
/// L'identité primaire décrit la structure gameplay de la map.
/// Aim, Speed et Reading ne peuvent pas être des identités primaires.
/// </summary>
public sealed class GameplayIdentity
{
    /// <summary>
    /// Identité gameplay structurelle principale de la map.
    ///
    /// Valeurs possibles :
    /// - "Jump"
    /// - "Stream"
    /// - "Tech"
    /// - "Classic / Mixed"
    /// </summary>
    public string Primary { get; init; } = "";

    /// <summary>
    /// Identité gameplay structurelle secondaire éventuellement détectée.
    ///
    /// Cette propriété ne peut représenter qu'une structure gameplay,
    /// jamais une dimension de skill comme Aim, Speed ou Reading.
    /// </summary>
    public string Secondary { get; init; } = "";

    /// <summary>
    /// Motif structurel global associé au gameplay.
    ///
    /// Cette propriété décrit le profil structurel de la map
    /// et ne représente pas directement Aim, Speed ou Reading.
    /// </summary>
    public string Pattern { get; init; } = "";

    /// <summary>
    /// Niveau de confiance de l'identité structurelle primaire.
    ///
    /// Valeur généralement exprimée sur une échelle de 0 à 100.
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Caractéristiques particulières détectées dans le gameplay.
    ///
    /// Exemples :
    /// - "Jump Heavy"
    /// - "High Aim Pressure"
    /// - "Technical Patterns"
    /// - "Stream Heavy"
    /// </summary>
    public List<string> Traits { get; init; } = [];

    public double StreamScore { get; init; }
    public double JumpScore { get; init; }
    public double TechScore { get; init; }

    /// <summary>
    /// Concepts gameplay détectés dans la map.
    ///
    /// Les concepts représentent des signaux ou caractéristiques
    /// complémentaires et ne définissent pas l'identité primaire.
    /// </summary>
    public List<string> Concepts { get; init; } = [];

    /// <summary>
    /// Nom complet de l'identité gameplay.
    ///
    /// Combine le Pattern et l'identité primaire lorsque cela
    /// est pertinent.
    /// </summary>
    public string FullName =>
        string.IsNullOrWhiteSpace(Pattern)
            ? Primary
            : Pattern;

    /// <summary>
    /// Version formatée des traits destinée à l'affichage dans l'UI.
    /// </summary>
    public string TraitsDisplay =>
        Traits.Count == 0
            ? "None"
            : string.Join(" • ", Traits.Distinct());

    /// <summary>
    /// Version formatée des concepts destinée à l'affichage dans l'UI.
    /// </summary>
    public string ConceptsDisplay =>
        Concepts.Count == 0
            ? "None"
            : string.Join(" • ", Concepts.Distinct());
}