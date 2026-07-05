using CodeGenerationEngine.Core;
using CodeGenerationEngine.Factory;
using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Service;

/// <summary>
/// Centralized command handler for handling user input commands, generating code, and managing projects.
/// </summary>
public static class CommandHandlingService
{
    /// <summary>
    /// Immutable bundle of everything a single command needs to execute.
    /// Replaces the previous mutable static <c>_workspaceRoot</c> field so
    /// the handler is free of hidden, non-thread-safe state.
    /// </summary>
    private sealed class CommandExecutionContext
    {
        public required string RawInput { get; init; }
        public required ParsedCommand Command { get; init; }
        public required string WorkspaceRoot { get; init; }
        public required IReadOnlyList<PropertyRule> ConfiguredRules { get; init; }

        // Project state is mutated through these so the caller's `ref`
        // parameter and persistence callback stay in sync.
        public required ProjectMetaData? CurrentProject { get; set; }
        public required Action<ProjectMetaData?> SetLastCreatedProject { get; init; }
    }

    public static bool ProcessInput(
        string input,
        ref ProjectMetaData? currentProject,
        List<PropertyRule> configuredRules,
        string workspaceRoot,
        Action<ProjectMetaData?> setLastCreatedProject,
        string? nlpAction = null)
    {
        // The "demo" shortcut bypasses parsing entirely and prints generated code.
        if (input.Equals("demo", StringComparison.OrdinalIgnoreCase))
        {
            var (demoCode, _) = BuildDemo();
            Console.WriteLine(StripCommentMarkers(demoCode));
            return true;
        }

        var command = ParseInput(input, workspaceRoot, nlpAction);

        var context = new CommandExecutionContext
        {
            RawInput = input,
            Command = command,
            WorkspaceRoot = workspaceRoot,
            ConfiguredRules = configuredRules,
            CurrentProject = currentProject,
            SetLastCreatedProject = setLastCreatedProject
        };

        bool result = Dispatch(context);

        // Feed the outcome back to the learning layer.
        LearningCoordinator.RecordOutcome(input, command.Intent, result, command);

        // Propagate any project change made by a handler back to the caller.
        currentProject = context.CurrentProject;
        return result;
    }

    private static ParsedCommand ParseInput(string input, string workspaceRoot, string? nlpAction)
    {
        var semantic = SemanticAnalyzer.Analyze(input, nlpAction);
        var intentType = Core.IntentAnalyzer.Detect(semantic);
        return EntityExtractor.Extract(semantic, intentType, workspaceRoot);
    }

    /// <summary>
    /// Maps each intent to its handler. A dictionary keeps registration flat and
    /// makes the supported-intent surface obvious, instead of a 200-line switch.
    /// </summary>
    private static readonly IReadOnlyDictionary<IntentType, Func<CommandExecutionContext, bool>> Handlers =
        new Dictionary<IntentType, Func<CommandExecutionContext, bool>>
        {
            [IntentType.Unknown] = HandleUnknown,
            [IntentType.GenerateDocumentation] = HandleGenerateDocument,
            [IntentType.GenerateDesignDocument] = HandleGenerateDocument,
            [IntentType.AnalyzeClass] = HandleAnalyzeClass,
            [IntentType.AnalyzeProject] = HandleAnalyzeProject,
            [IntentType.ListClasses] = ctx => ProjectManager.ListClasses(ctx.WorkspaceRoot),
            [IntentType.ListClassesInProject] = ctx => ProjectManager.ListClassesInProject(ctx.RawInput),
            [IntentType.Help] = HandleHelp,
            [IntentType.ShowProject] = _ => ProjectManager.ShowCurrentProject(),
            [IntentType.CurrentProject] = _ => ProjectManager.ShowCurrentProject(),
            [IntentType.CreateProject] = HandleCreateProject,
            [IntentType.UseProject] = ctx => ProjectManager.UseProject(ctx.RawInput),
            [IntentType.ListProjects] = _ => ProjectManager.ListProjects(),
            [IntentType.CreateClass] = HandleCreateClass,
            [IntentType.ShowClass] = HandleShowClass,
            [IntentType.RenameClass] = HandleRenameClass,
            [IntentType.DeleteClass] = HandleDeleteClass,
            [IntentType.RestoreClass] = HandleRestoreClass,
            [IntentType.ListDeletedClasses] = HandleListDeletedClasses,
            [IntentType.CreateMethod] = HandleCreateMethod,
            [IntentType.ListMethods] = HandleListMethods,
            [IntentType.AddValidation] = HandleAddValidation,
            [IntentType.GenerateValidator] = HandleGenerateValidator,
            [IntentType.CreateInterface] = HandleCreateInterface,
            [IntentType.ListProperties] = HandleListProperties
        };

