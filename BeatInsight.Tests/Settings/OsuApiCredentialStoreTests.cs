using BeatInsight;
using BeatInsight.Services.Settings;
using System.IO;

namespace BeatInsight.Tests.Settings;

public sealed class OsuApiCredentialStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"BeatInsight-credentials-{Guid.NewGuid():N}");

    private string FilePath => Path.Combine(directory, "credentials.json");

    [Fact]
    public void SaveAndLoad_ProtectsSecretAndKeepsClientId()
    {
        const string clientId = "12345";
        const string secret = "fake-secret-for-dpapi-test";
        var store = new DpapiOsuApiCredentialStore(FilePath);

        store.Save(clientId, secret);

        string persisted = File.ReadAllText(FilePath);
        var restartedStore = new DpapiOsuApiCredentialStore(FilePath);
        OsuApiCredentials loaded = Assert.IsType<OsuApiCredentials>(
            restartedStore.Load());
        Assert.Equal(clientId, loaded.ClientId);
        Assert.Equal(secret, loaded.ClientSecret);
        Assert.DoesNotContain(secret, persisted, StringComparison.Ordinal);
        Assert.True(restartedStore.HasCredentials);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void MissingFile_HasNoCredentials()
    {
        var store = new DpapiOsuApiCredentialStore(FilePath);

        Assert.Null(store.Load());
        Assert.False(store.HasCredentials);
    }

    [Fact]
    public void Clear_RemovesSavedCredentials()
    {
        var store = new DpapiOsuApiCredentialStore(FilePath);
        store.Save("12345", "fake-secret");

        store.Clear();

        Assert.False(File.Exists(FilePath));
        Assert.Null(store.Load());
    }

    [Fact]
    public void CorruptProtectedBlob_IsRejectedAndRemoved()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            FilePath,
            "{\"ClientId\":\"12345\",\"ProtectedSecret\":\"not-dpapi\"}");
        var store = new DpapiOsuApiCredentialStore(FilePath);

        Assert.Null(store.Load());
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void Resolver_PrefersSavedCredentialsOverEnvironment()
    {
        var stored = new OsuApiCredentials("111", "stored-secret");
        var resolver = new OsuApiCredentialResolver(
            new FakeStore(stored),
            name => name == OsuApiCredentials.ClientIdEnvironmentVariable
                ? "222"
                : "environment-secret");

        OsuApiCredentialResolution resolution = resolver.Resolve();

        Assert.Equal(OsuApiCredentialSource.UserStore, resolution.Source);
        Assert.Equal("111", resolution.Credentials.ClientId);
        Assert.Equal("stored-secret", resolution.Credentials.ClientSecret);
    }

    [Fact]
    public void Resolver_UsesEnvironmentOnlyAsFallback()
    {
        var resolver = new OsuApiCredentialResolver(
            new FakeStore(null),
            name => name == OsuApiCredentials.ClientIdEnvironmentVariable
                ? "222"
                : "environment-secret");

        OsuApiCredentialResolution resolution = resolver.Resolve();

        Assert.Equal(OsuApiCredentialSource.Environment, resolution.Source);
        Assert.Equal("222", resolution.Credentials.ClientId);
        Assert.Equal(
            "environment-secret",
            resolution.Credentials.ClientSecret);
    }

    [Fact]
    public void Resolver_ReturnsNoneWhenEverySourceIsIncomplete()
    {
        var resolver = new OsuApiCredentialResolver(
            new FakeStore(null),
            _ => null);

        OsuApiCredentialResolution resolution = resolver.Resolve();

        Assert.Equal(OsuApiCredentialSource.None, resolution.Source);
        Assert.False(resolution.Credentials.IsConfigured);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FakeStore(OsuApiCredentials? credentials) :
        IOsuApiCredentialStore
    {
        public bool HasCredentials => credentials?.IsConfigured == true;

        public OsuApiCredentials? Load() => credentials;

        public void Save(string clientId, string clientSecret) =>
            throw new NotSupportedException();

        public void Clear() => throw new NotSupportedException();
    }
}
