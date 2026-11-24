using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using ToolRunner.Tools;

namespace ToolRunner;

/// <summary>
/// Cross-file consistency audit (the former full_check.py). Uses nothing outside
/// the .NET base library.
///
/// Rule groups, in the order the sibling corpora learned to need them:
///   1  every trigger.json resolves to a runner that exists
///   2  manifest / dataset.json / README agree on the target framework
///   3  the planted package table matches the manifests
///   4  README sections present and in order (matched to WHOLE LINES -- a substring
///      search for "## Run" matches inside "## Running here" and reported a spurious
///      FAIL in the Python corpus)
///   5  the duplicate pair is identical modulo names
///   6  no empty source directories (an empty package is valid and invisible to every
///      other gate; building the wheel is what exposed it in the Python corpus)
///   7  all XML parses
///   8  PROSE AUDIT: no line states a version other than this branch's
///   9  no template placeholder survived into shipped output
///  10  no project manifest inside the tool data (task-explosion guard)
///  11  every tool folder carries data that could trigger its tool
///  12  every fixture carries this family's language marker
/// </summary>
public static partial class FullCheck
{
    private static readonly string[] ReadmeOrder =
    [
        "## Branch", "## Cell", "## Target framework", "## Language version",
        "## Supported tools", "## Build", "## Test", "## Tool entry points",
        "## What is expected to fire",
    ];

    private static readonly Dictionary<string, string> NoSourceTools = new()
    {
        ["pydriller"] = "its input is the repository's commit graph, not its source",
        ["git-churn"] = "its input is the repository's commit graph, not its source",
        ["trivy"] = "its .NET scanner reads manifests, lockfiles and SBOMs, never C# source",
    };

    private static readonly string[] ManifestNames =
    [
        "packages.config", "paket.lock", "paket.dependencies",
        "Directory.Packages.props", "package-lock.json",
    ];

    public static int Run(RepoPaths paths)
    {
        var ds = Dataset.Load(paths.DatasetPath);
        var audit = new Audit(paths);
        audit.TriggersResolve(ds);
        audit.TfmAgrees(ds);
        audit.PlantedMatches(ds);
        audit.Readme();
        audit.DuplicatePair();
        audit.NoEmptyDirs();
        audit.XmlParses();
        audit.Prose(ds);
        audit.NoPlaceholders();
        audit.NoManifestInToolData();
        audit.ToolDataPresent(ds);
        audit.LanguageMarker();

        if (audit.Fails.Count > 0)
        {
            Console.WriteLine($"FAIL ({audit.Fails.Count})");
            foreach (var f in audit.Fails)
            {
                Console.WriteLine("  - " + f);
            }

            return ExitCodes.Fail;
        }

        Console.WriteLine("OK - 12 rule groups clean");
        return ExitCodes.Ok;
    }

    [GeneratedRegex(@"\bnet(?:4[0-9]{1,2}|standard[0-9.]+|[0-9]+\.[0-9])\b")]
    private static partial Regex VersionToken();

    [GeneratedRegex(@"(?<!\$)\{[A-Z_]{3,}\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\b(?:class|record struct|record|struct)\s+\w*LanguageMarker\b")]
    private static partial Regex LanguageMarkerDeclaration();

