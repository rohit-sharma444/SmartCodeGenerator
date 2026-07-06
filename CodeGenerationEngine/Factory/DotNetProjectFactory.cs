using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using System.Diagnostics;

namespace CodeGenerationEngine.Factory;


/// <summary>
/// Factory class responsible for creating .NET projects based on the provided project type and framework.
/// </summary>
public static class DotNetProjectFactory
{
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

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = arguments,
                    WorkingDirectory = request.OutputFolder,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };

            process.Start();

            string output = process.StandardOutput.ReadToEnd();

            string error = process.StandardError.ReadToEnd();

            process.WaitForExit();

            var projectPath = Path.Combine(request.OutputFolder,
                $"{request.ProjectName}.csproj");

            var packageProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"add \"{projectPath}\" package System.ComponentModel.Annotations",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };

            packageProcess.Start();
            packageProcess.WaitForExit();
            var result = new ProjectMetaData
            {
                Success = process.ExitCode == 0,

                ProjectPath = request.OutputFolder,

                // Without ProjectName/CsProjPath a freshly created project is
                // invisible to 'list projects' and unselectable via 'use project'
                // (both key on ProjectName) until the app is restarted.
                ProjectName = request.ProjectName,
                CsProjPath = projectPath,

                Message = process.ExitCode == 0
                        ? output
                        : error
            };

            if (result.Success)
            {
                ProjectManager.Register(result);
                ProjectManager.SaveProjects();
            }

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
            _ => throw new NotSupportedException()
        };
    }
}