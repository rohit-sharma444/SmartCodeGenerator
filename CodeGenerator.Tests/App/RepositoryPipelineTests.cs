using AppScaffolding;
using AppScaffolding.Auth;
using AppScaffolding.Targets;
using CodeGenerationEngine.Core;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Repository&lt;T&gt; is the app's only write path, so it is where the pipeline has to run: it
/// builds a context for each of the three writes and refuses the write when a stage says no.
/// </summary>
/// <remarks>
/// Half of these assertions are substrings over emitted source, which cannot see whether the code
/// they match COMPILES — that is what the compile tests at the bottom are for, and why they cover
/// the flag combinations rather than only the default one.
/// </remarks>
[TestClass]
public class RepositoryPipelineTests
{
    [TestMethod]
    public void UpdatePassesTheStoredVersion_NotJustTheSubmittedOne()
    {
        // Stored is what makes an immutable-field rule and a status transition expressible at
        // all. An Update that passed null would leave both stages unable to tell what changed.
        var source = RepositoryTemplate.Base("Acme", validation: true);

        StringAssert.Contains(source, "var stored = GetById(GetId(item));");
        StringAssert.Contains(source, "item, ValidationOperation.Update, stored, new StoreEntityLookup(),");
    }

    [TestMethod]
    public void CreatePassesNoStoredVersion()
    {
        // An Insert has nothing on disk yet. Passing the submitted record as its own stored
        // version would make every "did this field change" rule answer no.
        var source = RepositoryTemplate.Base("Acme", validation: true);

        StringAssert.Contains(source, "item, ValidationOperation.Insert, null, new StoreEntityLookup(),");
    }

    [TestMethod]
    public void DeleteRunsThePipeline_AndReturnsItsReason()
    {
        // Delete is a new call site: nothing validated a delete before this phase. It returns a
        // SaveResult rather than a bool because a refusal that cannot say why is indistinguishable
        // from "no such record", and the console and Web API surface those differently.
        var source = RepositoryTemplate.Base("Acme", validation: true);

        StringAssert.Contains(source, "public SaveResult<T> Delete(int id)");
        StringAssert.Contains(source, "ValidationOperation.Delete");
    }

    [TestMethod]
    public void DeleteValidatesTheRecordThatIsAboutToGo_NotASubmittedOne()
    {
        // Delete is handed an id, not a record. The stored row is both the item under judgement
        // and the stored version: a delete rule reads the row's own fields and its references.
        var source = RepositoryTemplate.Base("Acme", validation: true);

        StringAssert.Contains(source, "var doomed = GetById(id);");
        StringAssert.Contains(source, "doomed, ValidationOperation.Delete, doomed, new StoreEntityLookup(),");
    }

    [TestMethod]
    public void DeleteWithNoSuchRecord_IsNotFound_NotARefusal()
    {
        var source = RepositoryTemplate.Base("Acme", validation: true);

        StringAssert.Contains(source, "NotFound = true");
    }

    [TestMethod]
    public void ARefusalCarriesItsFieldAttribution_AndItsSentences()
    {
        // Task 6's Failures, so the Web API's 400 payload can be keyed by property. Errors stays
        // beside it for a front end that only prints sentences.
        var source = RepositoryTemplate.Base("Acme", validation: true);

        StringAssert.Contains(
            source,
            "return new SaveResult<T> { Success = false, Failures = outcome.Errors, Errors = outcome.Messages };");
    }

    [TestMethod]
    public void WithValidationOff_DeleteStillReturnsASaveResult()
    {
        // The flag changes which checks run, never the shape of the API. A Delete that returned
        // bool under novalidation and SaveResult otherwise would make every caller conditional.
        var source = RepositoryTemplate.Base("Acme", validation: false);

        StringAssert.Contains(source, "public SaveResult<T> Delete(int id)");
        Assert.DoesNotContain("ValidationContext", source);

        // _validator, not "ValidationPipeline": the repository names the pipeline type under
        // NEITHER flag, so asserting its absence was vacuous and would have passed over a
        // repository that validated everything. The field IS emitted when validation is on.
        Assert.DoesNotContain("_validator", source);
    }

