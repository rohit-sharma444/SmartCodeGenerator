using AppScaffolding.Targets;
using CodeGenerator.Web.Workflow;
using DataIngestion;
using DataIngestion.Normalization;

namespace CodeGenerator.App.Resume;

/// <summary>
/// A <see cref="BuildSession"/> rebuilt from a snapshot.
/// </summary>
/// <remarks>
/// The order is most of it: answers, then the model, then walk to the stage. Two things are worth
/// knowing before changing this.
/// <para>
/// <see cref="BuildSession.GoTo"/> throws on a stage still marked Pending, so the walk has to be
/// <see cref="BuildSession.Advance"/> one node at a time — which is also what marks the nodes
/// behind it as answered, so the diagram comes back with the right boxes green.
/// </para>
/// <para>
/// <see cref="BuildSession.Autosave"/> is left null. The caller attaches it AFTER restoring, or
/// the walk through the setters writes a dozen partial snapshots over the good one.
/// </para>
/// </remarks>
internal static class ResumedSession
{
    /// <summary>
    /// The session <paramref name="snapshot"/> describes.
    /// </summary>
    /// <param name="notice">
    /// What could not be honoured, or null. A data source that cannot be read again is the only
    /// case: the workflow then opens on <see cref="BuildStage.Data"/> rather than resuming against
    /// a source it has no access to.
    /// </param>
    public static BuildSession From(
        BuildSnapshot snapshot, string workspaceRoot, out string? notice)
    {
        var source = Restorable(snapshot, out notice);

        var session = new BuildSession(
            snapshot.Description ?? string.Empty, workspaceRoot, snapshot.ToCustomization());

        // An unknown key rather than a throw: a snapshot from a build that knew a target this one
        // does not leaves the question unanswered, and the AppType stage then asks it.
        if (snapshot.AppType is { Length: > 0 } key
            && TargetScaffolders.TryResolve(key, out var target))
            session.SetTarget(target);

        session.Advance();

        if (source is not null)
            session.SetSource(source);
        else
            session.SetNoData();

        session.Advance();

        session.SetCustomization(snapshot.ToCustomization());
        session.Advance();

        if (snapshot.ToModel() is { } model)
        {
            session.SetModel(model, "Restored from a saved session");

            if (source is not null)
                notice = ReadRowsAgain(session, source, snapshot);
        }

        WalkTo(session, StageToResumeAt(snapshot, notice, session));

        return session;
    }

    /// <summary>
    /// The source this snapshot can be read from again, or null with a reason in
    /// <paramref name="notice"/>.
    /// </summary>
    private static DataSourceSpec? Restorable(BuildSnapshot snapshot, out string? notice)
    {
        notice = null;

        if (snapshot.SourceKind is null)
            return null;

        if (!Enum.TryParse<DataSourceKind>(snapshot.SourceKind, out var kind))
            return null;

        if (kind == DataSourceKind.Sql)
        {
            // Deliberate, and the manual says so: the connection string is never written to
            // .codegen, so there is nothing here to reconnect with.
            notice =
                $"This build read the {snapshot.SourceDatabase ?? "database"} database. The "
                + "connection string is never saved, so it needs giving again - the data stage is "
                + "open on it.";

            return null;
        }

        var path = snapshot.SourcePath;

        if (path is null)
            return null;

        if (!File.Exists(path))
        {
            notice = $"The file this build read is no longer at {path} - the data stage is open "
                   + "on it.";

            return null;
        }

        return new DataSourceSpec(kind, path);
    }

    /// <summary>
    /// Reads the rows again — they are never saved — and hands them to the session, or says why
    /// it could not.
    /// </summary>
    /// <remarks>
    /// Without this a resumed build generated an app with no imported rows at all: the model came
    /// back, the data it was read from did not, and nothing said so.
    /// <para>
    /// The ticked normalization findings are matched against what the re-read reports rather than
    /// rebuilt from the file, so a finding that no longer appears — the file changed — is simply
    /// not applied. The model is NOT rebuilt: it holds the user's review, which is the point.
    /// </para>
    /// </remarks>
    private static string? ReadRowsAgain(
        BuildSession session, DataSourceSpec source, BuildSnapshot snapshot)
    {
        if (!DiscoveryStep.TryRead(source, out var read, out var error))
            return $"The file this build read could not be read again ({error}) - the data "
                 + "stage is open on it.";

        var saved = snapshot.Normalized ?? [];

        var chosen = read.Report.Findings
            .Where(finding => saved.Any(s =>
                s.Kind == finding.Kind.ToString()
                && s.TableName == finding.TableName
                && s.ProposedEntity == finding.ProposedEntity
                && (s.Columns ?? []).SequenceEqual(finding.Columns)))
            .ToList();

        var tables = chosen.Count > 0
            ? NormalizationPlan.Apply(read.Tables, chosen)
            : read.Tables;

        session.SetImportedData(TabularImport.ToImportedData(tables), chosen);

        return null;
    }

    /// <summary>
    /// Where to pick up: the stage that was left, unless something has to be answered first.
    /// </summary>
    private static BuildStage StageToResumeAt(
        BuildSnapshot snapshot, string? notice, BuildSession session)
    {
        // A source that cannot be read pulls the workflow back to Data whatever the snapshot
        // says. Resuming at Generate against a source with no connection would offer a step that
        // cannot run.
        if (notice is not null)
            return BuildStage.Data;

        if (!Enum.TryParse<BuildStage>(snapshot.Stage, out var stage))
            return BuildStage.Describe;

        // Generate and Run are not resumable positions: nothing was written, so the honest place
        // to come back to is the last stage that still holds an answer.
        if (stage is BuildStage.Generate or BuildStage.Run)
            return session.Model is null ? BuildStage.Discover : BuildStage.Review;

        return stage;
    }

    /// <summary>
    /// Moves to <paramref name="stage"/> one node at a time.
    /// </summary>
    /// <remarks>
    /// <see cref="BuildSession.GoTo"/> refuses a stage that is still Pending — "has not been
    /// reached yet" — so a jump is not available. Advancing is also what leaves the nodes behind
    /// it marked as answered, which is what the stage diagram draws.
    /// <para>
    /// Backwards uses <c>GoTo</c>, which is safe because everything behind the current node has
    /// been reached by the walk that got here.
    /// </para>
    /// </remarks>
    private static void WalkTo(BuildSession session, BuildStage stage)
    {
        var guard = StageGraph.Nodes.Count + 1;

        while (session.Current != stage && guard-- > 0)
        {
            if (StageGraph.Next(session.Current) is null)
                break;

            session.Advance();
        }

        // Overshot, or the target was behind where the replay landed.
        if (session.Current != stage)
            session.GoTo(stage);
    }
}
