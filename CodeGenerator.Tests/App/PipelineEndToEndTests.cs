using System;
using System.IO;
using AppScaffolding;
using AppScaffolding.Targets;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The pipeline as a whole scaffold sees it: a real <see cref="AppScaffolder"/> run over a
/// two-entity model, asserting on the files that land on disk rather than on one template's
/// return value.
/// </summary>
/// <remarks>
/// The two scaffold tests and the two front-end tests here are regression PINS, not
/// red-then-green tests: none of the four behaviours is new, and they exist so that a later edit
/// which changes one has to change a test that says why it was that way. Each was confirmed to
/// bite by mutating the template it reads. <see cref="AThrowingAssertionStillLeavesNoWorkspaceBehind"/>
/// is the exception — it covers a real bug this file itself had.
/// </remarks>
[TestClass]
public class PipelineEndToEndTests
{
    [TestMethod]
    public void ATwoEntityAppEmitsNoEmptyStages()
    {
        // The mitigation for the spec's "seven stages is more machinery than a three-entity CRUD
        // app needs" risk, measured on a real scaffold rather than on one template's output. Two
        // entities, not three: InAScaffold builds Customer and Order, and the name said three
        // while the model said two.
        InAScaffold((_, project) =>
        {
            var validator = File.ReadAllText(
                Path.Combine(project, "Validation", "CustomerValidator.Generated.cs"));

            Assert.DoesNotContain("private static void Workflow(", validator);
            Assert.DoesNotContain("private static void Transaction(", validator);
            StringAssert.Contains(validator, "private static void Security(");
        });
    }

    [TestMethod]
    public void AConsoleAppHasNoRole_AndStillGenerates()
    {
        // The limit this phase cannot lift, pinned so nobody later "fixes" ActingUser into
        // something that names a Session the console target never emits.
        InAScaffold((_, project) =>
        {
            var repository = File.ReadAllText(Path.Combine(project, "Data", "Repository.cs"));

            StringAssert.Contains(repository, "ActingUser.Role(), ActingUser.Id()");
            Assert.DoesNotContain("Session.Current", repository);

            var actingUser = File.ReadAllText(
                Path.Combine(project, "Validation", "ActingUser.cs"));

            Assert.DoesNotContain("Session", actingUser);
            StringAssert.Contains(actingUser, "public static string? Role() => null;");
        });
    }

    /// <summary>
    /// The temp workspace goes even when the scaffold, or an assertion over it, throws.
    /// </summary>
    /// <remarks>
    /// This file's first version returned the two paths to the caller and left the caller to
    /// delete them in a <c>finally</c> — so the <c>Assert.IsTrue(result.Success)</c> guard threw
    /// BEFORE any <c>finally</c> existed, and a scaffold that failed leaked its directory every
    /// time. That is the leak <c>InAScaffold</c>'s own <c>finally</c> now closes, and this is what
    /// holds it closed: nothing about a passing run can tell you whether the failing path cleans
    /// up, so the failing path is what gets asserted.
    /// <para>
    /// The exact workspace is checked rather than counting <c>%TEMP%</c>: this assembly runs
    /// method-level parallel, and <c>GeneratedProjectBuildTests</c> is making and removing temp
    /// directories of its own at the same time.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void AThrowingAssertionStillLeavesNoWorkspaceBehind()
    {
        string? leaked = null;

        Assert.ThrowsExactly<AssertFailedException>(() => InAScaffold((workspace, _) =>
        {
            leaked = workspace;
            Assert.Fail("deliberate, to prove the workspace is removed regardless");
        }));

        Assert.IsNotNull(leaked, "the scaffold should have reached the assertion");
        Assert.IsFalse(Directory.Exists(leaked), "the temp workspace leaked: " + leaked);
    }

