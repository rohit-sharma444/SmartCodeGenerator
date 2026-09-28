using AppScaffolding;
using CodeGenerator.App.Resume;
using CodeGenerator.Web.Workflow;
using DataIngestion;

namespace CodeGenerator.Tests.App.Resume;

[TestClass]
public class ResumedSessionTests
{
    [TestMethod]
    public void The_answers_and_the_model_come_back()
    {
        var snapshot = new BuildSnapshot
        {
            Description = "a shop",
            AppType = "wpf",
            Stage = nameof(BuildStage.Review),
            Company = "Codis Ltd",
            Masters = "OrderLine",
            Entities = [new BuildSnapshot.Entity
            {
                Name = "Book",
                Fields = [new BuildSnapshot.Field { Name = "Title", Type = "string" }],
            }],
        };

        var session = ResumedSession.From(snapshot, Path.GetTempPath(), out var notice);

        Assert.AreEqual("a shop", session.Description);
        Assert.AreEqual("wpf", session.Target!.Key);
        Assert.AreEqual("Codis Ltd", session.Customization.Company);
        Assert.AreEqual("OrderLine", session.Customization.Masters);
        Assert.HasCount(1, session.Model!.Entities);
        Assert.IsNull(notice);
    }

    /// <summary>Restoring must not overwrite the good snapshot with a dozen partial ones on its
    /// way through the setters.</summary>
    [TestMethod]
    public void Restoring_saves_nothing()
    {
        var snapshot = new BuildSnapshot
        {
            Description = "a shop", AppType = "wpf", Stage = nameof(BuildStage.Review),
            Entities = [new BuildSnapshot.Entity { Name = "Book" }],
        };

        var session = ResumedSession.From(snapshot, Path.GetTempPath(), out _);

        Assert.IsNull(session.Autosave, "autosave must be attached by the caller, after restoring");
    }

    /// <summary>
    /// The connection string is never saved — the manual promises it — so a SQL build cannot
    /// resume straight to Generate. It opens on Data, and says why.
    /// </summary>
    [TestMethod]
    public void A_sql_build_comes_back_needing_its_connection_again()
    {
        var snapshot = new BuildSnapshot
        {
            Description = "trading",
            AppType = "wpf",
            Stage = nameof(BuildStage.Generate),
            SourceKind = nameof(DataSourceKind.Sql),
            SourceDatabase = "TRADING",
            Entities = [new BuildSnapshot.Entity { Name = "Trade" }],
        };

        var session = ResumedSession.From(snapshot, Path.GetTempPath(), out var notice);

        Assert.AreEqual(BuildStage.Data, session.Current);
        Assert.IsNotNull(notice);
        StringAssert.Contains(notice!, "TRADING");
        Assert.HasCount(1, session.Model!.Entities);
    }

    /// <summary>
    /// Re-entering the connection string is asked about, not silently applied — the session's
    /// existing "discard the model?" gate already covers this, the same one guards ANY change to
    /// the Data stage's answer once a model exists (<c>BuildSession.WouldDiscardModel</c>).
    /// </summary>
    [TestMethod]
    public void Re_entering_the_connection_asks_before_discarding_the_restored_model()
    {
        var snapshot = new BuildSnapshot
        {
            Description = "trading",
            AppType = "wpf",
            Stage = nameof(BuildStage.Generate),
            SourceKind = nameof(DataSourceKind.Sql),
            SourceDatabase = "TRADING",
            Entities = [new BuildSnapshot.Entity { Name = "Trade" }],
        };

        var session = ResumedSession.From(snapshot, Path.GetTempPath(), out _);
        session.Autosave = _ => { };

        var applied = session.SetSource(new DataSourceSpec(DataSourceKind.Sql, "Server=.;Database=TRADING;"));

        Assert.IsFalse(applied, "a model exists, so the change must be held, not applied outright");
        Assert.IsNotNull(session.PendingDiscard);
        Assert.HasCount(1, session.Model!.Entities, "the restored model must survive until confirmed");
    }

    [TestMethod]
    public void A_file_that_has_gone_comes_back_on_the_data_stage()
    {
        var snapshot = new BuildSnapshot
        {
            Description = "orders",
            AppType = "wpf",
            Stage = nameof(BuildStage.Generate),
            SourceKind = nameof(DataSourceKind.Csv),
            SourcePath = Path.Combine(Path.GetTempPath(), "gone-" + Guid.NewGuid() + ".csv"),
            Entities = [new BuildSnapshot.Entity { Name = "Order" }],
        };

        var session = ResumedSession.From(snapshot, Path.GetTempPath(), out var notice);

        Assert.AreEqual(BuildStage.Data, session.Current);
        Assert.IsNotNull(notice);
        StringAssert.Contains(notice!, "gone-");
    }

    [TestMethod]
    public void A_file_still_there_resumes_where_it_was()
    {
        var path = Path.Combine(Path.GetTempPath(), "resume-" + Guid.NewGuid() + ".csv");
        File.WriteAllText(path, "Title\nDune\n");

        try
        {
            var snapshot = new BuildSnapshot
            {
                Description = "orders",
                AppType = "wpf",
                Stage = nameof(BuildStage.Review),
                SourceKind = nameof(DataSourceKind.Csv),
                SourcePath = path,
                Entities = [new BuildSnapshot.Entity { Name = "Order" }],
            };

            var session = ResumedSession.From(snapshot, Path.GetTempPath(), out var notice);

            Assert.AreEqual(BuildStage.Review, session.Current);
            Assert.IsNull(notice);
            Assert.AreEqual(path, session.Source!.Location);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A target key from a build that knew a type this one does not must not throw.</summary>
    [TestMethod]
    public void An_unknown_app_type_comes_back_unanswered_rather_than_throwing()
    {
        var snapshot = new BuildSnapshot
        {
            Description = "a shop", AppType = "hologram", Stage = nameof(BuildStage.AppType),
        };

        var session = ResumedSession.From(snapshot, Path.GetTempPath(), out _);

        Assert.IsNull(session.Target);
    }

    /// <summary>
    /// Generate and Run are not resumable positions: nothing has been written, so the honest
    /// place to come back to is the last stage that still holds an answer.
    /// </summary>
    [TestMethod]
    public void A_build_left_at_generate_comes_back_to_review()
    {
        var snapshot = new BuildSnapshot
        {
            Description = "a shop",
            AppType = "wpf",
            Stage = nameof(BuildStage.Generate),
            Entities = [new BuildSnapshot.Entity { Name = "Book" }],
        };

        var session = ResumedSession.From(snapshot, Path.GetTempPath(), out _);

        Assert.AreEqual(BuildStage.Review, session.Current);
    }
}
