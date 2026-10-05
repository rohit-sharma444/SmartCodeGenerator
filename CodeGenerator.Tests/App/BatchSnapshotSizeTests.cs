using CodeGenerationEngine.Core;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

[TestClass]
[DoNotParallelize]   // redirects Console.Out
public sealed class BatchSnapshotSizeTests
{
    private static readonly SnapshotSizing Large = new(WorkspaceSnapshot.MaxFiles + 1, 0);

    private static (bool Proceed, string Said) Ask(SnapshotSizing sizing, bool redirected, TextReader input)
    {
        var original = Console.Out;
        var said = new StringWriter();
        Console.SetOut(said);
        try
        {
            return (BatchCommand.ConfirmSnapshotSize(sizing, redirected, input), said.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [TestMethod]
    public void A_small_workspace_proceeds_without_asking()
    {
        var input = new StringReader("next command\n");

        Assert.IsTrue(Ask(new SnapshotSizing(10, 100), redirected: true, input).Proceed);
        Assert.AreEqual("next command", input.ReadLine());
    }

    [TestMethod]
    public void A_typed_p_proceeds_with_a_large_workspace()
    {
        Assert.IsTrue(Ask(Large, redirected: false, new StringReader("p\n")).Proceed);
    }

    // S3: a piped run's next line was swallowed as the answer to a question its author could not
    // know would be asked — it depends on how many files the workspace holds that day.
    [TestMethod]
    public void Piped_input_is_never_read_as_the_answer_and_nothing_runs()
    {
        var input = new StringReader("p\ncreate class Book\n");

        var (proceed, said) = Ask(Large, redirected: true, input);

        Assert.IsFalse(proceed);
        StringAssert.Contains(said, "input is piped");
        Assert.AreEqual("p", input.ReadLine(), "the next line is left for the next command");
    }
}
