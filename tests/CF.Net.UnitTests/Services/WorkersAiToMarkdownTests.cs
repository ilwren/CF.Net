using System.Net.Http.Headers;
using System.Text;
using CFNet.Exceptions;
using CFNet.Http;
using CFNet.Models.WorkersAi;
using CFNet.Serialization;
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
        // reads a file from those two, since nothing in a multipart part says what it is. The quotes are taken
        // out before counting, because .NET writes a header parameter bare when its value needs no quoting and
        // this library quotes them anyway - Cloudflare's AI endpoints refuse a bare one with 400 and error
        // 6005, so what matters here is the value rather than the punctuation around it.
        var body = harness.LastRequest.Body!.Replace("\"", string.Empty);
        Assert.Equal(2, body.Split("name=files").Length - 1);
        Assert.Contains("filename=page.html", body, StringComparison.Ordinal);
        Assert.Contains("filename=notes.txt", body, StringComparison.Ordinal);
        Assert.Contains("text/html", body, StringComparison.Ordinal);
        Assert.Contains("text/plain", body, StringComparison.Ordinal);
        Assert.Contains("<h1>CF.Net</h1>", body, StringComparison.Ordinal);
        Assert.Contains("plain words", body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_multipart_part_goes_out_with_its_name_and_file_name_quoted()
    {
        // The one thing about a multipart body that Cloudflare's AI endpoints insist on, pinned on its own: a
        // live POST …/ai/tomarkdown with the names .NET writes bare answered 400 with error 6005, "Request
        // body is not valid multipart/form-data".
        var body = new MultipartRequestBody()
            .AddFile("files", Encoding.UTF8.GetBytes("<p>x</p>"), "text/html", "page.html")
            .AddField("conversionOptions", "{}", "application/json");

        using var form = (MultipartFormDataContent)body.CreateContent(CloudflareJson.Default);
        var written = form.ReadAsStringAsync().GetAwaiter().GetResult();

        Assert.Contains("name=\"files\"", written, StringComparison.Ordinal);
        Assert.Contains("filename=\"page.html\"", written, StringComparison.Ordinal);

        // A plain field is quoted too, and carries no file name at all.
        Assert.Contains("name=\"conversionOptions\"", written, StringComparison.Ordinal);
        Assert.DoesNotContain("filename=\"conversionOptions\"", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConvertToMarkdownAsync_reads_the_markdown_and_the_token_count_Cloudflare_answers()
    {
        using var harness = CloudflareHarness.Create();

        // Two files in one answer, the first exactly as Cloudflare's REST documentation writes one - tokens as
        // the number 49, format "markdown" for what came out rather than for what went in - and the second as
        // its own generated client types the same field, tokens as a string. Both read.
        harness.RespondWithResult(
            """[{"id":"1f0a2b3c-4d5e-6f70-8192-a3b4c5d6e7f8","name":"good.html","mimeType":"text/html","format":"markdown","tokens":49,"data":"# Image Embedded with a Data URI\n\nThis _image_ is directly encoded in the HTML."},{"id":"2f0a2b3c-4d5e-6f70-8192-a3b4c5d6e7f8","name":"notes.txt","mimeType":"text/plain","format":"markdown","tokens":"42","data":"plain words"}]""");

        var conversions = await harness.Client.WorkersAi.ConvertToMarkdownAsync(
            new[]
            {
                WorkersAiToMarkdownFile.OfHtml("good.html", "<h1>CF.Net</h1>"),
                WorkersAiToMarkdownFile.OfText("notes.txt", "plain words"),
            });

        Assert.Equal(2, conversions.Count);

        var converted = conversions[0];
        Assert.Equal("1f0a2b3c-4d5e-6f70-8192-a3b4c5d6e7f8", converted.Id);
        Assert.Equal("good.html", converted.Name);
        Assert.Equal("text/html", converted.MimeType);
        Assert.Equal("markdown", converted.Format);
        Assert.Equal("# Image Embedded with a Data URI\n\nThis _image_ is directly encoded in the HTML.", converted.Data);
        Assert.Equal(49, converted.TokenCount);
        Assert.True(converted.Succeeded);
        Assert.Equal("good.html (markdown)", converted.ToString());
        Assert.True(converted.AdditionalData is not { Count: > 0 });

        // The same field as a string, which is how the generated client types it.
        Assert.Equal(42, conversions[1].TokenCount);
        Assert.Null(new WorkersAiToMarkdownConversion().TokenCount);
    }

    [Fact]
    public async Task A_file_that_did_not_convert_is_answered_inside_a_successful_call()
    {
        using var harness = CloudflareHarness.Create();

        // One of two files failed and the call still answered 200: the failure is per file, in the answer, so
        // it is a field to read rather than an exception to catch.
        harness.RespondWithResult(
            """[{"id":"3f0a2b3c-4d5e-6f70-8192-a3b4c5d6e7f8","name":"good.html","mimeType":"text/html","format":"markdown","tokens":7,"data":"# Good"},{"id":"4f0a2b3c-4d5e-6f70-8192-a3b4c5d6e7f8","name":"bad.pdf","mimeType":"application/pdf","format":"error","error":"Some error that prevented this image from being converted"}]""");

        var conversions = await harness.Client.WorkersAi.ConvertToMarkdownAsync(
            new[]
            {
                WorkersAiToMarkdownFile.OfHtml("good.html", "<h1>Good</h1>"),
                WorkersAiToMarkdownFile.OfBytes("bad.pdf", new byte[] { 1, 2 }, "application/pdf"),
            });

        Assert.Equal(2, conversions.Count);
        Assert.True(conversions[0].Succeeded);

        var failed = conversions[1];
        Assert.False(failed.Succeeded);
        Assert.Equal("error", failed.Format);
        Assert.Null(failed.Data);
        Assert.Equal("Some error that prevented this image from being converted", failed.Error);
        Assert.Null(failed.TokenCount);
        Assert.Equal(
            "bad.pdf (error: Some error that prevented this image from being converted)",
            failed.ToString());
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
