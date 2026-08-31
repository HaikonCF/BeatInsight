namespace BeatInsight.Models;

/// <summary>
/// Contient le profil des influences gameplay transversales d'une beatmap.
///
/// Cette classe ne réalise aucun calcul.
/// Elle stocke les influences détectées par GameplayAnalyzer.
///
/// Contrairement à GameplayIdentity, cette classe ne décrit pas
/// la structure primaire de la map.
///
/// Les dimensions principales sont :
/// - Aim
/// - Speed
/// - Reading
/// </summary>
public class GameplayStyleProfile
{
    /// <summary>
    /// Skill dominant de la map.
    ///
    /// Valeurs possibles :
    /// - "Aim"
    /// - "Speed"
    /// - "Reading"
    /// - "Balanced"
    ///
    /// Cette propriété ne représente PAS l'identité gameplay primaire.
    /// </summary>
    public string DominantSkill { get; set; } = "Balanced";

    /// <summary>
    /// Influence de l'Aim dans le gameplay global.
    ///
    /// Valeur généralement comprise entre 0 et 100.
    /// </summary>
    public double AimInfluence { get; set; }

    /// <summary>
    /// Influence de la Speed dans le gameplay global.
    ///
    /// Valeur généralement comprise entre 0 et 100.
    /// </summary>
    public double SpeedInfluence { get; set; }

    /// <summary>
    /// Influence de la Reading dans le gameplay global.
    ///
    /// Valeur généralement comprise entre 0 et 100.
    /// </summary>
    public double ReadInfluence { get; set; }

    /// <summary>
    /// Description textuelle du profil de skill dominant.
    ///
    /// Cette propriété est principalement destinée à l'affichage.
    /// </summary>
    public string Description { get; set; } = "";
}