using CFNet.Configuration;
using CFNet.Exceptions;
using CFNet.Gallery.Infrastructure;

namespace CFNet.Gallery.ViewModels;

/// <summary>
/// Credentials and transport tuning, plus the persistence around them.
/// </summary>
/// <remarks>
/// The gallery starts from <see cref="CredentialStore"/>, which prefers the saved file and falls back
/// to the <c>CLOUDFLARE_*</c> environment variables - the same ones the GitHub Actions workflow sets,
/// so the sample and CI are configured identically.
/// </remarks>
public sealed class SettingsViewModel : PageViewModelBase
{
    private readonly GalleryViewModel _gallery;

    private string _apiToken = string.Empty;
    private string _globalApiKey = string.Empty;
    private string _apiEmail = string.Empty;
    private string _accountId = string.Empty;
    private string _zoneId = string.Empty;
    private string _baseAddress = string.Empty;
    private string _timeoutSeconds = "100";
    private string _maxRetries = "2";
    private bool _throwOnApiError = true;
    private bool _revealSecrets;
    private bool _rememberOnThisMachine;
    private int _selectedAuthModeIndex;
    private string _storageHint = string.Empty;

    /// <summary>Creates the settings view model and loads whatever is already stored.</summary>
    /// <param name="gallery">Owning shell, notified whenever a setting changes.</param>
    public SettingsViewModel(GalleryViewModel gallery)
    {
        _gallery = gallery ?? throw new ArgumentNullException(nameof(gallery));
        StorageHint = CredentialStore.FilePath;

        ApplyCommand = new RelayCommand(() => _gallery.ApplySettingsAsync());
        CheckConnectionCommand = new RelayCommand(() => _gallery.RunConnectionCheckAsync());
        SaveCommand = new RelayCommand(() =>
        {
            _gallery.SaveSettingsNow();
            return Task.CompletedTask;
        });
        ClearCommand = new RelayCommand(() =>
        {
            _gallery.ClearSettingsNow();
            return Task.CompletedTask;
        });
        ReloadCommand = new RelayCommand(() =>
        {
            Reload();
            return Task.CompletedTask;
        });

        Reload();
    }

    /// <summary>Applies the fields and re-checks connectivity.</summary>
    public RelayCommand ApplyCommand { get; }

    /// <summary>Runs the connectivity check.</summary>
    public RelayCommand CheckConnectionCommand { get; }

    /// <summary>Writes the fields to disk.</summary>
    public RelayCommand SaveCommand { get; }

    /// <summary>Removes the stored file and clears the fields.</summary>
    public RelayCommand ClearCommand { get; }

    /// <summary>Re-reads the stored file, or the environment.</summary>
    public RelayCommand ReloadCommand { get; }

    /// <inheritdoc />
    public override string Title => "Credentials";

    /// <inheritdoc />
    public override string Description =>
        "API token (or the legacy global key + email pair), default account and zone, and transport tuning.";

    /// <summary>Names shown in the authentication scheme picker.</summary>
    public IReadOnlyList<string> AuthModes { get; } = new[]
    {
        "API token  (Authorization: Bearer)",
        "Global API key + email  (X-Auth-Key / X-Auth-Email)",
    };

    /// <summary>API token.</summary>
    public string ApiToken
    {
        get => _apiToken;
        set
        {
            if (SetProperty(ref _apiToken, value))
            {
                OnPropertyChanged(nameof(AuthModeText));
                _gallery.InvalidateClient();
            }
        }
    }

    /// <summary>Legacy global API key.</summary>
    public string GlobalApiKey
    {
        get => _globalApiKey;
        set
        {
            if (SetProperty(ref _globalApiKey, value))
            {
                OnPropertyChanged(nameof(AuthModeText));
                _gallery.InvalidateClient();
            }
        }
    }