    /// <summary>
    /// The console menu's four delete outcomes, each of which was driven by hand in a running
    /// generated app before this test was written.
    /// </summary>
    /// <remarks>
    /// Nothing covered these branches at all — a later edit collapsing "no such record" back into
    /// the refusal arm, or dropping the parse guard, would have been caught by no assertion of any
    /// kind. Asserted as strings because they ARE the observable behaviour: what the person at the
    /// console reads is the whole of the difference between a missing record and a refused one.
    /// </remarks>
    [TestMethod]
    public void TheConsoleMenuSeparatesEveryDeleteOutcome()
    {
        var source = ConsoleMenuTemplate.Source("Acme");

        StringAssert.Contains(source, "\"That is not a number.\"");
        StringAssert.Contains(source, "\"Deleted.\"");
        StringAssert.Contains(source, "\"No record with that Id.\"");
        StringAssert.Contains(source, "\"Not deleted.\"");

        // Order matters as much as presence: success returns first, then the missing record, and
        // only what is left over is a refusal with reasons to print. A NotFound checked after the
        // reasons loop would print an empty list and then "Not deleted." for an Id nobody has.
        var deleted = source.IndexOf("\"Deleted.\"", StringComparison.Ordinal);
        var notFound = source.IndexOf("result.NotFound", StringComparison.Ordinal);
        var reasons = source.IndexOf("foreach (var error in result.Errors)", StringComparison.Ordinal);

        Assert.IsGreaterThan(0, notFound, "the delete should test NotFound");
        Assert.IsGreaterThan(0, reasons, "a refused delete should print its reasons");
        Assert.IsLessThan(notFound, deleted, "success is decided before a missing record");
        Assert.IsLessThan(reasons, notFound, "a missing record is decided before any reason is printed");
    }

    /// <summary>
    /// The Web API's three delete outcomes. Driven against a running app: 204 for a delete that
    /// went through, 404 for an Id nobody has, and 400 carrying the reason keyed by the property
    /// it belongs to.
    /// </summary>
    [TestMethod]
    public void TheWebApiKeepsAllThreeDeleteOutcomes()
    {
        var source = WebApiProgramTemplate.Build("Acme", new[] { "Customer" }, "Acme", seed: false);

        var mapDelete = source.IndexOf("MapDelete", StringComparison.Ordinal);
        Assert.IsGreaterThan(0, mapDelete, "the delete route should be mapped");

        var delete = source[mapDelete..];

        StringAssert.Contains(delete, "Results.NoContent()");
        StringAssert.Contains(delete, "Results.NotFound()");
        StringAssert.Contains(delete, "Results.ValidationProblem(result.AsDictionary())");

        // The 400 arm is the one a later edit would collapse back into the 404: a refusal is not
        // a missing record, and AsDictionary is what keys the messages by property name rather
        // than dropping them all under one empty key.
        Assert.IsLessThan(
            delete.IndexOf("Results.ValidationProblem", StringComparison.Ordinal),
            delete.IndexOf("Results.NotFound()", StringComparison.Ordinal),
            "a missing record must be answered before a refusal");
    }

    /// <summary>
    /// Scaffolds the two-entity console app into a fresh temp workspace, hands
    /// <paramref name="assert"/> the workspace and the project inside it, and deletes the
    /// workspace afterwards whatever happened.
    /// </summary>
    /// <remarks>
    /// The scaffold call, the success guard and the caller's assertions are all inside the one
    /// <c>try</c>, which is the whole point: a guard that throws outside the <c>finally</c> is how
    /// the first version of this file leaked a directory per failed run.
    /// </remarks>
    private static void InAScaffold(Action<string, string> assert)
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity
        {
            Name = "Customer",
            Fields = { new CandidateField { Name = "Surname", Type = "string" } }
        });
        model.Entities.Add(new CandidateEntity
        {
            Name = "Order",
            Fields = { new CandidateField { Name = "CustomerId", Type = "int" } }
        });

        Assert.IsTrue(TargetScaffolders.TryResolve("console", out var target));

        var workspace = Path.Combine(Path.GetTempPath(), "gen_" + Path.GetRandomFileName());
        Directory.CreateDirectory(workspace);

        try
        {
            var result = new AppScaffolder().Scaffold(
                ScaffoldRequest.For(model, "Shop", workspace, target));

            Assert.IsTrue(result.Success, "scaffold failed: " + result.Message);

            assert(workspace, result.ProjectPath);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }
}
