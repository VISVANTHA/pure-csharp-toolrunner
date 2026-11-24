using System.Text.Json.Nodes;

namespace ToolRunner.Tools;

/// <summary>
/// git log --numstat. stdlib git. Class C.
///
/// Counts Co-authored-by trailers, not just the author field. The TypeScript corpus
/// shipped an ownership metric that was WRONG rather than missing because its parser
/// split each record on the first newline and never saw the trailers.
/// </summary>
public sealed class GitChurn : ITool
{
    private const char RecordSeparator = '\x1e';

    public string Slug => "git-churn";

    public int Run(ToolContext ctx)
    {
        var git = ctx.RequireCmd("git");
        var log = ctx.Capture(git,
            ["-C", ctx.RepoRoot, "log", "--numstat", $"--format={RecordSeparator}%H%n%an%n%B"]);

        var authors = new OrderedCounter();
        var churn = new OrderedCounter();
        var commits = 0;
        foreach (var rec in log.Split(RecordSeparator))
        {
            if (string.IsNullOrWhiteSpace(rec))
            {
                continue;
            }

            commits++;
            var lines = rec.ReplaceLineEndings("\n").Split('\n');
            if (rec.EndsWith('\n'))
            {
                lines = lines[..^1];
            }

            if (lines.Length < 2)
            {
                continue;
            }

            authors.Add(lines[1].Trim());
            foreach (var ln in lines.Skip(2))
            {
                if (ln.StartsWith("co-authored-by:", StringComparison.OrdinalIgnoreCase))
                {
                    authors.Add(ln.Split(':', 2)[1].Split('<')[0].Trim());
                }

                var parts = ln.Split('\t');
                if (parts.Length == 3 && parts[0].Length > 0 && parts[0].All(char.IsAsciiDigit))
                {
                    churn.Add(parts[2], int.Parse(parts[0]) + (parts[1].Length == 0 ? 0 : int.Parse(parts[1])));
                }
            }
        }

        var total = Math.Max(authors.Total, 1);
        var ownership = new JsonObject();
        foreach (var (name, n) in authors.Items)
        {
            ownership[name] = Math.Round(100.0 * n / total, 1, MidpointRounding.ToEven);
        }

        var topChurn = new JsonArray();
        foreach (var (path, n) in churn.MostCommon(10))
        {
            topChurn.Add(new JsonArray(path, n));
        }

        JsonOut.Write(ctx.Report("git-churn.json"), new JsonObject
        {
            ["tool"] = "git-churn",
            ["commits"] = commits,
            ["contributors"] = authors.Count,
            ["ownership_pct"] = ownership,
            ["top_churn"] = topChurn,
        });
        return ExitCodes.Ok;
    }
}

/// <summary>A counter that keeps first-seen order, so ties rank as they were met.</summary>
internal sealed class OrderedCounter
{
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];

    public int Count => _order.Count;

    public int Total => _counts.Values.Sum();

    public IEnumerable<(string Key, int Count)> Items => _order.Select(k => (k, _counts[k]));

    public void Add(string key, int by = 1)
    {
        if (!_counts.TryAdd(key, by))
        {
            _counts[key] += by;
        }
        else
        {
            _order.Add(key);
        }
    }

    // OrderBy is stable, so equal counts keep insertion order.
    public IEnumerable<(string Key, int Count)> MostCommon(int n) =>
        Items.OrderByDescending(i => i.Count).Take(n);
}
