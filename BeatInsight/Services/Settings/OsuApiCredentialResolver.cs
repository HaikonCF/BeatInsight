namespace BeatInsight.Services.Settings;

internal enum OsuApiCredentialSource
{
    None,
    UserStore,
    Environment,
}

internal sealed class OsuApiCredentialResolution
{
    internal OsuApiCredentialResolution(
        OsuApiCredentials credentials,
        OsuApiCredentialSource source)
    {
        Credentials = credentials;
        Source = source;
    }

    internal OsuApiCredentials Credentials { get; }

    internal OsuApiCredentialSource Source { get; }
}

/// <summary>
/// Résout les credentials utilisateur en priorité, puis le fallback
/// développeur par variables d'environnement. Aucune migration automatique
/// n'est effectuée entre les deux sources.
/// </summary>
internal sealed class OsuApiCredentialResolver
{
    private readonly IOsuApiCredentialStore store;
    private readonly Func<string, string?> readEnvironmentVariable;

    internal OsuApiCredentialResolver(
        IOsuApiCredentialStore store,
        Func<string, string?>? readEnvironmentVariable = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        this.store = store;
        this.readEnvironmentVariable = readEnvironmentVariable
            ?? Environment.GetEnvironmentVariable;
    }

    internal OsuApiCredentialResolution Resolve()
    {
        OsuApiCredentials? stored = store.Load();
        if (stored?.IsConfigured == true)
        {
            return new OsuApiCredentialResolution(
                stored,
                OsuApiCredentialSource.UserStore);
        }

        OsuApiCredentials environment = OsuApiCredentials.FromEnvironment(
            readEnvironmentVariable);
        if (environment.IsConfigured)
        {
            return new OsuApiCredentialResolution(
                environment,
                OsuApiCredentialSource.Environment);
        }

        return new OsuApiCredentialResolution(
            OsuApiCredentials.Unavailable,
            OsuApiCredentialSource.None);
    }
}
