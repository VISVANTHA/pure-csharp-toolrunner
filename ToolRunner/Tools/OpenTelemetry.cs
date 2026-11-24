namespace ToolRunner.Tools;

/// <summary>
/// OpenTelemetry (.NET) + OTLP exporter. net8.0 is supported. Spans come from a
/// RUNNING, compiled application with the OpenTelemetry SDK wired in, so this runs
/// only when OrderKit.sln builds (see <see cref="DotnetSolution"/>) and some project
/// references OpenTelemetry. The solution builds, but no project references it: exit 3. Note separately that tracing is not path coverage. The TypeScript corpus
/// shipped an OTel bootstrap that registered a no-op processor, exported nothing and
/// exited 0 -- a structurally zero result.
/// </summary>
public sealed class OpenTelemetry : ITool
{
    public string Slug => "opentelemetry";

    public int Run(ToolContext ctx)
    {
        DotnetSolution.RequireBuild(ctx);

        var wired = Directory.EnumerateFiles(Path.Combine(ctx.RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Any(p => File.ReadAllText(p).Contains("OpenTelemetry", StringComparison.Ordinal));
        if (!wired)
        {
            ctx.Skip("no project references the OpenTelemetry SDK, so no span can be exported");
        }

        // Exporting needs a collector to receive the spans and a counter to tell a
        // real zero from a no-op processor; neither is part of this corpus.
        ctx.Missing("no OTLP collector is configured on this host");
        return ExitCodes.Ok;
    }
}
