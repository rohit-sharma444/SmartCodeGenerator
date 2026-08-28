using System.IO;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core.Validation;

/// <summary>Guards the `generate validator` command when no project is selected
/// (a fresh checkout, or the current project cleared because it sits outside the
/// open folder). The validator is written next to the class file, so the command
/// must locate the class by searching the workspace root rather than refusing
/// with "No project selected."</summary>
// Same reason as GenerateValidatorEndToEndTests: this writes ValidationRegistry and
// ProjectManager.CurrentProject, both process-wide statics.
[TestClass]
[DoNotParallelize]
public class GenerateValidatorWithoutProjectTests
{
    [TestMethod]
    public void Generates_by_searching_workspace_root_when_no_project_selected()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "cg_val_" + Guid.NewGuid().ToString("N"));
        var classDir = Path.Combine(workspace, "Domain");
        Directory.CreateDirectory(classDir);
        var classFile = Path.Combine(classDir, "Widget.cs");
        File.WriteAllText(classFile,
            "namespace Acme { public class Widget { public string Name { get; set; } } }");

        ValidationRegistry.Clear();
        ProjectManager.CurrentProject = null;

        var parseCtx = new ValidationParseContext { Fragment = "", ClassName = "Widget" };
        foreach (var rule in ValidationRuleCatalog.Default.Parse(
                     "Name is required", ValidationCategory.Data, parseCtx))
            ValidationRegistry.Add("Widget", rule);

        var command = new ParsedCommand { Intent = IntentType.GenerateValidator, ClassName = "Widget" };

        try
        {
            var ok = EntityExtractor.GenerateValidator(command, workspace);

            Assert.IsTrue(ok, "GenerateValidator should succeed by searching the workspace root.");
            var validatorFile = Path.Combine(classDir, "Widget.Validation.cs");
            Assert.IsTrue(File.Exists(validatorFile), $"Expected validator at {validatorFile}.");
            StringAssert.Contains(File.ReadAllText(validatorFile), "Name is required.");
        }
        finally
        {
            ValidationRegistry.Clear();
            try { Directory.Delete(workspace, recursive: true); } catch { }
        }
    }
}
