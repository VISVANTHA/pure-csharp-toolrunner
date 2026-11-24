using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace ToolRunner;

/// <summary>
/// What every runner is handed: its own folder, the repository root, the
/// output streams, and the status helpers of the exit-code contract
/// (<see cref="ExitCodes"/>). This is the C# form of the former _skip.sh.
/// </summary>
public sealed class ToolContext
{
    public ToolContext(RepoPaths paths, string slug, TextWriter stdout, TextWriter stderr)
    {
        Paths = paths;
        Slug = slug;
        ToolDir = paths.ToolDir(slug);
        Out = stdout;
        Err = stderr;
    }

    public RepoPaths Paths { get; }
    public string Slug { get; }
    public string ToolDir { get; }
    public string ToolsRoot => Paths.ToolsRoot;
    public string RepoRoot => Paths.RepoRoot;
    public TextWriter Out { get; }
    public TextWriter Err { get; }

    /// <summary>
    /// The codes a tool uses for "found something". That is a successful run, not
    /// a failure, so <see cref="AcceptFindings"/> treats them like 0.
    /// </summary>
    public int[] FindingsCodes { get; set; } = [];

    public void Reason(string message) => Err.WriteLine(message);

    /// <summary>Cannot run on this family. This is the measurement.</summary>
    [DoesNotReturn]
    public void Skip(string reason)
    {
        Reason("SKIPPED: " + reason);
        throw new ToolExitException(ExitCodes.Skip);
    }

    /// <summary>Absent from this host. A setup gap, not a finding.</summary>
    [DoesNotReturn]
    public void Missing(string reason)
    {
        Reason("NOT INSTALLED: " + reason);
        throw new ToolExitException(ExitCodes.Missing);
    }

    /// <summary>Probes an executable on PATH and returns its full path.</summary>
    public string RequireCmd(string name)
    {
        var path = FindOnPath(name);
        if (path is null)
        {
            Missing(name + " is not on PATH");
        }

        return path;
    }

    /// <summary>Probes the tool's OWN artefact, not its interpreter.</summary>
    public void RequireArtifact(string path, string? label = null)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            Missing((label ?? path) + " not present");
        }
    }

    /// <summary>Every NuGet-backed tool needs this, and none of them have it.</summary>
    public void RequireRestore()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!Directory.Exists(Path.Combine(RepoRoot, "packages"))
            && !Directory.Exists(Path.Combine(home, ".nuget", "packages")))
        {
            Skip("no restored package graph; NuGet is refused at this host's egress proxy");
        }
    }

    /// <summary>
    /// Translates a tool's own exit code without letting it collide with our
    /// namespace: true for 0 or a declared findings code, false otherwise. A
    /// tool's own status is never returned as 3 or 4.
    /// </summary>
    public bool AcceptFindings(int rc) => rc == 0 || FindingsCodes.Contains(rc);

    /// <summary>The shared reports folder, created on first use.</summary>
    public string ReportDir()
    {
        var dir = Path.Combine(RepoRoot, "reports");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public string Report(string name) => Path.Combine(ReportDir(), name);

    /// <summary>Where a child process's standard output goes.</summary>
    public sealed class Sink
    {
        public static readonly Sink Console = new(null, false);
        public static readonly Sink Discard = new(null, true);

        private Sink(TextWriter? writer, bool discard)
        {
            Writer = writer;
            IsDiscard = discard;
        }

        public TextWriter? Writer { get; }
        public bool IsDiscard { get; }

        public static Sink To(TextWriter writer) => new(writer, false);
    }

    /// <summary>Runs a process with output to this runner's streams; returns its exit code.</summary>
    public int Run(string file, IEnumerable<string> args, string? workDir = null) =>
        Run(file, args, Sink.Console, workDir);

    /// <summary>
    /// Runs a process and returns its exit code. Standard output goes where
    /// <paramref name="stdout"/> says; standard error goes to this runner's error
    /// stream unless <paramref name="quietStderr"/> is set.
    /// </summary>
    public int Run(string file, IEnumerable<string> args, Sink stdout, string? workDir = null,
                   bool quietStderr = false)
    {
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workDir ?? RepoRoot,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        psi.Environment["REPO_ROOT"] = RepoRoot;
        psi.Environment["TOOL_DIR"] = ToolDir;
        psi.Environment["TOOLS_ROOT"] = ToolsRoot;

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("could not start " + file);
        var outSink = stdout.IsDiscard ? null : stdout.Writer ?? Out;
        var errSink = stdout.IsDiscard || quietStderr ? null : Err;
        var outTask = Pump(process.StandardOutput, outSink);
        var errTask = Pump(process.StandardError, errSink);
        process.WaitForExit();
        Task.WaitAll(outTask, errTask);
        return process.ExitCode;
    }

    /// <summary>Runs a process with standard output written to a file.</summary>
    public int RunToFile(string file, IEnumerable<string> args, string outputPath, bool quietStderr)
    {
        using var writer = new StreamWriter(outputPath);
        return Run(file, args, Sink.To(writer), quietStderr: quietStderr);
    }

    /// <summary>Runs a process and returns its standard output; throws on a non-zero exit.</summary>
    public string Capture(string file, IEnumerable<string> args)
    {
        using var buffer = new StringWriter();
        var rc = Run(file, args, Sink.To(buffer), quietStderr: true);
        if (rc != 0)
        {
            throw new InvalidOperationException(Path.GetFileName(file) + " exited " + rc);
        }

        return buffer.ToString();
    }

    private static async Task Pump(StreamReader reader, TextWriter? sink)
    {
        var buffer = new char[4096];
        int n;
        while ((n = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            if (sink is null)
            {
                continue;
            }

            lock (sink)
            {
                sink.Write(buffer, 0, n);
            }
        }
    }

    /// <summary>Resolves a command the way a shell would, including PATHEXT on Windows.</summary>
    public static string? FindOnPath(string name)
    {
        // Windows cannot execute an extensionless file (npm puts a POSIX shell shim
        // named plain "jscpd" next to jscpd.cmd), so only PATHEXT candidates count
        // there unless the name already carries an extension.
        string[] exts = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !Path.HasExtension(name)
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [""];
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in dirs)
        {
            foreach (var ext in exts)
            {
                var candidate = Path.Combine(dir.Trim('"'), name + ext);
                if (File.Exists(candidate) && !IsWindowsStoreStub(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    // The WindowsApps "python3" alias is a zero-byte stub that opens the Store.
    private static bool IsWindowsStoreStub(string path) =>
        path.Contains(Path.Combine("Microsoft", "WindowsApps"), StringComparison.OrdinalIgnoreCase)
        && new FileInfo(path).Length == 0;
}
