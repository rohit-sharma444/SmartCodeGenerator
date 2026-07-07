using DomainDiscovery.Model;
using DomainDiscovery.SeedExtraction;

namespace DomainDiscovery.Review;

/// <summary>
/// Pure mutations behind the interactive review loop. Each method returns whether
/// it changed the model, so the CLI can report the outcome.
/// </summary>
public static class ModelEditor
{
    public static bool AddEntity(CandidateDomainModel model, string name)
    {
        var normalized = NameNormalizer.ToEntityName(name);
        if (normalized.Length == 0 || Find(model, normalized) is not null)
            return false;

        model.Entities.Add(new CandidateEntity { Name = normalized, Provenance = Provenance.Seed });
        return true;
    }

    public static bool RemoveEntity(CandidateDomainModel model, string name)
    {
        var target = Find(model, NameNormalizer.ToEntityName(name));
        if (target is null)
            return false;

        model.Entities.Remove(target);
        return true;
    }

    public static bool RenameEntity(CandidateDomainModel model, string oldName, string newName)
    {
        var target = Find(model, NameNormalizer.ToEntityName(oldName));
        var normalizedNew = NameNormalizer.ToEntityName(newName);
        if (target is null || normalizedNew.Length == 0 || Find(model, normalizedNew) is not null)
            return false;

        target.Name = normalizedNew;
        return true;
    }

    private static CandidateEntity? Find(CandidateDomainModel model, string name)
        => model.Entities.FirstOrDefault(
            e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
}
