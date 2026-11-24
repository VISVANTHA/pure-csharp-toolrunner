namespace ToolRunner.Tools;

/// <summary>
/// diff-cover. Class C: reads Cobertura/OpenCover XML. No coverage report can be
/// produced on this family, so it has nothing to read.
/// </summary>
public sealed class DiffCover : ITool
{
    public string Slug => "diff-cover";

    public int Run(ToolContext ctx)
    {
        ctx.Skip("Class C: reads Cobertura/OpenCover XML");

        // Below: kept so the runner is complete once a report exists.
        var diffCover = ctx.RequireCmd("diff-cover");

        // Reads this folder's sample Cobertura report. On every family of this corpus
        // no real report can be produced, which is why the verdict above is exit 3.
        var rc = ctx.Run(diffCover,
            [Path.Combine(ctx.ToolDir, "coverage.cobertura.xml"),
             "--json-report", ctx.Report("diff-cover.json")]);
        return ctx.AcceptFindings(rc) ? ExitCodes.Ok : ExitCodes.Fail;
    }
}