    /// <summary>Account email paired with the global API key.</summary>
    public string ApiEmail
    {
        get => _apiEmail;
        set
        {
            if (SetProperty(ref _apiEmail, value))
            {
                OnPropertyChanged(nameof(AuthModeText));
                _gallery.InvalidateClient();
            }
        }
    }

    /// <summary>Default account id used by every account-scoped call.</summary>
    public string AccountId
    {
        get => _accountId;
        set
        {
            if (SetProperty(ref _accountId, value))
            {
                _gallery.InvalidateClient();
            }
        }
    }

    /// <summary>Default zone id used by every zone-scoped call.</summary>
    public string ZoneId
    {
        get => _zoneId;
        set
        {
            if (SetProperty(ref _zoneId, value))
            {
                _gallery.InvalidateClient();
            }
        }
    }

    /// <summary>API base address; empty means the public endpoint.</summary>
    public string BaseAddress
    {
        get => _baseAddress;
        set
        {
            if (SetProperty(ref _baseAddress, value))
            {
                _gallery.InvalidateClient();
            }
        }
    }

    /// <summary>Per-request timeout in seconds, as typed.</summary>
    public string TimeoutSeconds
    {
        get => _timeoutSeconds;
        set
        {
            if (SetProperty(ref _timeoutSeconds, value))
            {
                _gallery.InvalidateClient();
            }
        }
    }

    /// <summary>Retries after the first attempt, as typed.</summary>
    public string MaxRetries
    {
        get => _maxRetries;
        set
        {
            if (SetProperty(ref _maxRetries, value))
            {
                _gallery.InvalidateClient();
            }
        }
    }

    /// <summary>Whether a Cloudflare error response throws.</summary>
    public bool ThrowOnApiError
    {
        get => _throwOnApiError;
        set
        {
            if (SetProperty(ref _throwOnApiError, value))
            {
                _gallery.InvalidateClient();
            }
        }
    }

    /// <summary>Shows secret fields in clear text.</summary>
    public bool RevealSecrets
    {
        get => _revealSecrets;
        set => SetProperty(ref _revealSecrets, value);
    }

    /// <summary>Whether the credentials may be written to disk.</summary>
    public bool RememberOnThisMachine
    {
        get => _rememberOnThisMachine;
        set => SetProperty(ref _rememberOnThisMachine, value);
    }

    /// <summary>Index into <see cref="AuthModes"/>; selecting one clears the other scheme.</summary>
    public int SelectedAuthModeIndex
    {
        get => _selectedAuthModeIndex;
        set
        {
            if (!SetProperty(ref _selectedAuthModeIndex, value))
            {
                return;
            }

            if (value == 0)
            {
                GlobalApiKey = string.Empty;
                ApiEmail = string.Empty;
            }
            else
            {
                ApiToken = string.Empty;
            }

            OnPropertyChanged(nameof(AuthModeText));
            _gallery.InvalidateClient();
        }
    }

    /// <summary>Where the credentials file lives.</summary>
    public string StorageHint
    {
        get => _storageHint;
        set => SetProperty(ref _storageHint, value);
    }

    /// <summary>Human readable description of the scheme the current fields imply.</summary>
    public string AuthModeText => BuildOptionsOrNull() is { } options
        ? options.AuthenticationMode switch
        {
            CloudflareAuthenticationMode.ApiToken => "API token  -  Authorization: Bearer ***",
            CloudflareAuthenticationMode.GlobalApiKey => "Global API key  -  X-Auth-Key *** / X-Auth-Email " + ApiEmail,
            _ => "No usable credentials yet",
        }
        : "No usable credentials yet";

    /// <summary>True when the stored file exists.</summary>
    public bool HasStoredSettings => CredentialStore.Exists;

