namespace CFNet.Exceptions;

/// <summary>
/// Raised when the client is misconfigured: missing credentials, missing account/zone id,
/// an invalid argument or an unusable request. These are always programming or configuration
/// mistakes, never transient network problems.
/// </summary>
public sealed class CloudflareConfigurationException : CloudflareException
{
    /// <summary>Creates an exception for a single problem.</summary>
    public CloudflareConfigurationException(string message)
        : base(message)
    {
        Problems = new[] { message };
    }

    /// <summary>Creates an exception aggregating several validation problems.</summary>
    public CloudflareConfigurationException(IReadOnlyList<string> problems)
        : base(BuildMessage(problems))
    {
        Problems = problems ?? Array.Empty<string>();
    }

    /// <summary>Creates an exception wrapping an inner failure.</summary>
    public CloudflareConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Problems = new[] { message };
    }

    /// <summary>Every individual problem that was detected.</summary>
    public IReadOnlyList<string> Problems { get; }

    private static string BuildMessage(IReadOnlyList<string>? problems)
    {
        if (problems is null || problems.Count == 0)
        {
            return "Invalid CF.Net configuration.";
        }

        return problems.Count == 1
            ? problems[0]
            : "Invalid CF.Net configuration:" + Environment.NewLine + string.Join(Environment.NewLine, problems.Select(p => " - " + p));
    }
}
