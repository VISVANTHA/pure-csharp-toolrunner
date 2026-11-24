using System.Xml.Linq;

namespace ToolRunner.Tools;

/// <summary>
/// The gate every build-dependent runner (Coverlet, AltCover, MiniCover, Stryker.NET,
/// OpenTelemetry) passes before it touches its tool: those tools instrument, mutate
/// or execute COMPILED code, so they can only run on a branch whose solution builds.
///
/// The check runs in a fixed order so its verdict does not depend on the host:
///   1. structure, read from the project files alone (exit 3 when broken -- no host
///      can fix it, so this is the measurement);
///   2. restore (exit 4 when NuGet is unreachable -- a setup gap);
///   3. build (exit 3 when it fails).
/// The outcome is computed once per run and replayed to every runner that asks.
/// </summary>
public static class DotnetSolution
{
    private static readonly Dictionary<string, (int Code, string Reason)> Verdicts = [];

    public static string SolutionPath(ToolContext ctx) => Path.Combine(ctx.RepoRoot, "OrderKit.sln");

    public static string TestProjectPath(ToolContext ctx) =>
        Path.Combine(ctx.RepoRoot, "tests", "OrderKit.Tests", "OrderKit.Tests.csproj");

    /// <summary>Returns only when OrderKit.sln restores and builds.</summary>
    public static void RequireBuild(ToolContext ctx)
    {
        lock (Verdicts)
        {
            if (!Verdicts.TryGetValue(ctx.RepoRoot, out var verdict))
            {
                verdict = Evaluate(ctx);
                Verdicts[ctx.RepoRoot] = verdict;
            }

            switch (verdict.Code)
            {
                case ExitCodes.Skip: ctx.Skip(verdict.Reason); break;
                case ExitCodes.Missing: ctx.Missing(verdict.Reason); break;
            }
        }
    }

    private static (int Code, string Reason) Evaluate(ToolContext ctx)
    {
        var problems = StructuralProblems(ctx.RepoRoot);
        if (problems.Count > 0)
        {
            return (ExitCodes.Skip, "OrderKit.sln cannot compile, so there is no assembly to work on: "
                                    + string.Join("; ", problems));
        }

        var dotnet = ToolContext.FindOnPath("dotnet");
        if (dotnet is null)
        {
            return (ExitCodes.Missing, "dotnet is not on PATH");
        }

        var restore = Restore(ctx, dotnet);
        if (restore is not null)
        {
            return (ExitCodes.Missing, restore);
        }

        using var log = new StringWriter();
        var rc = ctx.Run(dotnet, ["build", SolutionPath(ctx), "--no-restore", "-nologo", "-v", "q"],
                         ToolContext.Sink.To(log), quietStderr: true);
        if (rc != 0)
        {
            var firstError = log.ToString().Split('\n')
                .FirstOrDefault(l => l.Contains(": error ", StringComparison.Ordinal))?.Trim();
            return (ExitCodes.Skip, "OrderKit.sln does not build: " + (firstError ?? "dotnet build exited " + rc));
        }

        return (ExitCodes.Ok, "");
    }

    /// <summary>
    /// Restores the solution. Returns null on success, or why NuGet could not be
    /// used. NU1301 is NuGet's "unable to load the service index" -- the signature
    /// of a refused egress proxy.
    /// </summary>
    public static string? Restore(ToolContext ctx, string dotnet)
    {
        using var log = new StringWriter();
        var rc = ctx.Run(dotnet, ["restore", SolutionPath(ctx), "-nologo"], ToolContext.Sink.To(log), quietStderr: true);
        if (rc == 0)
        {
            return null;
        }

        return log.ToString().Contains("NU1301", StringComparison.Ordinal)
            ? "NuGet is unreachable from this host (NU1301), so nothing can be restored"
            : "dotnet restore exited " + rc;
    }

