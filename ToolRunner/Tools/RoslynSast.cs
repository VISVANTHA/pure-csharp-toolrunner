namespace ToolRunner.Tools;

/// <summary>
/// Roslyn SAST analyzers. The sheet says 'roslyn sast', also not a package. Resolved here
/// to SecurityCodeScan + Microsoft.CodeAnalysis.NetAnalyzers. NuGet unreachable.
/// </summary>
public sealed class RoslynSast : ITool
{
    public string Slug => "roslyn-sast";

    public int Run(ToolContext ctx)
    {
        ctx.Missing("The sheet says 'roslyn sast', also not a package");
        return ExitCodes.Ok;
    }
}
