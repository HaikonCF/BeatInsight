using BeatInsight.Models.Persistence;
using System.Windows.Input;

namespace BeatInsight.Services;

/// <summary>
/// Traduit les raccourcis clavier du mode Fast Labeling en actions sur
/// les labels humains. Pure et sans dépendance à MainWindow afin de
/// rester testable indépendamment de WPF.
/// </summary>
internal static class HumanLabelHotkeys
{
    /// <summary>
    /// 1-4 → label primaire correspondant. Aucune autre touche n'est
    /// reconnue.
    /// </summary>
    internal static bool TryMapPrimaryKey(Key key, out MlHumanLabel label)
    {
        switch (key)
        {
            case Key.D1:
            case Key.NumPad1:
                label = MlHumanLabel.Stream;
                return true;

            case Key.D2:
            case Key.NumPad2:
                label = MlHumanLabel.Jump;
                return true;

            case Key.D3:
            case Key.NumPad3:
                label = MlHumanLabel.Tech;
                return true;

            case Key.D4:
            case Key.NumPad4:
                label = MlHumanLabel.ClassicMixed;
                return true;

            default:
                label = default;
                return false;
        }
    }

    /// <summary>
    /// Shift+1-4 → label secondaire correspondant, Shift+0 → None
    /// (<paramref name="isNone"/> = true, <paramref name="label"/>
    /// inutilisé). Aucune autre touche n'est reconnue.
    /// </summary>
    internal static bool TryMapSecondaryKey(
        Key key,
        out bool isNone,
        out MlHumanLabel label)
    {
        switch (key)
        {
            case Key.D0:
            case Key.NumPad0:
                isNone = true;
                label = default;
                return true;

            case Key.D1:
            case Key.NumPad1:
                isNone = false;
                label = MlHumanLabel.Stream;
                return true;

            case Key.D2:
            case Key.NumPad2:
                isNone = false;
                label = MlHumanLabel.Jump;
                return true;

            case Key.D3:
            case Key.NumPad3:
                isNone = false;
                label = MlHumanLabel.Tech;
                return true;

            case Key.D4:
            case Key.NumPad4:
                isNone = false;
                label = MlHumanLabel.ClassicMixed;
                return true;

            default:
                isNone = false;
                label = default;
                return false;
        }
    }
}
