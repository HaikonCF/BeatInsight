namespace BeatInsight.Services.Settings;

/// <summary>
/// Persistance utilisateur des identifiants osu! API.
/// L'implémentation de production protège toujours le secret avant écriture.
/// </summary>
internal interface IOsuApiCredentialStore
{
    OsuApiCredentials? Load();

    void Save(string clientId, string clientSecret);

    void Clear();

    bool HasCredentials { get; }
}
