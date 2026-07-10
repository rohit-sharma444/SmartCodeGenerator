using CodeGenerationEngine.Models;
using NaturalLanguageEngine.Models.Semantics;

namespace NaturalLanguageBridge;

/// <summary>
/// Translates NLE's domain-neutral analysis into a CodeGenerationEngine
/// <see cref="IntentType"/>. This is where knowledge of code-generation
/// vocabulary lives — NLE itself never learns what a "class" is.
/// Stateless: safe for the method-parallel test run.
/// </summary>
public static class NlpCommandMapper
{
    private static readonly HashSet<string> Determiners = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "this", "that", "these", "those",
        "my", "your", "our", "their", "some", "any", "new"
    };

    // (verb, target) -> intent. Keys are lowercase. Built to mirror the
    // Actions x Targets rows of IntentRepository.GetDefaultRules().
    //
    // NLE does not reliably tag the first word of an "action target" phrase as
    // an Action entity: verbs it doesn't recognize (display, view, select,
    // switch, activate, current, active, new, recover, ...) come back as an
    // Object entity instead, and multi-word targets are sometimes split across
    // several entities (or, for "deleted classes", the modifier "deleted" is
    // itself tagged Action). Entity order matches word order in every case
    // observed, so instead of keying off EntityType, this flattens every
    // entity's text back into a single word sequence: the first word is the
    // verb and everything after it is the target. This is robust to how NLE
    // happened to type-tag each word.
    private static readonly Dictionary<(string Verb, string Target), IntentType> Table = Build();

    public static IntentType ResolveIntent(SemanticResult? analysis)
    {
        if (analysis is null)
            return IntentType.Unknown;

        var words = analysis.Entities
            .SelectMany(e => e.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(w => w.Trim().ToLowerInvariant())
            .Where(w => w.Length > 0)
            .ToArray();

        if (words.Length < 2)
            return IntentType.Unknown;

        // Prefer NLE's semantic signal: the first Action entity's last word,
        // mirroring NlpActionProvider.GetPrimaryAction. This is the reliable
        // path — trust it and stop there, no positional fallback layered on
        // top of it.
        var actionVerb = analysis.Entities
            .FirstOrDefault(e => e.Type == EntityType.Action)?
            .Text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault()?
            .ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(actionVerb))
        {
            var actionTarget = StripDeterminers(string.Join(' ', words.Skip(1)));
            return actionTarget is not null && Table.TryGetValue((actionVerb, actionTarget), out var actionIntent)
                ? actionIntent
                : IntentType.Unknown;
        }

        // Fallback (no Action entity tagged): some corpus verbs — "current",
        // "active", "selected", "new" — never come back as Action, so this is
        // still load-bearing. Try the literal first word as verb first; this
        // is exactly the old, corpus-proven behavior and covers phrases where
        // a word like "new" is both a determiner and a legitimate verb (e.g.
        // "new project"). Only if that fails to resolve AND the leading word
        // is a determiner/filler, strip it and retry — this stops a leading
        // determiner from silently becoming the verb lookup key for phrasing
        // NLE didn't tag with an Action entity.
        var remainder = words;
        while (true)
        {
            var verb = remainder[0];
            var target = StripDeterminers(string.Join(' ', remainder.Skip(1)));

            if (target is not null && Table.TryGetValue((verb, target), out var intent))
                return intent;

            if (remainder.Length > 2 && Determiners.Contains(remainder[0]))
            {
                remainder = remainder.Skip(1).ToArray();
                continue;
            }

            return IntentType.Unknown;
        }
    }

    private static string? StripDeterminers(string? text)
    {
        if (text is null)
            return null;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .SkipWhile(w => Determiners.Contains(w))
                        .ToArray();
        return words.Length == 0 ? null : string.Join(' ', words);
    }

    private static Dictionary<(string, string), IntentType> Build()
    {
        var t = new Dictionary<(string, string), IntentType>();
        void Add(IntentType intent, string[] verbs, string[] targets)
        {
            foreach (var v in verbs)
                foreach (var g in targets)
                    t[(v, g)] = intent;
        }

        Add(IntentType.ListProjects, ["list", "show", "display", "view"], ["project", "projects"]);
        Add(IntentType.UseProject, ["use", "select", "switch", "open", "activate"], ["project"]);
        Add(IntentType.CurrentProject, ["current", "active", "selected"], ["project"]);
        Add(IntentType.CreateProject, ["create", "new", "generate"], ["project", "console project", "webapi project", "class library", "worker service", "blazor project", "wpf project"]);
        Add(IntentType.CreateClass, ["create", "add", "generate"], ["class"]);
        Add(IntentType.ShowClass, ["show", "view", "display", "open"], ["class"]);
        Add(IntentType.ListClasses, ["list", "show", "display"], ["classes"]);
        Add(IntentType.DeleteClass, ["delete", "remove"], ["class"]);
        Add(IntentType.RestoreClass, ["restore", "recover"], ["class"]);
        Add(IntentType.RestoreLastClass, ["restore"], ["last class"]);
        Add(IntentType.ListDeletedClasses, ["list", "show"], ["deleted classes"]);
        Add(IntentType.RenameClass, ["rename"], ["class"]);
        Add(IntentType.CreateProperty, ["add", "create"], ["property"]);
        Add(IntentType.ListProperties, ["list", "show"], ["properties"]);
        Add(IntentType.DeleteProperty, ["delete", "remove"], ["property"]);
        Add(IntentType.CreateMethod, ["add", "create", "generate"], ["method"]);
        Add(IntentType.ListMethods, ["list", "show"], ["methods"]);
        Add(IntentType.CreateInterface, ["create", "generate"], ["interface"]);
        Add(IntentType.AnalyzeProject, ["analyze"], ["project"]);
        Add(IntentType.AnalyzeClass, ["analyze"], ["class"]);
        Add(IntentType.GenerateDocumentation, ["generate"], ["documentation", "project report"]);
        Add(IntentType.GenerateDesignDocument, ["generate"], ["design document", "architecture document", "technical document"]);
        return t;
    }
}
