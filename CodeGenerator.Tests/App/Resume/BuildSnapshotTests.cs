using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerationEngine.Models;
using CodeGenerator.App.Resume;
using CodeGenerator.Web.Workflow;
using DataIngestion;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App.Resume;

/// <summary>
/// What a snapshot carries, and — as much as anything here — what it must never carry.
/// </summary>
[TestClass]
public class BuildSnapshotTests
{
    [TestMethod]
    public void Carries_the_answers()
    {
        var snapshot = BuildSnapshot.Of(SessionWithModel());

        Assert.AreEqual("a shop", snapshot.Description);
        Assert.AreEqual("wpf", snapshot.AppType);
        Assert.AreEqual(BuildSnapshot.CurrentSchemaVersion, snapshot.SchemaVersion);
        Assert.AreEqual(nameof(BuildStage.Review), snapshot.Stage);
    }

    /// <summary>
    /// The edits are the whole point: discovery can be re-run, half an hour of renaming cannot.
    /// </summary>
    [TestMethod]
    public void Carries_the_model_with_its_edits()
    {
        var session = SessionWithModel();

        DomainDiscovery.Review.ModelEditor.RenameEntity(session.Model!, "Book", "Title");
        DomainDiscovery.Review.ModelEditor.AddField(session.Model!, "Title", "Isbn", "string");

        var restored = BuildSnapshot.Of(session).ToModel();

        Assert.IsNotNull(restored);
        var entity = restored!.Entities.Single(e => e.Name == "Title");
        Assert.IsNotEmpty(entity.Fields.Where(f => f.Name == "Isbn"));
    }

    [TestMethod]
    public void Carries_relationships_and_suggestions_and_the_decision_trail()
    {
        var session = SessionWithModel();
        var book = session.Model!.Entities.Single();
        book.Relationships.Add(new CandidateRelationship { Target = "Shelf", Role = "references" });
        session.Model.Record("Book", DecisionStage.SeedExtraction, DecisionOutcome.Kept, "read");

        var restored = BuildSnapshot.Of(session).ToModel()!;

        Assert.IsNotEmpty(restored.Entities.Single().Relationships);
        Assert.IsNotEmpty(restored.Suggestions);
        Assert.IsNotEmpty(restored.Decisions);
    }

    [TestMethod]
    public void Carries_the_customization_including_the_screen_split()
    {
        var session = SessionWithModel();
        session.SetCustomization(AppCustomization.Default with
        {
            Company = "Codis Ltd", Masters = "OrderLine", Transactions = "Customer"
        });

        var restored = BuildSnapshot.Of(session).ToCustomization();

        Assert.AreEqual("Codis Ltd", restored.Company);
        Assert.AreEqual("OrderLine", restored.Masters);
        Assert.AreEqual("Customer", restored.Transactions);
    }

    [TestMethod]
    public void A_file_source_keeps_its_path()
    {
        var session = SessionWith(new DataSourceSpec(DataSourceKind.Csv, @"D:\data\orders.csv"));

        var snapshot = BuildSnapshot.Of(session);

        Assert.AreEqual("Csv", snapshot.SourceKind);
        Assert.AreEqual(@"D:\data\orders.csv", snapshot.SourcePath);
        Assert.IsNull(snapshot.SourceDatabase);
    }

    /// <summary>
    /// The promise the manual makes as of the SQL Server branch: the connection string is never
    /// written to .codegen. A test, not an intention.
    /// </summary>
    [TestMethod]
    public void A_sql_source_keeps_the_database_name_and_never_the_connection_string()
    {
        var session = SessionWith(new DataSourceSpec(
            DataSourceKind.Sql,
            "Server=.;Database=Northwind;User ID=sa;Password=hunter2",
            SqlDatabase: "Northwind"));

        var snapshot = BuildSnapshot.Of(session);

        Assert.AreEqual("Sql", snapshot.SourceKind);
        Assert.AreEqual("Northwind", snapshot.SourceDatabase);
        Assert.IsNull(snapshot.SourcePath);

        var json = System.Text.Json.JsonSerializer.Serialize(snapshot);

        Assert.DoesNotContain("hunter2", json);
        Assert.DoesNotContain("Server=", json);
    }

    /// <summary>
    /// The other promise: the file stays small whatever the data size, because the rows are
    /// re-read rather than stored.
    /// </summary>
    [TestMethod]
    public void No_row_of_data_is_written_however_much_there_was()
    {
        var session = SessionWith(new DataSourceSpec(DataSourceKind.Csv, @"D:\data\orders.csv"));

        var rows = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>>
        {
            ["Book"] = [.. Enumerable.Range(0, 25_000).Select(
                i => (IReadOnlyDictionary<string, string?>)
                    new Dictionary<string, string?> { ["Title"] = $"row {i}" })]
        };

        session.SetImportedData(new ImportedData(rows, []));

        var json = System.Text.Json.JsonSerializer.Serialize(BuildSnapshot.Of(session));

        Assert.DoesNotContain("row 1000", json);
        Assert.IsLessThan(20_000, json.Length, $"the snapshot grew with the data: {json.Length}");
    }

    private static BuildSession SessionWithModel() => SessionWith(null);

