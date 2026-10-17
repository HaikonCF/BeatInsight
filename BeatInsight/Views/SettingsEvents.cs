namespace BeatInsight.Views;

internal sealed class OsuApiCredentialsSaveRequestedEventArgs(
    string clientId,
    string clientSecret) : EventArgs
{
    internal string ClientId { get; } = clientId;
    internal string ClientSecret { get; } = clientSecret;
}
