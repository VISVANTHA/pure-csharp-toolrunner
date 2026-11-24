using System.Xml.Linq;

namespace ToolRunner.Tools;

/// <summary>
/// AltCover. net8.0 is supported. AltCover rewrites COMPILED assemblies to record
/// visits, so it runs only when OrderKit.sln builds (see <see cref="DotnetSolution"/>).
/// AltCover itself comes from NuGet; where NuGet is unreachable this is exit 4.
///
/// The planted data asks for a path count that differs from the branch count, so
/// the runner refuses a report whose branch points are zero.
/// </summary>
public sealed class AltCover : ITool
{
    public string Slug => "altcover";

    public int Run(ToolContext ctx)
    {
        DotnetSolution.RequireBuild(ctx);
        var altcover = DotnetTool.Require(ctx, "altcover.global", "9.0.1", "altcover");

        var testProject = DotnetSolution.TestProjectPath(ctx);
        var bin = Path.Combine(Path.GetDirectoryName(testProject)!, "bin", "Debug", "net8.0");
        var report = ctx.Report("altcover.xml");
        Stryker.RemoveBackups(ctx);   // stale Stryker.NET backups would be copied too

        // A fresh folder per run: a copy left by an earlier run can still be held
        // open (by a test host or a virus scanner), and would block the overwrite.
        var runs = Path.Combine(ctx.ReportDir(), "altcover");
        foreach (var old in Directory.Exists(runs) ? Directory.GetDirectories(runs) : [])
        {
            try { Directory.Delete(old, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        var instrumented = Path.Combine(runs, "instrumented-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));

        // 1. Instrument a copy of the built output. The test assembly and the test
        //    framework are excluded, so only production code is measured.
        var rc = ctx.Run(altcover,
            ["--inputDirectory", bin, "--outputDirectory", instrumented, "--report", report,
             "--assemblyFilter", "NUnit3.TestAdapter", "--assemblyFilter", "nunit", "--assemblyFilter", "OrderKit.Tests", "-q"]);
        if (rc != 0)
        {
            return ExitCodes.Fail;
        }

        // 2. Run the tests against the instrumented copy; the Runner writes the
        //    recorded visits into the report.
        var dotnet = ctx.RequireCmd("dotnet");
        rc = ctx.Run(altcover,
            ["Runner", "--recorderDirectory", instrumented, "--executable", dotnet, "--",
             "test", Path.Combine(instrumented, "OrderKit.Tests.dll")]);
        if (rc != 0)
        {
            return ExitCodes.Fail;
        }

        var summary = XDocument.Load(report).Root?.Element("Summary");
        var branches = (int?)summary?.Attribute("numBranchPoints") ?? 0;
        if (branches == 0)
        {
            ctx.Reason("STRUCTURALLY ZERO: AltCover recorded no branch points, so nothing was measured.");
            return ExitCodes.Fail;
        }

        ctx.Out.WriteLine($"altcover: sequence {summary!.Attribute("sequenceCoverage")?.Value}%, "
                          + $"branch {summary.Attribute("branchCoverage")?.Value}% "
                          + $"({summary.Attribute("visitedBranchPoints")?.Value}/{branches} branch points), "
                          + $"max cyclomatic {summary.Attribute("maxCyclomaticComplexity")?.Value}");
        return ExitCodes.Ok;
    }
}
