using System.Text.Json;

namespace CodeCommenterPro.Helpers;

public class RuleLoader
{
    private static RuleSet? _cached;

    public static RuleSet Load(string path = "Config/rules.json")
    {
        if (_cached != null)
            return _cached;

        if (!File.Exists(path))
            return _cached = new RuleSet();

        var json = File.ReadAllText(path);
        var set = JsonSerializer.Deserialize<RuleSet>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new RuleSet();

        _cached = set;
        return set;
    }
}

public class RuleSet
{
    public List<PatternRule> Patterns { get; set; } = new();

    public List<PatternRule> BusinessRules { get; set; } = new();
}

public class PatternRule
{
    public string Match { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}
