namespace AppScaffolding.Screens;

/// <summary>What the classifier decided, for one discovered model.</summary>
/// <remarks>
/// Every lookup is case-insensitive: a caller asking about "order" means Order, and class names
/// double as file names on a case-insensitive filesystem anyway.
/// </remarks>
public sealed class EntityRoleMap
{
    private readonly IReadOnlyDictionary<string, EntityRole> _roles;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _lines;

    public EntityRoleMap(
        IReadOnlyDictionary<string, EntityRole> roles,
        IReadOnlyDictionary<string, IReadOnlyList<string>> lines)
    {
        _roles = roles;
        _lines = lines;
    }

    /// <summary>An empty model, so a caller with nothing discovered needs no null check.</summary>
    public static EntityRoleMap Empty { get; } = new(
        new Dictionary<string, EntityRole>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// The role of <paramref name="name"/>, or <see cref="EntityRole.Master"/> when it was never
    /// classified.
    /// </summary>
    /// <remarks>
    /// Master rather than an exception for an unknown name: the safe answer is the screen this
    /// generator has always produced, and a caller asking about an entity the classifier never saw
    /// is asking a reasonable question with a reasonable answer.
    /// </remarks>
    public EntityRole RoleOf(string name)
        => _roles.TryGetValue(name, out var role) ? role : EntityRole.Master;

    /// <summary>Every name with <paramref name="role"/>, in the order they were discovered.</summary>
    public IReadOnlyList<string> Names(EntityRole role)
        => _roles.Where(pair => pair.Value == role).Select(pair => pair.Key).ToList();

    /// <summary>The line entities belonging to <paramref name="transactionName"/>, or empty.</summary>
    public IReadOnlyList<string> LinesOf(string transactionName)
        => _lines.TryGetValue(transactionName, out var lines) ? lines : [];
}
