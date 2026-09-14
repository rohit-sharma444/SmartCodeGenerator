using System.Linq;
using AppScaffolding;
using AppScaffolding.Auth;
using AppScaffolding.Screens;
using CodeGenerationEngine.Core;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The grids delete without going anywhere near <c>Repository&lt;T&gt;</c>, so the pipeline the
/// rest of this phase built never sees them.
/// </summary>
/// <remarks>
/// The plain grid is the sharper case: it removes the row from an ObservableCollection, says "Not
/// saved yet", and persists the whole list later through one <c>SaveAll</c>. A whole-collection
/// overwrite cannot express "a record was deleted", so the store's coarse guard has nothing to
/// refuse and there is nowhere later to ask. The button itself is therefore the enforcement point.
/// </remarks>
[TestClass]
public class GridDeletePermissionTests
{
    [TestMethod]
    public void TheMasterScreenAsksTheMatrix_NotSessionIsAdmin()
    {
        // Same refusal as before for the default matrix, but routed through the one place that
        // decides it. The hardcoded check could not be changed by a matrix and could not agree
        // with the store's.
        var source = MasterScreenTemplate.Source("Acme", login: true);

        StringAssert.Contains(source, "Permissions.Allows");
        Assert.DoesNotContain("Session.IsAdmin", source);
    }

    [TestMethod]
    public void ThePlainGridDelete_GainsItsFirstCheck()
    {
        // ViewModelTemplates.Delete() had NO check of any kind: it removed the row from an
        // ObservableCollection and said "Not saved yet". The later SaveAll cannot tell that a
        // delete happened, so this is the only point where it can be refused.
        var source = ViewModelTemplates.EntityViewModel("Acme", validation: true, login: true);

        StringAssert.Contains(source, "Permissions.Allows(ValidationOperation.Delete");
    }

    [TestMethod]
    public void WithNoLogin_TheGridDoesNotNameSession()
    {
        // A target with screens but nologin emits no auth core, so naming Session would not
        // compile.
        var source = ViewModelTemplates.EntityViewModel("Acme", validation: true, login: false);

        Assert.DoesNotContain("Session.", source);
    }

    /// <summary>Permissions sit outside the novalidation flag, so the button has to be guarded in
    /// a `novalidation` build too — that build is exactly where nothing else would refuse it.
    /// </summary>
    [TestMethod]
    public void TurningValidationOffDoesNotTurnThePermissionOff()
    {
        var source = ViewModelTemplates.EntityViewModel("Acme", validation: false, login: true);

        StringAssert.Contains(source, "Permissions.Allows(ValidationOperation.Delete");
        StringAssert.Contains(source, "using Acme.Validation;");

        // ...and still names no validator: the flag drops the pipeline, not the matrix.
        Assert.IsFalse(source.Contains("ValidatorFactory", StringComparison.Ordinal));
    }

    /// <summary>The acting user comes from the desktop session, the same way the store and the
    /// repository read it — one shape, so a screen and a store cannot disagree about who is
    /// acting.</summary>
    [TestMethod]
    public void WithADesktopSession_TheGridReadsTheRoleFromIt()
    {
        StringAssert.Contains(
            ViewModelTemplates.EntityViewModel("Acme", validation: true, login: true),
            "private static string? CurrentRole() => Acme.Auth.Session.Current?.Role.ToString();");

        StringAssert.Contains(
            MasterScreenTemplate.Source("Acme", login: true),
            "private static string? CurrentRole() => Session.Current?.Role.ToString();");
    }

    [TestMethod]
    public void WithoutADesktopSession_ThereIsNobodyActing()
    {
        StringAssert.Contains(
            ViewModelTemplates.EntityViewModel("Acme", validation: true, login: false),
            "private static string? CurrentRole() => null;");

        StringAssert.Contains(
            MasterScreenTemplate.Source("Acme"),
            "private static string? CurrentRole() => null;");
    }

    /// <summary>
    /// The Blazor grid had the same hardcoded check the master screen did, and it is the same
    /// matrix that has to decide it. Blazor has no <c>Session</c>: it carries the signed-in user
    /// as a cascading authentication state, and the role lives in a ClaimTypes.Role claim.
    /// </summary>
    [TestMethod]
    public void TheBlazorGridAsksTheMatrixFromTheRoleClaim()
    {
        foreach (var component in BlazorGrids(login: true))
        {
            StringAssert.Contains(component, "@using System.Security.Claims");
            StringAssert.Contains(component, "@using Acme.Validation");
            StringAssert.Contains(component, "private static bool MayDelete(AuthenticationState? state)");
            StringAssert.Contains(component, "state?.User.FindFirst(ClaimTypes.Role)?.Value);");
            StringAssert.Contains(component, "ValidationOperation.Delete, typeof(TRecord).Name,");
            StringAssert.Contains(
                component, "_status = Permissions.Refusal(ValidationOperation.Delete, typeof(TRecord).Name);");

            // Both would have been the same decoration said twice, and only one of them can be
            // changed by a matrix.
            Assert.DoesNotContain("IsInRole(\"Admin\")", component);
            Assert.DoesNotContain("Only an administrator can delete a record.", component);

            // The button is still hidden from a user this matrix refuses -- but from that user
            // and no other. `Roles="Admin"` hid it from a plain user on a transaction screen too,
            // where the matrix permits the delete the handler would have allowed.
            StringAssert.Contains(component, "<AuthorizeView>");
            StringAssert.Contains(component, "@if (MayDelete(context))");
            Assert.DoesNotContain("Roles=\"Admin\"", component);
        }
    }

