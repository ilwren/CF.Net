using System.Net;
using CFNet.Http;
using CFNet.Models.Common;
using CFNet.Models.Zones;
using CFNet.Serialization;
using Xunit;

namespace CFNet.UnitTests.Http;

public class QueryStringBuilderTests
{
    [Fact]
    public void An_empty_builder_renders_nothing()
    {
        var builder = new QueryStringBuilder();

        Assert.True(builder.IsEmpty);
        Assert.Equal(0, builder.Count);
        Assert.Equal(string.Empty, builder.ToString());
    }

    [Fact]
    public void Null_values_are_skipped_but_empty_strings_are_kept()
    {
        var builder = new QueryStringBuilder()
            .Add("a", (string?)null)
            .Add("b", string.Empty)
            .Add("c", "value");

        Assert.Equal("b=&c=value", builder.ToString());
    }

    [Fact]
    public void AddIfNotEmpty_skips_whitespace_and_trims()
    {
        var builder = new QueryStringBuilder()
            .AddIfNotEmpty("a", null)
            .AddIfNotEmpty("b", "   ")
            .AddIfNotEmpty("c", "  value  ");

        Assert.Equal("c=value", builder.ToString());
    }

    [Fact]
    public void Numeric_and_boolean_overloads_use_invariant_culture()
    {
        var builder = new QueryStringBuilder()
            .Add("page", (int?)2)
            .Add("per_page", (int?)null)
            .Add("size", (long?)12345678901L)
            .Add("proxied", (bool?)true)
            .Add("paused", (bool?)false)
            .Add("unset", (bool?)null);

        Assert.Equal("page=2&size=12345678901&proxied=true&paused=false", builder.ToString());
    }

    [Fact]
    public void Values_are_percent_encoded_including_slashes()
    {
        // WebUtility.UrlEncode would leave the slash alone, which would change which KV key is addressed.
        var builder = new QueryStringBuilder()
            .Add("prefix", "users/42/profile")
            .Add("name", "a b&c=d")
            .Add("weird", "tilde~plus+quote'é");

        var rendered = builder.ToString();

        Assert.Equal(
            "prefix=users%2F42%2Fprofile&name=a%20b%26c%3Dd&weird=tilde~plus%2Bquote%27%C3%A9",
            rendered);
    }

    [Fact]
    public void Names_keep_the_dots_and_brackets_cloudflare_uses()
    {
        var builder = new QueryStringBuilder()
            .Add("account.id", "abc")
            .Add("content[0]", "x");

        Assert.Equal("account.id=abc&content[0]=x", builder.ToString());
    }

    [Fact]
    public void Duplicate_names_are_preserved_in_order()
    {
        var builder = new QueryStringBuilder()
            .Add("tag", "a")
            .Add("tag", "b")
            .Add("order", "name");

        Assert.Equal("tag=a&tag=b&order=name", builder.ToString());
        Assert.Equal(3, builder.Count);
        Assert.Equal(new[] { "tag", "tag", "order" }, builder.Items.Select(item => item.Key));
    }

    [Fact]
    public void AddMany_expands_a_sequence_under_one_name()
    {
        var builder = new QueryStringBuilder().AddMany("tag", new[] { "one", "two" });
        var withNull = new QueryStringBuilder().AddMany("tag", null);

        Assert.Equal("tag=one&tag=two", builder.ToString());
        Assert.True(withNull.IsEmpty);
    }

    [Fact]
    public void Timestamps_are_rendered_in_utc_without_fractional_seconds()
    {
        var moment = new DateTimeOffset(2024, 3, 15, 13, 45, 30, TimeSpan.FromHours(2));

        var builder = new QueryStringBuilder()
            .Add("since", moment)
            .Add("until", moment, "yyyy-MM-dd")
            .Add("never", (DateTimeOffset?)null);

        // Values go through Uri.EscapeDataString, which escapes ':' - harmless to Cloudflare, and
        // the same reason an "owner:web" filter arrives as owner%3Aweb.
        Assert.Equal("since=2024-03-15T11%3A45%3A30Z&until=2024-03-15", builder.ToString());
    }

    [Fact]
    public void Builders_are_chainable_and_expose_their_items()
    {
        var builder = new QueryStringBuilder();

        Assert.Same(builder, builder.Add("a", "1"));
        Assert.Same(builder, builder.AddIfNotEmpty("b", "2"));
        Assert.Same(builder, builder.AddMany("c", new[] { "3" }));
        Assert.Equal(3, builder.Items.Count);
    }
}

