using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BeatInsight.Services.Settings;

/// <summary>
/// Stocke le Client ID en clair et le Client Secret sous forme d'un blob
/// Windows DPAPI lié à l'utilisateur courant. DPAPI protège le stockage local
/// mais ne rend pas le secret inviolable pour le compte Windows connecté.
/// </summary>
internal sealed class DpapiOsuApiCredentialStore : IOsuApiCredentialStore
{
    private static readonly byte[] OptionalEntropy =
        Encoding.UTF8.GetBytes("BeatInsight.osu-api-credentials.v1");

    private readonly string filePath;

    internal static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
        "BeatInsight",
        "osu-api-credentials.json");

    internal DpapiOsuApiCredentialStore()
        : this(DefaultFilePath)
    {
    }

    internal DpapiOsuApiCredentialStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        this.filePath = filePath;
    }

    public bool HasCredentials => Load()?.IsConfigured == true;

    public OsuApiCredentials? Load()
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            PersistedCredentials? persisted = JsonSerializer.Deserialize<
                PersistedCredentials>(File.ReadAllText(filePath));

            if (persisted is null
                || string.IsNullOrWhiteSpace(persisted.ClientId)
                || string.IsNullOrWhiteSpace(persisted.ProtectedSecret))
            {
                return null;
            }

            byte[] protectedBytes = Convert.FromBase64String(
                persisted.ProtectedSecret);
            byte[] secretBytes = ProtectedData.Unprotect(
                protectedBytes,
                OptionalEntropy,
                DataProtectionScope.CurrentUser);

            try
            {
                string secret = Encoding.UTF8.GetString(secretBytes);
                return string.IsNullOrWhiteSpace(secret)
                    ? null
                    : new OsuApiCredentials(
                        persisted.ClientId.Trim(),
                        secret);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secretBytes);
            }
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or JsonException
            or FormatException
            or CryptographicException)
        {
            TryDelete(filePath);
            TryDelete(filePath + ".tmp");
            return null;
        }
    }

    public void Save(string clientId, string clientSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientSecret);

        byte[] secretBytes = Encoding.UTF8.GetBytes(clientSecret);

        try
        {
            byte[] protectedBytes = ProtectedData.Protect(
                secretBytes,
                OptionalEntropy,
                DataProtectionScope.CurrentUser);
            var persisted = new PersistedCredentials
            {
                ClientId = clientId.Trim(),
                ProtectedSecret = Convert.ToBase64String(protectedBytes),
            };

            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporaryPath = filePath + ".tmp";

            try
            {
                File.WriteAllText(
                    temporaryPath,
                    JsonSerializer.Serialize(persisted));
                File.Move(temporaryPath, filePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretBytes);
        }
    }

    public void Clear()
    {
        TryDelete(filePath);
        TryDelete(filePath + ".tmp");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class PersistedCredentials
    {
        public string ClientId { get; set; } = string.Empty;

        public string ProtectedSecret { get; set; } = string.Empty;
    }
}
