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

        // The line did not name the class; say which one the command acts on, before any
        // "not found" that would otherwise name a class the user never typed on this line.
        if (command.ClassFromSession && !string.IsNullOrWhiteSpace(command.ClassName)
            && ClassTargetIntents.Contains(command.Intent))
            Output.Detail($"Using the current class '{command.ClassName}'.");

        bool result = Dispatch(context);

        // Feed the outcome back to the learning layer.
        LearningCoordinator.RecordOutcome(command.OriginalSentence, command.Intent, result, command);

        // Propagate any project change made by a handler back to the caller.
        currentProject = context.CurrentProject;
        return result;
    }

    /// <summary>
    /// The intents that act on one class. The parser fills the session's class into every command
    /// that names none, create project included, so the note is kept to these.
    /// </summary>
    private static readonly HashSet<IntentType> ClassTargetIntents =
    [
        IntentType.CreateProperty, IntentType.DeleteProperty, IntentType.RenameProperty,
        IntentType.ChangePropertyType, IntentType.ListProperties,
        IntentType.CreateMethod, IntentType.DeleteMethod, IntentType.RenameMethod, IntentType.ListMethods,
        IntentType.ShowClass, IntentType.AnalyzeClass, IntentType.DeleteClass, IntentType.RenameClass,
        IntentType.ClassInherit,
        IntentType.AddValidation, IntentType.ShowValidation, IntentType.RemoveValidation,
        IntentType.AddBusinessValidation, IntentType.ShowBusinessValidation,
        IntentType.RemoveBusinessValidation, IntentType.GenerateValidator,
    ];

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
            [IntentType.CreateEnum] = HandleCreateEnum,
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

        // No name used to make a project called NewProject, which nobody asked for - and a name the
        // parser could not read (a quoted one, once) made it too, without a word.
        if (string.IsNullOrWhiteSpace(command.ProjectName))
        {
            Console.WriteLine(
                "A project name is required, e.g. create console project EmployeeManagement");
            return false;
        }

        string projectName = command.ProjectName;

        var projectReq = new ProjectRequest
        {
            ProjectName = projectName,
            // A relative folder is under the workspace, resolved once here: passed on as typed,
            // `dotnet new -o` ran inside it and nested it twice, and the registry kept a relative
            // path that counted as inside whatever the current directory was.
            OutputFolder = string.IsNullOrWhiteSpace(command.OutputFolder)
                ? Path.Combine(ctx.WorkspaceRoot, projectName)
                : Path.GetFullPath(Path.Combine(ctx.WorkspaceRoot, command.OutputFolder)),
            ProjectType = command.ProjectType ?? DotNetProjectType.Console,
            Framework = string.IsNullOrWhiteSpace(command.Framework) ? "net10.0" : command.Framework
        };

        // Said only when one is really created: an existing project printed "Creating project..."
        // and then "Existing project loaded".
        if (ExistingProjectFile(projectReq, ctx.WorkspaceRoot) == null)
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

        // A kind and modifier C# does not allow together, or a base the kind cannot have: refused
        // before anything is generated, so nothing is written.
        if ((KindRefusal(command) ?? RecordBaseRefusal(ctx)) is { } kindError)
        {
            Output.Error(kindError);
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

        if (ExistingTypeFile(ctx) is { } existing)
        {
            // It used to say "Saved generated class" and write over the file, losing every property
            // and method in it. "create class" makes a new class; changing one has its own commands.
            var name = EntityExtractor.DeclaredName(existing, classDef.Name);

            // Declared in a file of another name (a record OrderLine in Order.cs): delete class
            // works on a class's own file, so it is no way to start this one again.
            var ownFile = Path.GetFileNameWithoutExtension(existing).Equals(name, StringComparison.OrdinalIgnoreCase);

            Output.Error(ownFile
                ? $"Class '{name}' already exists: {existing}"
                : $"'{name}' already exists: it is declared in {existing}");
            Console.WriteLine(
                "  It was left as it is: creating it again would replace its properties and methods."
                + Environment.NewLine
                + $"  To change it, use add property <name> <type> to {name} or add method <name> to {name}"
                + (ownFile
                    ? ";" + Environment.NewLine + $"  to start it again, delete class {name} first."
                    : "."));
            return false;
        }

        var saved = GenerateAndSave(ctx);

        // After the class is on disk, and only if it got there: a note about what was left out of
        // a class that was never written would be the second confusing thing in a row.
        if (saved)
            ReportIgnoredProperties(command);

        return saved;
    }

    /// <summary>The C# rule a create command breaks, in the kind's terms, or null when it breaks none.</summary>
    private static string? KindRefusal(ParsedCommand command)
    {
        if (command.Metadata.TryGetValue(ParsedCommand.KindErrorKey, out var parseError)
            && !string.IsNullOrWhiteSpace(parseError))
            return parseError;

        var hasBase = !string.IsNullOrWhiteSpace(command.BaseClass);
        var hasInterface = command.Interfaces.Any(i => !string.IsNullOrWhiteSpace(i));

        if (command.Kind == DeclaredKind.Struct && hasBase)
            return "A struct cannot inherit from a class; use implements for an interface.";

        if (command.Modifier == TypeModifier.Static && hasBase)
            return "A static class cannot inherit from another class.";

        if (command.Modifier == TypeModifier.Static && hasInterface)
            return "A static class cannot implement an interface.";

        return null;
    }

    /// <summary>
    /// A record can inherit only from a record. Names the base and its kind when the project
    /// declares it as something else; a base the project does not declare is left to the compile.
    /// </summary>
    private static string? RecordBaseRefusal(CommandExecutionContext ctx)
    {
        var command = ctx.Command;
        if (command.Kind != DeclaredKind.Record || string.IsNullOrWhiteSpace(command.BaseClass))
            return null;

        var directory = ProjectLocator.Resolve(command, ctx.CurrentProject, ctx.WorkspaceRoot).Directory;
        var file = ClassFiles.DeclaringFile(directory, command.BaseClass);
        if (file is null)
            return null;

        var declaration = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(File.ReadAllText(file))
            .GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax>()
            .FirstOrDefault(d => d.Identifier.Text.Equals(command.BaseClass, StringComparison.OrdinalIgnoreCase));

        var kind = declaration switch
        {
            Microsoft.CodeAnalysis.CSharp.Syntax.RecordDeclarationSyntax => null,
            Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax => "a class",
            Microsoft.CodeAnalysis.CSharp.Syntax.StructDeclarationSyntax => "a struct",
            Microsoft.CodeAnalysis.CSharp.Syntax.EnumDeclarationSyntax => "an enum",
            Microsoft.CodeAnalysis.CSharp.Syntax.InterfaceDeclarationSyntax => "an interface",
            _ => null
        };

        return kind is null
            ? null
            : $"A record can inherit only from another record; '{declaration!.Identifier.Text}' is {kind}.";
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
            // "2x int" was told it had a type that is not primitive, when the name was the fault.
            var name = ignored.Split(' ', 2)[0];
            if (!Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsValidIdentifier(name))
            {
                Output.Warning($"Ignored '{ignored}': '{name}' is not a valid property name.");
                continue;
            }

            Output.Warning(
                $"Ignored '{ignored}': only built-in types (string, int, decimal, DateTime and the like, "
                + "also with ?, [] or in List<>) can be given here. "
                + "Relationships between classes are set separately — the tool will offer.");
        }

        // A method whose parameter list could not be read was left out, not written half-done.
        foreach (var (entry, error) in command.IgnoredMethods)
            Output.Warning($"Ignored method '{entry}': {error}");
    }

    private static bool HandleShowClass(CommandExecutionContext ctx)
    {
        // ShowClass prints the class, or the warning that says why not; its return value is a
        // status, and echoing it printed "Class 'Nope' not found." and then "Class not found.".
        return ClassValidator.ShowClass(ctx.Command.ClassName) == ClassValidator.ClassShown;
    }

    private static bool HandleRenameClass(CommandExecutionContext ctx)
    {
        var ok = ClassRenameService.RenameClass(
            ctx.WorkspaceRoot, ctx.Command.ClassName, ctx.Command.NewClassName, out var message, out var warnings);
        Output.Result(ok, message);
        foreach (var warning in warnings)
            Output.Warning(warning);
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

        Output.Result(ok, message);
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
        if (ctx.Command.Metadata.TryGetValue(ParsedCommand.ParameterErrorKey, out var parameterError))
        {
            Output.Error($"Method '{ctx.Command.MethodName}' was not added: {parameterError}");
            return false;
        }

        UseDeclaredNames(ctx, member: null);
        var ok = ClassMethodService.AddMethod(
            ctx.WorkspaceRoot, ctx.Command.ClassName,
            ctx.Command.MethodName, ctx.Command.ReturnType, ctx.Command.Parameters, out var message);
        Output.Result(ok, message);
        if (ok)
            WarnOfImplementersLacking(ctx, ctx.Command.MethodName);
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

        UseDeclaredNames(ctx, member: "Method");

        var ok = ClassMethodEditService.DeleteMethod(
            ctx.WorkspaceRoot, ctx.Command.ClassName, ctx.Command.MethodName, out var message);
        Console.WriteLine(message);
        if (ok)
        {
            WarnOfUsers(ctx, ctx.Command.MethodName, "Method", "removed");
            WarnOfImplementers(ctx, $"any {ctx.Command.MethodName} declared there stays, no longer part of the interface.");
        }
        return ok;
    }

    private static bool HandleRenameMethod(CommandExecutionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(ctx.Command.ClassName))
        {
            Console.WriteLine("No class specified for the method rename.");
            return false;
        }

        UseDeclaredNames(ctx, member: "Method");

        var ok = ClassMethodEditService.RenameMethod(
            ctx.WorkspaceRoot, ctx.Command.ClassName,
            ctx.Command.MethodName, ctx.Command.NewMethodName, out var message);
        Console.WriteLine(message);
        if (ok)
        {
            WarnOfUsers(ctx, ctx.Command.MethodName, "Method", "renamed");
            WarnOfImplementers(ctx, $"rename {ctx.Command.MethodName} to {ctx.Command.NewMethodName} there as well, or that code will not compile.");
        }
        return ok;
    }

    // The class, and the member the command edits, as declared: the warnings after an edit repeated
    // what was typed - "rules still name name", "rename area to Size there as well" for Area. The
    // services already find a name in another case; this makes the handlers' own messages agree.
    // A member that cannot be told (none, or two differing only in case) is left as typed, and the
    // service then says why.
    private static void UseDeclaredNames(CommandExecutionContext ctx, string? member)
    {
        if (string.IsNullOrWhiteSpace(ctx.Command.ClassName)
            || !ClassMethodEditService.TryLoadParts(ctx.WorkspaceRoot, ctx.Command.ClassName, out var parts, out _))
            return;

        ctx.Command.ClassName = parts[0].Node.Identifier.Text;

        if (member == "Property" && !string.IsNullOrWhiteSpace(ctx.Command.PropertyName)
            && ClassMethodEditService.PropertyNamed(parts, ctx.Command.PropertyName) is { } property)
            ctx.Command.PropertyName = property.Property.Identifier.Text;

        if (member == "Method" && !string.IsNullOrWhiteSpace(ctx.Command.MethodName)
            && ClassMethodEditService.MethodNamed(parts, ctx.Command.MethodName) is { } method)
            ctx.Command.MethodName = method;
    }

    // After a member is added to an interface: the classes implementing it that do not have it yet.
    // They are named, not edited - what their member should do is theirs to say.
    private static void WarnOfImplementersLacking(CommandExecutionContext ctx, string member)
    {
        if (InterfaceImplementers.AddedWarning(ctx.WorkspaceRoot, ctx.Command.ClassName, member) is { } warning)
            Output.Warning(warning);
    }

    // After a member of an interface is removed, renamed or retyped: the classes implementing it,
    // which the edit left as they were.
    private static void WarnOfImplementers(CommandExecutionContext ctx, string consequence)
    {
        if (InterfaceImplementers.ChangedWarning(ctx.WorkspaceRoot, ctx.Command.ClassName, consequence) is { } warning)
            Output.Warning(warning);
    }

    // After a member is removed or renamed: name the code that still uses its old name, which
    // the edit left alone and which no longer compiles.
    private static void WarnOfUsers(
        CommandExecutionContext ctx, string oldName, string what, string done, IReadOnlyCollection<string>? skipFiles = null)
    {
        if (MemberReferences.Warning(ctx.WorkspaceRoot, ctx.Command.ClassName, oldName, what, done, skipFiles) is { } warning)
            Output.Warning(warning);
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

        // A rule on an interface was stored and then had nothing to check: generate validator
        // skipped it, or wrote a partial class beside the interface.
        if (InterfaceImplementers.ValidationRefusalFor(ctx.WorkspaceRoot, ctx.Command.ClassName) is { } refusal)
        {
            Output.Error(refusal);
            return false;
        }

        var parseCtx = new ValidationParseContext
        {
            Fragment = ctx.Command.ValidationText,
            ClassName = ctx.Command.ClassName,
            TargetProperty = string.IsNullOrWhiteSpace(ctx.Command.PropertyName) ? null : ctx.Command.PropertyName
        };

        // One rule per command, as typed (see ParseOne).
        var rules = ValidationRuleCatalog.Default.ParseOne(ctx.Command.ValidationText, category, parseCtx);
        if (rules.Count == 0)
        {
            Output.Warning($"Could not parse a validation rule from: '{ctx.Command.ValidationText}'");
            return false;
        }

        // A rule on a class or property that is not there was accepted and reported as added, and
        // generate validator later skipped it with only a source comment to show for it.
        var className = ctx.Command.ClassName;
        var classFile = EntityExtractor.FindClassFile(ctx.WorkspaceRoot, className);
        if (classFile == null)
        {
            Output.Error($"Class '{className}' not found, so the rule was not added."
                + CloseNames.Hint(className, EntityExtractor.ClassNames(ctx.WorkspaceRoot)));
            return false;
        }

        // The class's own properties, in all its partial files: Street on a nested Address was
        // accepted (the validator then did not compile), and Notes in Customer.Notes.cs was not.
        var declared = EntityExtractor.DeclaredName(classFile, className);
        var known = EntityExtractor.ExtractProperties(classFile, declared).Select(p => p.Name).ToList();
        foreach (var rule in rules)
        {
            var missing = rule.Properties
                .FirstOrDefault(p => !known.Contains(p, StringComparer.OrdinalIgnoreCase));
            if (missing != null)
            {
                Output.Error($"'{declared}' has no property '{missing}', so the rule was not added."
                    + CloseNames.Hint(missing, known));
                return false;
            }
        }

        // The same rule twice is two identical checks and two identical messages.
        var existing = ValidationRegistry.Get(className, category);
        var fresh = rules
            .Where(rule => !existing.Any(r => r.Describe().Equals(rule.Describe(), StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (fresh.Count == 0)
        {
            Output.Warning($"{declared} already has this rule: {rules[0].Describe()}. It was not added again.");
            return true;
        }

        foreach (var rule in fresh)
            ValidationRegistry.Add(className, rule, ctx.Command.ValidationText);

        Output.Success(successPrefix);
        return true;
    }

    private static bool HandleAddBusinessValidation(CommandExecutionContext ctx) =>
        AddRules(ctx, ValidationCategory.Business,
            successPrefix: $"Business rule added to {ctx.Command.ClassName}: {ctx.Command.ValidationText}");

    private static bool HandleShowBusinessValidation(CommandExecutionContext ctx) =>
        ShowRules(ctx.WorkspaceRoot, ctx.Command.ClassName, ValidationCategory.Business, "Business rules");

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
        if (ExistingTypeFile(ctx) is { } existing)
        {
            // It used to write over the file unchecked, losing every member in it — and "create
            // interface ishape" names the file Ishape.cs, which on Windows is IShape.cs.
            var name = EntityExtractor.DeclaredName(existing, ctx.Command.ClassName);
            Output.Error($"'{name}' already exists: {existing}");
            Console.WriteLine(
                "  It was left as it is: creating it again would replace everything in it."
                + Environment.NewLine
                + $"  To start it again, delete class {name} first.");
            return false;
        }

        var ok = GenerateAndSave(ctx);
        if (ok)
            ReportIgnoredProperties(ctx.Command);

        // Say which name was made when it is not what was typed (InterfaceNaming).
        var typed = System.Text.RegularExpressions.Regex.Match(ctx.RawInput,
            @"\binterface\s+(?:(?:a|an|new)\s+)?(?<name>[A-Za-z_]\w*)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var made = ctx.Command.InterfaceName ?? ctx.Command.ClassName;
        if (ok && typed.Success && !string.IsNullOrWhiteSpace(made) && typed.Groups["name"].Value != made)
            Console.WriteLine($"Named the interface {made}: a name is kept as typed only when it is I followed by a "
                + $"capital letter, as IShape is; anything else is given the I prefix.");

        return ok;
    }

    private static bool HandleCreateEnum(CommandExecutionContext ctx)
    {
        var command = ctx.Command;

        if (!ClassValidator.IsValidClassName(command.ClassName))
        {
            Output.Error("Error: Could not determine a valid enum name from input.");
            return false;
        }

        // A value list that cannot be read, or a base the enum cannot have: refused before anything is written.
        if (command.Metadata.TryGetValue(ParsedCommand.KindErrorKey, out var kindError)
            && !string.IsNullOrWhiteSpace(kindError))
        {
            Output.Error(kindError);
            return false;
        }

        if (ExistingTypeFile(ctx) is { } existing)
        {
            var name = EntityExtractor.DeclaredName(existing, command.ClassName);
            Output.Error($"'{name}' already exists: {existing}");
            Console.WriteLine(
                "  It was left as it is: creating it again would replace everything in it."
                + Environment.NewLine
                + $"  To start it again, delete class {name} first.");
            return false;
        }

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

        // The class's own properties, in its other partial files as well (a property in
        // Customer.Extra.cs was not listed), and not those of a nested or sibling type in its
        // file (Address's Street and Money's Amount were listed as Customer's).
        var className = EntityExtractor.DeclaredName(file, ctx.Command.ClassName);
        var properties = EntityExtractor.ExtractProperties(file, className)
            .Select(p => (p.Type, p.Name))
            .ToList();

        if (properties.Count == 0)
        {
            Console.WriteLine($"No properties found in '{className}'.");
            return true;
        }

        Console.WriteLine($"Properties in {className}:");
        foreach (var (type, name) in properties)
            Console.WriteLine($"  {type} {name}");
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
        UseDeclaredNames(ctx, member: null);
        var ok = ClassPropertyService.AddProperty(
            ctx.WorkspaceRoot, ctx.Command.ClassName,
            ctx.Command.PropertyName, ctx.Command.PropertyType, out var message);
        Console.WriteLine(message);
        if (ok)
            WarnOfImplementersLacking(ctx, ctx.Command.PropertyName);
        return ok;
    }

    private static bool HandleDeleteProperty(CommandExecutionContext ctx)
    {
        UseDeclaredNames(ctx, member: "Property");
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

            // Regenerated first, so the users-of scan does not report the stale validator.
            var named = RefreshValidators(ctx, "was removed");
            WarnOfUsers(ctx, ctx.Command.PropertyName, "Property", "removed", named);
            WarnOfImplementers(ctx, $"any {ctx.Command.PropertyName} declared there stays, no longer part of the interface.");
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

        UseDeclaredNames(ctx, member: "Property");

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

            var named = RefreshValidators(ctx, $"was renamed to {ctx.Command.NewPropertyName}");
            WarnOfUsers(ctx, ctx.Command.PropertyName, "Property", "renamed", named);
            WarnOfImplementers(ctx, $"rename {ctx.Command.PropertyName} to {ctx.Command.NewPropertyName} there as well, or that code will not compile.");
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

        UseDeclaredNames(ctx, member: "Property");

        var ok = ClassPropertyTypeService.ChangeType(
            ctx.WorkspaceRoot, ctx.Command.ClassName,
            ctx.Command.PropertyName, ctx.Command.PropertyType, out var message);
        Console.WriteLine(message);
        if (ok)
        {
            var named = RefreshValidators(ctx, $"is now {ctx.Command.PropertyType}");

            // A member computed from it, or code elsewhere reading it, was written for the old type
            // (`int Total => Qty * 2` after Qty became long is CS0266). The validators just
            // regenerated or named are not repeated.
            WarnOfUsers(ctx, ctx.Command.PropertyName, "Property", $"changed to {ctx.Command.PropertyType}", named);
            WarnOfImplementers(ctx, $"change {ctx.Command.PropertyName} to {ctx.Command.PropertyType} there as well, or that code will not compile.");
        }

        return ok;
    }

    // After a property is removed, renamed or retyped: a validator generated earlier still reads it
    // as it was. generate validator's file is rebuilt from the current rules and properties; a
    // built app's .Generated.cs cannot be (see StaleValidators), so it is named.
    // Returns the validator files it dealt with, so the users-of warning does not name them again.
    private static List<string> RefreshValidators(CommandExecutionContext ctx, string change)
    {
        var handled = new List<string>();

        var regenerated = StaleValidators.Regenerate(ctx.WorkspaceRoot, ctx.Command.ClassName);
        if (regenerated != null)
        {
            Console.WriteLine($"Validator regenerated: {regenerated}");
            handled.Add(regenerated);
        }

        if (StaleValidators.AppValidatorWarning(
                ctx.WorkspaceRoot, ctx.Command.ClassName, ctx.Command.PropertyName, change) is { } warning)
        {
            Output.Warning(warning);
            handled.AddRange(StaleValidators.AppValidatorsReading(ctx.WorkspaceRoot, ctx.Command.ClassName, ctx.Command.PropertyName)
                .Select(f => Path.Combine(ctx.WorkspaceRoot, f)));
        }

        return handled;
    }

    private static bool HandleShowValidation(CommandExecutionContext ctx) =>
        ShowRules(ctx.WorkspaceRoot, ctx.Command.ClassName, ValidationCategory.Data, "Validation rules");

    private static bool ShowRules(string workspaceRoot, string className, ValidationCategory category, string heading)
    {
        var rules = ValidationRegistry.Get(className, category);
        if (rules.Count == 0)
        {
            // "No validation rules for 'Nope'" read as though Nope were a class with none.
            if (EntityExtractor.FindClassFile(workspaceRoot, className) == null)
            {
                Output.Warning($"Class '{className}' not found."
                    + CloseNames.Hint(className, EntityExtractor.ClassNames(workspaceRoot)));
                return true;
            }

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
            RegenerateValidatorAfterRuleRemoval(ctx.WorkspaceRoot, className);
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
            RegenerateValidatorAfterRuleRemoval(ctx.WorkspaceRoot, className);
            return true;
        }

        Output.Warning($"Say which rule: remove {words} from {className}.<field>, or "
            + $"remove {words} <number> from {className} with a number from show {words} {className}.");
        return false;
    }

    // A validator generate validator wrote still enforced the removed rule until it was run again,
    // as remove property already regenerates it.
    private static void RegenerateValidatorAfterRuleRemoval(string workspaceRoot, string className)
    {
        if (StaleValidators.Regenerate(workspaceRoot, className) is { } regenerated)
            Console.WriteLine($"Validator regenerated: {regenerated}");
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
    /// The file already holding the type <c>create class</c> or <c>create interface</c> would write
    /// (both save to <c>&lt;ClassName&gt;.cs</c>), or null. Looked for where the type would be saved —
    /// the file itself, matched without case because writing <c>employee.cs</c> on Windows replaces
    /// <c>Employee.cs</c>, and anywhere else in that folder (a built app keeps its classes in
    /// <c>Models</c>), skipping build output and DeletedClasses.
    /// </summary>
    private static string? ExistingTypeFile(CommandExecutionContext ctx)
    {
        var className = ctx.Command.ClassName;
        if (string.IsNullOrWhiteSpace(className))
            return null;

        var directory = ProjectLocator.Resolve(ctx.Command, ctx.CurrentProject, ctx.WorkspaceRoot).Directory;

        var sameFolder = Directory.EnumerateFiles(directory, "*.cs", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
                .Equals(className, StringComparison.OrdinalIgnoreCase));

        // Or a type of that name declared in another file: a record OrderLine in Order.cs.
        return sameFolder ?? ClassFiles.FileOf(directory, className) ?? ClassFiles.DeclaringFile(directory, className);
    }

    /// <summary>
    /// Generates code for the current command, runs semantic/compile checks,
    /// and persists the result. Shared by CreateClass and CreateInterface.
    /// </summary>
    private static bool GenerateAndSave(CommandExecutionContext ctx)
    {
        var code = NaturalLanguageCodeEngine.ExecuteCommand(ctx.Command, ctx.WorkspaceRoot);
        if (string.IsNullOrWhiteSpace(code))
            return false;

        if (ctx.Command.Intent is IntentType.CreateClass or IntentType.CreateEnum)
        {
            // Checked beside the rest of its project, which also fills in the members of the
            // interfaces it implements: compiled alone, a project interface was "CS0246 not found".
            var target = ProjectLocator.Resolve(ctx.Command, ctx.CurrentProject, ctx.WorkspaceRoot);
            var completed = NewClassCompletion.Complete(code, target);

            // Checked with its project's files, an error is the class's own: saved, it made the
            // project fail dotnet build while create class reported success.
            if (completed.Errors.Count > 0 && !string.IsNullOrWhiteSpace(target.ProjectFile))
            {
                NewClassCompletion.ReportRefused(completed);
                return false;
            }

            NewClassCompletion.Report(completed);
            code = completed.Code;
        }
        else
        {
            SemanticCompilerRunner.RunSemanticAndCompile(ctx.Command, code, ctx.ConfiguredRules);
        }

        GeneratedCodeService.Save(ctx.Command, code, ctx.CurrentProject, ctx.WorkspaceRoot);
        return true;
    }

    private static string? ExistingProjectFile(ProjectRequest request, string workspaceRoot) =>
        Directory.GetFiles(workspaceRoot, $"{request.ProjectName}.csproj", SearchOption.AllDirectories)
            .FirstOrDefault();

    // FIX #8: Correct path handling - store .csproj path and directory separately
    private static ProjectMetaData LoadOrCreateProject(ProjectRequest request, string workspaceRoot)
    {
        var existingProject = ExistingProjectFile(request, workspaceRoot);

        if (existingProject != null)
        {
            var projectDir = Path.GetDirectoryName(existingProject);
            return new ProjectMetaData
            {
                Success = true,
                ProjectPath = projectDir ?? Path.GetDirectoryName(workspaceRoot) ?? workspaceRoot,
                CsProjPath = existingProject,
                ProjectName = Path.GetFileNameWithoutExtension(existingProject),
                Message = $"Project '{Path.GetFileNameWithoutExtension(existingProject)}' already exists ({existingProject}); "
                    + "nothing was created, and it is now the current project."
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
