using System.Collections.ObjectModel;
using Avalonia.Threading;
using CFNet.Configuration;
using CFNet.Exceptions;
using CFNet.Gallery.Infrastructure;
using CFNet.Http;
using CFNet.Services;

namespace CFNet.Gallery.ViewModels;

/// <summary>
/// Shell view model: navigation, credentials, the shared request/response inspector and the client
/// every module talks through.
/// </summary>
public sealed class GalleryViewModel : ObservableObject, IDisposable
{
    private readonly TransportRecorder _recorder = new();
    private readonly ObservableCollection<string> _history = new();

    private CloudflareClient? _client;
    private RecordingTransport? _transport;
    private PageViewModelBase? _currentPage;
    private string _requestText = "No request yet.";
    private string _responseText = "No response yet.";
    private string _resultText = "Nothing to show yet - pick an operation and press Run.";
    private string _statusText = "Idle";
    private string _connectionText = "Not connected";
    private string _currentOperation = string.Empty;
    private bool _isBusy;
    private bool _disposed;

    /// <summary>Builds the shell and its modules.</summary>
    public GalleryViewModel()
    {
        Settings = new SettingsViewModel(this);

        Navigation = new PageViewModelBase[]
        {
            new ZonesViewModel(this),
            new WorkersViewModel(this),
            new PagesViewModel(this),
            new D1ViewModel(this),
            new KvViewModel(this),
            new R2ViewModel(this),
            new SslViewModel(this),
            Settings,
        };

        _currentPage = Navigation[0];
        _recorder.Recorded += OnRecorded;

        ApplyCommand = new RelayCommand(ApplyAsync);
        CheckConnectionCommand = new RelayCommand(CheckConnectionAsync);
        SaveSettingsCommand = new RelayCommand(SaveSettings);
        ClearSettingsCommand = new RelayCommand(ClearSettings);
        ClearInspectorCommand = new RelayCommand(ClearInspector);
    }

