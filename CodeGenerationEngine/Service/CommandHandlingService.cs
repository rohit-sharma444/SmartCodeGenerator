using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Factory;
using CodeGenerationEngine.Models;
using CodeGenerationEngine.Helper;

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

    /// <summary>
    /// Executes an already-parsed command. This is the seam: the bridge is the
    /// producer of <see cref="ParsedCommand"/>; CodeGenerationEngine only executes.
    /// </summary>
    public static bool Execute(
        ParsedCommand command,
        ref ProjectMetaData? currentProject,
        List<PropertyRule> configuredRules,
        string workspaceRoot,
        Action<ProjectMetaData?> setLastCreatedProject)
    {
        var context = new CommandExecutionContext
        {
            RawInput = command.OriginalSentence,
            Command = command,
            WorkspaceRoot = workspaceRoot,
            ConfiguredRules = configuredRules,
            CurrentProject = currentProject,
            SetLastCreatedProject = setLastCreatedProject
        };

        bool result = Dispatch(context);

        // Feed the outcome back to the learning layer.
        LearningCoordinator.RecordOutcome(command.OriginalSentence, command.Intent, result, command);

        // Propagate any project change made by a handler back to the caller.
        currentProject = context.CurrentProject;
        return result;
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
            [IntentType.ShowLearning] = _ => HandleShowLearning(),
            [IntentType.ResetLearning] = _ => HandleResetLearning(),
            [IntentType.ListClassesInProject] = ctx => ProjectManager.ListClassesInProject(ctx.Command.ProjectName),
            [IntentType.Help] = HandleHelp,
            [IntentType.ShowProject] = _ => ProjectManager.ShowCurrentProject(),
            [IntentType.CurrentProject] = _ => ProjectManager.ShowCurrentProject(),
            [IntentType.CreateProject] = HandleCreateProject,
            [IntentType.UseProject] = ctx => ProjectManager.UseProject(ctx.Command.ProjectName),
            [IntentType.ListProjects] = _ => ProjectManager.ListProjects(),
            [IntentType.CreateClass] = HandleCreateClass,
            [IntentType.ShowClass] = HandleShowClass,
            [IntentType.RenameClass] = HandleRenameClass,
            [IntentType.DeleteClass] = HandleDeleteClass,
            [IntentType.RestoreClass] = HandleRestoreClass,
            [IntentType.RestoreLastClass] = HandleRestoreLastClass,
            [IntentType.ClassInherit] = HandleClassInherit,
            [IntentType.ListDeletedClasses] = HandleListDeletedClasses,
            [IntentType.CreateMethod] = HandleCreateMethod,
            [IntentType.ListMethods] = HandleListMethods,
            [IntentType.DeleteMethod] = HandleDeleteMethod,
            [IntentType.RenameMethod] = HandleRenameMethod,
            [IntentType.AddValidation] = HandleAddValidation,
            [IntentType.ShowValidation] = HandleShowValidation,
            [IntentType.RemoveValidation] = ctx => RemoveRules(ctx, ValidationCategory.Data, "validation"),
            [IntentType.RemoveBusinessValidation] = ctx => RemoveRules(ctx, ValidationCategory.Business, "business validation"),
            [IntentType.AddBusinessValidation] = HandleAddBusinessValidation,
            [IntentType.ShowBusinessValidation] = HandleShowBusinessValidation,
            [IntentType.GenerateValidator] = HandleGenerateValidator,
            [IntentType.CreateInterface] = HandleCreateInterface,
            [IntentType.ListProperties] = HandleListProperties,
            [IntentType.CreateProperty] = HandleCreateProperty,
            [IntentType.DeleteProperty] = HandleDeleteProperty,
            [IntentType.RenameProperty] = HandleRenameProperty,
            [IntentType.ChangePropertyType] = HandleChangePropertyType,
            [IntentType.ValidateProject] = HandleValidateProject,
            [IntentType.CompileProject] = HandleValidateProject
        };

    /// <summary>
    /// Whether <paramref name="intent"/> has a handler and can therefore actually
    /// run. Callers that preview commands before executing them — the batch
    /// command — use this to avoid offering an intent that would only print
    /// "not supported yet".
    /// </summary>
    public static bool IsSupported(IntentType intent) => Handlers.ContainsKey(intent);

    private static bool Dispatch(CommandExecutionContext context)
    {
        if (Handlers.TryGetValue(context.Command.Intent, out var handler))
            return handler(context);

        Console.WriteLine($"Intent '{context.Command.Intent}' is not supported yet.");

        // Nothing ran, so this is a failure. Reporting success here would let the
        // batch command count an unexecuted action as applied and skip its rollback.
        return false;
    }

    // ----- Handlers -------------------------------------------------------

    private static bool HandleUnknown(CommandExecutionContext ctx)
    {
        // Report failure but stay silent: the CLI front-end owns the single
        // user-facing "Command not recognized / did you mean" message for
        // unrecognized input, keyed on PipelineOutcome.Recognized. Printing here
        // too produced two messages for the same unknown command.
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

    /// <summary>
    /// The overview, not the full catalog.
    /// </summary>
    /// <remarks>
    /// The CLI intercepts a typed <c>help</c> before the pipeline reaches here, so this path is
    /// what an intent-recognized phrasing ("show me the commands") lands on. It shows the same
    /// thing typing <c>help</c> does — a user who asked the same question two ways should not get
    /// two different answers. <c>help all</c> is the way to the complete list.
    /// </remarks>
    private static bool HandleHelp(CommandExecutionContext ctx)
    {
        Console.WriteLine(CommandDictionary.GetOverview());
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
            Output.Error("Error: Could not determine a valid class name from input.");
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

            // No class was generated, so the command failed.
            return false;
        }

        var saved = GenerateAndSave(ctx);

        // After the class is on disk, and only if it got there: a note about what was left out of
        // a class that was never written would be the second confusing thing in a row.
        if (saved)
            ReportIgnoredProperties(command);

        return saved;
    }

    /// <summary>
    /// Says which entries in a property list went nowhere, and where they are handled instead.
    /// </summary>
    /// <remarks>
    /// The list is carried on the command rather than re-derived here: it is found in
    /// <c>CommandEntityExtractor</c>, which is in an assembly that references this one and not the
    /// other way round, so the data has to travel and the handler has to be the one that speaks.
    /// A warning rather than an error - nothing failed, the class was written, and part of what
    /// was asked for was not.
    /// </remarks>
    private static void ReportIgnoredProperties(ParsedCommand command)
    {
        foreach (var ignored in command.IgnoredProperties)
        {
            Output.Warning(
                $"Ignored '{ignored}': only primitive property types can be given here. "
                + "Relationships between classes are set separately — the tool will offer.");
        }
    }

    private static bool HandleShowClass(CommandExecutionContext ctx)
    {
        Console.WriteLine(ClassValidator.ShowClass(ctx.Command.ClassName));
        return true;
    }

    private static bool HandleRenameClass(CommandExecutionContext ctx)
    {
        var ok = ClassRenameService.RenameClass(
            ctx.WorkspaceRoot, ctx.Command.ClassName, ctx.Command.NewClassName, out var message);
        Output.Result(ok, message);
        return ok;
    }

    private static bool HandleClassInherit(CommandExecutionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(ctx.Command.BaseClass))
        {
            Console.WriteLine("No base class specified.");
            return false;
        }

        var ok = ClassInheritanceService.AddInheritance(
            ctx.WorkspaceRoot, ctx.Command.ClassName, ctx.Command.BaseClass, out var message);

        Console.WriteLine(message);
        return ok;
    }

    private static bool HandleDeleteClass(CommandExecutionContext ctx)
    {
        var (deleted, message) = ClassDeletionService.DeleteClass(ctx.WorkspaceRoot, ctx.Command.ClassName);
        Output.Result(deleted, message);
        return deleted;
    }

    private static bool HandleRestoreClass(CommandExecutionContext ctx)
    {
        var (restored, message) = ClassRestoreService.RestoreClass(ctx.WorkspaceRoot, ctx.Command.ClassName);
        Output.Result(restored, message);
        return restored;
    }

    private static bool HandleRestoreLastClass(CommandExecutionContext ctx)
    {
        var last = DeletedClassRegistry.GetLast(ctx.WorkspaceRoot);
        if (last == null)
        {
            Output.Warning("No deleted classes to restore.");
            return false;
        }

        var (restored, message) = ClassRestoreService.RestoreClass(
            ctx.WorkspaceRoot, last.ClassName);
        Output.Result(restored, message);
        return restored;
    }

    private static bool HandleListDeletedClasses(CommandExecutionContext ctx)
    {
        Console.WriteLine(DeletedClassListingService.GetDeletedClasses(ctx.WorkspaceRoot));
        return true;
    }

    private static bool HandleCreateMethod(CommandExecutionContext ctx)
    {
        var ok = ClassMethodService.AddMethod(
            ctx.WorkspaceRoot, ctx.Command.ClassName,
            ctx.Command.MethodName, ctx.Command.ReturnType, out var message);
        Output.Result(ok, message);
        return ok;
    }

    private static bool HandleListMethods(CommandExecutionContext ctx)
    {
        return ClassMethodLister.ListMethods(ctx.WorkspaceRoot, ctx.Command.ClassName);
    }

    private static bool HandleDeleteMethod(CommandExecutionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(ctx.Command.ClassName))
        {
            Console.WriteLine("No class specified for the method delete.");
            return false;
        }

        var ok = ClassMethodEditService.DeleteMethod(
            ctx.WorkspaceRoot, ctx.Command.ClassName, ctx.Command.MethodName, out var message);
        Console.WriteLine(message);
        return ok;
    }

    private static bool HandleRenameMethod(CommandExecutionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(ctx.Command.ClassName))
        {
            Console.WriteLine("No class specified for the method rename.");
            return false;
        }

        var ok = ClassMethodEditService.RenameMethod(
            ctx.WorkspaceRoot, ctx.Command.ClassName,
            ctx.Command.MethodName, ctx.Command.NewMethodName, out var message);
        Console.WriteLine(message);
        return ok;
    }

    private static bool HandleAddValidation(CommandExecutionContext ctx)
    {
        return AddRules(ctx, ValidationCategory.Data,
            successPrefix: $"Validation added to {ctx.Command.ClassName}" +
                (string.IsNullOrWhiteSpace(ctx.Command.PropertyName)
                    ? "." : $".{ctx.Command.PropertyName}"));
    }

    // Shared by data and business add-commands: parse the rule text through the
    // catalog for the given category and register whatever was recognized.
    private static bool AddRules(CommandExecutionContext ctx, ValidationCategory category, string successPrefix)
    {
        if (string.IsNullOrWhiteSpace(ctx.Command.ClassName))
        {
            Output.Warning("No class specified for the validation rule.");
            return false;
        }

        var parseCtx = new ValidationParseContext
        {
            Fragment = ctx.Command.ValidationText,
            ClassName = ctx.Command.ClassName,
            TargetProperty = string.IsNullOrWhiteSpace(ctx.Command.PropertyName) ? null : ctx.Command.PropertyName
        };

        var rules = ValidationRuleCatalog.Default.Parse(ctx.Command.ValidationText, category, parseCtx);
        if (rules.Count == 0)
        {
            Output.Warning($"Could not parse a validation rule from: '{ctx.Command.ValidationText}'");
            return false;
        }

        foreach (var rule in rules)
            ValidationRegistry.Add(ctx.Command.ClassName, rule);

        Output.Success(successPrefix);
        return true;
    }

    private static bool HandleAddBusinessValidation(CommandExecutionContext ctx) =>
        AddRules(ctx, ValidationCategory.Business,
            successPrefix: $"Business rule added to {ctx.Command.ClassName}: {ctx.Command.ValidationText}");

    private static bool HandleShowBusinessValidation(CommandExecutionContext ctx) =>
        ShowRules(ctx.Command.ClassName, ValidationCategory.Business, "Business rules");

    private static bool HandleGenerateValidator(CommandExecutionContext ctx)
    {
        var command = ctx.Command;

        if (!string.IsNullOrWhiteSpace(command.ProjectName))
        {
            ProjectManager.Select(command.ProjectName);
            if (ProjectManager.CurrentProject != null)
                command.OutputFolder = ProjectManager.CurrentProject.ProjectPath;
        }

        // No selected project is fine: GenerateValidator falls back to the
        // workspace root to locate the class and writes the validator beside it.
        return EntityExtractor.GenerateValidator(command, ctx.WorkspaceRoot);
    }

    private static bool HandleCreateInterface(CommandExecutionContext ctx)
    {
        return GenerateAndSave(ctx);
    }

    private static bool HandleListProperties(CommandExecutionContext ctx)
    {
        // Locate the class by name anywhere in the workspace (the old code built
        // a path from the selected project without a ".cs" extension, so it never
        // found the file).
        var file = FindClassFile(ctx.WorkspaceRoot, ctx.Command.ClassName);
        if (file == null)
        {
            Console.WriteLine($"Class '{ctx.Command.ClassName}' not found.");
            return true;
        }

        var properties = EntityExtractor.ExtractProperties(file);
        if (properties.Count == 0)
        {
            Console.WriteLine($"No properties found in '{ctx.Command.ClassName}'.");
            return true;
        }

        Console.WriteLine($"Properties in {ctx.Command.ClassName}:");
        foreach (var property in properties)
            Console.WriteLine($"  {property.Type} {property.Name}");
        return true;
    }

    private static string? FindClassFile(string workspaceRoot, string className)
    {
        if (string.IsNullOrWhiteSpace(className))
            return null;

        return Directory.GetFiles(workspaceRoot, "*.cs", SearchOption.AllDirectories)
            .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
                .Equals(className, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HandleCreateProperty(CommandExecutionContext ctx)
    {
        var ok = ClassPropertyService.AddProperty(
            ctx.WorkspaceRoot, ctx.Command.ClassName,
            ctx.Command.PropertyName, ctx.Command.PropertyType, out var message);
        Console.WriteLine(message);
        return ok;
    }

    private static bool HandleDeleteProperty(CommandExecutionContext ctx)
    {
        var ok = ClassPropertyService.RemoveProperty(
            ctx.WorkspaceRoot, ctx.Command.ClassName,
            ctx.Command.PropertyName, out var message);
        Console.WriteLine(message);

        // A rule on a field that is gone is skipped by generate validator with only a comment
        // to show for it, so the rules go with the field.
        if (ok)
        {
            var removed = ValidationRegistry.RemoveOn(ctx.Command.ClassName, ValidationCategory.Data, ctx.Command.PropertyName)
                .Concat(ValidationRegistry.RemoveOn(ctx.Command.ClassName, ValidationCategory.Business, ctx.Command.PropertyName))
                .ToList();
            if (removed.Count > 0)
                Console.WriteLine($"Also removed {removed.Count} validation rule(s) on it: "
                    + string.Join("; ", removed.Select(r => r.Describe())));
        }

        return ok;
    }

    private static bool HandleRenameProperty(CommandExecutionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(ctx.Command.ClassName))
        {
            Console.WriteLine("No class specified for the property rename.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(ctx.Command.PropertyName)
            || string.IsNullOrWhiteSpace(ctx.Command.NewPropertyName))
        {
            Console.WriteLine("Both the current and the new property name are required.");
            return false;
        }

        var ok = ClassPropertyRenameService.RenameProperty(
            ctx.WorkspaceRoot, ctx.Command.ClassName,
            ctx.Command.PropertyName, ctx.Command.NewPropertyName, out var message);

        Console.WriteLine(message);

        // Rules are parsed objects, not text, so they are not rewritten — but left unsaid,
        // generate validator would skip them quietly.
        if (ok)
        {
            var stale = ValidationRegistry.Get(ctx.Command.ClassName)
                .Count(r => r.Properties.Any(p => p.Equals(ctx.Command.PropertyName, StringComparison.OrdinalIgnoreCase)));
            if (stale > 0)
                Output.Warning($"{stale} validation rule(s) still name {ctx.Command.PropertyName}. "
                    + $"Remove them (remove validation from {ctx.Command.ClassName}.{ctx.Command.PropertyName}) "
                    + $"and add them again for {ctx.Command.NewPropertyName}.");
        }

        return ok;
    }

    private static bool HandleChangePropertyType(CommandExecutionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(ctx.Command.ClassName))
        {
            Console.WriteLine("No class specified for the type change.");
            return false;
        }

        var ok = ClassPropertyTypeService.ChangeType(
            ctx.WorkspaceRoot, ctx.Command.ClassName,
            ctx.Command.PropertyName, ctx.Command.PropertyType, out var message);
        Console.WriteLine(message);
        return ok;
    }

    private static bool HandleShowValidation(CommandExecutionContext ctx) =>
        ShowRules(ctx.Command.ClassName, ValidationCategory.Data, "Validation rules");

    private static bool ShowRules(string className, ValidationCategory category, string heading)
    {
        var rules = ValidationRegistry.Get(className, category);
        if (rules.Count == 0)
        {
            Console.WriteLine($"No {heading.ToLowerInvariant()} for '{className}'.");
            return true;
        }

        Console.WriteLine($"{heading} for {className}:");
        for (var i = 0; i < rules.Count; i++)
            Console.WriteLine($"  {i + 1}. {rules[i].Describe()}");
        return true;
    }

    // Shared by `remove validation` and `remove business validation`: by field, or by the
    // number the matching show command printed. `words` is how the command is spelled.
    private static bool RemoveRules(CommandExecutionContext ctx, ValidationCategory category, string words)
    {
        var className = ctx.Command.ClassName;

        if (string.IsNullOrWhiteSpace(className))
        {
            Output.Warning($"No class specified for remove {words}.");
            return false;
        }

        if (ctx.Command.RuleNumber is int number)
        {
            var rule = ValidationRegistry.RemoveNumber(className, category, number);
            if (rule == null)
            {
                var count = ValidationRegistry.Get(className, category).Count;
                Output.Warning(count == 0
                    ? $"'{className}' has no {words} rules."
                    : $"'{className}' has {count} {words} rule(s), so there is no rule {number}. "
                        + $"See show {words} {className}.");
                return false;
            }

            Console.WriteLine($"Removed rule {number} from {className}: {rule.Describe()}");
            return true;
        }

        if (!string.IsNullOrWhiteSpace(ctx.Command.PropertyName))
        {
            var property = ctx.Command.PropertyName;
            var removed = ValidationRegistry.RemoveOn(className, category, property);
            if (removed.Count == 0)
            {
                Output.Warning($"No {words} rules on {className}.{property}.");
                return false;
            }

            Console.WriteLine($"Removed {removed.Count} rule(s) from {className}.{property}: "
                + string.Join("; ", removed.Select(r => r.Describe())));
            return true;
        }

        Output.Warning($"Say which rule: remove {words} from {className}.<field>, or "
            + $"remove {words} <number> from {className} with a number from show {words} {className}.");
        return false;
    }

    /// <summary>
    /// Returns true whenever the compilation RAN, even with errors found: the
    /// command did its job and the report says what it found. Returning false for
    /// "the code has errors" would make BatchCommand roll back a successful
    /// diagnostic run.
    /// </summary>
    private static bool HandleValidateProject(CommandExecutionContext ctx)
    {
        var root = string.IsNullOrWhiteSpace(ctx.CurrentProject?.ProjectPath)
            ? ctx.WorkspaceRoot
            : ctx.CurrentProject!.ProjectPath;

        var result = ProjectCompiler.CompileWorkspace(root);

        Console.WriteLine(result.Report());

        return result.FileCount > 0 || result.Success;
    }

    private static bool HandleShowLearning()
    {
        var entries = LearnedIntentStore.Current.Entries
            .OrderByDescending(e => e.Confidence)
            .ToList();

        if (entries.Count == 0)
        {
            Console.WriteLine("No learned commands yet.");
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("Learned commands");
            Console.WriteLine("--------------------------------");
            foreach (var entry in entries)
            {
                Console.WriteLine(
                    $"{entry.Intent,-22} conf={entry.Confidence:0.00} " +
                    $"(+{entry.SuccessCount}/-{entry.FailureCount})  [{entry.Signature}]");
            }
            Console.WriteLine();
        }

        ShowPhraseCandidates();
        return true;
    }

    /// <summary>
    /// The phrasings typed that were not understood, each with what the next, working command
    /// showed it meant - and the review file a person copies the right ones out of.
    /// </summary>
    private static void ShowPhraseCandidates()
    {
        var review = PhraseCandidates.Review();
        if (review.Count == 0)
            return;

        Console.WriteLine($"Phrasings to review ({review.Count})");
        Console.WriteLine("--------------------------------");
        foreach (var line in review)
            Console.WriteLine($"{line.Intent,-22} x{line.Count,-3} \"{line.Phrase}\"   e.g. {line.Example}");

        var file = PhraseCandidates.WriteReviewFile();
        if (file is not null)
            Console.WriteLine($"Written to {file}");

        Console.WriteLine();
    }

    private static bool HandleResetLearning()
    {
        LearnedIntentStore.Current.Reset();
        LearnedIntentStore.Save();
        PhraseCandidates.Clear();
        Console.WriteLine("Learned command memory cleared.");
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
