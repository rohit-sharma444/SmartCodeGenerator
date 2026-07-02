namespace CodeCommenterPro.Services;

using CodeCommenterPro.Configuration;
using CodeCommenterPro.Models;
using CodeCommenterPro.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

public class FileScanner
{
    private readonly Settings _settings;

    public FileScanner(
        Settings settings)
    {
        _settings = settings;
    }

    public FileStatistics ProcessFile(
        string file)
    {
        string source =
            File.ReadAllText(file);

        var tree =
            CSharpSyntaxTree.ParseText(source);

        var root =
            tree.GetRoot();

        // Try to get a full project/solution compilation via MSBuildWorkspace for better semantics
        Microsoft.CodeAnalysis.SemanticModel? semanticModel = null;

        try
        {
            var workspace = WorkspaceLoader.GetWorkspace();
            if (workspace is not null)
            {
                var wsType = workspace.GetType();
                var currentSolutionProp = wsType.GetProperty("CurrentSolution");
                var solution = currentSolutionProp?.GetValue(workspace);
                if (solution != null)
                {
                    var projectsProp = solution.GetType().GetProperty("Projects");
                    var projects = projectsProp?.GetValue(solution) as System.Collections.IEnumerable;
                    object? doc = null;

                    if (projects != null)
                    {
                        foreach (var p in projects)
                        {
                            var docsProp = p.GetType().GetProperty("Documents");
                            var docs = docsProp?.GetValue(p) as System.Collections.IEnumerable;
                            if (docs == null) continue;

                            foreach (var d in docs)
                            {
                                var filePathProp = d.GetType().GetProperty("FilePath");
                                var fp = filePathProp?.GetValue(d) as string;
                                if (string.Equals(fp, file, StringComparison.OrdinalIgnoreCase))
                                {
                                    doc = d;
                                    break;
                                }
                            }

                            if (doc != null) break;
                        }
                    }

                    if (doc != null)
                    {
                        var getModelAsync = doc.GetType().GetMethod("GetSemanticModelAsync");
                        if (getModelAsync != null)
                        {
                            var task = getModelAsync.Invoke(doc, null);
                            if (task != null)
                            {
                                var resultProp = task.GetType().GetProperty("Result");
                                var model = resultProp?.GetValue(task) as Microsoft.CodeAnalysis.SemanticModel;
                                if (model != null)
                                    semanticModel = model;
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // ignore workspace failures and fall back
        }

        if (semanticModel == null)
        {
            // Fallback: create a simple compilation for semantic analysis
            var refs = new List<Microsoft.CodeAnalysis.MetadataReference>();
            refs.Add(Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
            refs.Add(Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location));
            refs.Add(Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(Console).Assembly.Location));
            refs.Add(Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(System.Text.Json.JsonSerializer).Assembly.Location));

            var references = refs.ToArray();

            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                "Analysis",
                new[] { tree },
                references,
                new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                    Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));

            semanticModel = compilation.GetSemanticModel(tree);
        }

        var collector =
            new SyntaxCollector();

        collector.Visit(root);

        var docCounter =
            new DocumentationCounter();

        docCounter.Visit(root);

        var rewriter =
            new DocumentationRewriter(
                new CommentGenerator(), semanticModel);

        var newRoot =
            rewriter.Visit(root);

        var logicRewriter = new LogicCommentRewriter(semanticModel);

        newRoot = logicRewriter.Visit(newRoot);

        if (!_settings.DryRun)
        {
            if (_settings.CreateBackups)
            {
                new BackupService()
                    .CreateBackup(
                        _settings.RootFolder,
                        file);
            }

            File.WriteAllText(
                file,
                newRoot!.ToFullString());
        }

        return new FileStatistics
        {
            FileName = file,

            Classes = collector.ClassCount,
            Methods = collector.MethodCount,
            Properties = collector.PropertyCount,
            Constructors = collector.ConstructorCount,

            DocumentedClasses =
                docCounter.DocumentedClasses,

            DocumentedMethods =
                docCounter.DocumentedMethods,

            DocumentedProperties =
                docCounter.DocumentedProperties,

            DocumentedConstructors =
                docCounter.DocumentedConstructors
        };
    }
}