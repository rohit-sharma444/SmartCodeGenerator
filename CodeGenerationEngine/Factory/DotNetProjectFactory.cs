using CodeGenerationEngine.Core;
using CodeGenerationEngine.Helper;
using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Factory;


/// <summary>
/// Factory class responsible for creating .NET projects based on the provided project type and framework.
/// </summary>
public static class DotNetProjectFactory
{
    /// <summary>
    /// Generous enough for a cold NuGet restore on a slow link, short enough that a
    /// wedged `dotnet` does not strand the caller forever.
    /// </summary>
    private static readonly TimeSpan DotNetTimeout = TimeSpan.FromMinutes(2);

    public static ProjectMetaData CreateProject(ProjectRequest request)
    {
        try
        {
            if (!Directory.Exists(request.OutputFolder))
                Directory.CreateDirectory(request.OutputFolder);

            var template = GetTemplate(request.ProjectType);

            // "-o <OutputFolder>" emits the project directly into OutputFolder.
            // Without it, "dotnet new -n <name>" creates its own <name> subfolder,
            // producing a doubly nested <workspace>/<name>/<name>/<name>.csproj.
            var arguments = $"new {template} -n {request.ProjectName} " +
                $"-o \"{request.OutputFolder}\" -f {request.Framework}";

            var create = ProcessRunner.Run(
                "dotnet", arguments, request.OutputFolder, DotNetTimeout);

            var projectPath = Path.Combine(request.OutputFolder,
                $"{request.ProjectName}.csproj");

            var result = new ProjectMetaData
            {
                Success = create.ExitCode == 0,

                ProjectPath = request.OutputFolder,

                // Without ProjectName/CsProjPath a freshly created project is
                // invisible to 'list projects' and unselectable via 'use project'
                // (both key on ProjectName) until the app is restarted.
                ProjectName = request.ProjectName,
                CsProjPath = projectPath,

                Message = DescribeCreate(create, template)
            };

            if (!result.Success)
                return result;

            // Best-effort: the project is usable without it, so a failure here is
            // reported alongside the success rather than replacing it.
            var package = ProcessRunner.Run(
                "dotnet",
                $"add \"{projectPath}\" package System.ComponentModel.Annotations",
                request.OutputFolder,
                DotNetTimeout);

            if (package.ExitCode != 0)
                result.Message += Environment.NewLine + DescribeFailedPackageAdd(package);

            ProjectManager.Register(result);
            ProjectManager.SaveProjects();

            return result;
        }
        catch (Exception ex)
        {
            return new ProjectMetaData
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    private static string DescribeCreate(ProcessResult create, string template)
    {
        if (create.TimedOut)
            return $"`dotnet new {template}` did not finish within {DotNetTimeout.TotalMinutes:0} minutes and was cancelled.";

        return create.ExitCode == 0 ? create.StandardOutput : create.StandardError;
    }

    private static string DescribeFailedPackageAdd(ProcessResult package)
    {
        if (package.TimedOut)
            return $"Warning: adding System.ComponentModel.Annotations did not finish within "
                 + $"{DotNetTimeout.TotalMinutes:0} minutes and was cancelled.";

        return "Warning: adding System.ComponentModel.Annotations failed: "
             + package.StandardError.Trim();
    }

    private static string GetTemplate(DotNetProjectType projectType)
    {
        return projectType switch
        {
            DotNetProjectType.Console => "console",
            DotNetProjectType.ClassLibrary => "classlib",
            DotNetProjectType.WebApi => "webapi",
            DotNetProjectType.BlazorServer => "blazor",
            DotNetProjectType.BlazorWebAssembly => "blazorwasm",
            DotNetProjectType.Wpf => "wpf",
            DotNetProjectType.WinForms => "winforms",
            DotNetProjectType.WorkerService => "worker",
            DotNetProjectType.Mvc => "mvc",
            DotNetProjectType.XUnitTest => "xunit",
            _ => throw new NotSupportedException()
        };
    }
}