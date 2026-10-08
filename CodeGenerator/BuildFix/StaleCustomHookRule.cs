using System.Text.RegularExpressions;
using AppScaffolding;

namespace CodeGenerator.BuildFix;

/// <summary>
/// CS0759 / CS8795: a write-once <c>&lt;Class&gt;Validator.Custom.cs</c> still declares the hook in the
/// shape emitted before the validation pipeline. <see cref="ValidatorTemplate.HasOutdatedCustomHook"/>
/// documents why the tool does not rewrite a hand-written body. This rule changes the declaration only
/// when the hook body is EMPTY, which is an untouched stub: nothing of the user's is lost. Only the
/// declaration text is replaced, so usings, comments and other members in the file stay as they are.
/// </summary>
internal sealed class StaleCustomHookRule : IBuildFixRule
{
    private const string CustomSuffix = "Validator.Custom.cs";
    private const string GeneratedSuffix = "Validator.Generated.cs";

    // Optional modifiers, the entity type, then any parameter list, then an empty body.
    private static readonly Regex EmptyHook = new(
        @"(?:\b(?:private|static)\s+)*\bpartial\s+void\s+ValidateCustom\s*\(\s*(?<type>[\w.]+)\s+\w+\s*,[^)]*\)\s*\{\s*\}",
        RegexOptions.Compiled);

    public string Name => "update the empty validator stub";

    public bool Fix(BuildDiagnostic d, FixSession session)
    {
        if (d.Code is not ("CS0759" or "CS8795") || d.File.Length == 0)
            return false;

        var custom = d.File.EndsWith(GeneratedSuffix, StringComparison.OrdinalIgnoreCase)
            ? d.File[..^GeneratedSuffix.Length] + CustomSuffix
            : d.File;

        if (!custom.EndsWith(CustomSuffix, StringComparison.OrdinalIgnoreCase) || !File.Exists(custom))
            return false;

        var source = session.Read(custom);

        if (!ValidatorTemplate.HasOutdatedCustomHook(source))
            return false;

        var hook = EmptyHook.Match(source);

        if (!hook.Success)
            return false;

        var declaration = DocComment(source, hook.Index) + ValidatorTemplate.CustomHookDeclaration(hook.Groups["type"].Value) + " { }";
        var updated = source[..hook.Index] + declaration + source[(hook.Index + hook.Length)..];

        session.Write(custom, updated, Name);

        return true;
    }

    /// <summary>
    /// The stub's own doc comment, when the declaration has no comment above it. An old stub that
    /// has one (or any comment, or an attribute) is left exactly as it is.
    /// </summary>
    private static string DocComment(string source, int hookIndex)
    {
        var lineStart = source.LastIndexOf('\n', Math.Max(0, hookIndex - 1)) + 1;
        var indent = source[lineStart..hookIndex];

        if (indent.Trim().Length > 0)
            return string.Empty;

        var above = source[..lineStart].TrimEnd().Split('\n')[^1].Trim();

        if (above.StartsWith("//", StringComparison.Ordinal) || above.EndsWith("*/", StringComparison.Ordinal)
            || above.StartsWith('['))
            return string.Empty;

        var eol = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        return string.Concat(ValidatorTemplate.CustomHookDocLines.Select(l => l + eol + indent));
    }
}
