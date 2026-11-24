using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ToolRunner.Tools;

/// <summary>
/// Lizard. Python tokeniser. Class B: own parser, never loads a compiler, so the
/// TFM is irrelevant to it.
/// </summary>
public sealed partial class Lizard : ITool
{
    public string Slug => "lizard";

    public int Run(ToolContext ctx)
    {
        var lizard = ctx.RequireCmd("lizard");
        ctx.FindingsCodes = [1];

        var rc = ctx.RunToFile(lizard, ["-l", "csharp", "--xml", ctx.ToolDir],
                               ctx.Report("lizard.xml"), quietStderr: true);
        ctx.RunToFile(lizard, ["-l", "csharp", ctx.ToolDir], ctx.Report("lizard.txt"), quietStderr: true);
        if (!ctx.AcceptFindings(rc))
        {
            return ExitCodes.Fail;
        }

        var txt = File.ReadAllText(ctx.Report("lizard.txt"));
        var functions = FunctionRow().Matches(txt);
        if (functions.Count == 0)
        {
            ctx.Reason("STRUCTURALLY ZERO: lizard counted no functions. '-l csharp' is required; "
                       + "without it lizard guesses by extension and reports an empty analysis "
                       + "at exit 0.");
            return ExitCodes.Fail;
        }

        var peak = functions
            .Select(m => (Ccn: int.Parse(m.Groups[2].Value), Name: m.Groups[6].Value))
            .OrderByDescending(f => f.Ccn)
            .ThenByDescending(f => f.Name, StringComparer.Ordinal)
            .First();

        JsonOut.Write(ctx.Report("lizard.json"), new JsonObject
        {
            ["tool"] = "lizard",
            ["functions"] = functions.Count,
            ["max_ccn"] = peak.Ccn,
        });
        ctx.Out.WriteLine($"lizard: {functions.Count} functions, peak CCN {peak.Ccn}");
        return ExitCodes.Ok;
    }

    // NLOC CCN token PARAM length location@...
    [GeneratedRegex(@"^\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+(\S+)@", RegexOptions.Multiline)]
    private static partial Regex FunctionRow();
}
