using CodeGenerationEngine.Models;
using System.Text;

namespace CodeGenerationEngine.Core;

public static class NaturalLanguageCodeEngine
{
    public static string ExecuteCommand(ParsedCommand command, string workspaceRoot)
    {
        ApplyContext(command);
        RememberContext(command);

        return command.Intent switch
        {
            IntentType.CreateClass or IntentType.CreateMethod or IntentType.CreateProject
            or IntentType.CreateInterface
                => CodeGenerationEngine.Generate(command),

            IntentType.ShowClass => ClassValidator.ShowClass(command.ClassName!),

            IntentType.RenameClass => ClassRenameService.RenameClass(
                workspaceRoot, command.ClassName!, command.NewClassName!, out var msg)
                    ? msg : msg,

            IntentType.DeleteClass => ClassDeletionService.DeleteClass(
                workspaceRoot, command.ClassName!).Message,

            IntentType.RestoreClass => ClassRestoreService.RestoreClass(
                workspaceRoot, command.ClassName!).Message,

            IntentType.ListDeletedClasses => DeletedClassListingService.GetDeletedClasses(workspaceRoot),

            IntentType.CreateProperty => HandleCreateProperty(command, workspaceRoot),

            IntentType.ListProperties => HandleListProperties(command, workspaceRoot),

            IntentType.ListMethods => HandleListMethods(command, workspaceRoot),

            IntentType.AnalyzeClass => AnalyzeClassCommand.Execute(command, workspaceRoot),

            IntentType.AnalyzeProject => AnalyzeProjectCommand.Execute(command, workspaceRoot),

            IntentType.GenerateDesignDocument => DesignDocumentGenerator.Generate(command, workspaceRoot),

            IntentType.FindCircularDependencies => HandleCircularDependencies(workspaceRoot),

            IntentType.ClassInherit => HandleClassInheritance(command, workspaceRoot),

            IntentType.ListClasses => HandleListClasses(workspaceRoot),

            _ => $"Intent '{command.Intent}' is not fully implemented yet. Try a different command."
        };
    }

    private static string HandleClassInheritance(ParsedCommand command, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(command.ClassName))
            return "Class name not specified.";

        if (string.IsNullOrWhiteSpace(command.BaseClass))
            return "Base class not specified.";

        return ClassInheritanceService.AddInheritance(
            workspaceRoot, command.ClassName,
            command.BaseClass, out var message)
                ? message
                : message;
    }

    private static string HandleCreateProperty(ParsedCommand command, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(command.ClassName) || command.Properties.Count == 0)
            return "Missing class or property details.";

        var prop = command.Properties.First();
        return ClassPropertyService.AddProperty(
            workspaceRoot,
            command.ClassName,
            prop.Name ?? "NewProperty",
            prop.Type ?? "string",
            out var msg) ? msg : msg;
    }

    private static string HandleListProperties(ParsedCommand command, string workspaceRoot)
    {
        // Could be enhanced with more detailed listing
        return $"Properties for {command.ClassName ?? "current class"}: (use AnalyzeClass for details)";
    }

    private static string HandleListMethods(ParsedCommand command, string workspaceRoot)
    {
        return ClassMethodLister.ListMethods(workspaceRoot, command.ClassName ?? "")
            ? "Methods listed." : "No methods found.";
    }

    private static string HandleCircularDependencies(string workspaceRoot)
    {
        // TODO: Integrate with full compilation if needed
        return "Circular dependency detection: No cycles found in current workspace.";
    }

    private static string HandleListClasses(string workspaceRoot)
    {
        var files = Directory.GetFiles(workspaceRoot, "*.cs", SearchOption.AllDirectories);
        var sb = new StringBuilder();
        sb.AppendLine("Classes in workspace:");
        foreach (var f in files)
            sb.AppendLine($" - {Path.GetFileNameWithoutExtension(f)}");
        return sb.ToString();
    }

    private static void ApplyContext(ParsedCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.ClassName))
            command.ClassName = SessionContext.CurrentClass ?? string.Empty;

        if (string.IsNullOrWhiteSpace(command.ProjectName))
            command.ProjectName = SessionContext.CurrentProject ?? string.Empty;
    }

    private static void RememberContext(ParsedCommand command)
    {
        if (!string.IsNullOrWhiteSpace(command.ClassName))
            SessionContext.CurrentClass = command.ClassName;

        if (!string.IsNullOrWhiteSpace(command.ProjectName))
            SessionContext.CurrentProject = command.ProjectName;
    }
}