namespace ToolRunner;

/// <summary>
/// Exit-code contract shared by every runner:
///   0 ran | 1 failed | 3 SKIPPED (cannot run here) | 4 NOT INSTALLED (absent from host)
///
/// The status namespace is OURS. A tool's own exit codes are translated at this
/// boundary and are never allowed to escape as 3 or 4. 2 is reserved for
/// <c>--run</c>: a result that dataset.json does not already record.
/// </summary>
public static class ExitCodes
{
    public const int Ok = 0;
    public const int Fail = 1;
    public const int Unrecorded = 2;
    public const int Skip = 3;
    public const int Missing = 4;

    public static string Name(int rc) => rc switch
    {
        Ok => "ran",
        Fail => "FAILED",
        Skip => "skipped",
        Missing => "not installed",
        _ => "?",
    };
}

/// <summary>Thrown to end a runner early with a contract exit code.</summary>
public sealed class ToolExitException(int exitCode) : Exception($"tool exited {exitCode}")
{
    public int ExitCode { get; } = exitCode;
}
