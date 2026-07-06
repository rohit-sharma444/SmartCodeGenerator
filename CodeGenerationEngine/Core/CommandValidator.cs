using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core;

public static class CommandValidator
{
    public static List<string> Validate(ParsedCommand command)
    {
        List<string> errors = [];

        if (command.Intent == IntentType.CreateMethod &&
            string.IsNullOrWhiteSpace(command.ClassName))
        {
            errors.Add("No target class specified. Create or select a class first.");
        }

        if (command.Intent == IntentType.CreateClass &&
            string.IsNullOrWhiteSpace(command.ClassName))
        {
            errors.Add("Class name is required.");
        }

        if (command.Intent == IntentType.RenameClass)
        {
            if (string.IsNullOrWhiteSpace(command.ClassName))
                errors.Add("Source class name is required.");

            if (string.IsNullOrWhiteSpace(command.NewClassName))
                errors.Add("New class name is required.");
        }

        if (command.Intent == IntentType.CreateProject &&
            string.IsNullOrWhiteSpace(command.ProjectName))
        {
            errors.Add("Project name is required.");
        }

        return errors;
    }
}