using AppScaffolding.Auth;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace AppScaffolding.Screens;

/// <summary>
/// Everything the screen split depends on, worked out once from a discovered model.
/// </summary>
/// <remarks>
/// Extracted from <c>AppScaffolder</c> so the build workflow's review panel can show the split
/// BEFORE anything is written. Three steps have to happen in this order and with these arguments
/// or the answer is wrong, and each was a comment in the scaffolder rather than a thing a second
/// caller could reuse:
/// <list type="number">
/// <item>the auth core occupies "User", but only in a build that HAS a login and screens;</item>
/// <item>class names are not entity names — <c>ToUniqueClassName</c> renames collisions;</item>
/// <item>the overrides are matched against the CLASS name, which is what the user's
/// <c>transactions</c>/<c>masters</c> answers therefore have to hold.</item>
/// </list>
/// A panel that re-derived any of this itself would be a second copy free to drift, which is what
/// <c>RelationshipConvention</c> exists to stop happening to relationships.
/// </remarks>
public sealed class ScreenPlan
{
    private ScreenPlan(
        IReadOnlyList<ClassDefinition> classes,
        IReadOnlyDictionary<string, string> classNames,
        EntityRoleMap roles,
        bool login)
    {
        Classes = classes;
        ClassNames = classNames;
        Roles = roles;
        Login = login;
    }

    /// <summary>The classes the model maps to, named as the app will name them.</summary>
    public IReadOnlyList<ClassDefinition> Classes { get; }

    /// <summary>Entity name to class name, for the callers that key work by the other one.</summary>
    public IReadOnlyDictionary<string, string> ClassNames { get; }

    /// <summary>Which classes get which kind of screen.</summary>
    public EntityRoleMap Roles { get; }

    /// <summary>Whether this build emits the auth core — a login AND a target with screens.</summary>
    public bool Login { get; }

    public static ScreenPlan For(
        CandidateDomainModel model, AppCustomization customization, ITargetScaffolder target)
    {
        var login = customization.Login && target.HasScreens;

        var classes = DomainModelMapper.ToClassDefinitions(
            model, out var classNames, login ? AuthCoreFiles.ReservedNames : []);

        var roles = EntityRoles.Classify(
            classes,
            ScreenOverrides.Parse(customization.Transactions, customization.Masters));

        return new ScreenPlan(classes, classNames, roles, login);
    }

    /// <summary>
    /// The class <paramref name="entityName"/> is generated as, or the entity name itself when the
    /// mapper never saw it.
    /// </summary>
    /// <remarks>
    /// The fallback is the name asked about rather than an exception: a caller holding a model
    /// that has been edited since the plan was built is asking a reasonable question, and the
    /// answer for an unrenamed entity is its own name anyway.
    /// </remarks>
    public string ClassNameOf(string entityName)
        => ClassNames.TryGetValue(entityName, out var name) ? name : entityName;

    /// <summary>The role of the class <paramref name="entityName"/> becomes.</summary>
    public EntityRole RoleOfEntity(string entityName) => Roles.RoleOf(ClassNameOf(entityName));

    /// <summary>
    /// The transaction <paramref name="entityName"/> is edited inside, or null when it is not a
    /// line at all.
    /// </summary>
    /// <remarks>
    /// The other direction from <see cref="EntityRoleMap.LinesOf"/>. A caller drawing one class
    /// at a time holds the line and wants the transaction, and working that out by scanning every
    /// transaction's lines at each call site is the kind of duplication that ends with two
    /// answers.
    /// </remarks>
    public string? TransactionHolding(string entityName)
    {
        var className = ClassNameOf(entityName);

        return Roles.Names(EntityRole.Transaction)
            .FirstOrDefault(transaction => Roles.LinesOf(transaction)
                .Any(line => string.Equals(line, className, StringComparison.OrdinalIgnoreCase)));
    }
}