public class CloudflareUrlTests
{
    private static readonly Uri Base = new("https://api.cloudflare.com/client/v4/");

    [Fact]
    public void EncodeSegment_escapes_reserved_characters()
    {
        Assert.Equal("users%2F42%2Fprofile", CloudflareUrl.EncodeSegment("users/42/profile"));
        Assert.Equal("plain", CloudflareUrl.EncodeSegment("plain"));
        Assert.Equal("a%20b", CloudflareUrl.EncodeSegment("a b"));
        Assert.Equal(string.Empty, CloudflareUrl.EncodeSegment(string.Empty));
        Assert.Equal("%23hash", CloudflareUrl.EncodeSegment("#hash"));
        Assert.Equal("caf%C3%A9", CloudflareUrl.EncodeSegment("café"));
    }

    [Fact]
    public void EncodePath_keeps_slashes_as_separators()
    {
        Assert.Equal(
            "accounts/acct-1/storage/kv/namespaces/ns-1",
            CloudflareUrl.EncodePath("accounts/acct-1/storage/kv/namespaces/ns-1"));

        Assert.Equal(
            "a/b%20c/d",
            CloudflareUrl.EncodePath("a/b c/d"));
    }

    [Fact]
    public void Build_joins_base_path_and_query()
    {
        var uri = CloudflareUrl.Build(Base, "zones", "page=1&per_page=20");

        Assert.Equal("https://api.cloudflare.com/client/v4/zones?page=1&per_page=20", uri.OriginalString);
    }

    [Fact]
    public void Build_omits_the_question_mark_without_a_query()
    {
        Assert.Equal("https://api.cloudflare.com/client/v4/zones", CloudflareUrl.Build(Base, "zones").OriginalString);
        Assert.Equal("https://api.cloudflare.com/client/v4/zones", CloudflareUrl.Build(Base, "zones", string.Empty).OriginalString);
    }

    [Theory]
    [InlineData("zones", "https://api.cloudflare.com/client/v4/zones")]
    [InlineData("/zones", "https://api.cloudflare.com/client/v4/zones")]
    [InlineData("zones/", "https://api.cloudflare.com/client/v4/zones")]
    [InlineData("/zones/", "https://api.cloudflare.com/client/v4/zones")]
    public void Build_tolerates_redundant_slashes(string path, string expected)
    {
        Assert.Equal(expected, CloudflareUrl.Build(Base, path).OriginalString);
    }

    [Fact]
    public void Build_works_with_a_base_address_that_has_no_trailing_slash()
    {
        var uri = CloudflareUrl.Build(new Uri("https://api.cloudflare.com/client/v4"), "zones");

        Assert.Equal("https://api.cloudflare.com/client/v4/zones", uri.OriginalString);
    }

