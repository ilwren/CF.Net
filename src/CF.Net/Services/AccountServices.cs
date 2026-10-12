using CFNet.Configuration;
using CFNet.Http;
using CFNet.Models.Common;

namespace CFNet.Services;

/// <summary>
/// Credential verification and account discovery. These are the calls to make first: they prove the
/// credentials work and produce the account id every other service needs.
/// </summary>
public interface IUserService
{
    /// <summary>
    /// Verifies the configured token via <c>GET /user/tokens/verify</c>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The verification result. <see cref="CloudflareTokenVerification.IsActive"/> tells you whether the token is usable.</returns>
    /// <remarks>
    /// Only meaningful for API tokens. A Global API key is not a token, so Cloudflare answers this
    /// endpoint with an error for that scheme; use <see cref="GetAsync"/> instead.
    /// </remarks>
    Task<CloudflareTokenVerification> VerifyTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies an account-owned token via <c>GET /accounts/{account_id}/tokens/verify</c>.
    /// </summary>
    /// <param name="accountId">The account that owns the token; defaults to the configured account id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The verification result.</returns>
    /// <remarks>
    /// This is the endpoint that answers for a <c>cfat_...</c> token. The user-scoped
    /// <see cref="VerifyTokenAsync"/> rejects those with <c>1000 Invalid API Token</c>;
    /// <see cref="CheckConnectionAsync"/> picks between the two on its own.
    /// </remarks>
    Task<CloudflareTokenVerification> VerifyAccountTokenAsync(string? accountId = null, CancellationToken cancellationToken = default);

    /// <summary>Fetches the authenticated user via <c>GET /user</c>.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CloudflareUser> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs whichever check suits the configured credential scheme and reports the outcome without
    /// throwing. Handy as a "Test connection" button.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A description of what was checked and whether it succeeded.</returns>
    Task<CloudflareConnectionCheck> CheckConnectionAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class UserService : CloudflareService, IUserService
{
    /// <summary>Creates the service.</summary>
    /// <param name="transport">The transport to execute requests through.</param>
    public UserService(ICloudflareTransport transport)
        : base(transport)
    {
    }

    /// <inheritdoc />
    public async Task<CloudflareTokenVerification> VerifyTokenAsync(CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get("user/tokens/verify");
        request.OperationName = "Verify API Token";

        var response = await Transport.SendAsync<CloudflareTokenVerification>(request, cancellationToken).ConfigureAwait(false);
        return response.Result ?? new CloudflareTokenVerification();
    }

    /// <inheritdoc />
    public async Task<CloudflareTokenVerification> VerifyAccountTokenAsync(string? accountId = null, CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get($"{AccountPath(ResolveAccountId(accountId))}/tokens/verify");
        request.OperationName = "Verify Account API Token";

        var response = await Transport.SendAsync<CloudflareTokenVerification>(request, cancellationToken).ConfigureAwait(false);
        return response.Result ?? new CloudflareTokenVerification();
    }

    /// <inheritdoc />
    public async Task<CloudflareUser> GetAsync(CancellationToken cancellationToken = default)
    {
        var request = CloudflareRequest.Get("user");
        request.OperationName = "Get User";

        var response = await Transport.SendAsync<CloudflareUser>(request, cancellationToken).ConfigureAwait(false);
        return response.EnsureResult();
    }

    /// <inheritdoc />
    public async Task<CloudflareConnectionCheck> CheckConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (!Options.HasCredentials)
        {
            return new CloudflareConnectionCheck
            {
                IsSuccess = false,
                Method = CloudflareAuthenticationMode.None,
                Message = "No credentials are configured. Provide an API token, or a Global API key together with the account e-mail.",
            };
        }

        var start = DateTimeOffset.UtcNow;

        try
        {
            // For tokens the verify endpoint is the cheapest authoritative check. For a Global API key
            // it is not applicable, so fall back to reading the user object.
            if (Options.AuthenticationMode == CloudflareAuthenticationMode.ApiToken)
            {
                // An account-owned token is a service principal, and the user-scoped endpoint rejects
                // it, so go straight to the account endpoint when the prefix says that is what we have.
                if (Options.HasAccountOwnedToken && HasAccountId)
                {
                    return await CheckAccountTokenAsync(start, cancellationToken).ConfigureAwait(false);
                }

                try
                {
                    var verification = await VerifyTokenAsync(cancellationToken).ConfigureAwait(false);
                    return new CloudflareConnectionCheck
                    {
                        IsSuccess = verification.IsActive,
                        Method = CloudflareAuthenticationMode.ApiToken,
                        Message = verification.IsActive
                            ? "API token is active."
                            : $"API token is not active (status '{verification.Status ?? "unknown"}').",
                        Elapsed = DateTimeOffset.UtcNow - start,
                    };
                }
                catch (Exceptions.CloudflareApiException rejection) when (rejection.IsAuthenticationFailure && HasAccountId)
                {
                    // Worth one more try: a token issued before 2026 has no prefix, so an account-owned
                    // credential can look like a user one. Reporting "invalid token" without asking the
                    // account endpoint sends people off to recreate a secret that was never broken.
                    CloudflareConnectionCheck? accountCheck = null;
                    try
                    {
                        accountCheck = await CheckAccountTokenAsync(start, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exceptions.CloudflareApiException)
                    {
                        // The account endpoint refused the credential too, so there is nothing more to
                        // learn from it and the original rejection stands.
                    }

                    if (accountCheck is not null)
                    {
                        return accountCheck.IsSuccess
                            ? new CloudflareConnectionCheck
                            {
                                IsSuccess = true,
                                Method = accountCheck.Method,
                                Message = accountCheck.Message + " The user-scoped endpoint rejected this token, so it is account-owned.",
                                Elapsed = accountCheck.Elapsed,
                            }
                            : accountCheck;
                    }

                    return new CloudflareConnectionCheck
                    {
                        IsSuccess = false,
                        Method = CloudflareAuthenticationMode.ApiToken,
                        Message = rejection.Message,
                        StatusCode = rejection.StatusCode,
                        ErrorCode = rejection.ErrorCode,
                        Elapsed = DateTimeOffset.UtcNow - start,
                    };
                }
            }

            var user = await GetAsync(cancellationToken).ConfigureAwait(false);
            return new CloudflareConnectionCheck
            {
                IsSuccess = true,
                Method = CloudflareAuthenticationMode.GlobalApiKey,
                Message = $"Authenticated as {user.Email ?? user.Id ?? "unknown user"}.",
                AccountEmail = user.Email,
                Elapsed = DateTimeOffset.UtcNow - start,
            };
        }
        catch (Exceptions.CloudflareApiException exception)
        {
            return new CloudflareConnectionCheck
            {
                IsSuccess = false,
                Method = Options.AuthenticationMode,
                Message = exception.Message,
                StatusCode = exception.StatusCode,
                ErrorCode = exception.ErrorCode,
                Elapsed = DateTimeOffset.UtcNow - start,
            };
        }
        catch (Exceptions.CloudflareTransportException exception)
        {
            return new CloudflareConnectionCheck
            {
                IsSuccess = false,
                Method = Options.AuthenticationMode,
                Message = exception.Message,
                IsTimeout = exception.IsTimeout,
                Elapsed = DateTimeOffset.UtcNow - start,
            };
        }
    }

    /// <summary>True when an account id is available to verify an account-owned token against.</summary>
    private bool HasAccountId => !string.IsNullOrWhiteSpace(Options.AccountId);

    /// <summary>Verifies through the account-scoped endpoint and reports the outcome.</summary>
    /// <param name="start">When the check began, for <see cref="CloudflareConnectionCheck.Elapsed"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task<CloudflareConnectionCheck> CheckAccountTokenAsync(DateTimeOffset start, CancellationToken cancellationToken)
    {
        var verification = await VerifyAccountTokenAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return new CloudflareConnectionCheck
        {
            IsSuccess = verification.IsActive,
            Method = CloudflareAuthenticationMode.ApiToken,
            Message = verification.IsActive
                ? "Account-owned API token is active."
                : $"Account-owned API token is not active (status '{verification.Status ?? "unknown"}').",
            Elapsed = DateTimeOffset.UtcNow - start,
        };
    }
}

/// <summary>Outcome of <see cref="IUserService.CheckConnectionAsync"/>.</summary>
public sealed class CloudflareConnectionCheck
{
    /// <summary>Whether the credentials were accepted.</summary>
    public bool IsSuccess { get; init; }

