using System.Globalization;
using System.Xml.Linq;

namespace ToolRunner.Tools;

/// <summary>
/// Coverlet. net8.0 is fully supported; nothing about this target framework excludes
/// it. Coverlet instruments the compiled test assemblies, so it runs only when
/// OrderKit.sln builds and its tests pass (see <see cref="DotnetSolution"/>). Coverlet
/// itself comes from NuGet; where NuGet is unreachable this is exit 4.
/// </summary>
public sealed class Coverlet : ITool
{
    public string Slug => "coverlet";

    public int Run(ToolContext ctx)
    {
        DotnetSolution.RequireBuild(ctx);
        var coverlet = DotnetTool.Require(ctx, "coverlet.console", "6.0.4", "coverlet");

        var testProject = DotnetSolution.TestProjectPath(ctx);
        var testDll = Path.Combine(Path.GetDirectoryName(testProject)!, "bin", "Debug", "net8.0", "OrderKit.Tests.dll");
        var output = ctx.Report("coverlet.cobertura.xml");
        var rc = ctx.Run(coverlet,
            [testDll, "--target", "dotnet", "--targetargs", $"test \"{testProject}\" --no-build",
             "--format", "cobertura", "--output", output]);
        if (rc != 0)
        {
            return ExitCodes.Fail;
        }

        // The planted target makes statement and branch coverage differ; equal
        // numbers (typically 0 and 0) mean nothing was measured.
        var root = XDocument.Load(output).Root!;
        var line = double.Parse((string?)root.Attribute("line-rate") ?? "0", CultureInfo.InvariantCulture);
        var branch = double.Parse((string?)root.Attribute("branch-rate") ?? "0", CultureInfo.InvariantCulture);
        ctx.Out.WriteLine($"coverlet: statement {line:P1}, branch {branch:P1}");
        if (line == branch)
        {
            ctx.Reason("STRUCTURALLY ZERO: statement and branch coverage are equal, so the planted branch-dense target was not measured.");
            return ExitCodes.Fail;
        }

        return ExitCodes.Ok;
    }
}