    /// <summary>A rejected write is the one outcome that is neither a success nor an exception,
    /// so nothing else in the app would record it. It survived the move onto the new seam.</summary>
    [TestMethod]
    public void ARejectedWriteIsStillLogged_OnTheNewOutcome()
    {
        var source = RepositoryTemplate.Base("Acme", validation: true, logging: true);

        StringAssert.Contains(
            source, "Log.LogWarning(\"Rejected {Entity}: {Error}\", typeof(T).Name, messages[0]);");

        Assert.IsFalse(
            RepositoryTemplate.Base("Acme", validation: true).Contains("LogWarning", StringComparison.Ordinal),
            "a repository that logs nothing must not warn either");
    }

    /// <summary>A missing record is not a rejection, and logging it as one would report a refusal
    /// that never happened.</summary>
    [TestMethod]
    public void AMissingRecordIsNotLoggedAsARejection()
    {
        var source = RepositoryTemplate.Base("Acme", validation: true, logging: true);

        var guard = source.IndexOf("if (doomed is null)", StringComparison.Ordinal);
        Assert.IsGreaterThan(0, guard, "the delete guard should be emitted");

        var afterGuard = source[guard..];
        var earlyReturn = afterGuard.IndexOf("NotFound = true", StringComparison.Ordinal);
        var warning = afterGuard.IndexOf("LogWarning", StringComparison.Ordinal);

        Assert.IsGreaterThan(0, earlyReturn, "the guard should return NotFound");
        Assert.IsGreaterThan(0, warning, "the delete should still warn when it is refused");
        Assert.IsLessThan(warning, earlyReturn, "the missing record must return before any warning");
    }

    /// <summary>Same call site as the login case below, on purpose: the repository no longer
    /// decides this itself -- <c>auth</c> now goes unused here, and the "nobody is acting" half
    /// of the property lives entirely in <see cref="ActingUserTemplate"/>.</summary>
    [TestMethod]
    public void WithoutADesktopSession_ThereIsNobodyActing()
    {
        // Four of the seven targets have no login at all, and Blazor has one with no Session. A
        // null role is what the matrix reads as "no notion of who is acting".
        var source = RepositoryTemplate.Base("Acme", validation: true, logging: false, auth: false);

        StringAssert.Contains(source, "ActingUser.Role(), ActingUser.Id()");

        StringAssert.Contains(
            ActingUserTemplate.Source("Acme", sessionAuth: false),
            "public static string? Role() => null;");
    }

    /// <summary>The acting user comes from the desktop session, the same way the screens and the
    /// stores read it -- one shape, so nothing can disagree about who is acting. The repository
    /// no longer decides this itself: it always calls <c>ActingUser.Role()</c>, and
    /// <see cref="ActingUserTemplate"/> alone decides, from <c>AppScaffolder</c>'s own
    /// <c>sessionAuth</c>, whether that reads a real desktop <c>Session</c>.</summary>
    [TestMethod]
    public void WithADesktopSession_TheActingUserComesFromIt()
    {
        var source = RepositoryTemplate.Base("Acme", validation: true, logging: false, auth: true);

        StringAssert.Contains(source, "ActingUser.Role(), ActingUser.Id()");

        StringAssert.Contains(
            ActingUserTemplate.Source("Acme", sessionAuth: true),
            "Session.Current?.Role.ToString()");
    }

    /// <summary>Session.cs is emitted from exactly one place, DesktopViewModelFiles, which only
    /// these two targets reach — so "has a login" is the wrong question to ask before naming
    /// it.</summary>
    [TestMethod]
    public void OnlyTheDesktopTargetsHaveASession()
    {
        foreach (var target in TargetScaffolders.ByKey.Values)
        {
            var expected = target.Key is "winforms" or "wpf";

            Assert.AreEqual(expected, target.HasDesktopSession, target.Key);
        }
    }

