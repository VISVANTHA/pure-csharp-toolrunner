namespace ToolRunner.Tools;

/// <summary>
/// MiniCover. MiniCover 3.10.0 supports the .NET 8 SDK this branch uses
/// (its own TargetFrameworks are net10.0;net9.0;net8.0), so the target framework does
/// not exclude it. It instruments a BUILD and then runs the test suite, so it runs
/// only when OrderKit.sln builds (see <see cref="DotnetSolution"/>). MiniCover itself
/// comes from NuGet; where NuGet is unreachable this is exit 4.
///
/// Only the Debug test output is instrumented -- by default MiniCover takes every
/// .dll under the work dir, including other tools' instrumented copies in reports/ --
/// and its coverage and hit files are kept in reports/, which git ignores.
/// </summary>
public sealed class MiniCover : ITool
{
    public string Slug => "minicover";

    public int Run(ToolContext ctx)
    {
        DotnetSolution.RequireBuild(ctx);
        var minicover = DotnetTool.Require(ctx, "minicover", "3.10.0", "minicover");
        var dotnet = ctx.RequireCmd("dotnet");
        string[] workdir = ["--workdir", ctx.RepoRoot];
        var dataDir = Path.Combine(ctx.ReportDir(), "minicover");
        Directory.CreateDirectory(dataDir);
        string[] coverageFile = ["--coverage-file", Path.Combine(dataDir, "coverage.json")];
        string[] hitsDir = ["--hits-directory", Path.Combine(dataDir, "hits")];

        if (ctx.Run(minicover,
                ["instrument", .. workdir, .. coverageFile, .. hitsDir,
                 "--assemblies", "tests/OrderKit.Tests/bin/Debug/**/*.dll",
                 "--sources", "src/**/*.cs", "--tests", "tests/**/*.cs"]) != 0)
        {
            return ExitCodes.Fail;
        }

        try
        {
            ctx.Run(minicover, ["reset", .. workdir, .. hitsDir]);
            if (ctx.Run(dotnet, ["test", DotnetSolution.TestProjectPath(ctx), "--no-build"]) != 0)
            {
                return ExitCodes.Fail;
            }
        }
        finally
        {
            ctx.Run(minicover, ["uninstrument", .. workdir, .. coverageFile]);
        }

        var output = ctx.Report("minicover.cobertura.xml");
        var rc = ctx.Run(minicover, ["coberturareport", .. workdir, .. coverageFile, "--output", output]);
        if (rc != 0)
        {
            return ExitCodes.Fail;
        }

        // Instrumented statement hits are the measurement; a report with no hit
        // line means the instrumentation never ran.
        var lines = System.Xml.Linq.XDocument.Load(output).Descendants("line").ToList();
        var hit = lines.Count(l => (int?)l.Attribute("hits") > 0);
        if (hit == 0)
        {
            ctx.Reason("STRUCTURALLY ZERO: MiniCover recorded no statement hits.");
            return ExitCodes.Fail;
        }

        ctx.Out.WriteLine($"minicover: {hit} of {lines.Count} instrumented lines hit");
        return ExitCodes.Ok;
    }
}