    /// <summary>Builds validated options for a new client.</summary>
    /// <returns>Options ready to hand to <c>CloudflareTransport</c>.</returns>
    /// <exception cref="CloudflareConfigurationException">The fields are incomplete or invalid.</exception>
    public CloudflareClientOptions BuildOptions()
    {
        var options = BuildOptionsOrNull()
            ?? throw new CloudflareConfigurationException("No usable credentials. Set an API token, or a global API key together with the account email.");

        options.Validate();
        return options;
    }

    /// <summary>Builds options without validating, or <c>null</c> when nothing is configured.</summary>
    /// <returns>Options, or <c>null</c>.</returns>
    public CloudflareClientOptions? BuildOptionsOrNull()
    {
        var options = new CloudflareClientOptions
        {
            ApiToken = Blank(ApiToken),
            GlobalApiKey = Blank(GlobalApiKey),
            ApiEmail = Blank(ApiEmail),
            AccountId = Blank(AccountId),
            ZoneId = Blank(ZoneId),
            MaxRetries = ParseInt(MaxRetries, 2),
            ThrowOnApiError = ThrowOnApiError,
            Timeout = TimeSpan.FromSeconds(ParseInt(TimeoutSeconds, 100)),
        };

        var baseAddress = Blank(BaseAddress);
        if (baseAddress is not null)
        {
            options.BaseAddress = new Uri(baseAddress, UriKind.Absolute);
        }

        return options.HasCredentials ? options : null;
    }

    /// <summary>Re-reads the stored settings (or the environment).</summary>
    public void Reload()
    {
        var stored = CredentialStore.Load();

        ApiToken = stored.ApiToken ?? string.Empty;
        GlobalApiKey = stored.GlobalApiKey ?? string.Empty;
        ApiEmail = stored.ApiEmail ?? string.Empty;
        AccountId = stored.AccountId ?? string.Empty;
        ZoneId = stored.ZoneId ?? string.Empty;
        BaseAddress = stored.BaseAddress ?? string.Empty;
        TimeoutSeconds = (stored.TimeoutSeconds ?? 100).ToString();
        MaxRetries = (stored.MaxRetries ?? 2).ToString();
        ThrowOnApiError = stored.ThrowOnApiError ?? true;
        RememberOnThisMachine = CredentialStore.Exists;
        SelectedAuthModeIndex = string.IsNullOrWhiteSpace(GlobalApiKey) ? 0 : 1;

        OnPropertyChanged(nameof(HasStoredSettings));
        OnPropertyChanged(nameof(AuthModeText));
    }

    /// <summary>Writes the settings to disk.</summary>
    /// <returns>A message describing what happened.</returns>
    public string Save()
    {
        CredentialStore.Save(new StoredSettings
        {
            ApiToken = Blank(ApiToken),
            GlobalApiKey = Blank(GlobalApiKey),
            ApiEmail = Blank(ApiEmail),
            AccountId = Blank(AccountId),
            ZoneId = Blank(ZoneId),
            BaseAddress = Blank(BaseAddress),
            TimeoutSeconds = ParseInt(TimeoutSeconds, 100),
            MaxRetries = ParseInt(MaxRetries, 2),
            ThrowOnApiError = ThrowOnApiError,
        });

        OnPropertyChanged(nameof(HasStoredSettings));
        return $"Saved to {CredentialStore.FilePath}";
    }

    /// <summary>Deletes the stored file and clears every field.</summary>
    /// <returns>A message describing what happened.</returns>
    public string Clear()
    {
        CredentialStore.Clear();

        ApiToken = string.Empty;
        GlobalApiKey = string.Empty;
        ApiEmail = string.Empty;
        AccountId = string.Empty;
        ZoneId = string.Empty;
        BaseAddress = string.Empty;
        RememberOnThisMachine = false;

        OnPropertyChanged(nameof(HasStoredSettings));
        OnPropertyChanged(nameof(AuthModeText));
        _gallery.InvalidateClient();

        return "Stored credentials removed.";
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(Blank(value), out var parsed) ? Math.Clamp(parsed, 0, 600) : fallback;
}