    /// <summary>The wiring, not the fragment: a Blazor app has screens AND a login, so anything
    /// gated on the login alone names a type that app never receives. Neither the repository nor
    /// the store name <c>Session</c> themselves any more -- that decision moved entirely into the
    /// scaffolded <c>Validation/ActingUser.cs</c>, so this is now what the Blazor/desktop split is
    /// checked against.</summary>
    [TestMethod]
    public void TheScaffolderKeepsTheSessionOutOfABlazorApp()
    {
        var (blazorRepository, blazorStore, blazorActingUser) = ScaffoldedDataFolder(new BlazorTarget());

        Assert.IsFalse(blazorRepository.Contains("Auth.Session", StringComparison.Ordinal),
            "Blazor emits no Session, so the repository may not name one");
        Assert.IsFalse(blazorStore.Contains("Auth.Session", StringComparison.Ordinal),
            "Blazor emits no Session, so the store may not name one");
        Assert.IsFalse(blazorActingUser.Contains("Session", StringComparison.Ordinal),
            "Blazor emits no Session, so ActingUser may not name one either");

        var (desktopRepository, desktopStore, desktopActingUser) = ScaffoldedDataFolder(new WpfTarget());

        StringAssert.Contains(desktopRepository, "ActingUser.Role(), ActingUser.Id()");
        StringAssert.Contains(desktopStore, "ActingUser.Role()");
        StringAssert.Contains(desktopActingUser, "Session.Current?.Role.ToString()");
    }

    /// <summary>The whole point of the raw-literal fragments: one rebuilt with "\n" would leave
    /// the output one CR per line short on a CRLF checkout, and no other assertion here would
    /// see it.</summary>
    [TestMethod]
    public void TheNewFragmentsLeaveTheLineEndingsAlone()
    {
        foreach (var validation in new[] { true, false })
        foreach (var logging in new[] { true, false })
        foreach (var auth in new[] { true, false })
        {
            var source = RepositoryTemplate.Base("Acme", validation, logging, auth);

            var newlines = source.Count(c => c == '\n');
            var carriageReturns = source.Count(c => c == '\r');

            Assert.IsTrue(
                carriageReturns == 0 || carriageReturns == newlines,
                $"mixed line endings for ({validation}, {logging}, {auth}): "
                    + $"{newlines} newline(s), {carriageReturns} carriage return(s)");
        }
    }

    [TestMethod]
    public void TheRepositoryOnTheNewSeamCompiles()
        => AssertRepositoryCompiles(validation: true, logging: false, auth: false);

    /// <summary>The logging arm builds the same context and then reads the outcome twice — once
    /// for the warning and once for the result. A substring test cannot see that the second read
    /// still type-checks.</summary>
    [TestMethod]
    public void ALoggingRepositoryOnTheNewSeamCompiles()
        => AssertRepositoryCompiles(validation: true, logging: true, auth: false);

    /// <summary>Validation and logging are independent flags: `novalidation logging` reaches
    /// Delete's logging branch with no context built above it, and that branch has to compile on
    /// its own.</summary>
    [TestMethod]
    public void ANoValidationLoggingRepositoryCompiles()
        => AssertRepositoryCompiles(validation: false, logging: true, auth: false);

    /// <summary>The desktop arm names Session.Current?.Role and .Id. Both are real members of a
    /// real emitted type, and this is the only check that says so.</summary>
    [TestMethod]
    public void ARepositoryReadingTheDesktopSessionCompiles()
        => AssertRepositoryCompiles(validation: true, logging: false, auth: true);

