using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Bridge;

/// <summary>
/// (phrase, expected intent) pairs generated from the Actions x Targets
/// cross-product of IntentRepository.GetDefaultRules(), restricted to the 24
/// intents that have both a rule and a dispatch handler. Copied here as a
/// fixture so it outlives the deletion of IntentRepository in Plan 2.
/// </summary>
internal static class IntentCorpus
{
    // One (actions, targets, intent) row per gate rule. Phrases are the
    // cartesian product "action target".
    private static readonly (string[] Actions, string[] Targets, IntentType Intent)[] Rules =
    [
        (["list", "show", "display", "view"], ["project", "projects"], IntentType.ListProjects),
        (["use", "select", "switch", "open", "activate"], ["project"], IntentType.UseProject),
        (["current", "active", "selected"], ["project"], IntentType.CurrentProject),
        (["create", "new", "generate"], ["project", "console project", "webapi project", "class library", "worker service", "blazor project", "wpf project"], IntentType.CreateProject),
        (["create", "add", "generate"], ["class"], IntentType.CreateClass),
        (["show", "view", "display", "open"], ["class"], IntentType.ShowClass),
        (["list", "show", "display"], ["classes"], IntentType.ListClasses),
        (["list", "show", "display"], ["classes in"], IntentType.ListClassesInProject),
        (["delete", "remove"], ["class"], IntentType.DeleteClass),
        (["restore", "recover"], ["class"], IntentType.RestoreClass),
        (["restore"], ["last class"], IntentType.RestoreLastClass),
        (["list", "show"], ["deleted classes"], IntentType.ListDeletedClasses),
        (["rename"], ["class"], IntentType.RenameClass),
        (["add", "create"], ["property"], IntentType.CreateProperty),
        (["list", "show"], ["properties"], IntentType.ListProperties),
        (["delete", "remove"], ["property"], IntentType.DeleteProperty),
        (["add", "create", "generate"], ["method"], IntentType.CreateMethod),
        (["list", "show"], ["methods"], IntentType.ListMethods),
        (["create", "generate"], ["interface"], IntentType.CreateInterface),
        (["analyze"], ["project"], IntentType.AnalyzeProject),
        (["analyze"], ["class"], IntentType.AnalyzeClass),
        (["generate"], ["documentation", "project report"], IntentType.GenerateDocumentation),
        (["generate"], ["design document", "architecture document", "technical document"], IntentType.GenerateDesignDocument),
    ];

    public static IReadOnlyList<(string Phrase, IntentType Expected)> GateEntries { get; } =
        Rules
            .SelectMany(r => r.Actions
                .SelectMany(a => r.Targets.Select(t => ($"{a} {t}", r.Intent))))
            .ToList();
}
