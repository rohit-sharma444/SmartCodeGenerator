using CodeGenerationEngine.Models;

namespace AppScaffolding;

/// <summary>Emits the sources of the companion <c>&lt;app&gt;.Tests</c> xUnit project, and the
/// one edit needed to point its .csproj at the app project.</summary>
public static class TestProjectTemplates
{
    /// <summary>A self-cleaning CRUD round-trip over each entity's typed repository. Asserts on the
    /// row it creates (not on store counts), so the tests tolerate seeded data and each other.</summary>
    public static string RoundTripTests(string rootNamespace, IReadOnlyList<string> entityNames)
    {
        var facts = string.Join("\n\n", entityNames.Select(name => $$"""
    [Fact]
    public void {{name}}_round_trips() => RoundTrip(new {{name}}Repository());
"""));

        return $$"""
using System.Reflection;
using {{rootNamespace}}.Data;
using {{rootNamespace}}.Models;
using Xunit;

namespace {{rootNamespace}}.Tests;

/// <summary>CRUD round-trip over each entity's typed repository. Each test creates a row and
/// deletes it, asserting on that row rather than on store counts.</summary>
public class RepositoryRoundTripTests
{
{{facts}}

    private static void RoundTrip<T>(Repository<T> repository) where T : class, new()
    {
        // An app with a login refuses a write from nobody. Before Fill, because Fill creates a
        // real row for a foreign key through a repository too.
        using var acting = TestSupport.ActAsAdministrator();

        // A blank record no longer round-trips: the generated validators reject it. TestSupport
        // fills it with values of the shape those rules accept.
        var item = new T();
        TestSupport.Fill(item);

        var created = repository.Create(item);
        Assert.True(created.Success, string.Join("; ", created.Errors));

        var id = IdOf(created.Item!);
        Assert.NotEqual(0, id);
        Assert.NotNull(repository.GetById(id));

        var updated = repository.Update(created.Item!);
        Assert.True(updated.Success, string.Join("; ", updated.Errors));

        var deleted = repository.Delete(id);
        Assert.True(deleted.Success, string.Join("; ", deleted.Errors));
        Assert.Null(repository.GetById(id));
    }

    private static int IdOf<T>(T item)
    {
        var id = typeof(T).GetProperty("Id",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return (int)(id?.GetValue(item) ?? 0);
    }
}
""";
    }

    /// <summary>Source of <c><paramref name="entityName"/>ValidatorTests.cs</c>. When the model has
    /// a required property, also asserts a blank record is rejected — otherwise the only honest
    /// assertion is that a filled record passes.</summary>
    /// <remarks>
    /// Its contexts act as <c>TestSupport.ActingRole</c>: the administrator in an app with a login,
    /// whose Security stage refuses a null role before any field rule runs, and nobody in one
    /// without.
    /// </remarks>
    public static string ValidatorTests(
        string rootNamespace, string entityName, string? requiredPropertyName)
    {

        var requiredFact = requiredPropertyName is null ? string.Empty : $$"""


    [Fact]
    public void A_blank_{{entityName}}_reports_its_required_field()
    {
        var context = new ValidationContext<{{entityName}}>(
            new {{entityName}}(), ValidationOperation.Insert, null, new AlwaysFound(),
            TestSupport.ActingRole, null);
        var errors = new {{entityName}}Validator().Validate(context).Messages;

        Assert.Contains("{{requiredPropertyName}} is required.", errors);
    }
""";

        return $$"""
using {{rootNamespace}}.Models;
using {{rootNamespace}}.Validation;
using Xunit;

namespace {{rootNamespace}}.Tests;

/// <summary>Rules generated for {{entityName}}. Uses a fake lookup so no store is needed.</summary>
public class {{entityName}}ValidatorTests
{
    /// <summary>Every foreign key resolves, so only the property rules are under test.</summary>
    private sealed class AlwaysFound : IEntityLookup
    {
        public bool Exists<TEntity>(int id) where TEntity : class, new() => true;

        public int CountReferencing<TEntity>(string foreignKeyProperty, int id)
            where TEntity : class, new() => 0;

        public bool AnyOtherWith<TEntity>(string propertyName, object? value, int exceptId)
            where TEntity : class, new() => false;
    }

    [Fact]
    public void A_populated_{{entityName}}_is_valid()
    {
        // Fill saves a real row for each foreign key, and an app with a login refuses a save from
        // nobody - so without this the referenced row was never written, and every desktop app
        // with a relationship failed this test on its foreign key.
        using var acting = TestSupport.ActAsAdministrator();

        var item = new {{entityName}}();
        TestSupport.Fill(item);

        var context = new ValidationContext<{{entityName}}>(
            item, ValidationOperation.Insert, null, new AlwaysFound(), TestSupport.ActingRole, null);
        var errors = new {{entityName}}Validator().Validate(context).Messages;

        Assert.Empty(errors);
    }

    [Fact]
    public void A_null_{{entityName}}_is_reported_rather_than_throwing()
    {
        var context = new ValidationContext<{{entityName}}>(
            null!, ValidationOperation.Insert, null, new AlwaysFound(), TestSupport.ActingRole, null);
        var errors = new {{entityName}}Validator().Validate(context).Messages;

        Assert.NotEmpty(errors);
    }
{{requiredFact}}
}
""";
    }

