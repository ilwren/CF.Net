using CFNet.Exceptions;
using CFNet.Services;

namespace CFNet.Gallery.ViewModels;

/// <summary>
/// Shared plumbing for one sidebar page: title, busy handling, and a uniform way to run a Cloudflare
/// call and show its result.
/// </summary>
public abstract class ModuleViewModelBase : PageViewModelBase
{
    private string _status = "Idle";

    /// <summary>Wires the page to the shell.</summary>
    /// <param name="gallery">Owning shell.</param>
    protected ModuleViewModelBase(GalleryViewModel gallery)
    {
        Gallery = gallery ?? throw new ArgumentNullException(nameof(gallery));
    }

    /// <summary>The shell.</summary>
    protected GalleryViewModel Gallery { get; }

    /// <summary>Page-local status line.</summary>
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    /// <summary>The client, built from the current settings on first use.</summary>
    protected ICloudflareClient Client => Gallery.Client;

    /// <summary>Default account id from the settings page.</summary>
    protected string AccountId => Gallery.Settings.AccountId.Trim();

    /// <summary>Default zone id from the settings page.</summary>
    protected string ZoneId => Gallery.Settings.ZoneId.Trim();

    /// <summary>Runs a call that returns a value.</summary>
    /// <remarks>
    /// Generic rather than <c>Func&lt;Task&lt;object?&gt;&gt;</c> on purpose: <see cref="Task{TResult}"/>
    /// is invariant, so a lambda that awaits a service call returning <c>Task&lt;Zone&gt;</c> does not
    /// convert to <c>Task&lt;object?&gt;</c> and every page would need a cast.
    /// </remarks>
    /// <typeparam name="TResult">What the service call returns.</typeparam>
    /// <param name="operation">Name shown in the status line.</param>
    /// <param name="action">The service call.</param>
    protected async Task RunAsync<TResult>(string operation, Func<Task<TResult>> action)
    {
        if (Gallery.IsBusy)
        {
            return;
        }

        Gallery.BeginOperation(operation);
        Status = $"{operation} ...";

        try
        {
            var result = await action().ConfigureAwait(true);
            Gallery.CompleteOperation(result);
            Status = Summarise(result);
        }
        catch (Exception exception)
        {
            Gallery.FailOperation(operation, exception);
            Status = DescribeFailure(exception);
        }
    }

    /// <summary>Runs a call that returns nothing.</summary>
    /// <param name="operation">Name shown in the status line.</param>
    /// <param name="action">The service call.</param>
    protected Task RunVoidAsync(string operation, Func<Task> action) =>
        RunAsync(operation, async () =>
        {
            await action().ConfigureAwait(true);
            return "done";
        });

    /// <summary>Normalises a blank text field to <c>null</c>.</summary>
    /// <param name="value">Raw field value.</param>
    /// <returns>Trimmed value, or <c>null</c> when blank.</returns>
    protected static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Parses an optional integer field.</summary>
    /// <param name="value">Raw field value.</param>
    /// <returns>The parsed number, or <c>null</c> when blank or invalid.</returns>
    protected static int? OptionalInt(string? value) =>
        int.TryParse(Blank(value), out var parsed) ? parsed : null;

    /// <summary>Parses an optional boolean field.</summary>
    /// <param name="value">Raw field value: <c>true</c>, <c>false</c> or blank.</param>
    /// <returns>The parsed flag, or <c>null</c> when blank.</returns>
    protected static bool? OptionalBool(string? value) =>
        Blank(value) is { } text
            ? text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1" || text.Equals("yes", StringComparison.OrdinalIgnoreCase)
                ? true
                : text.Equals("false", StringComparison.OrdinalIgnoreCase) || text == "0" || text.Equals("no", StringComparison.OrdinalIgnoreCase)
                    ? false
                    : null
            : null;

    /// <summary>Splits a multi-line text field into non-empty lines.</summary>
    /// <param name="value">Raw field value.</param>
    /// <returns>One entry per line, or <c>null</c> when the field is blank.</returns>
    protected static List<string>? Lines(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var entries = value
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

        return entries.Count == 0 ? null : entries;
    }

    private static string Summarise(object? result) => result switch
    {
        null => "OK - the API returned no result",
        string text => $"OK - {text}",
        _ => $"OK - {result.GetType().Name}",
    };

    private static string DescribeFailure(Exception exception) => exception switch
    {
        CloudflareConfigurationException configuration =>
            configuration.Problems.Count == 0 ? configuration.Message : configuration.Problems[0],
        CloudflareApiException api =>
            $"FAILED - {(int?)api.StatusCode} {api.ErrorCode}: {(api.Errors.Count > 0 ? api.Errors[0].Message : api.Message)}",
        _ => $"FAILED - {exception.Message}",
    };
}
