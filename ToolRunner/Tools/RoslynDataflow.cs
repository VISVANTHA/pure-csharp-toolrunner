namespace ToolRunner.Tools;

/// <summary>
/// Roslyn SemanticModel.AnalyzeDataFlow. PROPOSED REPAIR, not in the source sheet. The
/// sheet assigns Data Flow to Stryker.NET, a mutation tester that computes no def-use
/// chains. AnalyzeDataFlow is a real Roslyn API that ships in the SDK and yields genuine
/// def/use sets.
/// </summary>
public sealed class RoslynDataflow : ITool
{
    public string Slug => "roslyn-dataflow";

    public int Run(ToolContext ctx)
    {
        ctx.Missing("PROPOSED REPAIR, not in the source sheet");
        return ExitCodes.Ok;
    }
}
