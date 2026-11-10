namespace CFNet.Exceptions;

/// <summary>
/// Base class for every exception raised by <c>CF.Net</c>. Catch this when you want to handle
/// "anything the SDK can complain about" without swallowing unrelated failures.
/// </summary>
public abstract class CloudflareException : Exception
{
    /// <inheritdoc />
    protected CloudflareException()
    {
    }

    /// <inheritdoc />
    protected CloudflareException(string? message)
        : base(message)
    {
    }

    /// <inheritdoc />
    protected CloudflareException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
