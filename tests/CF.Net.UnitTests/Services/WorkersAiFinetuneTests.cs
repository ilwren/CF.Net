using CFNet.Models.WorkersAi;
using CFNet.UnitTests.Testing;
using Xunit;

namespace CFNet.UnitTests.Services;

/// <summary>
/// The rest of Workers AI: the account's own fine-tuning jobs, the ones Cloudflare publishes, and the file
/// formats the to-markdown conversion accepts. Worth pinning because the fine-tune list's published shape and
/// its own summary disagree about whether it answers a list or one object, because one query parameter here is
/// camelCase where nearly everything Cloudflare sends is snake_case, and because one of the two to-markdown
/// endpoints is a multipart upload this library cannot express.
/// </summary>
public class WorkersAiFinetuneTests
{
    private const string JobJson =
        """{"id":"182bd5e5-6e1a-4fe4-a799-aa6d9a6ab26e","name":"support-replies","model":"@cf/meta/llama-3.2-1b-instruct","description":"Trained on this account's support replies.","created_at":"2019-12-27T18:11:19.117Z","modified_at":"2019-12-28T09:00:00Z"}""";

    [Fact]
    public async Task ListFinetunesAsync_gets_the_accounts_jobs_with_no_query()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");

        var jobs = await harness.Client.WorkersAi.ListFinetunesAsync();

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/ai/finetunes",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        // No filters exist for this list, so nothing is asked for and nothing goes out.
        Assert.Equal(string.Empty, harness.LastRequest.Query);
        Assert.Empty(jobs);
    }

    [Fact]
    public async Task ListFinetunesAsync_reads_an_array_of_jobs()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult($"[{JobJson},{JobJson}]");

        var jobs = await harness.Client.WorkersAi.ListFinetunesAsync();

        Assert.Equal(2, jobs.Count);
        var job = jobs[0];
        Assert.Equal("182bd5e5-6e1a-4fe4-a799-aa6d9a6ab26e", job.Id);
        Assert.Equal("support-replies", job.Name);
        Assert.Equal("@cf/meta/llama-3.2-1b-instruct", job.Model);
        Assert.Equal("Trained on this account's support replies.", job.Description);
        Assert.Equal(
            new DateTimeOffset(2019, 12, 27, 18, 11, 19, 117, TimeSpan.Zero),
            job.CreatedAt!.Value);
        Assert.Equal(new DateTimeOffset(2019, 12, 28, 9, 0, 0, TimeSpan.Zero), job.ModifiedAt!.Value);

        // Nothing in the published shape is left over.
        Assert.True(job.AdditionalData is not { Count: > 0 });

        // The list answers no public flag, and a field that did not arrive stays null rather than false.
        Assert.Null(job.Public);
    }

    [Fact]
    public async Task ListFinetunesAsync_reads_one_object_as_a_list_of_one_job()
    {
        using var harness = CloudflareHarness.Create();

        // The endpoint's own 200 example: result is a single job, not a list of them, though its summary says
        // it lists every job the account created. Both read, and neither is a guess about the other.
        harness.RespondWithResult(JobJson);

        var jobs = await harness.Client.WorkersAi.ListFinetunesAsync();

        var job = Assert.Single(jobs);
        Assert.Equal("support-replies", job.Name);
        Assert.Equal("@cf/meta/llama-3.2-1b-instruct", job.Model);
    }

    [Fact]
    public async Task ListFinetunesAsync_reads_a_null_or_empty_result_as_no_jobs()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("null");
        harness.RespondWithResult("{}");

        // A null result arrives as the JSON kind null, and an empty object as a job with nothing in it: the
        // first is no jobs, and the second is one job Cloudflare said nothing about.
        Assert.Empty(await harness.Client.WorkersAi.ListFinetunesAsync());
        Assert.Single(await harness.Client.WorkersAi.ListFinetunesAsync());
        Assert.Equal(2, harness.RequestCount);
    }

    [Fact]
    public async Task A_finetune_keeps_the_status_and_metrics_its_own_summary_promises()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """[{"id":"182bd5e5-6e1a-4fe4-a799-aa6d9a6ab26e","name":"support-replies","model":"m","status":"running","metrics":{"epochs":3,"loss":0.42}}]""");

        var job = Assert.Single(await harness.Client.WorkersAi.ListFinetunesAsync());

        Assert.Equal("support-replies", job.Name);
        Assert.Equal(
            new[] { "metrics", "status" },
            job.AdditionalData!.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray());
        Assert.Equal("running", job.AdditionalData["status"].GetString());
    }

    [Fact]
    public async Task ListPublicFinetunesAsync_sends_limit_offset_and_a_camel_case_order_by()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");

        await harness.Client.WorkersAi.ListPublicFinetunesAsync(
            new WorkersAiPublicFinetuneListParameters { Limit = 5, Offset = 10, OrderBy = "created_at" });

        Assert.EndsWith(
            "/accounts/acct-1234/ai/finetunes/public",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        // This list is paged by limit and offset rather than page and per_page, and orderBy is camelCase on
        // the wire - so these parameters are their own class rather than the paging every other list uses.
        Assert.Equal("limit=5&offset=10&orderBy=created_at", harness.LastRequest.Query.TrimStart('?'));
    }

    [Fact]
    public async Task ListPublicFinetunesAsync_sends_no_query_when_nothing_is_asked_for_and_reads_the_flag()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """[{"id":"f9d0e1f2-3a4b-5c6d-7e8f-0a1b2c3d4e5f","name":"cf-public-sentiment","model":"@cf/sentiment","public":true}]""");

        var jobs = await harness.Client.WorkersAi.ListPublicFinetunesAsync();

        Assert.Equal(string.Empty, harness.LastRequest.Query);
        var job = Assert.Single(jobs);
        Assert.True(job.Public);
        Assert.Equal("cf-public-sentiment (@cf/sentiment)", job.ToString());
    }

    [Fact]
    public async Task ListToMarkdownFormatsAsync_reads_the_camel_case_mime_type()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """[{"extension":"pdf","mimeType":"application/pdf"},{"extension":"docx","mimeType":"application/vnd.openxmlformats-officedocument.wordprocessingml.document"}]""");

        var formats = await harness.Client.WorkersAi.ListToMarkdownFormatsAsync();

        Assert.Equal(HttpMethod.Get, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/ai/tomarkdown/supported",
            harness.LastRequest.Path,
            StringComparison.Ordinal);
        Assert.Equal(string.Empty, harness.LastRequest.Query);

        Assert.Equal(2, formats.Count);
        Assert.Equal("pdf", formats[0].Extension);

        // mimeType is camelCase on the wire, which nearly nothing Cloudflare answers is, so it is named
        // explicitly rather than left to this library's snake_case policy.
        Assert.Equal("application/pdf", formats[0].MimeType);
        Assert.True(formats[0].AdditionalData is not { Count: > 0 });
        Assert.Equal("pdf (application/pdf)", formats[0].ToString());
    }

    [Fact]
    public async Task ListToMarkdownFormatsAsync_answers_nothing_when_the_list_is_empty()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");

        Assert.Empty(await harness.Client.WorkersAi.ListToMarkdownFormatsAsync());
        Assert.Equal(1, harness.RequestCount);
    }
}
