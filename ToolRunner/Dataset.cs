using System.Text.Json.Nodes;

namespace ToolRunner;

/// <summary>Read-only view of dataset.json, the corpus's asserted answers.</summary>
public sealed class Dataset
{
    public JsonNode Root { get; }

    private Dataset(JsonNode root) => Root = root;

    public static Dataset Load(string path) =>
        new(JsonNode.Parse(File.ReadAllText(path))
            ?? throw new InvalidDataException(path + " is empty"));

    public string Str(string key) => Root[key]?.GetValue<string>() ?? "";

    public IEnumerable<JsonNode> Tools => Root["tools"]!.AsArray().Select(t => t!);

    public static string Slug(JsonNode tool) => tool["slug"]!.GetValue<string>();

    public static int ExpectedExitCode(JsonNode tool) => tool["expectedExitCode"]!.GetValue<int>();
}