    /// <summary>Source of <c>TestSupport.cs</c>: entity population shared by the generated
    /// round-trip and validator tests.</summary>
    /// <remarks>
    /// A foreign-key-shaped int property now gets a REAL row's Id, not a placeholder: since the
    /// Entity stage checks a Transaction's or a DocumentLine's foreign key against the real store
    /// rather than skipping it at zero, a fixture that left it at 0 would fail its own generated
    /// validator. <c>ForeignKeyValue</c> reuses the referenced type's own repository -- reading an
    /// existing row when the store already has one, creating one (recursively filled the same way)
    /// when it does not -- so this works whether or not the app seeds sample data.
    /// </remarks>
    public static string TestSupport(string rootNamespace) => TestSupport(rootNamespace, login: false, sessionAuth: false);

    /// <summary>
    /// As <see cref="TestSupport(string)"/>, acting as the administrator when the app has a login.
    /// </summary>
    /// <remarks>
    /// An app with a login refuses a write from nobody, and the generated tests used to BE nobody:
    /// a Blazor app built with its default login failed its own round-trip and validator tests with
    /// "You are not permitted to insert a Customer record." The tests now say who is acting — the
    /// administrator, the one role the permission matrix lets do everything — in the way the app
    /// itself reads it: a desktop app's <c>Session</c>, or <c>ActingUser.ActAs</c> in an app with a
    /// login and no Session. The app's own checks are untouched.
    /// </remarks>
    /// <param name="login"><c>AppScaffolder</c>'s <c>auth</c>: whether the app has a login.</param>
    /// <param name="sessionAuth"><c>AppScaffolder</c>'s own flag of that name: whether that login
    /// lives in a desktop <c>Session</c>.</param>
    /// <param name="seed">Whether the app has a <c>SeedData</c> to call. When it does, the stores
    /// are seeded before any test touches one -- a real app seeds at startup, first, and a test
    /// that wrote to a store before seeding ran left it half-seeded: <c>Fill</c>'s foreign-key row
    /// made the parent store non-empty, so its seed was skipped while the child's, pointing at
    /// parents that were never written, went in.</param>
    public static string TestSupport(
        string rootNamespace, bool login, bool sessionAuth, bool seed = false,
        IReadOnlyList<ClassDefinition>? classes = null)
    {
        // The static constructor runs before the first use of TestSupport, which every round-trip
        // and validator test makes (ActAsAdministrator, Fill) before it goes near a store. Not a
        // [ModuleInitializer]: CA2255 warns about one in every generated test build.
        var accepted = string.Concat(AcceptedValues(classes ?? Array.Empty<ClassDefinition>())
            .Select(entry => $"        [{ValidatorTemplate.Quoted(entry.Key)}] = () => {entry.Expression},\n"));

        var seeding = seed
            ? $$"""
                  /// <summary>Seeds every store before the first test uses one, as the app itself does
                  /// at startup.</summary>
                  static TestSupport() => global::{{rootNamespace}}.Data.SeedData.EnsureSeeded();


              """
            : string.Empty;

        // Fully qualified, not imported: a using for the app's Models namespace would put every
        // entity's name in scope in this file, and one of them could be named like a member here.
        var acting = !login
            ? """
                  /// <summary>The role the generated tests act as. This app has no login, so there is
                  /// nobody to act as and the permission matrix refuses nobody.</summary>
                  public static string? ActingRole => null;

                  /// <summary>Nothing to do: this app has no login.</summary>
                  public static IDisposable ActAsAdministrator() => new EndScope(() => { });
              """
            : sessionAuth
            ? $$"""
                  /// <summary>The role the generated tests act as: the administrator, whom the
                  /// permission matrix lets do anything. This app has a login, and refuses a write
                  /// from nobody.</summary>
                  public static string? ActingRole => global::{{rootNamespace}}.Auth.AppRole.Admin.ToString();

                  /// <summary>Signs an administrator in to the desktop Session, and out again when the
                  /// returned scope is disposed.</summary>
                  public static IDisposable ActAsAdministrator()
                  {
                      global::{{rootNamespace}}.Auth.Session.SignIn(new global::{{rootNamespace}}.Models.User
                      {
                          UserName = "tests",
                          DisplayName = "Generated tests",
                          Role = global::{{rootNamespace}}.Auth.AppRole.Admin,
                      });

                      return new EndScope(global::{{rootNamespace}}.Auth.Session.Clear);
                  }
              """
            : $$"""
                  /// <summary>The role the generated tests act as: the administrator, whom the
                  /// permission matrix lets do anything. This app has a login, and refuses a write
                  /// from nobody.</summary>
                  public static string? ActingRole => global::{{rootNamespace}}.Auth.AppRole.Admin.ToString();

                  /// <summary>Acts as the administrator until the returned scope is disposed. This app
                  /// has no desktop Session, so it is told who is acting through ActingUser.</summary>
                  public static IDisposable ActAsAdministrator()
                      => global::{{rootNamespace}}.Validation.ActingUser.ActAs(ActingRole!, null);
              """;

        return $$"""
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace {{rootNamespace}}.Tests;

/// <summary>Builds entities that satisfy the generated validation rules.</summary>
internal static class TestSupport
{
{{seeding}}    /// <summary>Types currently being created for a caller further up the same call stack, so a
    /// foreign key that forms a cycle stops recursing instead of overflowing the stack. Never
    /// expected to hold more than one entry in a generated app, whose models describe no cycle.</summary>
    [ThreadStatic]
    private static HashSet<string>? _creating;

    /// <summary>The value for each property whose generated rules the plain values below would
    /// break -- a value list, a format, a short length, a floor above zero -- or which no two
    /// records may share. Keyed "Model.Property"; worked out from the rules when the app was
    /// generated.</summary>
    private static readonly Dictionary<string, Func<object>> Accepted = new(StringComparer.Ordinal)
    {
{{accepted}}    };

    /// <summary>Counts up from a random start, so a value no two records may share differs from
    /// the one Fill wrote for a foreign key's row, and from a row an earlier run left.</summary>
    private static int _distinct = Random.Shared.Next(100000, 900000);

    /// <summary><paramref name="template"/> with its {n} replaced by a number not used before.</summary>
    private static string Distinct(string template)
        => template.Replace("{n}", (_distinct++).ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>A midnight date in the past few years not used before. Local, as FilledDate is:
    /// a UTC date can be the local today, which the blank-record check treats as unfilled.</summary>
    private static DateTime DistinctDate() => DateTime.Today.AddDays(-(1 + _distinct++ % 3650));

    /// <summary>
    /// The date Fill writes: yesterday, at midnight, the same for every date of a record.
    /// </summary>
    /// <remarks>
    /// Not today: a new record's dates start at today, so the blank-record check counts today as
    /// unfilled, and a class of only dates and numbers came out of Fill as a blank record its own
    /// validator refused. Midnight, because a SQL date column's rule refuses a time of day; one
    /// shared date, because a rule comparing two dates is satisfied by equal ones.
    /// </remarks>
    private static readonly DateTime FilledDate = DateTime.Today.AddDays(-1);

    /// <summary>Populates every writable property with a value the validators accept.</summary>
    public static void Fill<T>(T item)
    {
        // The model's own assembly and namespace, not TestSupport's: T (e.g. Shop.Models.Order)
        // lives in the APP assembly, and that is where {Target}Repository and {Target} itself are
        // declared too -- TestSupport's own assembly is the separate Tests project and reflecting
        // against it would never find either type.
        var appAssembly = typeof(T).Assembly;
        var root = typeof(T).Namespace![..^".Models".Length];

        foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanWrite)
                continue;

            if (Accepted.TryGetValue(typeof(T).Name + "." + property.Name, out var accepted))
                property.SetValue(item, accepted());
            else if (property.PropertyType == typeof(string))
                property.SetValue(item, Placeholder(property.Name));
            else if (property.PropertyType == typeof(DateTime))
                property.SetValue(item, FilledDate);
            else if (property.PropertyType == typeof(int) && IsForeignKey(property.Name))
                property.SetValue(item, ForeignKeyValue(property.Name, appAssembly, root));
        }
    }

    /// <summary>A value of the right shape for this property. A property the validator treats as
    /// an email or a phone number needs a value of that shape, so a plain placeholder will not do.</summary>
    public static string Placeholder(string propertyName)
    {
        if (propertyName.Contains("Email", StringComparison.OrdinalIgnoreCase))
            return "test@example.com";

        if (propertyName.Contains("Phone", StringComparison.OrdinalIgnoreCase))
            return "+1 555 0100";

        return "Test";
    }

    /// <summary>Whether <paramref name="propertyName"/> is shaped like a foreign key -- the same
    /// name test AppScaffolding.ForeignKeys applies when it decides what the generated validators
    /// check.</summary>
    private static bool IsForeignKey(string propertyName)
        => propertyName.Length > 2
           && propertyName.EndsWith("Id", StringComparison.Ordinal)
           && !string.Equals(propertyName, "Id", StringComparison.OrdinalIgnoreCase);

    /// <summary>The Id of a real row of the type <paramref name="propertyName"/> references --
    /// an existing one if the store already has one, else one created through that type's own
    /// repository (and, recursively, Filled the same way this item was). Neither type existing by
    /// that name (an incidental "...Id" that names no entity) nor the creation failing is fatal:
    /// both fall back to 0, exactly what this property held before this method existed.</summary>
    private static int ForeignKeyValue(string propertyName, Assembly appAssembly, string root)
    {
        var targetName = propertyName[..^2];

        var repositoryType = appAssembly.GetType(root + ".Data." + targetName + "Repository");
        if (repositoryType is null)
            return 0;

        _creating ??= new HashSet<string>(StringComparer.Ordinal);
        if (!_creating.Add(targetName))
            return 0;

        try
        {
            var repository = Activator.CreateInstance(repositoryType)!;

            var existing = (IEnumerable)repositoryType.GetMethod("GetAll")!.Invoke(repository, null)!;
            foreach (var row in existing)
                return IdOf(row);

            var modelType = appAssembly.GetType(root + ".Models." + targetName);
            if (modelType is null)
                return 0;

            var target = Activator.CreateInstance(modelType)!;
            typeof(TestSupport).GetMethod(nameof(Fill))!.MakeGenericMethod(modelType)
                .Invoke(null, new[] { target });

            var created = repositoryType.GetMethod("Create")!.Invoke(repository, new[] { target })!;
            if (!(bool)created.GetType().GetProperty("Success")!.GetValue(created)!)
                return 0;

            var createdItem = created.GetType().GetProperty("Item")!.GetValue(created);
            return createdItem is null ? 0 : IdOf(createdItem);
        }
        finally
        {
            _creating.Remove(targetName);
        }
    }

    private static int IdOf(object item)
    {
        var id = item.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
        return (int)(id?.GetValue(item) ?? 0);
    }

{{acting}}

    /// <summary>Runs <c>end</c> when disposed: the end of an ActAsAdministrator scope.</summary>
    private sealed class EndScope : IDisposable
    {
        private readonly Action _end;

        public EndScope(Action end) => _end = end;

        public void Dispose() => _end();
    }
}
""";
    }

