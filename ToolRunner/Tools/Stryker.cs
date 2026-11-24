using System.Text.Json.Nodes;

namespace ToolRunner.Tools;

/// <summary>
/// Stryker.NET. This branch is an SDK-style net8.0 solution, which Stryker.NET
/// supports. It mutates the code under test, REBUILDS it and runs the test project for
/// every mutant, so it runs only when OrderKit.sln builds and its tests pass (see
/// <see cref="DotnetSolution"/>). Stryker.NET itself comes from NuGet; where NuGet is
/// unreachable this is exit 4. Also: the sheet makes it primary for 16 Data Flow
/// metrics, which it cannot compute -- see roslyn-dataflow.
///
/// It runs in solution mode: the test project references OrderKit.Core and OrderKit.Services,
/// and solution mode keeps the run pinned to OrderKit.sln.
/// </summary>
public sealed class Stryker : ITool
{
    public string Slug => "stryker";

    public int Run(ToolContext ctx)
    {
        DotnetSolution.RequireBuild(ctx);
        var stryker = DotnetTool.Require(ctx, "dotnet-stryker", "4.6.0", "dotnet-stryker");
        var output = Path.Combine(ctx.ReportDir(), "stryker");

        // --break-at 0: a low mutation score is the measurement, not a failure.
        var rc = ctx.Run(stryker,
            ["--solution", DotnetSolution.SolutionPath(ctx), "--reporter", "json", "--reporter", "progress",
             "--output", output, "--break-at", "0"],
            workDir: ctx.RepoRoot);
        RemoveBackups(ctx);
        if (rc != 0)
        {
            return ExitCodes.Fail;
        }

        var report = Directory.GetFiles(output, "mutation-report.json", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (report is null)
        {
            ctx.Reason("Stryker.NET exited 0 but wrote no mutation-report.json.");
            return ExitCodes.Fail;
        }

        var byStatus = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in JsonNode.Parse(File.ReadAllText(report))!["files"]!.AsObject())
        {
            foreach (var mutant in file.Value!["mutants"]!.AsArray())
            {
                var status = mutant!["status"]!.GetValue<string>();
                byStatus[status] = byStatus.GetValueOrDefault(status) + 1;
            }
        }

        // Mutation score as Stryker defines it: detected / (detected + undetected).
        var detected = byStatus.GetValueOrDefault("Killed") + byStatus.GetValueOrDefault("Timeout");
        var undetected = byStatus.GetValueOrDefault("Survived") + byStatus.GetValueOrDefault("NoCoverage");
        if (detected + undetected == 0)
        {
            ctx.Reason("STRUCTURALLY ZERO: Stryker.NET tested no mutants.");
            return ExitCodes.Fail;
        }

        ctx.Out.WriteLine($"stryker: mutation score {100.0 * detected / (detected + undetected):F2}% "
                          + $"({string.Join(", ", byStatus.Select(kv => $"{kv.Value} {kv.Key}"))})");
        return ExitCodes.Ok;
    }

    /// <summary>
    /// Stryker.NET leaves a "&lt;assembly&gt;.dll.stryker-unchanged" backup beside each
    /// assembly it swapped in the test output. The assembly itself is restored, so
    /// the backup is a stale copy -- but the next tool to copy that folder (AltCover)
    /// picks it up. Remove a backup only when its assembly is present.
    /// </summary>
    internal static void RemoveBackups(ToolContext ctx)
    {
        const string Suffix = ".stryker-unchanged";
        foreach (var dir in new[] { "src", "tests" }.Select(d => Path.Combine(ctx.RepoRoot, d)).Where(Directory.Exists))
        {
            foreach (var backup in Directory.EnumerateFiles(dir, "*" + Suffix, SearchOption.AllDirectories))
            {
                if (File.Exists(backup[..^Suffix.Length]))
                {
                    File.Delete(backup);
                }
            }
        }
    }
}
