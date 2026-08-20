using AppScaffolding;
using AppScaffolding.Auth;
using AppScaffolding.Targets;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

/// <summary>
/// A discovered entity called User collides with the emitted auth model.
/// </summary>
/// <remarks>
/// "A system with users, roles and orders" is an entirely ordinary description, and it produced a
/// broken project: the auth core's <c>Models/User.cs</c> is written after the discovered classes
/// and OVERWRITES the discovered one, so the generated UserRepository, UserValidator, seed rows
/// and EntityViewModel&lt;User&gt; all compile against the wrong shape. Under SQLite it is worse —
/// the entity list gains a second "User" and AppDbContext emits <c>DbSet&lt;User&gt;</c> twice,
/// which is CS0102.
/// </remarks>
[TestClass]
public class UserNameCollisionTests
{
    /// <summary>What DomainModelMapper renames the discovered entity to: its own
    /// DisambiguatingSuffix applied to the reserved name, the same as any other collision.</summary>
    private const string RenamedEntity = "UserItem";

    private static CandidateDomainModel ModelWithAUserEntity()
    {
        var model = new CandidateDomainModel();

        model.Entities.Add(new CandidateEntity
        {
            Name = "User",
            Fields = { new CandidateField { Name = "Nickname", Type = "string" } },
        });

        model.Entities.Add(new CandidateEntity
        {
            Name = "Order",
            Fields = { new CandidateField { Name = "Reference", Type = "string" } },
        });

        return model;
    }

    private static ScaffoldResult Scaffold(PersistenceKind persistence, bool login = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "coll-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        var request = ScaffoldRequest.For(
                ModelWithAUserEntity(), "Shop", root, new WpfTarget())
            with
            {
                Persistence = persistence,
                Customization = AppCustomization.Default with { Login = login },
            };

        return new AppScaffolder().Scaffold(request);
    }

    /// <summary>The discovered entity is renamed, not dropped: the user described it and it has
    /// to appear somewhere.</summary>
    [TestMethod]
    public void The_discovered_entity_is_renamed_rather_than_lost()
    {
        var result = Scaffold(PersistenceKind.Json);

        Assert.IsTrue(
            File.Exists(Path.Combine(result.ProjectPath, "Models", $"{RenamedEntity}.cs")),
            "the discovered User should have been renamed, not silently replaced");

        var renamed = File.ReadAllText(
            Path.Combine(result.ProjectPath, "Models", $"{RenamedEntity}.cs"));

        StringAssert.Contains(renamed, "Nickname", "the renamed class keeps the discovered fields");
    }

    /// <summary>And the auth model survives intact, or nobody can sign in.</summary>
    [TestMethod]
    public void The_auth_model_is_the_one_that_keeps_the_name()
    {
        var result = Scaffold(PersistenceKind.Json);

        var user = File.ReadAllText(Path.Combine(result.ProjectPath, "Models", "User.cs"));

        StringAssert.Contains(user, "PasswordHash");
        Assert.IsFalse(
            user.Contains("Nickname", StringComparison.Ordinal),
            "the discovered class overwrote the auth model");
    }

    /// <summary>The exact failure the collision caused under SQLite: two DbSets of the same
    /// name.</summary>
    [TestMethod]
    public void The_sqlite_context_declares_each_table_once()
    {
        var context = File.ReadAllText(
            Path.Combine(Scaffold(PersistenceKind.Sqlite).ProjectPath, "Data", "AppDbContext.cs"));

        var occurrences = context.Split("DbSet<User>").Length - 1;

        Assert.AreEqual(1, occurrences, "DbSet<User> is declared more than once (CS0102)");
    }

    /// <summary>With no login there is no auth model, so nothing collides and the discovered
    /// entity keeps the name the user gave it.</summary>
    [TestMethod]
    public void Nologin_leaves_a_discovered_user_entity_alone()
    {
        var result = Scaffold(PersistenceKind.Json, login: false);

        Assert.IsTrue(File.Exists(Path.Combine(result.ProjectPath, "Models", "User.cs")));

        StringAssert.Contains(
            File.ReadAllText(Path.Combine(result.ProjectPath, "Models", "User.cs")), "Nickname");
    }

    /// <summary>The rename has to be visible, or the user goes looking for a class that is not
    /// there under the name they described.</summary>
    [TestMethod]
    public void The_rename_is_reported()
        => StringAssert.Contains(Scaffold(PersistenceKind.Json).Message, RenamedEntity);
}
