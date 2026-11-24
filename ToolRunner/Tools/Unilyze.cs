using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace ToolRunner.Tools;

/// <summary>
/// unilyze. REPAIRED. Real tool (github.com/bigdra50/unilyze, MIT, v0.6.1) -- see
/// this folder's README.md and trigger.json for the full correction and the
/// verification evidence. Class B: own Roslyn-based syntax walk, no restore or
/// build required, so the TFM is irrelevant to it, like Lizard/jscpd/Semgrep.
/// Installed from its GitHub Release binary, a channel this host's egress proxy
/// does not block, unlike the NuGet-gated dotnet-tool channel.
/// </summary>
public sealed class Unilyze : ITool
{
    private const string Version = "0.6.1";

    public string Slug => "unilyze";

    public int Run(ToolContext ctx)
    {
        var cacheRoot = Environment.GetEnvironmentVariable("UNILYZE_CACHE_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                            ".cache", "unilyze-bin");
        var cacheDir = Path.Combine(cacheRoot, Version);
        var bin = Path.Combine(cacheDir, "unilyze");

        if (!File.Exists(bin))
        {
            Install(ctx, cacheDir, bin);
        }

        ctx.RequireArtifact(bin, "unilyze binary");

        var output = ctx.Report("unilyze.json");
        var hotspotOutput = ctx.Report("unilyze-hotspot.json");

        var rc = ctx.Run(bin, ["-p", ctx.ToolDir, "-f", "json", "-o", output, "--no-open"]);
        if (rc != 0)
        {
            ctx.Reason($"unilyze exited {rc} analyzing {ctx.RepoRoot}/src");
            return ExitCodes.Fail;
        }

        var hrc = ctx.Run(bin, ["hotspot", "-p", ctx.ToolDir, "--since", "100.year", "-o", hotspotOutput],
                          ToolContext.Sink.Discard);
        if (hrc != 0)
        {
            ctx.Reason($"unilyze hotspot exited {hrc}");
            return ExitCodes.Fail;
        }

        return Summarise(ctx, output, hotspotOutput);
    }

    private static void Install(ToolContext ctx, string cacheDir, string bin)
    {
        string rid;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            rid = "linux-x64";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            rid = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        }
        else
        {
            ctx.Missing("unsupported host OS for unilyze's self-contained binary: "
                        + RuntimeInformation.OSDescription);
            return;
        }

        Directory.CreateDirectory(cacheDir);
        var url = $"https://github.com/bigdra50/unilyze/releases/download/v{Version}/unilyze-{Version}-{rid}.tar.gz";
        var archive = Path.Combine(cacheDir, "unilyze.tar.gz");
        try
        {
            using var http = new HttpClient();
            using (var response = http.GetAsync(url).GetAwaiter().GetResult())
            {
                response.EnsureSuccessStatusCode();
                using var file = File.Create(archive);
                response.Content.CopyToAsync(file).GetAwaiter().GetResult();
            }
        }
        catch (HttpRequestException)
        {
            ctx.Missing($"could not download unilyze {Version} from GitHub Releases ({url})");
        }

        using (var gz = new GZipStream(File.OpenRead(archive), CompressionMode.Decompress))
        {
            TarFile.ExtractToDirectory(gz, cacheDir, overwriteFiles: true);
        }

        if (!OperatingSystem.IsWindows() && File.Exists(bin))
        {
            File.SetUnixFileMode(bin, File.GetUnixFileMode(bin)
                | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }

        File.Delete(archive);
    }

    private static int Summarise(ToolContext ctx, string resultPath, string hotspotPath)
    {
        var d = JsonNode.Parse(File.ReadAllText(resultPath))!;
        (double Value, string? Name) bestCyc = (-1, null);
        (double Value, string? Name) bestCog = (-1, null);
        foreach (var t in d["types"]?.AsArray() ?? [])
        {
            foreach (var m in t!["members"]?.AsArray() ?? [])
            {
                var name = $"{Str(t["namespace"])}.{Str(t["name"])}.{Str(m!["name"])}";
                if (Num(m["cyclomaticComplexity"]) is { } cyc && cyc > bestCyc.Value)
                {
                    bestCyc = (cyc, name);
                }

                if (Num(m["cognitiveComplexity"]) is { } cog && cog > bestCog.Value)
                {
                    bestCog = (cog, name);
                }
            }
        }

        if (bestCyc.Name is null)
        {
            ctx.Reason("STRUCTURALLY ZERO: unilyze found no members with a cyclomaticComplexity "
                       + "field -- the JSON schema has changed or analysis silently found nothing.");
            return ExitCodes.Fail;
        }

        var hotspots = JsonNode.Parse(File.ReadAllText(hotspotPath))!["hotspots"]?.AsArray() ?? [];
        var peakChanges = hotspots.Select(h => Num(h?["changeCount"]) ?? 0).DefaultIfEmpty(0).Max();

        var summary = new JsonObject
        {
            ["tool"] = "unilyze",
            ["toolVersion"] = d["toolVersion"]?.DeepClone(),
            ["max_cyclomatic_complexity"] = JsonOut.Number(bestCyc.Value),
            ["max_cyclomatic_complexity_member"] = bestCyc.Name,
            ["max_cognitive_complexity"] = JsonOut.Number(bestCog.Value),
            ["max_cognitive_complexity_member"] = bestCog.Name,
            ["hotspot_peak_change_count"] = JsonOut.Number(peakChanges),
            ["hotspot_type_count"] = hotspots.Count,
        };
        JsonOut.Write(Path.Combine(Path.GetDirectoryName(resultPath)!, "unilyze-summary.json"), summary);
        ctx.Out.WriteLine($"unilyze: max CCN {bestCyc.Value:0} ({bestCyc.Name}), "
                          + $"max cognitive {bestCog.Value:0} ({bestCog.Name}), "
                          + $"{hotspots.Count} churn hotspots (peak {peakChanges:0} changes)");
        return ExitCodes.Ok;
    }

    private static string Str(JsonNode? node) => node?.GetValue<string>() ?? "None";

    private static double? Num(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<double>(out var x) ? x : null;
}
