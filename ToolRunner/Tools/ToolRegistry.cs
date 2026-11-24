namespace ToolRunner.Tools;

/// <summary>Every runner, keyed by slug.</summary>
public static class ToolRegistry
{
    public static readonly IReadOnlyList<ITool> All =
    [
        new Lizard(),
        new Jscpd(),
        new Dolos(),
        new Semgrep(),
        new Opengrep(),
        new Trivy(),
        new PyDriller(),
        new GitChurn(),
        new DiffCover(),
        new RoslynMetrics(),
        new RoslynSast(),
        new RoslynDataflow(),
        new NugetAudit(),
        new Coverlet(),
        new AltCover(),
        new MiniCover(),
        new Stryker(),
        new OpenTelemetry(),
        new Unilyze(),
    ];

    public static ITool? Find(string slug) =>
        All.FirstOrDefault(t => string.Equals(t.Slug, slug, StringComparison.Ordinal));

    /// <summary>The source file that implements a runner, relative to the repository root.</summary>
    public static string SourceFile(ITool tool) => "ToolRunner/Tools/" + tool.GetType().Name + ".cs";
}