    /// <summary>Scaffolds a real app and returns its emitted Repository.cs, JsonStore.cs and
    /// Validation/ActingUser.cs.</summary>
    private static (string Repository, string Store, string ActingUser) ScaffoldedDataFolder(
        ITargetScaffolder target)
    {
        var root = Path.Combine(Path.GetTempPath(), "repo-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        try
        {
            var result = new AppScaffolder().Scaffold(
                ScaffoldRequest.For(TestModels.OneEntity(), "Shop", root, target));

            Assert.IsTrue(result.Success, result.Message);

            return (
                File.ReadAllText(Path.Combine(result.ProjectPath, "Data", "Repository.cs")),
                File.ReadAllText(Path.Combine(result.ProjectPath, "Data", "JsonStore.cs")),
                File.ReadAllText(Path.Combine(result.ProjectPath, "Validation", "ActingUser.cs")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }

    /// <summary>
    /// Compiles the emitted Repository.cs against every file it names, each as its own syntax tree
    /// keeping its own real namespace — so the cross-namespace references a generated app actually
    /// makes are the ones under test, rather than a flattened approximation of them.
    /// </summary>
    private static void AssertRepositoryCompiles(bool validation, bool logging, bool auth)
    {
        var files = new List<(string Path, string Code)>
        {
            ("Models/Order.cs", ModelsStub),
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Validation/Permissions.cs", PermissionsTemplate.Source("Acme", Array.Empty<string>(), login: false)),
            // The repository under test calls ActingUser.Role()/.Id() unconditionally inside its
            // validation arm; sessionAuth mirrors auth, exactly what Auth/Session.cs below is
            // gated on too.
            ("Validation/ActingUser.cs", ActingUserTemplate.Source("Acme", sessionAuth: auth)),
            ("Data/IStore.cs", StoreSeamTemplate.Source("Acme")),
            ("Data/SaveResult.cs", SaveResultTemplate.Source("Acme")),
            ("Data/StoreFactory.cs", StoreFactoryTemplate.Source("Acme", PersistenceKind.Json)),
            ("Data/JsonStore.cs", JsonStoreTemplate.Source("Acme.Data", auth)),
            ("Data/Repository.cs", RepositoryTemplate.Base("Acme", validation, logging, auth)),
        };

        if (validation)
        {
            files.Add(("Validation/IValidator.cs", ValidatorTemplate.ValidatorInterface("Acme")));
            files.Add(("Validation/StoreEntityLookup.cs", ValidatorTemplate.StoreEntityLookup("Acme")));
            files.Add(("Validation/ValidatorFactory.cs",
                ValidatorTemplate.Factory("Acme", Array.Empty<string>())));
        }

        // Microsoft.Extensions.Logging is not among Compiler.Compile's references, and pulling the
        // package in to type-check two method names would be a poor trade — see LoggingTests.
        if (logging)
            files.Add(("Logging/AppLog.cs", LoggingStubs));

        if (auth)
        {
            files.Add(("Models/User.cs", AuthModelTemplates.User("Acme")));
            files.Add(("Auth/AppRole.cs", AuthModelTemplates.Role("Acme")));
            files.Add(("Auth/Session.cs", SessionTemplate.Source("Acme")));
        }

        var result = Compiler.CompileSources(files);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();

        Assert.IsEmpty(errors, "Compilation errors:\n" + string.Join("\n", errors));
        Assert.IsTrue(result.Success);
    }

    /// <summary>One entity, so the emitted ValidatorFactory's `using Acme.Models;` resolves. The
    /// factory is generated with no entities here: Repository is generic and reaches it through
    /// NullValidator, which is what the compile is checking.</summary>
    private const string ModelsStub = """
        namespace Acme.Models;

        public sealed class Order
        {
            public int Id { get; set; }
        }
        """;

    /// <summary>Stand-ins for the two Microsoft.Extensions.Logging members the emitted repository
    /// calls, and for the app's own AppLog. Real namespaces, so the emitted usings resolve.</summary>
    private const string LoggingStubs = """
        namespace Microsoft.Extensions.Logging
        {
            public interface ILogger
            {
            }

            public static class LoggerExtensions
            {
                public static void LogInformation(this ILogger logger, string message, params object?[] args) { }

                public static void LogWarning(this ILogger logger, string message, params object?[] args) { }
            }
        }

        namespace Acme.Logging
        {
            public static class AppLog
            {
                public static Microsoft.Extensions.Logging.ILogger For<T>() => null!;
            }
        }
        """;
}