    private static bool Dispatch(CommandExecutionContext context)
    {
        if (Handlers.TryGetValue(context.Command.Intent, out var handler))
            return handler(context);

        Console.WriteLine($"Intent '{context.Command.Intent}' is not supported yet.");
        return true;
    }

    // ----- Handlers -------------------------------------------------------

    private static bool HandleUnknown(CommandExecutionContext ctx)
    {
        Console.WriteLine($"Could not determine intent for input: '{ctx.RawInput}'");
        return false;
    }

    // Shared by GenerateDocumentation and GenerateDesignDocument (identical behavior).
    private static bool HandleGenerateDocument(CommandExecutionContext ctx)
    {
        Console.WriteLine(DesignDocumentGenerator.Generate(ctx.Command, ctx.WorkspaceRoot));
        return true;
    }

    private static bool HandleAnalyzeClass(CommandExecutionContext ctx)
    {
        Console.WriteLine(AnalyzeClassCommand.Execute(ctx.Command, ctx.WorkspaceRoot));
        return true;
    }

    private static bool HandleAnalyzeProject(CommandExecutionContext ctx)
    {
        Console.WriteLine(AnalyzeProjectCommand.Execute(ctx.Command, ctx.WorkspaceRoot));
        return true;
    }

    private static bool HandleHelp(CommandExecutionContext ctx)
    {
        Console.WriteLine(CommandDictionary.GetHelpText());
        return true;
    }

    private static bool HandleCreateProject(CommandExecutionContext ctx)
    {
        var command = ctx.Command;

        // Resolve the project name once so the name and the default output
        // folder stay consistent (the old code defaulted them independently).
        string projectName = string.IsNullOrWhiteSpace(command.ProjectName)
            ? "NewProject"
            : command.ProjectName;

        var projectReq = new ProjectRequest
        {
            ProjectName = projectName,
            OutputFolder = string.IsNullOrWhiteSpace(command.OutputFolder)
                ? Path.Combine(ctx.WorkspaceRoot, projectName)
                : command.OutputFolder,
            ProjectType = command.ProjectType ?? DotNetProjectType.Console,
            Framework = string.IsNullOrWhiteSpace(command.Framework) ? "net10.0" : command.Framework
        };

        Console.WriteLine(
            $"Creating project '{projectReq.ProjectName}' of type '{projectReq.ProjectType}' " +
            $"in '{projectReq.OutputFolder}' targeting '{projectReq.Framework}'...");

        var projResult = LoadOrCreateProject(projectReq, ctx.WorkspaceRoot);
        ProjectManager.Register(projResult);
        ctx.CurrentProject = projResult;
        ctx.SetLastCreatedProject?.Invoke(projResult); // persist for later saves
        Console.WriteLine(projResult.Message);
        return true;
    }

    private static bool HandleCreateClass(CommandExecutionContext ctx)
    {
        var command = ctx.Command;

        if (!ClassValidator.IsValidClassName(command.ClassName))
        {
            Console.WriteLine("Error: Could not determine a valid class name from input.");
            return false;
        }

        var classDef = new ClassDefinition
        {
            Name = command.ClassName ?? string.Empty,
            Properties = command.Properties?.ToList() ?? new List<PropertyDefinition>(),
            Methods = command.Methods?.ToList() ?? new List<MethodDefinition>()
        };

        var classErrors = ClassValidator.Validate(classDef);
        if (classErrors.Count > 0)
        {
            foreach (var err in classErrors)
                Console.WriteLine(err);
            return true;
        }

        return GenerateAndSave(ctx);
    }

    private static bool HandleShowClass(CommandExecutionContext ctx)
    {
        Console.WriteLine(ClassValidator.ShowClass(ctx.Command.ClassName));
        return true;
    }

    private static bool HandleRenameClass(CommandExecutionContext ctx)
    {
        return ClassRenameService.RenameClass(
            ctx.WorkspaceRoot, ctx.Command.ClassName, ctx.Command.NewClassName, out _);
    }

    private static bool HandleDeleteClass(CommandExecutionContext ctx)
    {
        var (deleted, message) = ClassDeletionService.DeleteClass(ctx.WorkspaceRoot, ctx.Command.ClassName);
        Console.WriteLine(message);
        return deleted;
    }

    private static bool HandleRestoreClass(CommandExecutionContext ctx)
    {
        var (restored, message) = ClassRestoreService.RestoreClass(ctx.WorkspaceRoot, ctx.Command.ClassName);
        Console.WriteLine(message);
        return restored;
    }

    private static bool HandleListDeletedClasses(CommandExecutionContext ctx)
    {
        Console.WriteLine(DeletedClassListingService.GetDeletedClasses(ctx.WorkspaceRoot));
        return true;
    }

