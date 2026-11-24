using System.Text.Json.Nodes;

namespace ToolRunner.Tools;

/// <summary>
/// jscpd. Node, own tokeniser. Class B.
///
/// jscpd's threshold is a FAILURE BUDGET, not a detection floor: at 0 it exits 1
/// the moment it finds anything, and this corpus plants a duplicate pair on
/// purpose. A correct run is not a failure.
///
/// The format token is "csharp", NOT "c#". Given "c#" jscpd prints a warning to
/// stderr, matches ZERO files, writes a valid-looking report saying clones: 0, and
/// EXITS 0 -- a platform grading by exit code grades that green. This runner
/// therefore asserts it scanned something: an unexplained zero is worse than a
/// failure, because nothing in the exit-code contract can see it.
/// </summary>
public sealed class Jscpd : ITool
{
    public string Slug => "jscpd";

    public int Run(ToolContext ctx)
    {
        var jscpd = ctx.RequireCmd("jscpd");
        ctx.FindingsCodes = [1];

        var output = ctx.Report("jscpd");
        var rc = ctx.Run(jscpd,
            [".", "--format", "csharp", "--min-tokens", "50",
             "--reporters", "json,console", "--output", output, "--threshold", "100"],
            workDir: ctx.ToolDir);
        if (!ctx.AcceptFindings(rc))
        {
            return ExitCodes.Fail;
        }

        return AssertNonzero(ctx, Path.Combine(output, "jscpd-report.json"));
    }

    /// <summary>
    /// Refuse a zero this runner cannot explain (the former assert_nonzero.py).
    ///
    /// A report saying <c>clones: 0</c> is indistinguishable from a genuinely clean
    /// repository unless something asserts the expected count -- the same shape as
    /// the TypeScript corpus's OpenTelemetry bootstrap, which registered a no-op
    /// processor, exported nothing and exited 0. dataset.json declares the expected
    /// floor; this enforces it.
    /// </summary>
    public static int AssertNonzero(ToolContext ctx, string reportPath)
    {
        var d = JsonNode.Parse(File.ReadAllText(reportPath))!;
        var total = d["statistics"]!["total"]!;
        if (total["sources"]!.GetValue<int>() == 0)
        {
            ctx.Reason("STRUCTURALLY ZERO: jscpd matched no files. Check the --format token: "
                       + "'c#' is not a supported format and yields exit 0 with an empty report. "
                       + "The correct token is 'csharp'.");
            return ExitCodes.Fail;
        }

        var clones = d["duplicates"]!.AsArray().Count;
        if (clones < 1)
        {
            ctx.Reason("STRUCTURALLY ZERO: the planted duplicate pair was not found, but "
                       + "dataset.json asserts jscpd.duplicates >= 1.");
            return ExitCodes.Fail;
        }

        ctx.Out.WriteLine($"jscpd: {total["sources"]} sources, {clones} clones, "
                          + $"{total["percentage"]!.GetValue<double>():F2}% duplicated");
        return ExitCodes.Ok;
    }
}