    [GeneratedRegex(@"(DuplicateBlockB|SimilarSubmissionB)\.cs$")]
    private static partial Regex UnmarkedPairHalf();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private sealed class Audit(RepoPaths paths)
    {
        public List<string> Fails { get; } = [];

        private string Root => paths.RepoRoot;
        private string ToolsDir => paths.ToolsRoot;

        private void Fail(string rule, string msg) => Fails.Add($"{rule}: {msg}");

        // 1 ---------------------------------------------------------------------
        public void TriggersResolve(Dataset ds)
        {
            var onDisk = ToolFolders().ToHashSet(StringComparer.Ordinal);
            foreach (var name in onDisk.Order(StringComparer.Ordinal))
            {
                var trigger = Path.Combine(ToolsDir, name, "trigger.json");
                if (!File.Exists(trigger))
                {
                    Fail("triggers", $"{name} has no trigger.json");
                    continue;
                }

                string? runner;
                try
                {
                    runner = JsonNode.Parse(File.ReadAllText(trigger))?["runner"]?.GetValue<string>();
                }
                catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
                {
                    Fail("triggers", $"{name} trigger.json does not parse: {ex.Message}");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(runner))
                {
                    Fail("triggers", $"{name} trigger.json declares no runner");
                }
                else if (!File.Exists(Path.Combine(RepoPaths.HarnessRoot, runner)))
                {
                    Fail("triggers", $"{name} runner {runner} does not exist");
                }
                else if (ToolRegistry.Find(name) is null)
                {
                    Fail("triggers", $"{name} runner {runner} is not registered in ToolRegistry");
                }
            }

            var declared = ds.Tools.Select(Dataset.Slug).ToHashSet(StringComparer.Ordinal);
            var absent = declared.Except(onDisk).Order(StringComparer.Ordinal).ToList();
            var undeclared = onDisk.Except(declared).Order(StringComparer.Ordinal).ToList();
            if (absent.Count > 0)
            {
                Fail("triggers", "declared but absent: " + string.Join(", ", absent));
            }

            if (undeclared.Count > 0)
            {
                Fail("triggers", "on disk but undeclared: " + string.Join(", ", undeclared));
            }
        }

        // 2 ---------------------------------------------------------------------
        public void TfmAgrees(Dataset ds)
        {
            var tfm = ds.Str("targetFramework");
            foreach (var path in Files(Path.Combine(Root, "src")).Where(p => p.EndsWith(".csproj")))
            {
                var body = File.ReadAllText(path);
                var n = Path.GetFileName(path);
                if (body.Contains("<TargetFramework>"))
                {
                    if (!body.Contains($"<TargetFramework>{tfm}<"))
                    {
                        Fail("tfm", $"{n} does not target {tfm}");
                    }
                }
                else if (!body.Contains("<TargetFrameworkVersion>"))
                {
                    Fail("tfm", $"{n} declares no target framework at all");
                }
            }
        }

        // 3 ---------------------------------------------------------------------
        public void PlantedMatches(Dataset ds)
        {
            var planted = ds.Root["plantedFixtures"]!["plantedPackages"]!.AsArray()
                .ToDictionary(p => p!["id"]!.GetValue<string>(), p => p!["version"]!.GetValue<string>());
            var found = new HashSet<string>();
            string[] manifests = ["packages.config", "paket.lock", "paket.dependencies", "Directory.Packages.props"];
            foreach (var path in Files(Root))
            {
                var n = Path.GetFileName(path);
                if (!manifests.Contains(n) && !n.EndsWith(".csproj"))
                {
                    continue;
                }

                var body = File.ReadAllText(path);
                foreach (var (id, version) in planted)
                {
                    if (body.Contains(id) && body.Contains(version))
                    {
                        found.Add(id);
                    }
                }
            }

            var missing = planted.Keys.Except(found).Order(StringComparer.Ordinal).ToList();
            if (missing.Count > 0)
            {
                Fail("planted", "declared in dataset.json but in no manifest: " + string.Join(", ", missing));
            }

            var hits = 0;
            foreach (var path in Files(Path.Combine(Root, "src")).Where(p => p.EndsWith(".cs")))
            {
                var body = File.ReadAllText(path);
                hits += planted.Keys.Count(body.Contains);
            }

            if (hits < planted.Count)
            {
                Fail("planted", "not every planted package is named in source; the TypeScript "
                                + "corpus shipped declared-but-unimported packages and measured zero");
            }
        }

        // 4 ---------------------------------------------------------------------
        public void Readme()
        {
            var path = Path.Combine(Root, "README.md");
            if (!File.Exists(path))
            {
                Fail("readme", "no README.md");
                return;
            }

            var seen = File.ReadAllLines(path).Select(l => l.Trim()).Where(ReadmeOrder.Contains).ToList();
            var ordered = ReadmeOrder.Where(seen.Contains).ToList();
            if (!seen.SequenceEqual(ordered))
            {
                Fail("readme", "sections out of order: " + string.Join(", ", seen));
            }

            foreach (var want in ReadmeOrder.Where(w => !seen.Contains(w)))
            {
                Fail("readme", "missing section " + want);
            }
        }

        // 5 ---------------------------------------------------------------------
        public void DuplicatePair()
        {
            string? a = null, b = null;
            foreach (var path in Files(Path.Combine(Root, "src")))
            {
                switch (Path.GetFileName(path))
                {
                    case "DuplicateProcessorA.cs": a = File.ReadAllText(path); break;
                    case "DuplicateProcessorB.cs": b = File.ReadAllText(path); break;
                }
            }

            if (a is null || b is null)
            {
                Fail("duplicate", "the planted duplicate pair is not both present");
                return;
            }

            string Norm(string s) => Whitespace().Replace(
                s.Replace("DuplicateProcessorA", "X").Replace("DuplicateProcessorB", "X"), " ").Trim();
            if (Norm(a) != Norm(b))
            {
                Fail("duplicate", "the pair is not identical modulo names");
            }
        }

        // 6 ---------------------------------------------------------------------
        public void NoEmptyDirs()
        {
            foreach (var bas in new[] { Path.Combine(Root, "src"), ToolsDir, Path.Combine(Root, "tests") })
            {
                if (!Directory.Exists(bas))
                {
                    continue;
                }

                foreach (var dir in Directory.EnumerateDirectories(bas, "*", SearchOption.AllDirectories).Prepend(bas))
                {
                    // bin/ and obj/ are git-ignored build output, not shipped content.
                    var rel = Path.GetRelativePath(bas, dir).Split(Path.DirectorySeparatorChar);
                    if (rel.Any(s => s is "bin" or "obj"))
                    {
                        continue;
                    }

                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        Fail("empty", "empty directory " + paths.Rel(dir));
                    }
                }
            }
        }

