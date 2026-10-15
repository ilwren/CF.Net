using System.Text;

namespace CFNet.TestKit;

/// <summary>What one line of an integration run report says happened.</summary>
public enum RunReportOutcome
{
    /// <summary>The test ended early because something it needs was not configured.</summary>
    Skipped,

    /// <summary>Anything else a human reading the CI summary should see.</summary>
    Info,
}

/// <summary>One entry of an integration run report.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Test">The test that reported it.</param>
/// <param name="Detail">Why, in one line.</param>
public sealed record RunReportEntry(RunReportOutcome Outcome, string Test, string Detail);

/// <summary>
/// An append-only log of what an integration run really did, so a CI summary can tell "every test
/// passed" apart from "every test had nothing to do".
/// </summary>
/// <remarks>
/// <para>
/// xunit v2 cannot skip a test at run time, so the integration suite ends a test early and prints
/// <c>SKIPPED: ...</c> instead. That keeps a run without credentials green rather than red, but the
/// reason ends up in per-test output nobody reads. When <c>CLOUDFLARE_IT_REPORT</c> names a file,
/// every skip is also appended there as one tab-separated line, and the workflow turns that file into
/// a table in its step summary.
/// </para>
/// <para>
/// Reporting is best effort in both directions. With the variable unset - the normal case at a
/// developer's terminal - nothing is written anywhere. A write that fails, because the directory is
/// read-only or the path is a folder, is swallowed: losing a report must never cost a test result.
/// </para>
/// </remarks>
public static class RunReport
{
    /// <summary>Environment variable holding the report file path.</summary>
    public const string PathVariable = "CLOUDFLARE_IT_REPORT";

    /// <summary>Separates the fields of one entry.</summary>
    public const char Separator = '\t';

    private static readonly object Gate = new();

    /// <summary>The report file this process appends to, or <see langword="null"/> when reporting is off.</summary>
    public static string? ReportPath
    {
        get
        {
            var path = Environment.GetEnvironmentVariable(PathVariable);
            return string.IsNullOrWhiteSpace(path) ? null : path.Trim();
        }
    }

    /// <summary>Appends one entry to the configured report, when there is one.</summary>
    /// <param name="outcome">What happened.</param>
    /// <param name="test">The test reporting it.</param>
    /// <param name="detail">Why, flattened onto one line.</param>
    public static void Record(RunReportOutcome outcome, string test, string detail)
    {
        var path = ReportPath;
        if (path is null)
        {
            return;
        }

        Record(path, new RunReportEntry(outcome, test, detail));
    }

    /// <summary>Appends one entry to a specific file, whatever the environment says.</summary>
    /// <param name="path">The file to append to.</param>
    /// <param name="entry">What to append.</param>
    public static void Record(string path, RunReportEntry entry)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(entry);

        try
        {
            // One process can run several test collections in parallel, so serialise the appends.
            lock (Gate)
            {
                File.AppendAllText(path, Format(entry) + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            // Deliberately ignored: see the remarks on this class.
        }
    }

    /// <summary>Flattens one entry into a single line.</summary>
    /// <param name="entry">The entry to render.</param>
    /// <returns>Three fields separated by <see cref="Separator"/>.</returns>
    public static string Format(RunReportEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return string.Join(
            Separator,
            entry.Outcome.ToString().ToUpperInvariant(),
            OneLine(entry.Test),
            OneLine(entry.Detail));
    }

    /// <summary>Parses report content. Blank lines and lines that are not well formed are dropped.</summary>
    /// <param name="content">The whole file, or <see langword="null"/>.</param>
    /// <returns>Every entry that could be read, in file order.</returns>
    public static IReadOnlyList<RunReportEntry> Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Array.Empty<RunReportEntry>();
        }

        var entries = new List<RunReportEntry>();
        foreach (var line in content.Split('\n'))
        {
            var fields = line.TrimEnd('\r').Split(Separator);
            if (fields.Length < 3 ||
                !Enum.TryParse<RunReportOutcome>(fields[0], ignoreCase: true, out var outcome))
            {
                continue;
            }

            entries.Add(new RunReportEntry(outcome, fields[1], fields[2]));
        }

        return entries;
    }

    /// <summary>Reads and parses a report file.</summary>
    /// <param name="path">The file to read; may be <see langword="null"/> or missing.</param>
    /// <returns>Every entry in it, or nothing when the file is absent or unreadable.</returns>
    public static IReadOnlyList<RunReportEntry> Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return Array.Empty<RunReportEntry>();
        }

        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return Array.Empty<RunReportEntry>();
        }
    }

    /// <summary>Removes the characters that would break the one-line-per-entry contract.</summary>
    /// <remarks>
    /// Every run of whitespace - a tab, a wrapped message's <c>\r\n</c>, a paragraph break - becomes
    /// a single space, and the ends are trimmed. A field can therefore never introduce a separator or
    /// a second line, and the same text always renders the same way.
    /// </remarks>
    /// <param name="value">The raw text.</param>
    /// <returns>The text collapsed onto one line.</returns>
    private static string OneLine(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                // A run of whitespace collapses to a single space, and leading whitespace is dropped
                // because nothing has been appended yet.
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        // Trailing whitespace never reaches the builder: a pending space is only written when a
        // further character follows it.
        return builder.ToString();
    }
}
