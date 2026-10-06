using System.Linq;
using CodeGenerationEngine.Core.Validation;

namespace CodeGenerationEngine.Core;

/// <summary>Store of validation rules per class, keyed case-insensitively.
/// Holds both data and business rules; the command layer filters by category.</summary>
/// <remarks>
/// Once <see cref="Attach"/> has named a workspace, every change is saved to that workspace's
/// <c>.codegen/validation-rules.json</c> (see <see cref="ValidationRuleFile"/>), and attaching
/// loads it — so rules outlive the session. Undo needs nothing more: it puts rules back through
/// <see cref="Restore"/> and <see cref="Set"/>, which save like any other change. Not attached (the
/// tests' default, and after <see cref="Clear"/>), it is in memory only.
/// </remarks>
public static class ValidationRegistry
{
    private static readonly Dictionary<string, List<IValidationRule>> _rules =
        new(System.StringComparer.OrdinalIgnoreCase);

    // The text each rule was parsed from: what is saved. A rule with none is kept in memory only.
    private static readonly Dictionary<IValidationRule, string> _sources =
        new(ReferenceEqualityComparer.Instance);

    // Saved entries no parser reads any more, written back on every save rather than lost.
    private static List<SavedValidationRule> _unreadable = [];

    private static string? _file;

    /// <summary>The file changes are saved to, or null when the rules are in memory only.</summary>
    public static string? FilePath => _file;

    /// <summary>Why the last save failed, or null.</summary>
    public static string? LastSaveProblem { get; private set; }

    /// <summary>
    /// Replaces the rules with those saved in <paramref name="workspaceRoot"/>, and saves every
    /// later change there. Null detaches, leaving the registry empty and in memory only.
    /// </summary>
    public static ValidationRuleFileLoad Attach(string? workspaceRoot)
    {
        _rules.Clear();
        _sources.Clear();
        _unreadable = [];
        _file = string.IsNullOrWhiteSpace(workspaceRoot) ? null : ValidationRuleFile.PathFor(workspaceRoot);

        if (_file is null)
            return new([], [], null);

        var load = ValidationRuleFile.Read(_file);
        foreach (var (entry, rule) in load.Rules)
        {
            if (!_rules.TryGetValue(entry.ClassName, out var list))
                _rules[entry.ClassName] = list = [];
            list.Add(rule);
            _sources[rule] = entry.Text;
        }

        _unreadable = [.. load.Unreadable];
        return load;
    }

    /// <summary>Loads the attached file again, after something put an older copy of it back.</summary>
    public static void ReloadFromDisk()
    {
        if (_file is not null)
            Attach(Path.GetDirectoryName(Path.GetDirectoryName(_file)));
    }

    /// <summary>
    /// Adds <paramref name="rule"/>. <paramref name="source"/> is the text it was parsed from,
    /// which is what is saved; a rule added without one lasts only for the session.
    /// </summary>
    public static void Add(string className, IValidationRule rule, string? source = null)
    {
        if (string.IsNullOrWhiteSpace(className) || rule == null)
            return;

        if (!_rules.TryGetValue(className, out var list))
        {
            list = new List<IValidationRule>();
            _rules[className] = list;
        }

        list.Add(rule);
        if (!string.IsNullOrWhiteSpace(source))
            _sources[rule] = source.Trim();
        Save();
    }

    /// <summary>The text <paramref name="rule"/> was parsed from, or null.</summary>
    public static string? SourceOf(IValidationRule rule) =>
        _sources.TryGetValue(rule, out var text) ? text : null;

    public static IReadOnlyList<IValidationRule> Get(string className) =>
        _rules.TryGetValue(className ?? string.Empty, out var list)
            ? list
            : System.Array.Empty<IValidationRule>();

    public static IReadOnlyList<IValidationRule> Get(string className, ValidationCategory category) =>
        Get(className).Where(r => r.Category == category).ToList();