        // 7 ---------------------------------------------------------------------
        public void XmlParses()
        {
            foreach (var path in Files(Root).Where(p => p.EndsWith(".csproj") || p.EndsWith(".props") || p.EndsWith(".config")))
            {
                try
                {
                    new XmlDocument().Load(path);
                }
                catch (XmlException ex)
                {
                    Fail("xml", $"{paths.Rel(path)} does not parse: {ex.Message}");
                }
            }
        }

        // 8 ---------------------------------------------------------------------

        /// <summary>
        /// A claim in prose is checked against the artifact it claims about, or it may
        /// not name a version at all. The first draft of this rule in the Python corpus
        /// PASSED the bug it was written to catch, because it exempted any line
        /// containing the word 'family'. The distinction that works is arithmetic: count
        /// the versions on the line. A line naming several is a comparison and is allowed.
        /// </summary>
        public void Prose(Dataset ds)
        {
            var ours = ds.Str("targetFramework");
            var allowed = ds.Root["prose"]?["additionalVersionsAllowed"]?.AsObject();
            var docs = new List<string> { Path.Combine(Root, "README.md") };
            docs.AddRange(Files(ToolsDir).Where(p => p.EndsWith(".md")));
            foreach (var doc in docs.Where(File.Exists))
            {
                var rel = paths.Rel(doc);
                var allowedHere = allowed?[rel]?.AsArray().Select(v => v!.GetValue<string>()).ToHashSet() ?? [];
                var i = 0;
                foreach (var line in File.ReadLines(doc))
                {
                    i++;
                    var found = VersionToken().Matches(line).Select(m => m.Value).ToHashSet();
                    if (found.Count == 1 && !found.Contains(ours))
                    {
                        var v = found.Single();
                        if (!allowedHere.Contains(v))
                        {
                            var text = line.Trim();
                            Fail("prose", $"{rel}:{i} states {v}, not {ours}: {text[..Math.Min(70, text.Length)]}");
                        }
                    }
                }
            }
        }

        // 9 ---------------------------------------------------------------------
        public void NoPlaceholders()
        {
            string[] exts = [".md", ".cs", ".csproj", ".json", ".cake"];
            foreach (var path in Files(Root).Where(p => exts.Contains(Path.GetExtension(p))))
            {
                foreach (Match m in Placeholder().Matches(File.ReadAllText(path)))
                {
                    Fail("placeholder", $"{paths.Rel(path)} contains {m.Value}");
                }
            }
        }