    private async Task ApplyAsync()
    {
        BeginOperation("Apply settings");

        try
        {
            InvalidateClient();
            EnsureClient();

            var check = await _client!.CheckConnectionAsync();
            ConnectionText = check.IsSuccess
                ? $"Connected - {check.Message}"
                : $"Not connected - {check.Message}";

            StatusText = check.IsSuccess ? "OK - settings applied" : "FAILED - settings applied";
            ResultText = Json.Pretty(check);
        }
        catch (Exception exception)
        {
            FailOperation("Apply settings", exception);
            ConnectionText = "Not configured";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CheckConnectionAsync()
    {
        BeginOperation("Check connection");

        try
        {
            var check = await Client.CheckConnectionAsync();
            ConnectionText = check.IsSuccess
                ? $"Connected - {check.Message}"
                : $"Not connected - {check.Message}";

            CompleteOperation(check);
        }
        catch (Exception exception)
        {
            FailOperation("Check connection", exception);
        }
    }

    private void SaveSettings()
    {
        StatusText = Settings.RememberOnThisMachine
            ? Settings.Save()
            : "Saving is off - tick 'Remember on this machine' first.";
    }

    private void ClearSettings()
    {
        StatusText = Settings.Clear();
        ConnectionText = "Not configured";
    }

    private void ClearInspector()
    {
        RequestText = "No request yet.";
        ResponseText = "No response yet.";
        ResultText = string.Empty;
        StatusText = "Idle";
        _history.Clear();
    }

    /// <summary>Credentials and transport settings.</summary>
    public SettingsViewModel Settings { get; }

    /// <summary>Every page shown in the sidebar: the seven Cloudflare modules plus credentials.</summary>
    public IReadOnlyList<PageViewModelBase> Navigation { get; }

    /// <summary>Currently selected page.</summary>
    public PageViewModelBase? CurrentPage
    {
        get => _currentPage;
        set => SetProperty(ref _currentPage, value);
    }

    /// <summary>Rendered outgoing request.</summary>
    public string RequestText
    {
        get => _requestText;
        set => SetProperty(ref _requestText, value);
    }

    /// <summary>Rendered incoming response.</summary>
    public string ResponseText
    {
        get => _responseText;
        set => SetProperty(ref _responseText, value);
    }

    /// <summary>The deserialized, strongly typed result.</summary>
    public string ResultText
    {
        get => _resultText;
        set => SetProperty(ref _resultText, value);
    }

    /// <summary>One-line status shown in the header.</summary>
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    /// <summary>Outcome of the last connectivity check.</summary>
    public string ConnectionText
    {
        get => _connectionText;
        set => SetProperty(ref _connectionText, value);
    }

    /// <summary>True while an operation is in flight.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
            }
        }
    }

    /// <summary>True when no operation is running.</summary>
    public bool IsIdle => !_isBusy;

    /// <summary>Every call made this session, newest first.</summary>
    public ObservableCollection<string> History => _history;

    /// <summary>Applies the settings and re-checks connectivity.</summary>
    public RelayCommand ApplyCommand { get; }

    /// <summary>Runs <see cref="ICloudflareClient.CheckConnectionAsync"/>.</summary>
    public RelayCommand CheckConnectionCommand { get; }

    /// <summary>Writes the credentials to disk.</summary>
    public RelayCommand SaveSettingsCommand { get; }

    /// <summary>Removes the stored credentials.</summary>
    public RelayCommand ClearSettingsCommand { get; }

    /// <summary>Clears the inspector panes.</summary>
    public RelayCommand ClearInspectorCommand { get; }

    /// <summary>
    /// The client every module uses. Built on first use from the current settings; throws
    /// <see cref="CloudflareConfigurationException"/> when they are incomplete.
    /// </summary>
    public ICloudflareClient Client
    {
        get
        {
            EnsureClient();
            return _client!;
        }
    }

    /// <summary>Discards the cached client so the next call picks up new settings.</summary>
    public void InvalidateClient()
    {
        _client?.Dispose();
        _client = null;

        _transport?.Dispose();
        _transport = null;

        ConnectionText = "Settings changed - press Apply";
    }

    /// <summary>Marks the start of an operation.</summary>
    /// <param name="operation">Human readable name.</param>
    public void BeginOperation(string operation)
    {
        _currentOperation = operation;
        IsBusy = true;
        StatusText = $"Running {operation} ...";
        ResultText = string.Empty;
    }

    /// <summary>Marks an operation as succeeded and renders its typed result.</summary>
    /// <param name="result">The value the service returned.</param>
    public void CompleteOperation(object? result)
    {
        IsBusy = false;
        StatusText = $"OK - {_currentOperation}";
        ResultText = Json.Pretty(result);
    }

    /// <summary>Marks an operation as failed and renders the exception.</summary>
    /// <param name="operation">Human readable name.</param>
    /// <param name="exception">What went wrong.</param>
    public void FailOperation(string operation, Exception exception)
    {
        IsBusy = false;
        _currentOperation = operation;
        StatusText = $"FAILED - {operation}";
        ResultText = DescribeException(exception);
        ConnectionText = exception is CloudflareConfigurationException ? "Not configured" : ConnectionText;
    }

    /// <summary>Renders an exception for the result pane.</summary>
    /// <param name="exception">The exception to describe.</param>
    /// <returns>Multi-line description.</returns>
    public static string DescribeException(Exception exception)
    {
        var builder = new System.Text.StringBuilder();

        builder.Append(exception.GetType().Name).AppendLine(": ").AppendLine(exception.Message);

        if (exception is CloudflareApiException api)
        {
            builder.AppendLine();
            builder.Append("status:   ").AppendLine(api.StatusCode.HasValue ? $"{(int)api.StatusCode.Value} {api.StatusCode.Value}" : "(none)");
            builder.Append("CF-RAY:   ").AppendLine(api.RayId ?? "(none)");
            builder.Append("endpoint: ").AppendLine(api.RequestUri?.ToString() ?? "(unknown)");

            foreach (var error in api.Errors)
            {
                builder.Append("error:    ").AppendLine(error.ToString());
            }
        }
        else if (exception is CloudflareConfigurationException configuration)
        {
            foreach (var problem in configuration.Problems)
            {
                builder.Append("  - ").AppendLine(problem);
            }
        }

        builder.AppendLine();
        builder.AppendLine(exception.ToString());

        return builder.ToString();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _recorder.Recorded -= OnRecorded;
        _client?.Dispose();
        _client = null;
        _transport?.Dispose();
        _transport = null;
    }

    /// <summary>Applies the current settings and re-checks connectivity.</summary>
    /// <returns>A task completing when the check finishes.</returns>
    public Task ApplySettingsAsync() => ApplyAsync();

    /// <summary>Runs the connectivity check on its own.</summary>
    /// <returns>A task completing when the check finishes.</returns>
    public Task RunConnectionCheckAsync() => CheckConnectionAsync();

    /// <summary>
    /// Lists the accounts the credentials can see, which is where an account id to paste into the settings
    /// comes from.
    /// </summary>
    /// <returns>A task completing when the call finishes.</returns>
    public Task RunListAccountsAsync() => DiscoverAsync("List accounts", () => Client.Accounts.ListAsync(new AccountListParameters { PerPage = 50 }));

    /// <summary>Reads the one account the configured id names.</summary>
    /// <returns>A task completing when the call finishes.</returns>
    public Task RunGetAccountAsync() => DiscoverAsync("Get account", () => Client.Accounts.GetAsync());

    /// <summary>Reads the user the credentials belong to.</summary>
    /// <returns>A task completing when the call finishes.</returns>
    public Task RunReadUserAsync() => DiscoverAsync("Read user", () => Client.User.GetAsync());

    /// <summary>
    /// Runs one of the discovery calls behind the settings page. They all read something the credentials
    /// themselves describe, so they share one shape: report the result, or report why it failed.
    /// </summary>
    private async Task DiscoverAsync<TResult>(string operation, Func<Task<TResult>> call)
    {
        BeginOperation(operation);

        try
        {
            CompleteOperation(await call());
        }
        catch (Exception exception)
        {
            FailOperation(operation, exception);
        }
    }

    /// <summary>Persists the settings when the user allowed it.</summary>
    public void SaveSettingsNow() => SaveSettings();

    /// <summary>Removes the persisted settings.</summary>
    public void ClearSettingsNow() => ClearSettings();

    private void EnsureClient()
    {
        if (_client is not null)
        {
            return;
        }

        var options = Settings.BuildOptions();

        // The recording decorator is what feeds the inspector; the library only ever sees
        // ICloudflareTransport, so no service code knows a UI is listening.
        var inner = new CloudflareTransport(options);
        _transport = new RecordingTransport(inner, _recorder);
        _client = new CloudflareClient(_transport);
    }

    private void OnRecorded(TransportRecord record)
    {
        // The transport runs with ConfigureAwait(false), so this callback can arrive on a pool
        // thread. Bound properties must be set on the UI thread.
        Dispatcher.UIThread.Post(() =>
        {
            RequestText = record.RequestText;
            ResponseText = record.ErrorText ?? record.ResponseText;
            _history.Insert(0, $"{record.StartedUtc:HH:mm:ss}  {record.Operation}  {(record.ErrorText is null ? "ok" : "FAILED")}");

            while (_history.Count > 200)
            {
                _history.RemoveAt(_history.Count - 1);
            }
        }, DispatcherPriority.Background);
    }
}
