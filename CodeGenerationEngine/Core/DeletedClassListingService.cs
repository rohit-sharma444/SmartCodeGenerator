namespace CodeGenerationEngine.Core;

/// <summary>
/// Service responsible for listing deleted classes in the workspace.
/// </summary>
public static class DeletedClassListingService
{
    public static string GetDeletedClasses(string workspaceRoot)
    {
        var items = DeletedClassRegistry.GetAll(workspaceRoot);

        if (items.Count == 0)
            return "No deleted classes found.";

        var lines = new List<string>
        {
            "",
            "Deleted Classes",
            "--------------------------"
        };

        foreach (var item in items)
        {
            lines.Add($"{item.ClassName}  |  {item.DeletedAt.ToLocalTime():g}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}