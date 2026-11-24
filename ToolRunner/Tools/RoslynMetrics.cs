namespace ToolRunner.Tools;

/// <summary>
/// Roslyn analyzers (cognitive complexity). The sheet says 'Roslyn', which is a compiler
/// platform, not a package. Resolved here to SonarAnalyzer.CSharp (rule S3776 is
/// cognitive complexity). Analyzer packages come from NuGet, which is refused at this
/// host's egress proxy.
/// </summary>
public sealed class RoslynMetrics : ITool
{
    public string Slug => "roslyn-metrics";

    public int Run(ToolContext ctx)
    {
        ctx.Missing("The sheet says 'Roslyn', which is a compiler platform, not a package");
        return ExitCodes.Ok;
    }
}