    /// <summary>
    /// Defects visible in the project files themselves, without restoring or
    /// building: a ProjectReference to a file that does not exist, and a
    /// Properties/AssemblyInfo.cs whose attributes duplicate the ones the SDK
    /// generates (CS0579) unless GenerateAssemblyInfo is switched off.
    /// </summary>
    public static List<string> StructuralProblems(string repoRoot)
    {
        var problems = new List<string>();
        var projects = new[] { "src", "tests" }
            .Select(d => Path.Combine(repoRoot, d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.csproj", SearchOption.AllDirectories))
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .Order(StringComparer.Ordinal);

        var duplicateInfo = new List<string>();
        foreach (var project in projects)
        {
            var dir = Path.GetDirectoryName(project)!;
            var name = Path.GetFileNameWithoutExtension(project);
            var xml = XDocument.Load(project);

            var missing = xml.Descendants("ProjectReference")
                .Select(r => (string?)r.Attribute("Include"))
                .Where(inc => inc is not null
                              && !File.Exists(Path.GetFullPath(Path.Combine(dir, inc.Replace('\\', Path.DirectorySeparatorChar)))))
                .ToList();
            if (missing.Count > 0)
            {
                problems.Add($"{name} references {missing.Count} project(s) that do not exist ({missing[0]})");
            }

            var info = Path.Combine(dir, "Properties", "AssemblyInfo.cs");
            if (!GeneratesAssemblyInfo(xml, dir, repoRoot) && File.Exists(info))
            {
                continue;
            }

            if (File.Exists(info) && File.ReadAllText(info).Contains("[assembly: AssemblyVersion", StringComparison.Ordinal))
            {
                duplicateInfo.Add(name);
            }
        }

        if (duplicateInfo.Count > 0)
        {
            problems.Insert(0, $"{duplicateInfo.Count} project(s) ship a Properties/AssemblyInfo.cs that duplicates "
                               + "the SDK-generated assembly attributes (CS0579), e.g. " + duplicateInfo[0]);
        }

        return problems;
    }

    /// <summary>
    /// Whether the SDK will generate assembly attributes for a project. The switch
    /// can sit in the project file itself or in the Directory.Build.props MSBuild
    /// imports for it -- the NEAREST one at or above the project's folder (only the
    /// first found is imported). A setting in the project file wins, since it is
    /// evaluated after the imported props.
    /// </summary>
    private static bool GeneratesAssemblyInfo(XDocument project, string projectDir, string repoRoot)
    {
        static bool? Setting(XDocument doc)
        {
            var last = doc.Descendants("GenerateAssemblyInfo").LastOrDefault();
            return last is null ? null : !string.Equals(last.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase);
        }

        if (Setting(project) is { } own)
        {
            return own;
        }

        var root = Path.GetFullPath(repoRoot);
        for (var dir = new DirectoryInfo(projectDir); dir is not null; dir = dir.Parent)
        {
            var props = Path.Combine(dir.FullName, "Directory.Build.props");
            if (File.Exists(props))
            {
                return Setting(XDocument.Load(props)) ?? true;
            }

            if (string.Equals(dir.FullName.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar),
                              StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        return true;
    }
}

/// <summary>
/// Installs a .NET tool package into a per-version cache folder (never globally)
/// and returns the path of its command. A failed install means NuGet could not
/// supply it: exit 4, absent from this host.
/// </summary>
public static class DotnetTool
{
    public static string Require(ToolContext ctx, string packageId, string version, string command)
    {
        var dotnet = ctx.RequireCmd("dotnet");
        var toolPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "orderkit-dotnet-tools", packageId, version);
        var exe = Path.Combine(toolPath, OperatingSystem.IsWindows() ? command + ".exe" : command);
        if (File.Exists(exe))
        {
            return exe;
        }

        var rc = ctx.Run(dotnet, ["tool", "install", packageId, "--version", version, "--tool-path", toolPath],
                         ToolContext.Sink.Discard);
        if (rc != 0 || !File.Exists(exe))
        {
            ctx.Missing($"could not install {packageId} {version} from NuGet");
        }

        return exe;
    }
}
