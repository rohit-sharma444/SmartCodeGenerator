using System.Text;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Processes project-related commands such as listing projects and selecting a current project.
/// </summary>
public static class ProjectCommandProcessor
{
    public static string Execute(string command)
    {
        if (command.Equals("list projects", StringComparison.OrdinalIgnoreCase))
        {
            var projects = ProjectManager.GetProjects();

            if (!projects.Any())
                return "No projects available.";

            var sb = new StringBuilder();

            for (int i = 0; i < projects.Count; i++)
            {
                var name = !string.IsNullOrWhiteSpace(projects[i].ProjectName)
                    ? projects[i].ProjectName
                    : Path.GetFileName(projects[i].ProjectPath);

                sb.AppendLine($"{i + 1}. {name}");
            }

            //for (int i = 0; i < projects.Count; i++)
            //{
            //    sb.AppendLine($"{i + 1}. {projects[i].ProjectName}");
            //}

            return sb.ToString();
        }

        if (command.StartsWith("use project ", StringComparison.OrdinalIgnoreCase))
        {
            var value = command["use project ".Length..].Trim();

            if (int.TryParse(value, out int index))
            {
                if (ProjectManager.Select(index))
                {
                    return $"Current project set to '{ProjectManager.CurrentProject!.ProjectName}'.";
                }

                return "Project not found.";
            }

            if (ProjectManager.Select(value))
            {
                return $"Current project set to '{ProjectManager.CurrentProject!.ProjectName}'.";
            }

            return "Project not found.";
        }

        return string.Empty;
    }
}