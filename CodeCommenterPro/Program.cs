namespace CodeCommenterPro;

using CodeCommenterPro.Configuration;
using CodeCommenterPro.Models;
using CodeCommenterPro.Services;
using System.Text.Json;

public static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                ShowHelp();
                return 1;
            }

            string command = args[0].ToLowerInvariant();

            switch (command)
            {
                case "scan":
                    return ExecuteScan(args);

                case "restore":
                    return ExecuteRestore(args);

                default:
                    ShowHelp();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine("ERROR");
            Console.WriteLine(ex.Message);

            return -1;
        }
    }

    private static int ExecuteScan(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine(
                "Missing folder path.");
            return 1;
        }

        string rootFolder = args[1];

        var settings = Settings.Load();

        Console.WriteLine($"Backups: {settings.CreateBackups}");

        Console.WriteLine($"HTML Report: {settings.GenerateHtmlReport}");

        Console.WriteLine($"Dry Run: {settings.DryRun}");

        settings.RootFolder = rootFolder;

        Console.WriteLine();
        Console.WriteLine("CodeCommenterPro");
        Console.WriteLine("----------------");
        Console.WriteLine();

        Console.WriteLine(
            $"Folder: {rootFolder}");

        var scanner =
            new SolutionScanner(settings);

        CoverageReport report =
            scanner.Process(rootFolder);

        Console.WriteLine();
        Console.WriteLine("Completed");
        Console.WriteLine();

        Console.WriteLine(
            $"Files: {report.TotalFiles}");

        Console.WriteLine(
            $"Classes: {report.TotalClasses}");

        Console.WriteLine(
            $"Methods: {report.TotalMethods}");

        Console.WriteLine(
            $"Properties: {report.TotalProperties}");

        Console.WriteLine(
            $"Constructors: {report.TotalConstructors}");

        Console.WriteLine(
            $"Coverage: {report.Coverage}%");

        return 0;
    }

    private static int ExecuteRestore(
        string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine(
                "Missing folder path.");
            return 1;
        }

        string folder = args[1];

        var restore =
            new RestoreService();

        restore.Restore(folder);

        Console.WriteLine(
            "Restore completed.");

        return 0;
    }

    private static void ShowHelp()
    {
        Console.WriteLine();
        Console.WriteLine(
            "CodeCommenterPro");
        Console.WriteLine();

        Console.WriteLine(
            "scan <folder>");
        Console.WriteLine(
            "restore <folder>");
        Console.WriteLine();
    }


    public static Settings Load()
    {
        string file =
            Path.Combine(
                AppContext.BaseDirectory,
                "settings.json");

        Console.WriteLine($"Loading settings from: {file}");

        if (!File.Exists(file))
        {
            Console.WriteLine("Settings file not found.");
            return new Settings();
        }

        string json = File.ReadAllText(file);

        Console.WriteLine($"Settings content: [{json}]");

        return JsonSerializer.Deserialize<Settings>(json)
               ?? new Settings();
    }
}