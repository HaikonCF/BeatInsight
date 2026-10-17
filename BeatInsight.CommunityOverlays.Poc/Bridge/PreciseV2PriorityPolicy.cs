namespace BeatInsight.CommunityOverlays.Poc.Bridge;

public static class PreciseV2PriorityPolicy
{
    public static bool IsV2Active(bool isConnected, double? messageAgeMilliseconds, double maxAgeMilliseconds) =>
        isConnected && messageAgeMilliseconds is { } age && age < maxAgeMilliseconds;
}
