using System.Text.Json.Nodes;
using ToolRunner.Tools;

namespace ToolRunner;

/// <summary>
/// Banner / --verify / --run for the tool roster (the former tool_integration.py).
///
/// --run distinguishes a skip that dataset.json ALREADY RECORDS (the measurement,
/// exit 0) from a skip it does not (a new finding, exit 2).
/// </summary>
public static class Integration
{
    public static int Banner(RepoPaths paths)
    {
        var ds = Dataset.Load(paths.DatasetPath);
        var cell = ds.Root["cell"]!;
        var exp = ds.Root["expectations"]!;
        Console.WriteLine($"OrderKit - {ds.Str("netLabel")} ({ds.Str("targetFramework")}), C# {ds.Str("languageVersion")}");
        Console.WriteLine($"branch {ds.Str("branch")}  cell {cell["bundler"]}/{cell["packageManager"]}/{cell["architecture"]}");
        Console.WriteLine($"{exp["toolsTotal"]} tools declared, {exp["toolsActive"]} expected to fire");
        return ExitCodes.Ok;
    }

    public static int Verify(RepoPaths paths)
    {
        var ds = Dataset.Load(paths.DatasetPath);
        var problems = new List<string>();
        var tools = ds.Tools.ToList();
        foreach (var t in tools)
        {
            var slug = Dataset.Slug(t);
            var tool = ToolRegistry.Find(slug);
            if (tool is null)
            {
                problems.Add(slug + ": no runner");
                continue;
            }

            var trigger = Path.Combine(paths.ToolDir(slug), "trigger.json");
            if (!File.Exists(trigger))
            {
                problems.Add(slug + ": no trigger.json");
                continue;
            }

            var runner = JsonNode.Parse(File.ReadAllText(trigger))?["runner"]?.GetValue<string>();
            if (runner != ToolRegistry.SourceFile(tool))
            {
                problems.Add($"{slug}: trigger.json names runner '{runner}', not '{ToolRegistry.SourceFile(tool)}'");
            }
        }

        if (problems.Count > 0)
        {
            Console.WriteLine("VERIFY FAILED");
            foreach (var p in problems)
            {
                Console.WriteLine("  - " + p);
            }

            return ExitCodes.Fail;
        }

        Console.WriteLine($"OK - {tools.Count} runners present");
        return ExitCodes.Ok;
    }

    public static int RunAll(RepoPaths paths)
    {
        Banner(paths);
        Console.WriteLine();

        var ds = Dataset.Load(paths.DatasetPath);
        var surprises = new List<string>();
        var tally = new SortedDictionary<int, int>();
        foreach (var t in ds.Tools)
        {
            var slug = Dataset.Slug(t);
            var expected = Dataset.ExpectedExitCode(t);
            var tool = ToolRegistry.Find(slug);

            // Each runner's own output is captured, as the Python harness did, so
            // the roster prints one line per tool.
            int rc;
            if (tool is null)
            {
                rc = ExitCodes.Fail;
            }
            else
            {
                using var quiet = new StringWriter();
                rc = Execute(tool, new ToolContext(paths, slug, quiet, quiet));
            }

            tally[rc] = tally.GetValueOrDefault(rc) + 1;
            var mark = rc == expected ? "ok " : "NEW";
            if (rc != expected)
            {
                surprises.Add($"{slug}: expected {expected} ({ExitCodes.Name(expected)}), got {rc} ({ExitCodes.Name(rc)})");
            }

            Console.WriteLine($"  {mark} {slug,-18} {rc} {ExitCodes.Name(rc)}");
        }

        Console.WriteLine();
        Console.WriteLine(string.Join(", ", tally.Select(kv =>
            $"{kv.Value} {(ExitCodes.Name(kv.Key) == "?" ? kv.Key.ToString() : ExitCodes.Name(kv.Key))}")));
        if (surprises.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("UNRECORDED RESULTS - these are new findings, not measurements:");
            foreach (var s in surprises)
            {
                Console.WriteLine("  - " + s);
            }

            return ExitCodes.Unrecorded;
        }

        Console.WriteLine("every result matches dataset.json");
        return ExitCodes.Ok;
    }

    /// <summary>Runs one runner and maps an early exit or a crash onto the contract.</summary>
    public static int Execute(ITool tool, ToolContext ctx)
    {
        try
        {
            var rc = tool.Run(ctx);

            // A runner's own status is never allowed to escape as 3 or 4 unless it
            // came through Skip/Missing.
            return rc is ExitCodes.Skip or ExitCodes.Missing ? ExitCodes.Fail : rc;
        }
        catch (ToolExitException exit)
        {
            return exit.ExitCode;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ctx.Reason($"{tool.Slug}: {ex.GetType().Name}: {ex.Message}");
            return ExitCodes.Fail;
        }
    }
}
