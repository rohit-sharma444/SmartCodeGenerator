using AppScaffolding;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>The auth core is shared, so it is emitted once by the scaffolder rather than three
/// times by the targets — and it must be absent, entirely, under `nologin`.</summary>
[TestClass]
public class AuthCoreEmissionTests
{
    private static readonly AppCustomization NoLogin =
        AppCustomization.Default with { Login = false };

    private static ScaffoldResult Run(
        AppCustomization customization, PersistenceKind persistence, ITargetScaffolder? target = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "auth-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        var request = ScaffoldRequest.For(
                TestModels.OneEntity(), "Shop", root, target ?? new WpfTarget())
            with { Customization = customization, Persistence = persistence };

        return new AppScaffolder().Scaffold(request);
    }

    private static string Scaffold(AppCustomization customization, PersistenceKind persistence)
        => Run(customization, persistence).ProjectPath;

    private static bool Has(string projectPath, params string[] parts)
        => File.Exists(Path.Combine(new[] { projectPath }.Concat(parts).ToArray()));

    [TestMethod]
    public void A_default_build_emits_the_auth_core()
    {
        var path = Scaffold(AppCustomization.Default, PersistenceKind.Json);

        Assert.IsTrue(Has(path, "Models", "User.cs"));
        Assert.IsTrue(Has(path, "Auth", "AppRole.cs"));
        Assert.IsTrue(Has(path, "Auth", "PasswordHasher.cs"));
        Assert.IsTrue(Has(path, "Auth", "AuthService.cs"));
        Assert.IsTrue(Has(path, "Auth", "UserSeed.cs"));
    }

    [TestMethod]
    public void Nologin_emits_no_auth_folder_at_all()
    {
        var path = Scaffold(NoLogin, PersistenceKind.Json);

        Assert.IsFalse(Directory.Exists(Path.Combine(path, "Auth")));
        Assert.IsFalse(Has(path, "Models", "User.cs"));
    }

    /// <summary>A target with no screens has nothing to gate, so it gets none of it however the
    /// flag is set.</summary>
    [TestMethod]
    public void A_screenless_target_never_gets_the_auth_core()
    {
        foreach (ITargetScaffolder target in
                 new ITargetScaffolder[] { new ConsoleTarget(), new WebApiTarget(), new WorkerTarget() })
        {
            var path = Run(AppCustomization.Default, PersistenceKind.Json, target).ProjectPath;

            Assert.IsFalse(
                Directory.Exists(Path.Combine(path, "Auth")),
                $"{target.Key} has no screens to gate.");
        }
    }

    /// <summary>The SQLite backend needs a table. Without the DbSet the first sign-in throws at
    /// the store, and nothing before runtime says so.</summary>
    [TestMethod]
    public void The_sqlite_context_gains_a_user_table()
    {
        var path = Scaffold(AppCustomization.Default, PersistenceKind.Sqlite);

        StringAssert.Contains(
            File.ReadAllText(Path.Combine(path, "Data", "AppDbContext.cs")), "DbSet<User>");
    }

    [TestMethod]
    public void A_nologin_sqlite_context_has_no_user_table()
    {
        var path = Scaffold(NoLogin, PersistenceKind.Sqlite);

        Assert.IsFalse(
            File.ReadAllText(Path.Combine(path, "Data", "AppDbContext.cs"))
                .Contains("DbSet<User>", StringComparison.Ordinal));
    }

    /// <summary>User is EMITTED, not DISCOVERED. Counting it would tell the user their model has
    /// a class they never described.</summary>
    [TestMethod]
    public void The_user_record_is_not_counted_as_a_discovered_class()
    {
        var result = Run(AppCustomization.Default, PersistenceKind.Json);

        StringAssert.Contains(result.Message, "1 class(es)");
    }

    /// <summary>No validator, no repository, no seed rows: User is not part of the domain the
    /// user described, and generating those would put it on screens it does not belong on.</summary>
    [TestMethod]
    public void The_user_record_gets_no_validator_no_repository_and_no_seed_rows()
    {
        var path = Scaffold(AppCustomization.Default, PersistenceKind.Json);

        Assert.IsFalse(Has(path, "Validation", "UserValidator.Generated.cs"));
        Assert.IsFalse(Has(path, "Data", "UserRepository.cs"));

        var seed = Path.Combine(path, "Data", "SeedData.cs");

        if (File.Exists(seed))
        {
            Assert.IsFalse(
                File.ReadAllText(seed).Contains("new User", StringComparison.Ordinal),
                "Sample rows are for the discovered model; accounts come from UserSeed.");
        }
    }

    /// <summary>
    /// The emitted auth core compiles against the real generated data layer, in the real
    /// namespaces — not the flattened global-namespace fixtures the template tests use.
    /// </summary>
    /// <remarks>
    /// The discovered entity is deliberately NOT in this set. A generated model file carries no
    /// <c>using System;</c> and leans on the csproj's ImplicitUsings for <c>DateTime</c>, which
    /// <c>Compiler.CompileSources</c> does not enable — including it would fail this assertion for
    /// a reason that has nothing to do with the auth core. The auth files each declare their own
    /// usings and stand up on their own, which is what this proves.
    /// </remarks>
    [TestMethod]
    public void The_emitted_auth_core_compiles_against_the_generated_data_layer()
    {
        var path = Scaffold(AppCustomization.Default, PersistenceKind.Json);

        string Read(params string[] parts)
            => File.ReadAllText(Path.Combine(new[] { path }.Concat(parts).ToArray()));

        EmittedDesktopSource.AssertCompilesTogether(
            ("User.cs", Read("Models", "User.cs")),
            ("AppRole.cs", Read("Auth", "AppRole.cs")),
            ("PasswordHasher.cs", Read("Auth", "PasswordHasher.cs")),
            ("AuthService.cs", Read("Auth", "AuthService.cs")),
            ("UserSeed.cs", Read("Auth", "UserSeed.cs")),
            // The desktop store and repository read the acting user from Session, so a WPF build
            // now names it from Data/ — see ITargetScaffolder.HasDesktopSession. Session.cs is
            // emitted beside the view models rather than by AuthCoreFiles, which is why it was
            // not already here.
            ("Session.cs", Read("Auth", "Session.cs")),
            ("IStore.cs", Read("Data", "IStore.cs")),
            ("JsonStore.cs", Read("Data", "JsonStore.cs")),
            ("StoreFactory.cs", Read("Data", "StoreFactory.cs")),
            // JsonStore now asks Permissions before every write (Task 7's coarse store guard);
            // read from the same real scaffold, not a fixture, for the same reason as everything
            // else here.
            ("ValidationContext.cs", Read("Validation", "ValidationContext.cs")),
            ("IEntityLookup.cs", Read("Validation", "IEntityLookup.cs")),
            ("Permissions.cs", Read("Validation", "Permissions.cs")));
    }
}
