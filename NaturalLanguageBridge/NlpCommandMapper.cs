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

        var verb = words[0];
        var target = StripDeterminers(string.Join(' ', words.Skip(1)));

        if (target is null)
            return IntentType.Unknown;

        return Table.TryGetValue((verb, target), out var intent)
            ? intent
            : IntentType.Unknown;
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
                    t[(v, g)] = intent; // last write wins; order rows by ascending priority
        }

        Add(IntentType.ListProjects, ["list", "show", "display", "view"], ["project", "projects"]);
        Add(IntentType.UseProject, ["use", "select", "switch", "open", "activate"], ["project"]);
        Add(IntentType.CurrentProject, ["current", "active", "selected"], ["project"]);
        Add(IntentType.CreateProject, ["create", "new", "generate"], ["project", "console project", "webapi project", "class library", "worker service", "blazor project", "wpf project"]);
        Add(IntentType.CreateClass, ["create", "add", "generate"], ["class"]);
        Add(IntentType.ShowClass, ["show", "view", "display", "open"], ["class"]);
        Add(IntentType.ListClasses, ["list", "show", "display"], ["classes"]);
        Add(IntentType.ListClassesInProject, ["list", "show", "display"], ["classes in"]);
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
