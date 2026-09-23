using BeatInsight;
using BeatInsight.Services.Settings;
using BeatInsight.ViewModels;

namespace BeatInsight.Tests.ViewModels;

public sealed class OsuApiSettingsViewModelTests
{
    [Fact]
    public void NoCredentials_ShowsNotConfigured()
    {
        var viewModel = new OsuApiSettingsViewModel();

        viewModel.ApplyResolution(new OsuApiCredentialResolution(
            OsuApiCredentials.Unavailable,
            OsuApiCredentialSource.None));

        Assert.Equal("Not configured", viewModel.StatusText);
        Assert.Empty(viewModel.ClientId);
    }

    [Theory]
    [InlineData("", "secret", "Client ID must be a positive number.")]
    [InlineData("abc", "secret", "Client ID must be a positive number.")]
    [InlineData("123", "", "Client Secret is required.")]
    public void SaveValidation_RejectsInvalidInput(
        string clientId,
        string secret,
        string expectedStatus)
    {
        var viewModel = new OsuApiSettingsViewModel
        {
            ClientId = clientId,
        };

        Assert.False(viewModel.TryValidateForSave(secret, out string error));
        Assert.Equal(expectedStatus, error);
        Assert.Equal(expectedStatus, viewModel.StatusText);
    }

    [Fact]
    public void SaveValidation_AcceptsNumericIdAndSecret()
    {
        var viewModel = new OsuApiSettingsViewModel
        {
            ClientId = " 12345 ",
        };

        Assert.True(viewModel.TryValidateForSave(
            "fake-secret",
            out string error));
        Assert.Empty(error);
        Assert.Equal("12345", viewModel.ClientId);
    }

    [Theory]
    [InlineData("Connected", "Connected")]
    [InlineData(
        "InvalidCredentials",
        "Authentication failed")]
    [InlineData(
        "NetworkUnavailable",
        "Network unavailable")]
    [InlineData(
        "UnexpectedApiError",
        "Unexpected API error")]
    public void TestConnection_MapsToSafeStatus(
        string resultName,
        string expectedStatus)
    {
        var viewModel = new OsuApiSettingsViewModel();
        viewModel.SetTesting();
        Assert.False(viewModel.CanEdit);

        viewModel.SetTestResult(Enum.Parse<OsuApiConnectionTestResult>(
            resultName));

        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.CanEdit);
        Assert.Equal(expectedStatus, viewModel.StatusText);
    }

    [Fact]
    public void RemoveResolution_ReturnsToNotConfigured()
    {
        var viewModel = new OsuApiSettingsViewModel();
        viewModel.ApplyResolution(new OsuApiCredentialResolution(
            new OsuApiCredentials("12345", "fake-secret"),
            OsuApiCredentialSource.UserStore));

        viewModel.ApplyResolution(new OsuApiCredentialResolution(
            OsuApiCredentials.Unavailable,
            OsuApiCredentialSource.None));

        Assert.Equal("Not configured", viewModel.StatusText);
        Assert.Empty(viewModel.ClientId);
    }
}
