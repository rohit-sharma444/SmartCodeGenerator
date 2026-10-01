using CodeGenerator.App.Relationships;

namespace CodeGenerator.Tests.App.Relationships;

/// <summary>
/// The conversation, driven by a scripted asker instead of a person.
/// </summary>
/// <remarks>
/// The offer is remembered per candidate rather than per session: a user who declines once and
/// then creates more classes must still be asked about the new ones, and a candidate they refused
/// must never come back.
/// </remarks>
[TestClass]
public class RelationshipFlowTests
{
    [TestMethod]
    public void Nothing_to_propose_means_nothing_is_asked()
    {
        var root = NewRoot();
        Write(root, "Customer.cs", "namespace Shop;\n\npublic class Customer\n{\n    public int Id { get; set; }\n}\n");

        var asker = new ScriptedAsker();

        var offered = new RelationshipFlow(() => root, asker, _ => { }).Run();

        Assert.IsFalse(offered);
        Assert.IsEmpty(asker.Asked);
    }

    [TestMethod]
    public void Accepting_the_offer_and_the_proposal_writes_the_relationship()
    {
        var root = TwoClasses();

        // 0 = yes to the offer, 0 = the first cardinality (one Customer per Order).
        var asker = new ScriptedAsker(0, 0);
        var said = new List<string>();

        var offered = new RelationshipFlow(() => root, asker, said.Add).Run();

        Assert.IsTrue(offered);
        Assert.Contains("Customer Customer", File.ReadAllText(Path.Combine(root, "Order.cs")));
        Assert.IsTrue(said.Any(s => s.Contains("Orders")), "it must say what it wrote");
    }

    // save session cannot replay the offer, so it needs a note of what the offer wrote.
    [TestMethod]
    public void What_was_written_is_handed_over_once_as_a_note()
    {
        var root = TwoClasses();
        var flow = new RelationshipFlow(() => root, new ScriptedAsker(0, 0), _ => { });

        flow.Run();

        var note = flow.TakeApplied().Single();
        StringAssert.StartsWith(note, "# relationship from the offer, not replayed: Order -> Customer, one to many; added ");
        Assert.IsEmpty(flow.TakeApplied());
    }

    [TestMethod]
    public void A_skipped_proposal_hands_over_nothing()
    {
        var root = TwoClasses();
        var flow = new RelationshipFlow(() => root, new ScriptedAsker(0, 3), _ => { });

        flow.Run();

        Assert.IsEmpty(flow.TakeApplied());
    }

    [TestMethod]
    public void Declining_the_offer_writes_nothing()
    {
        var root = TwoClasses();
        var before = File.ReadAllText(Path.Combine(root, "Order.cs"));

        var asker = new ScriptedAsker(1);

        new RelationshipFlow(() => root, asker, _ => { }).Run();

        Assert.AreEqual(before, File.ReadAllText(Path.Combine(root, "Order.cs")));
    }

    [TestMethod]
    public void A_refused_candidate_is_not_offered_again()
    {
        var root = TwoClasses();
        var flow = new RelationshipFlow(() => root, new ScriptedAsker(1), _ => { });

        Assert.IsTrue(flow.Run(), "the first run has something to offer");
        Assert.IsFalse(flow.Run(), "the same candidate must not come back");
    }

    [TestMethod]
    public void A_candidate_that_appears_later_re_arms_the_offer()
    {
        var root = TwoClasses();
        var flow = new RelationshipFlow(() => root, new ScriptedAsker(1), _ => { });

        flow.Run();

        Write(root, "Invoice.cs", "namespace Shop;\n\npublic class Invoice\n{\n    public int Id { get; set; }\n    public int CustomerId { get; set; }\n}\n");

        Assert.IsTrue(flow.Run(), "a class created after the decline must still be asked about");
    }

    [TestMethod]
    public void Cancelling_the_offer_is_a_decline_not_a_yes()
    {
        // Menu.Choose returns null on Esc or when input ends. Anything but an explicit yes
        // must leave the schema alone.
        var root = TwoClasses();
        var before = File.ReadAllText(Path.Combine(root, "Order.cs"));

        // (int?)null is one cancelled answer. A bare null would bind to the params array itself.
        new RelationshipFlow(() => root, new ScriptedAsker((int?)null), _ => { }).Run();

        Assert.AreEqual(before, File.ReadAllText(Path.Combine(root, "Order.cs")));
    }

