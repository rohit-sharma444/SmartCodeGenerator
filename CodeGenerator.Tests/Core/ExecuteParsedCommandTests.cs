using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using CodeGenerationEngine.Service;

namespace CodeGenerator.Tests.Core;

[TestClass]
[DoNotParallelize]
public sealed class ExecuteParsedCommandTests
{
    [TestMethod]
    public void Execute_runs_the_handler_for_a_prebuilt_command()
    {
        LearnedIntentStore.UseInMemory();
        var workspace = Path.Combine(Path.GetTempPath(), "cg_exec_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            var command = new ParsedCommand
            {
                Intent = IntentType.CreateClass,
                ClassName = "Customer",
                OriginalSentence = "create a class called Customer"
            };
            ProjectMetaData? project = null;

            var handled = CommandHandlingService.Execute(command, ref project, [], workspace, _ => { });

            Assert.IsTrue(handled);
            Assert.IsNotEmpty(Directory.GetFiles(workspace, "Customer.cs", SearchOption.AllDirectories));
        }
        finally
        {
            LearnedIntentStore.UseInMemory();
            try { Directory.Delete(workspace, recursive: true); } catch (IOException) { }
        }
    }

    [TestMethod]
    public void Execute_reports_failure_for_a_dead_intent()
    {
        LearnedIntentStore.UseInMemory();
        var command = new ParsedCommand { Intent = IntentType.CreateRepository, OriginalSentence = "create a repository for Customer" };
        ProjectMetaData? project = null;

        var handled = CommandHandlingService.Execute(command, ref project, [], Path.GetTempPath(), _ => { });

        Assert.IsFalse(handled);
    }
}