    [Fact]
    public void Build_preserves_an_encoded_slash_inside_a_path_segment()
    {
        // This is the KV key case: users/42/profile must reach Cloudflare as one segment.
        var uri = CloudflareUrl.Build(Base, "accounts/acct-1/storage/kv/namespaces/ns-1/values/" + CloudflareUrl.EncodeSegment("users/42/profile"));

        Assert.Contains("values/users%2F42%2Fprofile", uri.OriginalString, StringComparison.Ordinal);
        Assert.Contains("values/users%2F42%2Fprofile", uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains("values/users%2F42%2Fprofile", uri.PathAndQuery, StringComparison.Ordinal);

        // If canonicalisation were enabled the Uri would silently rewrite %2F back to /.
        Assert.DoesNotContain("values/users/42/profile", uri.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_preserves_encoded_characters_in_the_query()
    {
        var uri = CloudflareUrl.Build(Base, "zones/z-1/dns_records", "name=www.example.com&content=1.2.3.4&type=A");

        Assert.Equal("name=www.example.com&content=1.2.3.4&type=A", uri.Query.TrimStart('?'));
    }

    [Fact]
    public void Build_rejects_a_relative_base_address()
    {
        Assert.Throws<ArgumentException>(() => CloudflareUrl.Build(new Uri("client/v4", UriKind.Relative), "zones"));
        Assert.Throws<ArgumentNullException>(() => CloudflareUrl.Build(null!, "zones"));
        Assert.Throws<ArgumentNullException>(() => CloudflareUrl.Build(Base, null!));
    }

    [Fact]
    public void EncodeSegment_rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() => CloudflareUrl.EncodeSegment(null!));
        Assert.Throws<ArgumentNullException>(() => CloudflareUrl.EncodePath(null!));
    }

    [Fact]
    public void Format_helpers_are_culture_invariant()
    {
        Assert.Equal("42", CloudflareUrl.Format(42));
        Assert.Equal("12345678901", CloudflareUrl.Format(12345678901L));
        Assert.Equal("true", CloudflareUrl.Format(true));
        Assert.Equal("false", CloudflareUrl.Format(false));
        Assert.Equal(
            "2024-03-15T11:45:30Z",
            CloudflareUrl.Format(new DateTimeOffset(2024, 3, 15, 13, 45, 30, TimeSpan.FromHours(2))));
    }
}

public class CloudflareRequestTests
{
    [Fact]
    public void Factories_set_the_right_verb_and_body_shape()
    {
        var get = CloudflareRequest.Get("zones");
        var post = CloudflareRequest.Post("zones", new { name = "example.com" });
        var put = CloudflareRequest.Put("zones/z-1", new { paused = true });
        var patch = CloudflareRequest.Patch("zones/z-1", new { paused = false });
        var delete = CloudflareRequest.Delete("zones/z-1");

        Assert.Equal(HttpMethod.Get, get.Method);
        Assert.Equal(HttpMethod.Post, post.Method);
        Assert.Equal(HttpMethod.Put, put.Method);
        Assert.Equal(HttpMethod.Patch, patch.Method);
        Assert.Equal(HttpMethod.Delete, delete.Method);

        Assert.Null(get.Body);
        Assert.Null(delete.Body);
        Assert.IsType<JsonRequestBody>(post.Body);
        Assert.IsType<JsonRequestBody>(put.Body);
        Assert.IsType<JsonRequestBody>(patch.Body);
    }

    [Fact]
    public void Post_without_a_payload_has_no_body()
    {
        Assert.Null(CloudflareRequest.Post("zones/z-1/activation_check").Body);
    }

    [Fact]
    public void Tolerate_and_WithHeader_are_fluent()
    {
        var request = CloudflareRequest.Get("zones/z-1")
            .Tolerate(HttpStatusCode.NotFound, HttpStatusCode.Forbidden)
            .WithHeader("cf-r2-jurisdiction", "eu");

        Assert.Contains(HttpStatusCode.NotFound, request.ToleratedStatusCodes);
        Assert.Contains(HttpStatusCode.Forbidden, request.ToleratedStatusCodes);
        Assert.Equal("eu", request.Headers["CF-R2-Jurisdiction"]);
        Assert.Empty(CloudflareRequest.Get("zones").ToleratedStatusCodes);
    }

    [Fact]
    public void ToString_shows_the_operation_name_and_query()
    {
        var request = CloudflareRequest.Get("zones");
        request.OperationName = "List Zones";
        request.Query.Add("page", 2);

        Assert.Equal("List Zones zones?page=2", request.ToString());
        Assert.Equal("GET zones", CloudflareRequest.Get("zones").ToString());
    }