    /// <summary>Which credential scheme was checked.</summary>
    public CloudflareAuthenticationMode Method { get; init; }

    /// <summary>Human readable summary, safe to show in a UI.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>HTTP status code when Cloudflare answered with a failure.</summary>
    public System.Net.HttpStatusCode? StatusCode { get; init; }

    /// <summary>Cloudflare error code when one was reported.</summary>
    public int? ErrorCode { get; init; }

    /// <summary>Authenticated account e-mail, when it could be determined.</summary>
    public string? AccountEmail { get; init; }

    /// <summary>True when the failure was a client side timeout.</summary>
    public bool IsTimeout { get; init; }

    /// <summary>How long the check took.</summary>
    public TimeSpan Elapsed { get; init; }
}

/// <summary>
/// Account discovery: <c>GET /accounts</c>. Use it to find the account id to configure when several
/// accounts are reachable with one token.
/// </summary>
public interface IAccountsService
{
    /// <summary>Lists the accounts the credentials can see.</summary>
    /// <param name="parameters">Optional paging.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CloudflarePage<CloudflareAccount>> ListAsync(
        AccountListParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches one account.</summary>
    /// <param name="accountId">Account id; falls back to the configured default when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The account, or <c>null</c> when it does not exist.</returns>
    Task<CloudflareAccount?> GetAsync(string? accountId = null, CancellationToken cancellationToken = default);
}

/// <summary>Query filters for <c>GET /accounts</c>.</summary>
public sealed class AccountListParameters : CloudflarePagedParameters
{
    /// <summary>Filter by account name.</summary>
    public string? Name { get; set; }

    /// <inheritdoc />
    public override void Apply(QueryStringBuilder query)
    {
        base.Apply(query);
        query.AddIfNotEmpty("name", Name);
    }
}

/// <inheritdoc />
public sealed class AccountsService : CloudflareService, IAccountsService
{
    /// <summary>Creates the service.</summary>
    /// <param name="transport">The transport to execute requests through.</param>
    public AccountsService(ICloudflareTransport transport)
        : base(transport)
    {
    }

    /// <inheritdoc />
    public async Task<CloudflarePage<CloudflareAccount>> ListAsync(
        AccountListParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var request = Apply(CloudflareRequest.Get("accounts"), parameters);
        request.OperationName = "List Accounts";

        var response = await Transport.SendAsync<List<CloudflareAccount>>(request, cancellationToken).ConfigureAwait(false);
        return response.ToPage();
    }

    /// <inheritdoc />
    public async Task<CloudflareAccount?> GetAsync(string? accountId = null, CancellationToken cancellationToken = default)
    {
        var id = ResolveAccountId(accountId);
        var request = CloudflareRequest.Get(AccountPath(id)).Tolerate(System.Net.HttpStatusCode.NotFound);
        request.OperationName = "Get Account";

        var response = await Transport.SendAsync<CloudflareAccount>(request, cancellationToken).ConfigureAwait(false);
        return response.Result;
    }
}