    [TestMethod]
    public void Skipping_one_proposal_writes_nothing_for_it()
    {
        // 0 = yes to the offer, 3 = "skip this one". The offer was accepted, so this is not the
        // cancel path: the user is answering the cardinality question with "none of these".
        var root = TwoClasses();
        var before = File.ReadAllText(Path.Combine(root, "Order.cs"));

        new RelationshipFlow(() => root, new ScriptedAsker(0, 3), _ => { }).Run();

        Assert.AreEqual(before, File.ReadAllText(Path.Combine(root, "Order.cs")));
    }

    [TestMethod]
    public void The_root_is_read_at_every_run_so_a_re_rooted_workspace_is_followed()
    {
        // `use project` and `load` re-root the workspace mid-session. A flow that captured the
        // root once would go on reading the project the session started in and write
        // relationships into classes the user is no longer looking at.
        var first = NewRoot();
        var second = TwoClasses();
        var root = first;

        var flow = new RelationshipFlow(() => root, new ScriptedAsker(0, 0), _ => { });

        Assert.IsFalse(flow.Run(), "the first workspace is empty, so there is nothing to offer");

        root = second;

        Assert.IsTrue(flow.Run(), "after the re-root the new workspace is what gets read");
        Assert.Contains("Customer Customer", File.ReadAllText(Path.Combine(second, "Order.cs")));
    }

    [TestMethod]
    public void The_offer_says_how_many_candidates_declining_would_dismiss()
    {
        // Declining refuses every outstanding candidate, not just the one named in the question,
        // so the count has to be in the question the user answers.
        var root = TwoClasses();
        Write(root, "Invoice.cs", "namespace Shop;\n\npublic class Invoice\n{\n    public int Id { get; set; }\n    public int CustomerId { get; set; }\n}\n");

        var asker = new ScriptedAsker(1);

        new RelationshipFlow(() => root, asker, _ => { }).Run();

        Assert.Contains("2", asker.Asked[0]);
    }

    [TestMethod]
    public void The_cardinality_labels_name_both_classes_in_both_directions()
    {
        var labels = RelationshipFlow.CardinalityLabels("Order", "Customer");

        Assert.HasCount(4, labels);
        Assert.Contains("Order", labels[0]);
        Assert.Contains("Customer", labels[0]);
    }

    [TestMethod]
    public void The_many_to_many_label_says_many_in_both_directions()
    {
        // The label at index 1 maps to ManyToMany, which builds a third class. A label reading
        // only "each Order has many Products" describes a reversed one-to-many, so a user picking
        // it would get a join class they did not ask for.
        var label = RelationshipFlow.CardinalityLabels("Order", "Product")[1];

        Assert.Contains("many Products", label);
        Assert.Contains("many Orders", label);
    }

    private static string TwoClasses()
    {
        var root = NewRoot();
        Write(root, "Order.cs", "namespace Shop;\n\npublic class Order\n{\n    public int Id { get; set; }\n    public int CustomerId { get; set; }\n}\n");
        Write(root, "Customer.cs", "namespace Shop;\n\npublic class Customer\n{\n    public int Id { get; set; }\n}\n");
        return root;
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "rf-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Write(string directory, string name, string code)
        => File.WriteAllText(Path.Combine(directory, name), code);

    /// <summary>
    /// Answers in order, then cancels for anything further.
    /// </summary>
    /// <remarks>
    /// The null coalesce is not defensive noise. <c>new ScriptedAsker(null)</c> binds null to the
    /// params ARRAY rather than to a single element, so a test scripting one cancellation gets a
    /// NullReferenceException out of the fixture instead of the behaviour it is testing. Call
    /// sites say <c>(int?)null</c> to mean one cancel; this makes the other reading harmless
    /// rather than a crash in a place nobody looks.
    /// </remarks>
    private sealed class ScriptedAsker(params int?[] answers) : IChoiceAsker
    {
        private readonly int?[] _answers = answers ?? [];

        private int _at;

        public List<string> Asked { get; } = [];

        public int? Choose(string title, IReadOnlyList<string> options, int defaultIndex)
        {
            Asked.Add(title);
            return _at < _answers.Length ? _answers[_at++] : null;
        }
    }
}