    /// <summary>
    /// The emitted <c>Accepted</c> table: for each property whose rules Fill's plain values break,
    /// or which no two records may share, the C# expression of a value the generated validator
    /// accepts.
    /// </summary>
    /// <remarks>
    /// Fill wrote "Test" into every string and left every number at 0, so once a user accepted a
    /// value list (Status: Open, InProgress, Closed) every generated app failed its own
    /// A_populated_..._is_valid and ..._round_trips tests. Each value here is checked against the
    /// same rules <see cref="ValidatorTemplate"/> emits, the way the emitted validator checks
    /// them, before it is chosen; a property no candidate satisfies is left to Fill's plain value.
    /// A rule spanning fields (a Business or Workflow rule) needs nothing: Fill's equal dates and
    /// zero numbers already satisfy every one the scan proposes.
    /// </remarks>
    internal static IReadOnlyList<(string Key, string Expression)> AcceptedValues(
        IReadOnlyList<ClassDefinition> classes)
    {
        var entries = new List<(string Key, string Expression)>();

        foreach (var definition in classes)
        foreach (var property in definition.Properties)
        {
            // Fill gives a foreign key a real row's Id, and the Id is the store's to assign.
            if (string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase)
                || (property.Type == "int" && IsForeignKeyName(property.Name)))
                continue;

            if (AcceptedValue(property) is { } expression)
                entries.Add(($"{definition.Name}.{property.Name}", expression));
        }

