using ToolRunner;
using ToolRunner.Tools;

// Entry points (from the repository root):
//
//   dotnet run --project ToolRunner                        banner
//   dotnet run --project ToolRunner -- --verify            every runner present
//   dotnet run --project ToolRunner -- --run               run them all, compare to dataset.json
//   dotnet run --project ToolRunner -- --check             cross-file consistency audit
//   dotnet run --project ToolRunner -- --tool <slug>       run one tool's runner
//
// Exit codes follow ExitCodes: --tool returns the runner's contract code, --run
// returns 2 when a result is not already recorded in dataset.json.

try
{
    var paths = RepoPaths.Discover();
    var mode = args.Length > 0 ? args[0].TrimStart('-') : "";
    return mode switch
    {
        "" or "banner" => Integration.Banner(paths),
        "verify" => Integration.Verify(paths),
        "run" => Integration.RunAll(paths),
        "check" => FullCheck.Run(paths),
        "tool" when args.Length == 2 => RunOne(paths, args[1]),
        _ => Usage(),
    };
}
catch (Exception ex) when (ex is InvalidOperationException or IOException or InvalidDataException)
{
    Console.Error.WriteLine("ToolRunner: " + ex.Message);
    return ExitCodes.Fail;
}

static int RunOne(RepoPaths paths, string slug)
{
    var tool = ToolRegistry.Find(slug);
    if (tool is null)
    {
        Console.Error.WriteLine("no runner for '" + slug + "'. Known: "
                                + string.Join(", ", ToolRegistry.All.Select(t => t.Slug)));
        return ExitCodes.Fail;
    }

    return Integration.Execute(tool, new ToolContext(paths, slug, Console.Out, Console.Error));
}

static int Usage()
{
    Console.Error.WriteLine("usage: ToolRunner [--verify | --run | --check | --tool <slug>]");
    return ExitCodes.Fail;
}