    /// <summary>Removes every rule of <paramref name="category"/> that names
    /// <paramref name="property"/>, and returns them.</summary>
    public static IReadOnlyList<IValidationRule> RemoveOn(
        string className, ValidationCategory category, string property)
    {
        if (!_rules.TryGetValue(className ?? string.Empty, out var list))
            return System.Array.Empty<IValidationRule>();

        var removed = list
            .Where(r => r.Category == category
                && r.Properties.Any(p => p.Equals(property, System.StringComparison.OrdinalIgnoreCase)))
            .ToList();

        list.RemoveAll(removed.Contains);
        if (removed.Count > 0)
            Save();
        return removed;
    }

    /// <summary>Removes the <paramref name="number"/>th rule of <paramref name="category"/>,
    /// counting from 1 in the order <see cref="Get(string, ValidationCategory)"/> returns
    /// them — the numbers the show commands print. Null when there is no such rule.</summary>
    public static IValidationRule? RemoveNumber(string className, ValidationCategory category, int number)
    {
        var rules = Get(className, category);
        if (number < 1 || number > rules.Count)
            return null;

        var rule = rules[number - 1];
        _rules[className].Remove(rule);
        Save();
        return rule;
    }

    /// <summary>A copy of every class's rules, for undo. Rules are immutable, so copying the lists
    /// is enough.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<IValidationRule>> Snapshot() =>
        _rules.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<IValidationRule>)kv.Value.ToList(),
            System.StringComparer.OrdinalIgnoreCase);

    public static void Restore(IReadOnlyDictionary<string, IReadOnlyList<IValidationRule>> snapshot)
    {
        _rules.Clear();
        foreach (var (className, rules) in snapshot)
            _rules[className] = rules.ToList();
        Save();
    }

    /// <summary>Replaces one class's rules, leaving every other class's alone.</summary>
    public static void Set(string className, IReadOnlyList<IValidationRule> rules)
    {
        _rules[className] = rules.ToList();
        Save();
    }

    /// <summary>
    /// Moves every rule of <paramref name="oldName"/> to <paramref name="newName"/>, after any it
    /// already has, for a class that was renamed. Returns how many rules moved.
    /// </summary>
    public static int RenameClass(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)
            || !_rules.TryGetValue(oldName, out var moving) || moving.Count == 0)
            return 0;

        // Only the case changed: the key is case-insensitive, so there is nothing to move.
        if (oldName.Equals(newName, System.StringComparison.OrdinalIgnoreCase))
            return moving.Count;

        _rules.Remove(oldName);
        if (_rules.TryGetValue(newName, out var existing))
            existing.AddRange(moving);
        else
            _rules[newName] = moving;

        // Unreadable entries belong to the class by name, so they follow it too.
        _unreadable = [.. _unreadable.Select(e => e.ClassName.Equals(oldName, System.StringComparison.OrdinalIgnoreCase)
            ? e with { ClassName = newName } : e)];

        Save();
        return moving.Count;
    }

    /// <summary>Drops every rule of a class that was deleted. Returns how many rules were dropped.</summary>
    public static int RemoveClass(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !_rules.TryGetValue(name, out var list))
            return 0;

        _rules.Remove(name);
        _unreadable.RemoveAll(e => e.ClassName.Equals(name, System.StringComparison.OrdinalIgnoreCase));
        Save();
        return list.Count;
    }

    /// <summary>Empties the registry and detaches it from any file, which is left as it is.</summary>
    public static void Clear() => Attach(null);

    // Writes every rule that has its text, class by class in the order they were added, plus the
    // entries that could not be read. A workspace folder that is gone (a test's, deleted) is not
    // recreated.
    private static void Save()
    {
        if (_file is null || Path.GetDirectoryName(Path.GetDirectoryName(_file)) is not { } root
            || !Directory.Exists(root))
            return;

        var entries = _rules
            .SelectMany(kv => kv.Value
                .Where(_sources.ContainsKey)
                .Select(rule => new SavedValidationRule(kv.Key, rule.Category, _sources[rule])))
            .Concat(_unreadable)
            .ToList();

        LastSaveProblem = ValidationRuleFile.Write(_file, entries);
        if (LastSaveProblem is not null)
            global::CodeGenerationEngine.Helper.Output.Warning(
                $"The validation rules could not be saved to {_file}: {LastSaveProblem}");
    }
}