        return entries;
    }

    /// <summary>Values tried, in order, for a string with a rule "Test" breaks. Each is a shape
    /// one of the formats RuleProposals offers accepts, or short enough for a small length.</summary>
    private static readonly string[] PlainCandidates =
    [
        "Test", "test@example.com", "+1 555 0100", "www.example.com", "SW1A 1AA",
        "00000000-0000-0000-0000-000000000001", "10.0.0.1", "T", "1",
    ];

    /// <summary>As <see cref="PlainCandidates"/>, for a value no two records may share: the
    /// emitted Distinct replaces {n} with a six-digit number.</summary>
    private static readonly string[] DistinctCandidates =
    [
        "Test {n}", "test{n}@example.com", "+1 555 {n}", "www.test{n}.com",
        "00000000-0000-0000-0000-000000{n}", "T{n}", "{n}",
    ];

    /// <summary>The C# expression for <paramref name="property"/>'s value, or null when Fill's
    /// plain value already passes and need not differ from record to record.</summary>
    private static string? AcceptedValue(PropertyDefinition property)
    {
        var unique = HasRule(property, "unique")
                     && ValidatorTemplate.CanEmitEntityRule(property.Type, "unique");

        // The types Fill itself writes. A nullable number or date is left null by Fill, and a
        // null passes every rule.
        switch (property.Type)
        {
            case "string":
                return AcceptedString(property, unique);

            case "DateTime":
                // Fill's FilledDate passes every date rule; only a shared value is a problem.
                return unique ? "DistinctDate()" : null;

            case "int" or "decimal":
                return AcceptedNumber(property);

            default:
                return null;
        }
    }

    private static string? AcceptedString(PropertyDefinition property, bool unique)
    {
        var plain = Placeholder(property.Name);

        if (!unique && AcceptsString(property, plain))
            return null;

        var allowed = Rule(property, "allowedvalues")?.AllowedValues;

        // A value list: its first value that passes the other rules. Sharing one is unavoidable,
        // since a value list and "no two share" together allow only as many records as values.
        if (allowed is { Count: > 0 })
            return allowed.FirstOrDefault(value => AcceptsString(property, value)) is { } listed
                ? ValidatorTemplate.Quoted(listed)
                : null;

        if (unique)
        {
            // Two different numbers stand in for {n}: every number Distinct produces has six digits.
            var template = DistinctCandidates.FirstOrDefault(candidate =>
                    AcceptsString(property, candidate.Replace("{n}", "100000"))
                    && AcceptsString(property, candidate.Replace("{n}", "999999")));

            if (template is not null)
                return $"Distinct({ValidatorTemplate.Quoted(template)})";

            if (AcceptsString(property, plain))
                return null;
        }

        return PlainCandidates.FirstOrDefault(candidate => AcceptsString(property, candidate)) is { } value
            ? ValidatorTemplate.Quoted(value)
            : null;
    }

    /// <summary>Whether the string checks <see cref="ValidatorTemplate"/> emits for
    /// <paramref name="property"/> accept <paramref name="value"/>, judged as the emitted code
    /// judges them.</summary>
    private static bool AcceptsString(PropertyDefinition property, string value)
    {
        if (Rule(property, "maxlength")?.MaxLength is { } max && value.Length > max)
            return false;

        if (Rule(property, "allowedvalues")?.AllowedValues is { Count: > 0 } allowed
            && !allowed.Contains(value, StringComparer.Ordinal))
            return false;

        if (Rule(property, "pattern") is { RegexPattern.Length: > 0 } format
            && !System.Text.RegularExpressions.Regex.IsMatch(value, format.RegexPattern!))
            return false;

        if (property.Attributes.Contains("EmailAddress"))
        {
            var at = value.IndexOf('@');
            if (at <= 0 || at == value.Length - 1)
                return false;
        }

        if (property.Attributes.Contains("Phone")
            && value.Any(c => !char.IsDigit(c) && c is not (' ' or '+' or '-' or '(' or ')')))
            return false;

        return true;
    }

    /// <summary>A number with a floor above zero, or a ceiling below it, takes that bound;
    /// otherwise Fill's 0 passes.</summary>
    private static string? AcceptedNumber(PropertyDefinition property)
    {
        var min = Rule(property, "minvalue")?.MinValue;
        var max = Rule(property, "maxvalue")?.MaxValue;

        decimal value;
        if (min is > 0)
            value = min.Value;
        else if (max is < 0)
            value = max.Value;
        else
            return null;

        var culture = System.Globalization.CultureInfo.InvariantCulture;

        if (property.Type == "int")
        {
            var whole = value > 0 ? Math.Ceiling(value) : Math.Floor(value);
            return ((long)whole).ToString(culture);
        }

        // Rounded away from zero to the column's scale, so it stays inside its bound.
        if (Rule(property, "scale")?.Scale is { } places && decimal.Round(value, places) != value)
        {
            var factor = (decimal)Math.Pow(10, places);
            value = value > 0 ? Math.Ceiling(value * factor) / factor : Math.Floor(value * factor) / factor;
        }

        return value.ToString(culture) + "m";
    }

    private static PropertyRule? Rule(PropertyDefinition property, string name)
        => property.Rules.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

    private static bool HasRule(PropertyDefinition property, string name) => Rule(property, name) is not null;

    /// <summary>The emitted TestSupport.Placeholder, which Fill writes into a string with no
    /// entry in the table. The two must agree.</summary>
    private static string Placeholder(string propertyName)
    {
        if (propertyName.Contains("Email", StringComparison.OrdinalIgnoreCase))
            return "test@example.com";

        if (propertyName.Contains("Phone", StringComparison.OrdinalIgnoreCase))
            return "+1 555 0100";

        return "Test";
    }

    /// <summary>The emitted TestSupport.IsForeignKey.</summary>
    private static bool IsForeignKeyName(string propertyName)
        => propertyName.Length > 2
           && propertyName.EndsWith("Id", StringComparison.Ordinal)
           && !string.Equals(propertyName, "Id", StringComparison.OrdinalIgnoreCase);

    /// <summary>Asserts EnsureSeeded populates the first entity's store and is idempotent.</summary>
    public static string SeedTests(string rootNamespace, string firstEntityName) => $$"""
using {{rootNamespace}}.Data;
using Xunit;

namespace {{rootNamespace}}.Tests;

/// <summary>SeedData populates empty stores on first run and does nothing on the second.</summary>
public class SeedDataTests
{
    [Fact]
    public void EnsureSeeded_populates_stores_and_is_idempotent()
    {
        SeedData.EnsureSeeded();
        var afterFirst = new {{firstEntityName}}Repository().GetAll().Count;
        Assert.True(afterFirst > 0);

        SeedData.EnsureSeeded();
        var afterSecond = new {{firstEntityName}}Repository().GetAll().Count;
        Assert.Equal(afterFirst, afterSecond);
    }
}
""";

    /// <summary>Disables xUnit parallelization: the JSON stores are shared files, so the tests
    /// run sequentially to stay deterministic.</summary>
    /// <summary>The framework to hand `dotnet new xunit -f` for an app on
    /// <paramref name="appFramework"/>: the same one with its platform dropped, so
    /// net10.0-windows becomes net10.0.</summary>
    /// <remarks>The template lists only the plain frameworks, and SDK 10.0.401 rejects
    /// net10.0-windows as "not a valid value for -f" — which is why no WPF or WinForms app ever
    /// had a test project. The platform goes back on afterwards, through
    /// <see cref="CsprojEditing.SetTargetFramework"/>.</remarks>
    public static string TemplateFramework(string appFramework)
    {
        var dash = appFramework.IndexOf('-');
        return dash < 0 ? appFramework : appFramework[..dash];
    }

    public static string DisableParallelization() => """
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
""";

    /// <summary>Inserts a ProjectReference to the app project into a freshly-generated test
    /// .csproj, just before the closing &lt;/Project&gt; tag.</summary>
    public static string InjectProjectReference(string csproj, string relativeCsprojPath)
    {
        var itemGroup =
            "  <ItemGroup>\n" +
            $"    <ProjectReference Include=\"{relativeCsprojPath}\" />\n" +
            "  </ItemGroup>\n";

        var close = csproj.LastIndexOf("</Project>", StringComparison.Ordinal);
        return close < 0 ? csproj : csproj.Insert(close, itemGroup);
    }
}
