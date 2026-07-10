using CodeGenerationEngine.Models;

namespace NaturalLanguageBridge;

/// <summary>
/// Renders a <see cref="DetectedAction"/> as a single line for the review list.
/// Presentation only; kept apart from <see cref="BatchCommandDetector"/> because
/// it is the part most likely to churn.
/// </summary>
public static class ActionFormatter
{
    private const int IntentColumnWidth = 14;

    /// <summary>
    /// Formats one detected action. Unactionable lines show the original
    /// sentence and the reason it was skipped.
    /// </summary>
    public static string Format(DetectedAction action)
    {
        if (!action.IsActionable)
            return $"(skipped)  \"{action.Sentence}\"  — {action.Note}";

        // Preview is non-null whenever IsActionable is true; see DetectOne.
        var command = action.Preview!;

        var detail = action.Intent switch
        {
            IntentType.CreateProject
                or IntentType.UseProject
                or IntentType.AnalyzeProject => command.ProjectName,

            IntentType.RenameClass => $"{command.ClassName} -> {command.NewClassName}",

            IntentType.CreateClass
                or IntentType.DeleteClass
                or IntentType.ShowClass
                or IntentType.RestoreClass
                or IntentType.AnalyzeClass
                or IntentType.ClassInherit => command.ClassName,

            IntentType.CreateProperty
                or IntentType.DeleteProperty => $"{command.PropertyName} : {command.PropertyType}",

            IntentType.CreateMethod
                or IntentType.DeleteMethod => command.MethodName,

            _ => string.Empty
        };

        return detail.Length == 0
            ? action.Intent.ToString()
            : $"{action.Intent.ToString().PadRight(IntentColumnWidth)} {detail}";
    }
}