    /// <summary>
    /// The nologin variant is a second, synchronous handler with no check at all. A null role is
    /// what the matrix reads as "no notion of who is acting", so this guard refuses nothing today
    /// — it is there so the two variants cannot drift, and so this is the line that starts
    /// refusing the day the matrix distinguishes an anonymous caller.
    /// </summary>
    [TestMethod]
    public void TheBlazorGridWithoutALoginStillAsksTheMatrix()
    {
        foreach (var component in BlazorGrids(login: false))
        {
            StringAssert.Contains(component, "@using Acme.Validation");
            StringAssert.Contains(
                component,
                "Permissions.Allows(ValidationOperation.Delete, typeof(TRecord).Name, null)");

            // No login means no authentication state to await, so the handler must stay
            // synchronous rather than grow an async signature it has nothing to await in.
            Assert.DoesNotContain("private async Task Delete()", component);
            StringAssert.Contains(component, "private void Delete()");

            // ...and it must not import a claims namespace it never reads.
            Assert.DoesNotContain("@using System.Security.Claims", component);
        }
    }

    /// <summary>
    /// The floor these substring assertions cannot reach. Every file the emitted view model names,
    /// each as its own syntax tree keeping its own real namespace — so the cross-namespace
    /// references a generated app actually makes are the ones under test, rather than a flattened
    /// approximation of them.
    /// </summary>
    [TestMethod]
    [DataRow(true, true)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(false, false)]
    public void TheGuardedGridCompiles(bool validation, bool login)
        => AssertGridCompiles(validation, login);

    /// <summary>The whole reason these fragments are raw literals: one rebuilt with "\n" would
    /// leave the output one CR per line short on a CRLF checkout, and no other assertion here
    /// would see it.</summary>
    [TestMethod]
    public void TheNewFragmentsLeaveTheLineEndingsAlone()
    {
        var sources = new List<string>();

        foreach (var login in new[] { true, false })
        {
            sources.Add(MasterScreenTemplate.Source("Acme", login));

            foreach (var validation in new[] { true, false })
                sources.Add(ViewModelTemplates.EntityViewModel("Acme", validation, login));
        }

        foreach (var source in sources)
        {
            var newlines = source.Count(c => c == '\n');
            var carriageReturns = source.Count(c => c == '\r');

            Assert.IsTrue(
                carriageReturns == 0 || carriageReturns == newlines,
                $"mixed line endings: {newlines} newline(s), {carriageReturns} carriage return(s)");
        }
    }

    private static string[] BlazorGrids(bool login) =>
    [
        BlazorScreenTemplates.MasterScreen("Acme", login),
        BlazorScreenTemplates.TransactionScreen("Acme", login),
    ];

    private static void AssertGridCompiles(bool validation, bool login)
    {
        var files = new List<(string Path, string Code)>
        {
            ("Models/Order.cs", ModelsStub),
            ("Screens/EntityRole.cs", ScreenTemplates.RoleEnum("Acme")),
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Validation/Permissions.cs", PermissionsTemplate.Source("Acme", new[] { "Order" }, login: false)),
            ("Data/IStore.cs", StoreSeamTemplate.Source("Acme")),
            ("Data/SaveResult.cs", SaveResultTemplate.Source("Acme")),
            ("Data/StoreFactory.cs", StoreFactoryTemplate.Source("Acme", PersistenceKind.Json)),
            ("Data/JsonStore.cs", JsonStoreTemplate.Source("Acme.Data", login)),
            ("ViewModels/ObservableObject.cs", ViewModelTemplates.ObservableObject("Acme")),
            ("ViewModels/RelayCommand.cs", ViewModelTemplates.PortableRelayCommand("Acme")),
            ("ViewModels/IEntityViewModel.cs", ViewModelTemplates.EntityViewModelInterface("Acme")),
            ("ViewModels/EntityViewModel.cs",
                ViewModelTemplates.EntityViewModel("Acme", validation, login)),
        };

        if (validation)
        {
            files.Add(("Validation/IValidator.cs", ValidatorTemplate.ValidatorInterface("Acme")));
            files.Add(("Validation/StoreEntityLookup.cs", ValidatorTemplate.StoreEntityLookup("Acme")));
            files.Add(("Validation/ValidatorFactory.cs",
                ValidatorTemplate.Factory("Acme", Array.Empty<string>())));
        }

        if (login)
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

    /// <summary>One entity, so the emitted ValidatorFactory's `using Acme.Models;` resolves, and
    /// so the permission matrix has a master to classify Order as.</summary>
    private const string ModelsStub = """
        namespace Acme.Models;

        public sealed class Order
        {
            public int Id { get; set; }
        }
        """;
}
