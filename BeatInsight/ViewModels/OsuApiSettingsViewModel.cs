using BeatInsight.Services.Settings;

namespace BeatInsight.ViewModels;

public sealed class OsuApiSettingsViewModel : ViewModelBase
{
    private string clientId = string.Empty;
    private string statusText = "Not configured";
    private bool isBusy;

    public string ClientId
    {
        get => clientId;
        set => SetProperty(ref clientId, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                OnPropertyChanged(nameof(CanEdit));
            }
        }
    }

    public bool CanEdit => !IsBusy;

    internal void ApplyResolution(OsuApiCredentialResolution resolution)
    {
        ClientId = resolution.Credentials.IsConfigured
            ? resolution.Credentials.ClientId
            : string.Empty;
        StatusText = resolution.Source switch
        {
            OsuApiCredentialSource.UserStore =>
                "Configured — test connection",
            OsuApiCredentialSource.Environment =>
                "Configured from environment",
            _ => "Not configured",
        };
        IsBusy = false;
    }

    internal bool TryValidateForSave(
        string? clientSecret,
        out string validationError)
    {
        string normalizedClientId = ClientId.Trim();

        if (!long.TryParse(normalizedClientId, out long numericClientId)
            || numericClientId <= 0)
        {
            validationError = "Client ID must be a positive number.";
            StatusText = validationError;
            return false;
        }

        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            validationError = "Client Secret is required.";
            StatusText = validationError;
            return false;
        }

        ClientId = normalizedClientId;
        validationError = string.Empty;
        return true;
    }

    internal void SetSaved()
    {
        StatusText = "Saved — test connection";
        IsBusy = false;
    }

    internal void SetSaving()
    {
        StatusText = "Saving credentials...";
        IsBusy = true;
    }

    internal void SetSaveFailed()
    {
        StatusText = "Unable to save credentials";
        IsBusy = false;
    }

    internal void SetTesting()
    {
        StatusText = "Testing connection...";
        IsBusy = true;
    }

    internal void SetRemoving()
    {
        StatusText = "Removing credentials...";
        IsBusy = true;
    }

    internal void SetRemoveFailed()
    {
        StatusText = "Unable to remove credentials";
        IsBusy = false;
    }

    internal void SetTestResult(OsuApiConnectionTestResult result)
    {
        StatusText = result switch
        {
            OsuApiConnectionTestResult.Connected => "Connected",
            OsuApiConnectionTestResult.NotConfigured => "Not configured",
            OsuApiConnectionTestResult.InvalidCredentials =>
                "Authentication failed",
            OsuApiConnectionTestResult.NetworkUnavailable =>
                "Network unavailable",
            _ => "Unexpected API error",
        };
        IsBusy = false;
    }

    internal void SetExternalLinkFailed()
    {
        StatusText = "Unable to open osu! account settings";
    }

    internal void SetEnvironmentRemovalUnavailable()
    {
        StatusText = "Environment credentials must be removed outside BeatInsight";
    }
}