    [Fact]
    public void Constructor_rejects_nulls()
    {
        Assert.Throws<ArgumentNullException>(() => new CloudflareRequest(null!, "zones"));
        Assert.Throws<ArgumentNullException>(() => new CloudflareRequest(HttpMethod.Get, null!));
    }
}

public class CloudflareRequestBodyTests
{
    [Fact]
    public async Task Json_body_renders_the_runtime_type_in_snake_case()
    {
        var body = new JsonRequestBody(new { ZoneName = "example.com", PerPage = 20 });

        Assert.Equal("{\"zone_name\":\"example.com\",\"per_page\":20}", body.ToJson(CloudflareJson.Default));

        using var content = body.CreateContent(CloudflareJson.Default);
        var text = await content.ReadAsStringAsync();

        Assert.Equal("{\"zone_name\":\"example.com\",\"per_page\":20}", text);
        Assert.StartsWith("application/json", content.Headers.ContentType!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Text_body_is_written_verbatim_with_its_media_type()
    {
        using var content = new TextRequestBody("hello", "text/plain").CreateContent(CloudflareJson.Default);

        Assert.Equal("hello", await content.ReadAsStringAsync());
        Assert.Equal("text/plain", content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Binary_body_preserves_every_byte()
    {
        var payload = new byte[] { 0, 1, 2, 250, 255 };

        using var content = new BinaryRequestBody(payload, "application/octet-stream").CreateContent(CloudflareJson.Default);
        var read = await content.ReadAsByteArrayAsync();

        Assert.Equal(payload, read);
        Assert.Equal("application/octet-stream", content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Binary_body_of_null_becomes_empty()
    {
        using var content = new BinaryRequestBody(null!).CreateContent(CloudflareJson.Default);

        Assert.Empty(await content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Multipart_body_writes_the_metadata_field_and_every_module_as_a_file()
    {
        var body = new MultipartRequestBody()
            .AddJson("metadata", new { MainModule = "worker.mjs", CompatibilityDate = "2025-01-01" })
            .AddFile("worker.mjs", "export default { fetch() {} }"u8.ToArray(), "application/javascript+module");

        using var content = body.CreateContent(CloudflareJson.Default);
        var text = await content.ReadAsStringAsync();

        Assert.Equal("multipart/form-data", content.Headers.ContentType!.MediaType);

        // The metadata part is a plain form field: no filename, JSON media type.
        Assert.Contains("name=metadata", text, StringComparison.Ordinal);
        Assert.Contains("application/json", text, StringComparison.Ordinal);
        Assert.Contains("\"main_module\":\"worker.mjs\"", text, StringComparison.Ordinal);

        // Each module is a file part named after the module, with the Workers module media type.
        // A plain string field instead of a file part is what makes Cloudflare answer
        // "10021 Main module name is not present in bundle", so the filename matters.
        Assert.Contains("name=worker.mjs", text, StringComparison.Ordinal);
        Assert.Contains("filename", text, StringComparison.Ordinal);
        Assert.Contains("application/javascript+module", text, StringComparison.Ordinal);
        Assert.Contains("export default { fetch() {} }", text, StringComparison.Ordinal);

        // Metadata first, then the modules: the legacy "script" flow depends on that ordering.
        Assert.True(text.IndexOf("name=metadata", StringComparison.Ordinal) < text.IndexOf("name=worker.mjs", StringComparison.Ordinal));
    }

    [Fact]
    public void Multipart_body_can_be_built_from_existing_parts()
    {
        var parts = new[] { new MultipartPart("metadata") { Json = new { } }, new MultipartPart("worker.mjs") { Bytes = new byte[] { 1 } } };

        var body = new MultipartRequestBody(parts);

        Assert.Equal(2, body.Parts.Count);
        Assert.Equal("metadata", body.Parts[0].Name);
        Assert.Null(body.Parts[0].FileName);
        Assert.Equal("application/octet-stream", body.Parts[1].ContentType);
    }

    [Fact]
    public void Multipart_body_of_null_parts_is_empty()
    {
        Assert.Empty(new MultipartRequestBody(null!).Parts);
    }

    [Fact]
    public async Task AddField_writes_a_form_field_with_no_filename()
    {
        // The DNS import endpoint takes ordinary form fields next to the uploaded zone file.
        var body = new MultipartRequestBody()
            .AddFile("file", "a.test. 300 IN A 198.51.100.1"u8.ToArray(), "text/plain", "zone.txt")
            .AddField("proxied", "false");

        using var content = body.CreateContent(CloudflareJson.Default);
        var text = await content.ReadAsStringAsync();

        Assert.Contains("name=file; filename=zone.txt", text, StringComparison.Ordinal);
        Assert.Contains("name=proxied", text, StringComparison.Ordinal);
        Assert.Contains("false", text, StringComparison.Ordinal);

        // Exactly one filename in the whole body: the plain field must not carry one.
        Assert.Equal(1, CountOccurrences(text, "filename="));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }
}

public class CloudflarePageTests
{
    private static CloudflareResultInfo Info(int? page, int? totalPages, int? totalCount, string? cursor = null, string? cursorsAfter = null) =>
        new()
        {
            Page = page,
            TotalPages = totalPages,
            TotalCount = totalCount,
            Cursor = cursor,
            Cursors = cursorsAfter is null ? null : new CloudflareCursors { After = cursorsAfter },
        };

    [Fact]
    public void An_empty_page_is_safe_to_enumerate()
    {
        var page = new CloudflarePage<Zone>();

        Assert.Empty(page.Items);
        Assert.Equal(0, page.Count);
        Assert.Null(page.Info);
        Assert.Null(page.TotalCount);
        Assert.Null(page.Page);
        Assert.Null(page.PerPage);
        Assert.False(page.HasMore);
        Assert.Null(page.NextCursor);
        Assert.Null(page.NextPage);
        Assert.Equal("Page of 0 item(s) (last page)", page.ToString());
    }

    [Fact]
    public void Constructor_tolerates_null_items()
    {
        var page = new CloudflarePage<Zone>(null!, Info(1, 1, 0));

        Assert.Empty(page.Items);
    }

    [Fact]
    public void Page_number_pagination_advances_until_the_last_page()
    {
        var first = new CloudflarePage<Zone>(new[] { new Zone(), new Zone() }, Info(1, 3, 5));
        var middle = new CloudflarePage<Zone>(new[] { new Zone() }, Info(2, 3, 5));
        var last = new CloudflarePage<Zone>(new[] { new Zone() }, Info(3, 3, 5));

        Assert.True(first.HasMore);
        Assert.Equal(2, first.NextPage);
        Assert.Equal(5, first.TotalCount);
        Assert.Equal(1, first.Page);

        Assert.True(middle.HasMore);
        Assert.Equal(3, middle.NextPage);

        Assert.False(last.HasMore);
        Assert.Null(last.NextPage);
        Assert.Equal("Page of 1 item(s) out of 5 (last page)", last.ToString());
    }

    [Fact]
    public void Cursor_pagination_exposes_the_plain_cursor()
    {
        var page = new CloudflarePage<Zone>(new[] { new Zone() }, Info(null, null, null, cursor: "abc123"));

        Assert.True(page.HasMore);
        Assert.Equal("abc123", page.NextCursor);
        Assert.Null(page.NextPage);
    }

    [Fact]
    public void Cursor_pagination_prefers_the_after_cursor_of_the_pair()
    {
        var page = new CloudflarePage<Zone>(new[] { new Zone() }, Info(null, null, null, cursor: "ignored", cursorsAfter: "next"));

        Assert.Equal("next", page.NextCursor);
    }

    [Fact]
    public void An_empty_after_cursor_ends_the_sequence()
    {
        var page = new CloudflarePage<Zone>(
            new[] { new Zone() },
            new CloudflareResultInfo { Cursors = new CloudflareCursors { Before = "prev", After = string.Empty } });

        Assert.False(page.HasMore);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public void Result_info_without_any_pagination_field_reports_no_more_pages()
    {
        var page = new CloudflarePage<Zone>(new[] { new Zone() }, new CloudflareResultInfo());

        Assert.False(page.HasMore);
        Assert.Null(page.NextCursor);
        Assert.Null(page.NextPage);
    }

    [Fact]
    public void ToPage_copies_result_and_metadata()
    {
        var response = new CloudflareResponse<List<Zone>>
        {
            Success = true,
            Result = new List<Zone> { new() { Name = "a.test" } },
            ResultInfo = Info(1, 2, 3),
        };

        var page = response.ToPage();

        Assert.Single(page.Items);
        Assert.Equal("a.test", page.Items[0].Name);
        Assert.Equal(3, page.TotalCount);
        Assert.True(page.HasMore);
    }

    [Fact]
    public void ToPage_of_a_null_result_yields_an_empty_page()
    {
        var page = new CloudflareResponse<List<Zone>> { Success = true, Result = null }.ToPage();

        Assert.Empty(page.Items);
        Assert.False(page.HasMore);
    }

    [Fact]
    public void ToPage_with_a_selector_unwraps_a_named_array()
    {
        // R2 returns { "buckets": [...] } rather than a bare array.
        var response = new CloudflareResponse<Wrapper>
        {
            Success = true,
            Result = new Wrapper { Buckets = new List<Zone> { new() { Name = "x" } } },
            ResultInfo = Info(null, null, null, cursor: "next"),
        };

        var page = response.ToPage((Wrapper? result) => (IReadOnlyList<Zone>)(result?.Buckets ?? new List<Zone>()));

        Assert.Single(page.Items);
        Assert.Equal("next", page.NextCursor);
    }

    [Fact]
    public void FromResponse_matches_ToPage()
    {
        var response = new CloudflareResponse<List<Zone>> { Result = new List<Zone> { new() } };

        Assert.Equal(CloudflarePage<Zone>.FromResponse(response).Count, response.ToPage().Count);
    }

    private sealed class Wrapper
    {
        public List<Zone>? Buckets { get; set; }
    }
}
