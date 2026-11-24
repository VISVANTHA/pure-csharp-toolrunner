namespace ToolRunner.Tools;

/// <summary>
/// Trivy. Standalone Go binary, absent from this host. Class C: reads
/// manifests/lockfiles.
/// </summary>
public sealed class Trivy : ITool
{
    public string Slug => "trivy";

    public int Run(ToolContext ctx)
    {
        ctx.Missing("Standalone Go binary, absent from this host");

        // Below: kept so the runner is complete once trivy is installed.
        var trivy = ctx.RequireCmd("trivy");

        // Reads this folder's SBOM, not a project manifest: a manifest here would
        // make the platform treat the folder as its own project.
        var rc = ctx.Run(trivy,
            ["sbom", "--format", "json", "--output", ctx.Report("trivy.json"),
             Path.Combine(ctx.ToolDir, "sbom.cdx.json")]);
        return ctx.AcceptFindings(rc) ? ExitCodes.Ok : ExitCodes.Fail;
    }
}
