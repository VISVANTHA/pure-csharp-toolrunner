namespace ToolRunner.Tools;

/// <summary>
/// Semgrep. Python, tree-sitter C# grammar. Class B.
///
/// Rules live in this folder's rules.json (Semgrep reads JSON rule files as well as
/// YAML). Rule orderkit-tainted-query was CORRECTED 2026-10-08. The previous form was
///   pattern: string.Format("SELECT $...ANY", $...ARGS)
/// which never matched: metavariables are not interpolated inside a string literal,
/// so it required a string whose text is literally `SELECT $...ANY`. The format
/// string is captured and tested with a regex instead.
/// </summary>
public sealed class Semgrep : ITool
{
    public string Slug => "semgrep";

    public int Run(ToolContext ctx)
    {
        var semgrep = ctx.RequireCmd("semgrep");
        ctx.FindingsCodes = [1]; // semgrep exits 1 when rules match; that is a successful run
        var rc = ctx.Run(semgrep,
            ["--config", Path.Combine(ctx.ToolDir, "rules.json"),
             "--json", "--output", ctx.Report("semgrep.json"), ctx.ToolDir]);
        return ctx.AcceptFindings(rc) ? ExitCodes.Ok : ExitCodes.Fail;
    }
}
