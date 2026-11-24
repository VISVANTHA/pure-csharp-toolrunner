namespace ToolRunner.Tools;

/// <summary>
/// One runner per folder of the tool data. <see cref="Run"/> returns a contract
/// exit code (<see cref="ExitCodes"/>), or ends early through
/// <see cref="ToolContext.Skip"/> / <see cref="ToolContext.Missing"/>.
/// </summary>
public interface ITool
{
    /// <summary>The tool data folder this runner belongs to, as dataset.json names it.</summary>
    string Slug { get; }

    int Run(ToolContext ctx);
}
