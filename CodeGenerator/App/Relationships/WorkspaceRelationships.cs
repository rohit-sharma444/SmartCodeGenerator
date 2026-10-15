using AppScaffolding;
using CodeGenerationEngine.Core.Relationships;
using CodeGenerator.Web.Workflow;

namespace CodeGenerator.App.Relationships;

/// <summary>
/// The classes in a workspace and what each one is related to, ready to draw.
/// </summary>
/// <remarks>
/// Relationships are read through <see cref="ForeignKeys.LinksOf"/> rather than by looking for
/// an id or a navigation property here. That is the single definition of a relationship the seeder,
/// the generated validators, the console picker and the master/transaction screen split all share,
/// so a picture built from it cannot claim two classes are joined when nothing else in the tool
/// agrees. It also means half a relationship — an <c>int CustomerId</c> with no <c>Customer</c>
/// beside it — reads as unrelated, which is the truth: nothing downstream can see it, and it is
/// exactly the case the relationship offer exists to finish.
/// <para>
/// Every key, not only the ones spelled for their target: a payment's <c>PaidById</c> and
/// <c>ApprovedById</c> (two lines to Staff) and a member of staff's <c>ManagerId</c> (a line to
/// itself) are relationships the validators, the delete refusal and the seeder all act on, so
/// the picture shows them too.
/// </para>
/// <para>
/// This lives in the CLI rather than in the engine because it is the one place that can see both
/// <see cref="ClassSnapshots"/> and <c>ForeignKeys</c> — AppScaffolding references the engine, so
/// the engine cannot reach back for it.
/// </para>
/// </remarks>
public static class WorkspaceRelationships
{
    public static IReadOnlyList<RelationshipSource> Of(string workspaceRoot)
    {
        var classes = ClassSnapshots.Read(workspaceRoot);

        var definitions = classes.Select(c => c.ToDefinition()).ToList();
        var names = ForeignKeys.ClassNameSet(definitions);

        return
        [
            .. definitions
                .OrderBy(d => d.Name, StringComparer.Ordinal)
                .Select(d => RelationshipSource.WithLinks(
                    d.Name,
                    [.. ForeignKeys.LinksOf(d, names).Select(l => new RelationshipLink(l.Property, l.Target))],
                    [.. d.Properties.Select(p => p.Name)]))
        ];
    }
}
