using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Provides functionality to rename a class in a C# project.
/// </summary>
public static class ClassRenameService
{
    public static bool RenameClass(string workspaceRoot, string oldClassName, string newClassName, out string message)
    {
        message = string.Empty;

        if (MemberNames.NotAnIdentifier(newClassName, "class") is { } bad)
        {
            message = bad;
            return false;
        }

        try
        {
            var file = Directory
                .GetFiles(workspaceRoot, "*.cs", SearchOption.AllDirectories)
                .FirstOrDefault(x =>
                    Path.GetFileNameWithoutExtension(x)
                        .Equals(oldClassName,
                            StringComparison.OrdinalIgnoreCase));

            if (file == null)
            {
                message = $"Class '{oldClassName}' not found.";
                return false;
            }

            // The move below overwrites, so renaming onto a class that exists threw that class
            // away; one in another folder would become a second class of the same name. Only the
            // file being renamed may hold the new name (a rename that changes only the case).
            var taken = ClassFiles.Under(workspaceRoot).FirstOrDefault(f =>
                Path.GetFileNameWithoutExtension(f).Equals(newClassName, StringComparison.OrdinalIgnoreCase)
                && !Path.GetFullPath(f).Equals(Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase));

            if (taken != null)
            {
                message = $"Class '{newClassName}' already exists: {taken}. '{oldClassName}' was not renamed.";
                return false;
            }

            var code = File.ReadAllText(file);

            code = Regex.Replace(code,
                $@"\bclass\s+{Regex.Escape(oldClassName)}\b",
                $"class {newClassName}");

            File.WriteAllText(file, code);

            var newFile = Path.Combine(Path.GetDirectoryName(file)!,
                    $"{newClassName}.cs");

            File.Move(file, newFile, true);

            message = $"Class '{oldClassName}' renamed to '{newClassName}'.";

            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }
}