        // 10 --------------------------------------------------------------------

        /// <summary>
        /// A project manifest inside a tool folder makes the platform treat that folder
        /// as its own project and run the whole roster against it. With 19 folders on
        /// 312 branches that is a task explosion, and it is not hypothetical: the
        /// TypeScript corpus shipped 7 package-lock.json under its Clean folder and 203
        /// of 276 tasks in a run were fixture overhead.
        /// </summary>
        public void NoManifestInToolData()
        {
            foreach (var path in Files(ToolsDir))
            {
                var n = Path.GetFileName(path);
                if (n.EndsWith(".csproj") || n.EndsWith(".sln") || n.EndsWith(".nuspec") || ManifestNames.Contains(n))
                {
                    Fail("tool-data", $"{paths.Rel(path)} is a project manifest inside the tool data; it "
                                      + "would be detected as a separate project");
                }
            }
        }

        // 11 --------------------------------------------------------------------

        /// <summary>
        /// Every tool folder must carry data that could trigger its tool. Whether the
        /// tool can RUN on this family is a separate question and is recorded in
        /// trigger.json; the data has to be there either way.
        /// </summary>
        public void ToolDataPresent(Dataset ds)
        {
            foreach (var slug in ds.Tools.Select(Dataset.Slug))
            {
                var d = Path.Combine(ToolsDir, slug);
                if (!Directory.Exists(d))
                {
                    continue;
                }

                var names = Directory.GetFiles(d).Select(Path.GetFileName).ToList();
                if (!names.Contains("DATA.md"))
                {
                    Fail("tool-data", $"{slug} has no DATA.md");
                }

                var hasSource = names.Any(n => n!.EndsWith(".cs"));
                if (NoSourceTools.TryGetValue(slug, out var why))
                {
                    if (hasSource)
                    {
                        Fail("tool-data", $"{slug} ships a .cs fixture, but {why}");
                    }
                }
                else if (!hasSource)
                {
                    Fail("tool-data", $"{slug} has no .cs fixture");
                }
            }
        }

        // 12 --------------------------------------------------------------------

        /// <summary>
        /// Each fixture carries a LanguageMarker class pinning it to this family's C#
        /// version. Its absence would mean the file is at no particular version, which
        /// is the claim this corpus exists to make checkable.
        /// </summary>
        public void LanguageMarker()
        {
            foreach (var path in Files(ToolsDir).Where(p => p.EndsWith(".cs")))
            {
                // A DECLARATION, not the substring: every fixture's banner mentions
                // "the LanguageMarker class" in prose, so a substring test can never fail.
                if (LanguageMarkerDeclaration().IsMatch(File.ReadAllText(path)))
                {
                    continue;
                }

                // The B half of a duplicate / similarity pair carries no marker on
                // purpose: a marker in both halves would be part of the duplicated
                // run, and a different marker in each would break the pair.
                var rel = paths.Rel(path);
                if (!UnmarkedPairHalf().IsMatch(rel))
                {
                    Fail("tool-data", $"{rel} has no LanguageMarker");
                }
            }
        }

        // -----------------------------------------------------------------------
        private IEnumerable<string> ToolFolders() =>
            Directory.GetDirectories(ToolsDir)
                .Select(Path.GetFileName)
                .Where(n => !n!.StartsWith('.') && !n.StartsWith('_'))!;

        /// <summary>
        /// Every file under <paramref name="dir"/>, skipping .git and build output
        /// (bin/obj), which are not shipped content.
        /// </summary>
        private static IEnumerable<string> Files(string dir)
        {
            if (!Directory.Exists(dir))
            {
                yield break;
            }

            var pending = new Stack<string>();
            pending.Push(dir);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                foreach (var file in Directory.GetFiles(current))
                {
                    yield return file;
                }

                foreach (var sub in Directory.GetDirectories(current))
                {
                    var name = Path.GetFileName(sub);
                    if (name is ".git" or "bin" or "obj" or "reports")
                    {
                        continue;
                    }

                    pending.Push(sub);
                }
            }
        }
    }
}
