using System.Text.Json.Nodes;

namespace ToolRunner.Tools;

/// <summary>
/// PyDriller. Reads git only. Class C: never touches the source language.
///
/// Written in C# with no Python: this runner reproduces what the PyDriller driver
/// computed -- Repository(root).traverse_commits() walks the history oldest first,
/// counting each commit's author plus every Co-authored-by trailer in its message --
/// by reading the same history straight from git.
/// </summary>
public sealed class PyDriller : ITool
{
    private const char RecordSeparator = '\x1e';

    public string Slug => "pydriller";

    public int Run(ToolContext ctx)
    {
        var git = ctx.RequireCmd("git");

        // Oldest first, as traverse_commits() yields them, so authors keep the
        // order in which they first appear.
        var log = ctx.Capture(git,
            ["-C", ctx.RepoRoot, "log", "--reverse", $"--format={RecordSeparator}%an%n%B"]);

        var authors = new OrderedCounter();
        var commits = 0;
        foreach (var rec in log.Split(RecordSeparator))
        {
            if (rec.Length == 0)
            {
                continue;
            }

            commits++;
            var lines = rec.ReplaceLineEndings("\n").Split('\n');
            authors.Add(lines[0]);
            foreach (var line in lines.Skip(1))
            {
                if (line.StartsWith("co-authored-by:", StringComparison.OrdinalIgnoreCase))
                {
                    authors.Add(line.Split(':', 2)[1].Split('<')[0].Trim());
                }
            }
        }

        var byAuthor = new JsonObject();
        foreach (var (name, n) in authors.Items)
        {
            byAuthor[name] = n;
        }

        JsonOut.Write(ctx.Report("pydriller.json"), new JsonObject
        {
            ["tool"] = "pydriller",
            ["commits"] = commits,
            ["contributors"] = authors.Count,
            ["by_author"] = byAuthor,
        });
        return ExitCodes.Ok;
    }
}