    private static bool HandleCreateMethod(CommandExecutionContext ctx)
    {
        return ClassMethodService.AddMethod(
            ctx.WorkspaceRoot, ctx.Command.ClassName,
            ctx.Command.MethodName, ctx.Command.ReturnType, out _);
    }

    private static bool HandleListMethods(CommandExecutionContext ctx)
    {
        return ClassMethodLister.ListMethods(ctx.WorkspaceRoot, ctx.Command.ClassName);
    }

    private static bool HandleAddValidation(CommandExecutionContext ctx)
    {
        var rules = ValidationRuleParser.Parse(ctx.Command.ValidationText);
        foreach (var rule in rules)
            ValidationRegistry.AddRule(ctx.Command.ClassName, rule);
        Console.WriteLine($"Validation added to {ctx.Command.ClassName}");
        return true;
    }

    private static bool HandleGenerateValidator(CommandExecutionContext ctx)
    {
        var command = ctx.Command;
        EntityExtractor.ExtractClassName(ctx.RawInput, command);

        if (!string.IsNullOrWhiteSpace(command.ProjectName))
        {
            ProjectManager.Select(command.ProjectName);
            if (ProjectManager.CurrentProject != null)
                command.OutputFolder = ProjectManager.CurrentProject.ProjectPath;
        }

        return EntityExtractor.GenerateValidator(command);
    }

    private static bool HandleCreateInterface(CommandExecutionContext ctx)
    {
        return GenerateAndSave(ctx);
    }

    private static bool HandleListProperties(CommandExecutionContext ctx)
    {
        // Build the class file path safely with Path.Combine instead of manual
        // string concatenation, and only when a project is actually selected.
        var projectPath = ProjectManager.CurrentProject?.ProjectPath;
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            Console.WriteLine("No current project selected.");
            return true;
        }

        var filePath = Path.Combine(ctx.WorkspaceRoot, projectPath, ctx.Command.ClassName);
        var properties = EntityExtractor.ExtractProperties(filePath);
        foreach (var property in properties)
            Console.WriteLine($"{property.Type} {property.Name}");
        return true;
    }

    // ----- Shared helpers -------------------------------------------------

    /// <summary>
    /// Generates code for the current command, runs semantic/compile checks,
    /// and persists the result. Shared by CreateClass and CreateInterface.
    /// </summary>
    private static bool GenerateAndSave(CommandExecutionContext ctx)
    {
        var code = NaturalLanguageCodeEngine.ExecuteCommand(ctx.Command, ctx.WorkspaceRoot);
        if (string.IsNullOrWhiteSpace(code))
            return false;

        SemanticCompilerRunner.RunSemanticAndCompile(ctx.Command, code, ctx.ConfiguredRules);
        GeneratedCodeService.Save(ctx.Command, code, ctx.CurrentProject, ctx.WorkspaceRoot);
        return true;
    }

    private static string StripCommentMarkers(string code) => code.Replace("//", string.Empty);

    // FIX #8: Correct path handling - store .csproj path and directory separately
    private static ProjectMetaData LoadOrCreateProject(ProjectRequest request, string workspaceRoot)
    {
        var existingProject = Directory
            .GetFiles(workspaceRoot, $"{request.ProjectName}.csproj", SearchOption.AllDirectories)
            .FirstOrDefault();

        if (existingProject != null)
        {
            var projectDir = Path.GetDirectoryName(existingProject);
            return new ProjectMetaData
            {
                Success = true,
                ProjectPath = projectDir ?? Path.GetDirectoryName(workspaceRoot) ?? workspaceRoot,
                CsProjPath = existingProject,
                ProjectName = Path.GetFileNameWithoutExtension(existingProject),
                Message = $"Existing project loaded: {existingProject}"
            };
        }

        return DotNetProjectFactory.CreateProject(request);
    }

    public static (string code, ParsedCommand parsed) BuildDemo()
    {
        var customer = new ClassDefinition
        {
            Name = "Customer",
            Properties =
            [
                new PropertyDefinition { Name = "Id", Type = "int" },
                new PropertyDefinition { Name = "Name", Type = "string" },
                new PropertyDefinition { Name = "Email", Type = "string" }
            ],
            Methods =
            [
                new MethodDefinition { Name = "Save", ReturnType = "void" },
                new MethodDefinition { Name = "Validate", ReturnType = "bool" }
            ]
        };

        var code = RoslynClassGenerator.Generate(customer);
        var parsed = new ParsedCommand
        {
            Intent = IntentType.CreateClass,
            ClassName = customer.Name,
            Properties = customer.Properties,
            Methods = customer.Methods
        };

        return (code, parsed);
    }
}
