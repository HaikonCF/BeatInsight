using BeatInsight.Models;
using BeatInsight.Models.Persistence;

namespace BeatInsight.Services.Ml;

internal readonly record struct HumanLabelPrefill(
    MlHumanLabel Primary,
    MlHumanLabel? Secondary);

internal static class HumanLabelPrefillPolicy
{
    internal const double MinimumConfidence = 80.0;

    internal static bool TryCreate(
        GameplayIdentity identity,
        out HumanLabelPrefill prefill)
    {
        ArgumentNullException.ThrowIfNull(identity);

        prefill = default;

        if (identity.Confidence < MinimumConfidence
            || !TryMap(identity.Primary, out MlHumanLabel primary))
        {
            return false;
        }

        MlHumanLabel? secondary =
            TryMap(identity.Secondary, out MlHumanLabel mappedSecondary)
                && mappedSecondary != primary
                ? mappedSecondary
                : null;

        prefill = new HumanLabelPrefill(primary, secondary);
        return true;
    }

    private static bool TryMap(
        string? classification,
        out MlHumanLabel label)
    {
        label = classification?.Trim() switch
        {
            "Stream" => MlHumanLabel.Stream,
            "Jump" => MlHumanLabel.Jump,
            "Tech" => MlHumanLabel.Tech,
            "Classic/Mixed" or "Classic / Mixed" => MlHumanLabel.ClassicMixed,
            _ => default,
        };

        return classification?.Trim() switch
        {
            "Stream" or "Jump" or "Tech"
                or "Classic/Mixed" or "Classic / Mixed" => true,
            _ => false,
        };
    }
}
