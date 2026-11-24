using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ToolRunner;

/// <summary>Writes report JSON with two-space indentation.</summary>
public static class JsonOut
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void Write(string path, JsonNode node) =>
        File.WriteAllText(path, node.ToJsonString(Options) + "\n");

    /// <summary>A number as JSON: integral values without a fraction.</summary>
    public static JsonNode Number(double value) =>
        value == Math.Floor(value) && Math.Abs(value) < 1e15
            ? JsonValue.Create((long)value)
            : JsonValue.Create(value);
}
