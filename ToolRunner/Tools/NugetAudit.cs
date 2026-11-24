using System.Text.Json.Nodes;

namespace ToolRunner.Tools;

/// <summary>
/// dotnet list package --vulnerable. This branch is SDK-style with PackageReference
/// under Central Package Management, so the audit applies. It needs a restore and
/// live advisory data from NuGet; where NuGet is unreachable it exits 4.
///
/// The expected packages come from this folder's expected-advisories.json. Advisory
/// COUNTS are read live, never recalled. A run that flags none of the planted pins
/// is refused as structurally zero; a planted pin NuGet does not flag is reported,
/// not hidden.
/// </summary>
public sealed class NugetAudit : ITool
{
    public string Slug => "nuget-audit";

    public int Run(ToolContext ctx)
    {
        var dotnet = ctx.RequireCmd("dotnet");
        var restore = DotnetSolution.Restore(ctx, dotnet);
        if (restore is not null)
        {
            ctx.Missing(restore);
        }

        var report = ctx.Report("nuget-audit.json");
        var rc = ctx.RunToFile(dotnet,
            ["list", DotnetSolution.SolutionPath(ctx), "package", "--vulnerable", "--include-transitive", "--format", "json"],
            report, quietStderr: false);
        if (rc != 0)
        {
            return ExitCodes.Fail;
        }

        var flagged = new Dictionary<string, (string Version, HashSet<string> Severities)>(StringComparer.OrdinalIgnoreCase);
        var audit = JsonNode.Parse(File.ReadAllText(report))!;
        foreach (var project in audit["projects"]?.AsArray() ?? [])
        {
            foreach (var framework in project!["frameworks"]?.AsArray() ?? [])
            {
                foreach (var kind in new[] { "topLevelPackages", "transitivePackages" })
                {
                    foreach (var package in framework![kind]?.AsArray() ?? [])
                    {
                        var id = package!["id"]!.GetValue<string>();
                        var entry = flagged.TryGetValue(id, out var e)
                            ? e
                            : (package["resolvedVersion"]?.GetValue<string>() ?? "", new HashSet<string>());
                        foreach (var v in package["vulnerabilities"]?.AsArray() ?? [])
                        {
                            entry.Item2.Add(v!["severity"]?.GetValue<string>() ?? "?");
                        }

                        flagged[id] = entry;
                    }
                }
            }
        }

        var expected = JsonNode.Parse(File.ReadAllText(Path.Combine(ctx.ToolDir, "expected-advisories.json")))!;
        var planted = expected["packages"]!.AsArray()
            .Select(p => (Id: p!["id"]!.GetValue<string>(), Version: p["version"]!.GetValue<string>()))
            .ToList();
        var found = planted.Where(p => flagged.TryGetValue(p.Id, out var f) && f.Version == p.Version).ToList();
        var notFlagged = planted.Except(found).ToList();

        if (found.Count == 0)
        {
            ctx.Reason("STRUCTURALLY ZERO: none of the planted pins was flagged. Check that the restore "
                       + "read Directory.Packages.props and that advisory data was reachable.");
            return ExitCodes.Fail;
        }

        ctx.Out.WriteLine($"nuget-audit: {flagged.Count} vulnerable packages; {found.Count} of {planted.Count} planted pins flagged: "
                          + string.Join(", ", found.Select(p => $"{p.Id} {p.Version} ({string.Join("/", flagged[p.Id].Severities.Order())})")));
        foreach (var p in notFlagged)
        {
            ctx.Out.WriteLine($"  planted but not flagged by NuGet's advisory feed: {p.Id} {p.Version}");
        }

        return ExitCodes.Ok;
    }
}
