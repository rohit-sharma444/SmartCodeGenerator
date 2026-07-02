public sealed class DomainConcept
{
    public string Name { get; set; } = string.Empty;

    public int UsageCount { get; set; }

    public HashSet<string> RelatedConcepts { get; } = new();

    public HashSet<string> Methods { get; } = new();

    public HashSet<string> Properties { get; } = new();
}


public sealed class DomainModel
{
    public Dictionary<string, DomainConcept> Concepts { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public DomainConcept GetOrCreate(
        string name)
    {
        if (!Concepts.TryGetValue(name, out var concept))
        {
            concept = new DomainConcept
            {
                Name = name
            };

            Concepts[name] = concept;
        }

        return concept;
    }
}