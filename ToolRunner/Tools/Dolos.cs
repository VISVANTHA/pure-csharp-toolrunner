namespace ToolRunner.Tools;

/// <summary>
/// Dolos. Node, tree-sitter C# grammar. Class B, and the sheet's declaration
/// ('C# language-version independent - source similarity, not tied to compiler') is
/// correct. MEASURED, not assumed: @dodona/dolos 2.9.3 fails to install on this host
/// because @dodona/dolos-parsers 1.4.1 needs node-gyp native compilation, which fails
/// under node 22.22.2. That is exit 4 (absent from this host), not exit 3 (cannot run
/// on this family) -- collapsing the two would let a missing binary masquerade as a
/// .NET Framework result.
/// </summary>
public sealed class Dolos : ITool
{
    public string Slug => "dolos";

    public int Run(ToolContext ctx)
    {
        ctx.Missing("Node, tree-sitter C# grammar");

        // Below: kept so the runner is complete once dolos installs.
        var dolos = ctx.RequireCmd("dolos");
        var sources = Directory.GetFiles(ctx.ToolDir, "*.cs").Order(StringComparer.Ordinal);
        var rc = ctx.Run(dolos,
            ["run", "-f", "csv", "-o", ctx.Report("dolos"), "-l", "c-sharp", .. sources]);
        return ctx.AcceptFindings(rc) ? ExitCodes.Ok : ExitCodes.Fail;
    }
}
