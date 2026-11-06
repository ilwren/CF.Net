using System.Text;
using CFNet.Exceptions;
using CFNet.Models.WorkersAi;
using CFNet.UnitTests.Testing;
using Xunit;

namespace CFNet.UnitTests.Services;

/// <summary>
/// To-markdown: the formats the conversion accepts, and the conversion itself. Worth pinning because the
/// conversion is a multipart upload in which every part is named <c>files</c> - so what identifies a file is
/// its part's filename and content type, not anything in a body - because Cloudflare publishes the answer's
/// <c>tokens</c> as a string, and because a camelCase <c>mimeType</c> turns up in both of its answers.
/// </summary>
public class WorkersAiToMarkdownTests
{
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

    [Fact]
    public async Task ConvertToMarkdownAsync_uploads_every_file_as_a_part_named_files()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult("[]");

        await harness.Client.WorkersAi.ConvertToMarkdownAsync(
            new[]
            {
                WorkersAiToMarkdownFile.OfHtml("page.html", "<h1>CF.Net</h1>"),
                WorkersAiToMarkdownFile.OfText("notes.txt", "plain words"),
            });

        Assert.Equal(HttpMethod.Post, harness.LastRequest.Method);
        Assert.EndsWith(
            "/accounts/acct-1234/ai/tomarkdown",
            harness.LastRequest.Path,
            StringComparison.Ordinal);

        // One part per file and all of them named "files", each carrying its own filename and type: Cloudflare
        // reads a file from those two, since nothing in a multipart part says what it is.
        var body = harness.LastRequest.Body!;
        Assert.Equal(2, body.Split("name=\"files\"").Length - 1);
        Assert.Contains("filename=\"page.html\"", body, StringComparison.Ordinal);
        Assert.Contains("filename=\"notes.txt\"", body, StringComparison.Ordinal);
        Assert.Contains("text/html", body, StringComparison.Ordinal);
        Assert.Contains("text/plain", body, StringComparison.Ordinal);
        Assert.Contains("<h1>CF.Net</h1>", body, StringComparison.Ordinal);
        Assert.Contains("plain words", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConvertToMarkdownAsync_reads_the_markdown_and_the_token_count_Cloudflare_answers()
    {
        using var harness = CloudflareHarness.Create();
        harness.RespondWithResult(
            """[{"data":"# CF.Net\n\nTyped **Cloudflare** API v4.","format":"html","mimeType":"text/html","name":"page.html","tokens":"42"}]""");

        var conversions = await harness.Client.WorkersAi.ConvertToMarkdownAsync(
            new[] { WorkersAiToMarkdownFile.OfHtml("page.html", "<h1>CF.Net</h1>") });

        var conversion = Assert.Single(conversions);
        Assert.Equal("# CF.Net\n\nTyped **Cloudflare** API v4.", conversion.Data);
        Assert.Equal("html", conversion.Format);
        Assert.Equal("text/html", conversion.MimeType);
        Assert.Equal("page.html", conversion.Name);

        // Cloudflare publishes the token count as a string, so the string is what is kept and the number is
        // read from it - and is null rather than 0 when the string holds no number.
        Assert.Equal("42", conversion.Tokens);
        Assert.Equal(42, conversion.TokenCount);
        Assert.Null(new WorkersAiToMarkdownConversion { Tokens = "a lot" }.TokenCount);
        Assert.Equal("page.html (html)", conversion.ToString());
        Assert.True(conversion.AdditionalData is not { Count: > 0 });
    }

    [Fact]
    public async Task ConvertToMarkdownAsync_refuses_a_conversion_Cloudflare_could_not_make()
    {
        using var harness = CloudflareHarness.Create();

        var refused = new Func<Task>[]
        {
            () => harness.Client.WorkersAi.ConvertToMarkdownAsync(Array.Empty<WorkersAiToMarkdownFile>()),
            () => harness.Client.WorkersAi.ConvertToMarkdownAsync(
                new[] { WorkersAiToMarkdownFile.OfText("   ", "words") }),
            () => harness.Client.WorkersAi.ConvertToMarkdownAsync(
                new[] { WorkersAiToMarkdownFile.OfText("notes.txt", string.Empty) }),
            () => harness.Client.WorkersAi.ConvertToMarkdownAsync(
                new[] { new WorkersAiToMarkdownFile("notes.txt", Array.Empty<byte>()) }),
        };
        foreach (var call in refused)
        {
            var exception = await Assert.ThrowsAsync<CloudflareConfigurationException>(call);
            Assert.NotEmpty(exception.Problems);
        }

        // Nothing was refused by Cloudflare, because nothing was sent.
        Assert.Equal(0, harness.RequestCount);
    }

    [Fact]
    public void The_file_factories_send_the_type_their_bytes_are()
    {
        Assert.Equal("text/plain", WorkersAiToMarkdownFile.OfText("notes.txt", "words").ContentType);
        Assert.Equal("text/html", WorkersAiToMarkdownFile.OfHtml("page.html", "<p>words</p>").ContentType);
        Assert.Equal(
            "application/octet-stream",
            WorkersAiToMarkdownFile.OfBytes("file.pdf", new byte[] { 1, 2 }).ContentType);
        Assert.Equal(
            "application/pdf",
            WorkersAiToMarkdownFile.OfBytes("file.pdf", new byte[] { 1, 2 }, "application/pdf").ContentType);

        // A blank type falls back rather than going out blank, and text goes out as UTF-8.
        Assert.Equal(
            "application/octet-stream",
            new WorkersAiToMarkdownFile("notes.txt", new byte[] { 1 }, "   ").ContentType);
        Assert.Equal(
            "words",
            Encoding.UTF8.GetString(WorkersAiToMarkdownFile.OfText("notes.txt", "words").Content!));
        Assert.Equal("notes.txt (text/plain)", WorkersAiToMarkdownFile.OfText("notes.txt", "words").ToString());
    }
}
