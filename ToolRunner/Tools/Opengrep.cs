namespace ToolRunner.Tools;

/// <summary>
/// Opengrep. Standalone binary, absent from this host. Class B when present. NOTE: the
/// sheet declares 'C# 12+', which inverts the hazard -- a tree-sitter grammar has no
/// language floor, only a construct set, so the risk runs toward NEWER syntax.
///
/// Opengrep reads Semgrep's rule format; these are the same three planted flows
/// the semgrep folder asserts, kept here so this folder is self-contained and can
/// be run without reaching into a sibling.
///
/// Rule 1 carries the same correction as the semgrep folder's copy: the shipped
/// form `string.Format("SELECT $...ANY", $...ARGS)` never matched, because
/// metavariables are not interpolated inside a string literal.
///
/// NOTE ON THE SHEET: the tool list declares Opengrep as "C# 12+". That inverts the
/// hazard. Opengrep matches through a tree-sitter C# grammar, which has no language
/// FLOOR at all -- it has a construct set. The risk therefore runs toward NEWER
/// syntax the grammar has not learned yet, not toward older syntax. On this corpus
/// the fixture core is C# 5-compatible, so the only construct that can trouble a
/// grammar is this family's language marker.
/// </summary>
public sealed class Opengrep : ITool
{
    public string Slug => "opengrep";

    public int Run(ToolContext ctx)
    {
        ctx.Missing("Standalone binary, absent from this host");
        return ExitCodes.Ok;
    }
}
