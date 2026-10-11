using CFNet.TestKit;
using Xunit;

namespace CFNet.UnitTests.Testing;

/// <summary>
/// The run report is what turns a green integration run into something a human can interpret, so its
/// file format is pinned down here - offline, where it can be tested properly.
/// </summary>
public class RunReportTests
{
    [Fact]
    public void Format_keeps_one_entry_on_one_line()
    {
        var line = RunReport.Format(new RunReportEntry(
            RunReportOutcome.Skipped,
            "ReadOnlyIntegrationTests.Lists_zones",
            "no Cloudflare credentials found\r\nset CLOUDFLARE_API_TOKEN"));

        Assert.Equal(
            "SKIPPED\tReadOnlyIntegrationTests.Lists_zones\tno Cloudflare credentials found set CLOUDFLARE_API_TOKEN",
            line);
        Assert.DoesNotContain('\n', line);
        Assert.Equal(3, line.Split(RunReport.Separator).Length);
    }

    [Fact]
    public void Parse_reads_back_what_Format_wrote()
    {
        var entries = new[]
        {
            new RunReportEntry(RunReportOutcome.Skipped, "Writes.Creates_a_bucket", "CLOUDFLARE_IT_ENABLE_WRITES is not 'true'."),
            new RunReportEntry(RunReportOutcome.Info, "Writes.Creates_a_bucket", "cleaned up"),
        };

        var content = string.Join(Environment.NewLine, entries.Select(RunReport.Format));
        var parsed = RunReport.Parse(content);

        Assert.Equal(entries, parsed);
    }

    [Fact]
    public void Parse_drops_blank_and_malformed_lines()
    {
        var content = string.Join(
            '\n',
            "SKIPPED\tOne.Test\treason",
            "",
            "   ",
            "SKIPPED\tOnlyTwoFields",
            "NOT_AN_OUTCOME\tTwo.Fields\treason",
            "info\tLower.Case\toutcomes are matched without regard to case",
            "trailing rubbish without separators");

        var parsed = RunReport.Parse(content);

        Assert.Equal(2, parsed.Count);
        Assert.Equal("One.Test", parsed[0].Test);
        Assert.Equal(RunReportOutcome.Info, parsed[1].Outcome);
    }

    [Fact]
    public void Parse_returns_nothing_for_empty_content()
    {
        Assert.Empty(RunReport.Parse(null));
        Assert.Empty(RunReport.Parse(string.Empty));
        Assert.Empty(RunReport.Parse("   "));
    }

    [Fact]
    public void Load_returns_nothing_for_a_missing_file()
    {
        Assert.Empty(RunReport.Load(null));
        Assert.Empty(RunReport.Load("   "));
        Assert.Empty(RunReport.Load(Path.Combine(Path.GetTempPath(), $"cfnet-does-not-exist-{Guid.NewGuid():N}.tsv")));
    }

    [Fact]
    public void Load_reads_a_file_that_Record_wrote()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cfnet-run-report-{Guid.NewGuid():N}.tsv");

        try
        {
            RunReport.Record(path, new RunReportEntry(RunReportOutcome.Skipped, "Reads.Lists_zones", "no credentials"));
            RunReport.Record(path, new RunReportEntry(RunReportOutcome.Skipped, "Reads.Lists_workers", "no account id"));

            var entries = RunReport.Load(path);

            Assert.Equal(2, entries.Count);
            Assert.Equal("no account id", entries[1].Detail);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Record_survives_a_path_it_cannot_write_to()
    {
        // A directory is not a file, and a report must never be able to fail a test.
        RunReport.Record(Path.GetTempPath(), new RunReportEntry(RunReportOutcome.Info, "Any.Test", "ignored"));
        RunReport.Record(Path.Combine(Path.GetTempPath(), "no-such-directory", "report.tsv"), new RunReportEntry(RunReportOutcome.Info, "Any.Test", "ignored"));
    }
}

/// <summary>
/// Reporting is driven by an environment variable, and mutating one is a process-wide side effect, so
/// these run in the collection that disables parallelism.
/// </summary>
[Collection("Environment")]
public sealed class RunReportEnvironmentTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"cfnet-run-report-env-{Guid.NewGuid():N}.tsv");
    private readonly string? _previous = Environment.GetEnvironmentVariable(RunReport.PathVariable);

    [Fact]
    public void Reporting_is_off_until_the_variable_names_a_file()
    {
        Environment.SetEnvironmentVariable(RunReport.PathVariable, null);

        Assert.Null(RunReport.ReportPath);

        RunReport.Record(RunReportOutcome.Skipped, "Any.Test", "written nowhere");

        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void Reporting_appends_to_the_file_the_variable_names()
    {
        Environment.SetEnvironmentVariable(RunReport.PathVariable, _path);

        Assert.Equal(_path, RunReport.ReportPath);

        RunReport.Record(RunReportOutcome.Skipped, "Reads.Lists_zones", "no Cloudflare credentials found");
        RunReport.Record(RunReportOutcome.Skipped, "Writes.Creates_a_worker", "CLOUDFLARE_IT_ENABLE_WRITES is not 'true'.");

        var entries = RunReport.Load(_path);

        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry => Assert.Equal(RunReportOutcome.Skipped, entry.Outcome));
        Assert.Equal("Reads.Lists_zones", entries[0].Test);
    }

    [Fact]
    public void A_blank_variable_value_counts_as_off()
    {
        Environment.SetEnvironmentVariable(RunReport.PathVariable, "   ");

        Assert.Null(RunReport.ReportPath);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(RunReport.PathVariable, _previous);
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