    private static BuildSession SessionWith(DataSourceSpec? source, CandidateDomainModel? model = null)
    {
        var session = new BuildSession("a shop", Path.GetTempPath(), AppCustomization.Default);

        session.SetTarget(new WpfTarget());
        session.Advance();

        if (source is null)
            session.SetNoData();
        else
            session.SetSource(source);

        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();
        session.SetModel(model ?? AModel());

        while (session.Current != BuildStage.Review)
            session.Advance();

        return session;
    }

    private static CandidateDomainModel AModel()
    {
        var model = new CandidateDomainModel();

        var book = new CandidateEntity { Name = "Book", Provenance = Provenance.Wiki };
        book.Fields.Add(new CandidateField
        {
            Name = "Title", Type = "string", Provenance = Provenance.Wiki
        });

        model.Entities.Add(book);
        model.Suggestions.Add(new CandidateEntity { Name = "Shelf", Provenance = Provenance.Wiki });

        return model;
    }

    /// <summary>A model with one entity and one field, carrying the given facts.</summary>
    private static CandidateDomainModel ModelWithFacts(int maxLength, bool nullable)
    {
        var model = new CandidateDomainModel();

        var book = new CandidateEntity { Name = "Book", Provenance = Provenance.Wiki };
        book.Fields.Add(new CandidateField
        {
            Name = "Title",
            Type = "string",
            Provenance = Provenance.Data,
            Facts = new TypeFacts { MaxLength = maxLength, IsNullable = nullable },
        });

        model.Entities.Add(book);
        return model;
    }

    /// <summary>A model with one entity and one field, carrying two proposals: the first accepted
    /// exactly when <paramref name="acceptFirst"/>, the second never.</summary>
    private static CandidateDomainModel ModelWithProposals(bool acceptFirst)
    {
        var model = new CandidateDomainModel();

        var book = new CandidateEntity { Name = "Book", Provenance = Provenance.Wiki };
        var field = new CandidateField { Name = "Status", Type = "string", Provenance = Provenance.Data };

        field.Proposals.Add(new RuleProposal
        {
            Kind = RuleProposalKind.Status,
            Rule = new PropertyRule
            {
                PropertyName = "Status", Name = "allowedvalues", AllowedValues = ["Open", "Closed"]
            },
            Evidence = "Status holds only Open, Closed in every sampled value",
            Accepted = acceptFirst,
        });

        field.Proposals.Add(new RuleProposal
        {
            Kind = RuleProposalKind.Unique,
            Rule = new PropertyRule { PropertyName = "Status", Name = "unique", IsUnique = true },
            Evidence = "no two sampled entries share a Status",
            Accepted = false,
        });

        book.Fields.Add(field);
        model.Entities.Add(book);
        return model;
    }

    /// <summary>Builds a session carrying <paramref name="model"/>, snapshots it, and restores it —
    /// what a resume actually does.</summary>
    private static CandidateDomainModel RoundTrip(CandidateDomainModel model)
        => BuildSnapshot.Of(SessionWith(null, model)).ToModel()!;

    private static BuildSnapshot SnapshotOf(CandidateDomainModel model)
        => BuildSnapshot.Of(SessionWith(null, model));

    private static string Serialize(BuildSnapshot snapshot)
        => System.Text.Json.JsonSerializer.Serialize(snapshot);

    private static IEnumerable<string> RuleNames(
        IReadOnlyList<CodeGenerationEngine.Models.ClassDefinition> classes)
        => classes.SelectMany(c => c.Properties).SelectMany(p => p.Rules).Select(r => r.Name);

    [TestMethod]
    public void A_resumed_field_keeps_what_its_column_declared()
    {
        var model = ModelWithFacts(maxLength: 50, nullable: false);

        var restored = RoundTrip(model);

        var facts = restored.Entities.Single().Fields.Single().Facts;
        Assert.IsNotNull(facts, "the facts every inferred field rule is derived from");
        Assert.AreEqual(50, facts.MaxLength);
        Assert.IsFalse(facts.IsNullable);
    }

    [TestMethod]
    public void A_resumed_build_infers_the_same_rules_as_an_unresumed_one()
    {
        var model = ModelWithFacts(maxLength: 50, nullable: false);

        var before = AppScaffolding.DomainModelMapper.ToClassDefinitions(model, out _);
        var after = AppScaffolding.DomainModelMapper.ToClassDefinitions(RoundTrip(model), out _);

        CollectionAssert.AreEquivalent(
            RuleNames(before).ToArray(), RuleNames(after).ToArray());
    }

    [TestMethod]
    public void A_resumed_build_keeps_the_proposals_that_were_accepted()
    {
        var model = ModelWithProposals(acceptFirst: true);

        var restored = RoundTrip(model);

        var proposals = restored.Entities.Single().Fields.Single().Proposals;
        Assert.HasCount(2, proposals);
        Assert.IsTrue(proposals[0].Accepted);
        Assert.IsFalse(proposals[1].Accepted);
    }

    [TestMethod]
    public void The_rows_are_still_not_written_to_disk()
    {
        // Unchanged, and asserted rather than intended: the file stays small whatever the data size,
        // and the connection string is never written to .codegen at all.
        var json = Serialize(SnapshotOf(ModelWithProposals(acceptFirst: true)));

        Assert.IsFalse(json.Contains("Rows", StringComparison.OrdinalIgnoreCase));
    }
}
