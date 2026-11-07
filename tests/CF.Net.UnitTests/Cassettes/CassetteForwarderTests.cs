using System.Net;
using CFNet.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CFNet.UnitTests.Cassettes;

/// <summary>
/// A cassette run shares one <see cref="CassetteHandler"/>: one run should produce one cassette, and a
/// replay should consume a recording in the order it was made. Sharing a
/// <see cref="System.Net.Http.DelegatingHandler"/> between <c>IHttpClientFactory</c> pipelines is
/// exactly what the factory forbids, so <see cref="CassetteForwarder"/> exists — and these tests are
/// what stops it from quietly ceasing to.
/// </summary>
public class CassetteForwarderTests
{
    private const string VerifyUrl = "https://api.cloudflare.com/client/v4/user/tokens/verify";

    private static Cassette CassetteWithTwoVerifications()
    {
        var cassette = new Cassette("shared", "https://api.cloudflare.com/client/v4/");
        for (var index = 0; index < 2; index++)
        {
            cassette.Exchanges.Add(new RecordedExchange
            {
                Name = $"verify-{index}",
                Method = "GET",
                PathAndQuery = "/client/v4/user/tokens/verify",
                StatusCode = 200,
                ResponseContentType = "application/json",
                ResponseBody = "{\"success\":true,\"errors\":[],\"messages\":[],\"result\":{\"status\":\"active\"}}",
            });
        }

        return cassette;
    }

    [Fact]
    public async Task Several_client_pipelines_can_share_one_recorder()
    {
        using var shared = CassetteHandler.Replaying(CassetteWithTwoVerifications());

        var services = new ServiceCollection();
        for (var index = 0; index < 2; index++)
        {
            services.AddHttpClient($"client-{index}")
                .AddHttpMessageHandler(() => new CassetteForwarder(shared));
        }

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        // Registering the shared handler directly would throw "The inner handler has already been
        // assigned" on the second client. Each forwarder is a fresh instance, so both build.
        for (var index = 0; index < 2; index++)
        {
            using var client = factory.CreateClient($"client-{index}");
            using var response = await client.GetAsync(VerifyUrl);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(2, shared.ReplayedCount);
    }

    [Fact]
    public async Task A_forwarder_does_not_dispose_the_handler_it_borrowed()
    {
        using var shared = CassetteHandler.Replaying(CassetteWithTwoVerifications());

        using (var forwarder = new CassetteForwarder(shared))
        using (var invoker = new HttpMessageInvoker(forwarder))
        {
            using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, VerifyUrl), CancellationToken.None);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // The borrowed handler is still alive and still answering.
        using var again = new HttpMessageInvoker(shared, disposeHandler: false);
        using var second = await again.SendAsync(new HttpRequestMessage(HttpMethod.Get, VerifyUrl), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(2, shared.ReplayedCount);
    }
}
