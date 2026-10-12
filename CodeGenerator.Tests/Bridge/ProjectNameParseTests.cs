using CodeGenerationEngine.Models;
using CodeGenerationEngine.Service;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

/// <summary>
/// The project name in <c>create ... project &lt;name&gt;</c> is the name the user typed.
/// </summary>
/// <remarks>
/// It was read from the normalised sentence, which drops stop words ("A") and punctuation
/// ("Data.Core" became "Data", a quoted name nothing at all), so the project came out as
/// NewProject or under a different name - and a lower-case name was not recognised at all.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class ProjectNameParseTests
{
    [TestMethod]
    [DataRow("create console project A", "A", DotNetProjectType.Console)]
    [DataRow("create console project \"Quoted\"", "Quoted", DotNetProjectType.Console)]
    [DataRow("create console project 'Quoted'", "Quoted", DotNetProjectType.Console)]
    [DataRow("create console project Data.Core", "Data.Core", DotNetProjectType.Console)]
    [DataRow("create console project Data.Core.", "Data.Core", DotNetProjectType.Console)]
    [DataRow("create console project shop", "Shop", DotNetProjectType.Console)]
    [DataRow("create class library mylib", "Mylib", DotNetProjectType.ClassLibrary)]
    [DataRow("create webapi project orders", "Orders", DotNetProjectType.WebApi)]
    [DataRow("create worker service jobs", "Jobs", DotNetProjectType.WorkerService)]
    [DataRow("create wpf project EmployeeManagement", "EmployeeManagement", DotNetProjectType.Wpf)]
    [DataRow("create project EmployeeManagement", "EmployeeManagement", null)]
    [DataRow("create project named shop", "Shop", null)]
    [DataRow("new console project Shop", "Shop", DotNetProjectType.Console)]
    // Gaps 5 #2: a type word that is also a noun ("classlib", "library", "service") took the word
    // "project" after it as the name.
    [DataRow("create classlib project Shop", "Shop", DotNetProjectType.ClassLibrary)]
    [DataRow("create class library project Shop", "Shop", DotNetProjectType.ClassLibrary)]
    [DataRow("create worker service project Shop", "Shop", DotNetProjectType.WorkerService)]
    [DataRow("create worker project Shop", "Shop", DotNetProjectType.WorkerService)]
    [DataRow("create webapi project Shop", "Shop", DotNetProjectType.WebApi)]
    [DataRow("create web api project Shop", "Shop", DotNetProjectType.WebApi)]
    [DataRow("create api project Shop", "Shop", DotNetProjectType.WebApi)]
    [DataRow("create blazor project Shop", "Shop", DotNetProjectType.BlazorServer)]
    [DataRow("create blazorserver project Shop", "Shop", DotNetProjectType.BlazorServer)]
    [DataRow("create blazorwasm project Shop", "Shop", DotNetProjectType.BlazorWebAssembly)]
    [DataRow("create wasm project Shop", "Shop", DotNetProjectType.BlazorWebAssembly)]
    [DataRow("create wpf project Shop", "Shop", DotNetProjectType.Wpf)]
    [DataRow("create winforms project Shop", "Shop", DotNetProjectType.WinForms)]
    [DataRow("create mvc project Shop", "Shop", DotNetProjectType.Mvc)]
    [DataRow("create console project Shop", "Shop", DotNetProjectType.Console)]
    public void The_project_name_is_kept_as_typed(string sentence, string expected, DotNetProjectType? type)
    {
        var cmd = CommandFront.Parse(sentence, "");

        Assert.AreEqual(IntentType.CreateProject, cmd.Intent, sentence);
        Assert.AreEqual(expected, cmd.ProjectName, sentence);
        Assert.AreEqual(type, cmd.ProjectType, sentence);
    }

    [TestMethod]
    public void The_folder_and_framework_survive_too()
    {
        var cmd = CommandFront.Parse(@"create console project Shop in C:\work\apps targeting net8.0", "");

        Assert.AreEqual("Shop", cmd.ProjectName);
        Assert.AreEqual(@"C:\work\apps", cmd.OutputFolder);
        Assert.AreEqual("net8.0", cmd.Framework);
    }

    [TestMethod]
    [DataRow("create project")]
    [DataRow("create console project")]
    [DataRow("create class library")]
    [DataRow("create classlib project")]
    [DataRow(@"create console project in C:\work")]
    public void No_name_leaves_the_name_empty(string sentence)
    {
        var cmd = CommandFront.Parse(sentence, "");

        Assert.AreEqual(IntentType.CreateProject, cmd.Intent, sentence);
        Assert.AreEqual(string.Empty, cmd.ProjectName ?? string.Empty, sentence);
    }

    // It made a project called NewProject, which nobody asked for.
    [TestMethod]
    public void Create_project_with_no_name_is_refused_with_an_example()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "pn-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(workspace);
        var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        try
        {
            ProjectMetaData? project = null;
            var ok = CommandHandlingService.Execute(
                CommandFront.Parse("create project", workspace), ref project, [], workspace, _ => { });

            Assert.IsFalse(ok);
            Assert.IsFalse(Directory.Exists(Path.Combine(workspace, "NewProject")));
            StringAssert.Contains(output.ToString(), "create console project EmployeeManagement");
        }
        finally
        {
            Console.SetOut(original);
            Directory.Delete(workspace, recursive: true);
        }
    }

    [TestMethod]
    [DataRow("create class Project")]
    [DataRow("I want to create projects later")]
    [DataRow("create class library-like thing")]
    public void A_sentence_that_is_not_create_project_is_left_alone(string sentence)
    {
        var cmd = CommandFront.Parse(sentence, "");
        Assert.AreNotEqual(IntentType.CreateProject, cmd.Intent, $"{sentence} made project {cmd.ProjectName}");
    }
}
