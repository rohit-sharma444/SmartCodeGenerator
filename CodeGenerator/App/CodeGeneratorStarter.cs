using CodeGenerationEngine.Core;
using CodeGenerationEngine.Helper;
using CodeGenerationEngine.Models;
using CodeGenerationEngine.Service;
using Microsoft.CodeAnalysis;
using NaturalLanguageBridge;
using System.Text.Json;

namespace CodeGenerator.App;

/// <summary>
/// The main application class for the Code Generator console app.
/// </summary>
internal class CodeGeneratorStarter
{
    // workspace root (resolved once)
    private string _workspaceRoot = string.Empty;

    private List<PropertyRule> _configuredRules = [];
    private List<string> _currentSuggestions = [];
    private readonly List<string> _commandHistory = [];
    private readonly string _historyPath;
    private const string HISTORY_FILENAME = ".codegen_history";

    private readonly NaturalLanguagePipeline _pipeline = new();
    private readonly BuildAppCommand _buildApp = new();

    internal CodeGeneratorStarter()
    {
        _workspaceRoot = ResolveWorkspaceRoot();

        _historyPath = Path.Combine(_workspaceRoot, HISTORY_FILENAME);

        LoadConfiguredRules();

        ProjectManager.LoadProject();

        // At startup, prefer an existing project in the workspace.
        InitializeExistingProjects();

        // The registry file is global; drop anything not in this workspace so
        // projects from other workspaces don't leak into list/use/generate.
        ProjectManager.RetainOnlyWithin(_workspaceRoot);

        // Load per-workspace learned intent memory. Consult/capture honor
        // CG_NO_LEARN, but this init still loads the file regardless.
        LearnedIntentStore.Initialize(_workspaceRoot);

        LoadCommandHistory();
    }

    private static IReadOnlyList<CommandInfo> GetCommandHints(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return CommandDictionary.Commands.Take(10).ToList();

        input = input.Trim();

        return CommandDictionary.Commands
            .Where(c =>
                c.Command.Contains(
                    input,
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Command)
            .Take(10)
            .ToList();
    }

    /// <summary>
    /// Resolves the workspace root directory.
    /// FIX #4: Proper environment variable handling
    /// 1. Check PROJECTPATH environment variable
    /// 2. If not set or empty, use current directory
    /// </summary>
    private static string ResolveWorkspaceRoot()
    {
        var projectPath = Environment.GetEnvironmentVariable("PROJECTPATH");

        // When launched from Visual Studio / dotnet run the current directory is
        // the build-output folder (bin\Debug\...). PathHelper climbs out of it to
        // the real project/solution root so generated classes are saved somewhere
        // visible instead of being buried under bin.
        var resolved = PathHelper.ResolveWorkspaceRoot(
            projectPath, Directory.GetCurrentDirectory());

        Console.WriteLine($"Workspace: {resolved}");
        return resolved;
    }

    /// <summary>
    /// Executes a single command non-interactively (no key-by-key editor) and
    /// returns. Used for scripting/automation, e.g. `CodeGenerator "create ..."`.
    /// Goes through the exact same pipeline as the interactive loop.
    /// </summary>
    internal bool RunOnce(string command)
    {
        IntentRepository.Initialize();

        ProjectMetaData? currentProject = null;

        try
        {
            if (BuildAppCommand.IsBuildApp(command))
            {
                _buildApp.Handle(command, _workspaceRoot);
                return true;
            }

            var outcome = _pipeline.Handle(command, ref currentProject,
                _configuredRules, _workspaceRoot, r => ProjectManager.CurrentProject = r);
            return outcome.Handled;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error [{ex.GetType().Name}]: {ex.Message}");
            return false;
        }
    }

    internal void Run()
    {
        PrintHeader();

        IntentRepository.Initialize();

        ProjectMetaData? currentProject = null;

        while (true)
        {
            Console.Write("CodeGenerator >> ");

            var editor = new CommandLineEditor(
                _commandHistory, _historyPath);


            var input = editor.ReadCommand();

            //if (string.IsNullOrWhiteSpace(input))
            //    continue;

            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                break;

            // FIX #9: Improved exception handling with specific catch blocks
            try
            {
                if (BuildAppCommand.IsBuildApp(input))
                {
                    _buildApp.Handle(input, _workspaceRoot);
                    continue;
                }

                // NLE analyzes first, then CodeGenerator generates from the result.
                // The NLE analysis is used internally only; we surface just the
                // command's own output (e.g. the project list), not the raw
                // SemanticResult, so no onAnalyzed callback is passed.
                _pipeline.Handle(input, ref currentProject,
                     _configuredRules, _workspaceRoot, r => ProjectManager.CurrentProject = r);
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"Invalid argument: {ex.Message}");
            }
            catch (FileNotFoundException ex)
            {
                Console.WriteLine($"File not found: {ex.Message}");
            }
            catch (DirectoryNotFoundException ex)
            {
                Console.WriteLine($"Directory not found: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"Invalid operation: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error [{ex.GetType().Name}]: {ex.Message}");

                // Include stack trace only in debug mode
                if (System.Diagnostics.Debugger.IsAttached)
                {
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                }
            }
        }

        // Save history before exiting
        try
        {
            // Optional: Save any pending state
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to save state on exit: {ex.Message}");
        }
    }

    //private string ReadCommandWithHistory()
    //{
    //    var buffer = new List<char>();
    //    var cursorPosition = 0;

    //    _historyIndex = _commandHistory.Count;

    //    _currentSuggestions.Clear();
    //    _suggestionIndex = -1;

    //    while (true)
    //    {
    //        var key = Console.ReadKey(intercept: true);

    //        switch (key.Key)
    //        {
    //            case ConsoleKey.Enter:
    //                {
    //                    var result = new string(buffer.ToArray());

    //                    Console.WriteLine();

    //                    if (!string.IsNullOrWhiteSpace(result) &&
    //                        (_commandHistory.Count == 0 ||
    //                         !_commandHistory[^1].Equals(
    //                             result,
    //                             StringComparison.Ordinal)))
    //                    {
    //                        _commandHistory.Add(result);

    //                        try
    //                        {
    //                            FileEncryption.AppendEncryptedLine(
    //                                _encryptedHistoryPath,
    //                                result);
    //                        }
    //                        catch
    //                        {
    //                            // Ignore history persistence failures
    //                        }
    //                    }

    //                    return result;
    //                }

    //            case ConsoleKey.Tab:
    //                {
    //                    var currentText =
    //                        new string(buffer.ToArray());

    //                    if (_currentSuggestions.Count == 0)
    //                    {
    //                        _currentSuggestions =
    //                            GetCommandHints(currentText)
    //                                .ToList();

    //                        _suggestionIndex = -1;
    //                    }

    //                    if (_currentSuggestions.Count > 0)
    //                    {
    //                        _suggestionIndex++;

    //                        if (_suggestionIndex >=
    //                            _currentSuggestions.Count)
    //                        {
    //                            _suggestionIndex = 0;
    //                        }

    //                        var suggestion =
    //                            _currentSuggestions[
    //                                _suggestionIndex];

    //                        buffer = suggestion.ToList();
    //                        cursorPosition = buffer.Count;
    //                    }

    //                    break;
    //                }

    //            case ConsoleKey.Escape:
    //                {
    //                    _currentSuggestions.Clear();
    //                    _suggestionIndex = -1;
    //                    break;
    //                }

    //            case ConsoleKey.LeftArrow:
    //                {
    //                    ResetSuggestions();

    //                    if ((key.Modifiers &
    //                         ConsoleModifiers.Control) != 0)
    //                    {
    //                        cursorPosition =
    //                            FindPreviousWord(
    //                                buffer,
    //                                cursorPosition);
    //                    }
    //                    else if (cursorPosition > 0)
    //                    {
    //                        cursorPosition--;
    //                    }

    //                    break;
    //                }

    //            case ConsoleKey.RightArrow:
    //                {
    //                    ResetSuggestions();

    //                    if ((key.Modifiers &
    //                         ConsoleModifiers.Control) != 0)
    //                    {
    //                        cursorPosition =
    //                            FindNextWord(
    //                                buffer,
    //                                cursorPosition);
    //                    }
    //                    else if (cursorPosition < buffer.Count)
    //                    {
    //                        cursorPosition++;
    //                    }

    //                    break;
    //                }

    //            case ConsoleKey.Home:
    //                {
    //                    ResetSuggestions();
    //                    cursorPosition = 0;
    //                    break;
    //                }

    //            case ConsoleKey.End:
    //                {
    //                    ResetSuggestions();
    //                    cursorPosition = buffer.Count;
    //                    break;
    //                }

    //            case ConsoleKey.Backspace:
    //                {
    //                    ResetSuggestions();

    //                    if (cursorPosition > 0)
    //                    {
    //                        buffer.RemoveAt(cursorPosition - 1);
    //                        cursorPosition--;
    //                    }

    //                    break;
    //                }

    //            case ConsoleKey.Delete:
    //                {
    //                    ResetSuggestions();

    //                    if (cursorPosition < buffer.Count)
    //                    {
    //                        buffer.RemoveAt(cursorPosition);
    //                    }

    //                    break;
    //                }

    //            case ConsoleKey.UpArrow:
    //                {
    //                    ResetSuggestions();

    //                    if (_commandHistory.Count == 0)
    //                        break;

    //                    _historyIndex =
    //                        Math.Max(
    //                            0,
    //                            _historyIndex - 1);

    //                    buffer =
    //                        _commandHistory[_historyIndex]
    //                            .ToList();

    //                    cursorPosition =
    //                        buffer.Count;

    //                    break;
    //                }

    //            case ConsoleKey.DownArrow:
    //                {
    //                    ResetSuggestions();

    //                    if (_commandHistory.Count == 0)
    //                        break;

    //                    _historyIndex =
    //                        Math.Min(
    //                            _commandHistory.Count,
    //                            _historyIndex + 1);

    //                    var historyText =
    //                        _historyIndex ==
    //                        _commandHistory.Count
    //                            ? string.Empty
    //                            : _commandHistory[
    //                                _historyIndex];

    //                    buffer = historyText.ToList();

    //                    cursorPosition =
    //                        buffer.Count;

    //                    break;
    //                }

    //            default:
    //                {
    //                    if (!char.IsControl(key.KeyChar))
    //                    {
    //                        ResetSuggestions();

    //                        buffer.Insert(
    //                            cursorPosition,
    //                            key.KeyChar);

    //                        cursorPosition++;
    //                    }

    //                    break;
    //                }
    //        }

    //        RewriteCurrentLine(
    //            new string(buffer.ToArray()),
    //            cursorPosition);
    //    }
    //}

    private void ResetSuggestions()
    {
        _currentSuggestions.Clear();
    }

    private static void RewriteCurrentLine(string text, int cursorPosition)
    {
        const string prompt = "CodeGenerator >> ";

        int currentTop = Console.CursorTop;

        Console.SetCursorPosition(0, currentTop);
        Console.Write(new string(' ', Console.BufferWidth - 1));
        Console.SetCursorPosition(0, currentTop);
        Console.Write(prompt);
        Console.Write(text);

        var hints = GetCommandHints(text);

        if (hints.Count > 0 && !hints.Any(h =>
                h.Command.Equals(text, StringComparison.OrdinalIgnoreCase)))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine();
            Console.WriteLine("Suggestions:");

            foreach (var hint in hints)
                Console.WriteLine($"  {hint}");

            Console.ResetColor();
        }

        int targetLeft = prompt.Length + cursorPosition;

        if (targetLeft >= Console.BufferWidth)
            targetLeft = Console.BufferWidth - 1;

        Console.SetCursorPosition(targetLeft, currentTop);
    }

    private static int FindPreviousWord(IReadOnlyList<char> buffer, int position)
    {
        position--;

        while (position > 0 &&
               char.IsWhiteSpace(buffer[position]))
        {
            position--;
        }

        while (position > 0 &&
               !char.IsWhiteSpace(buffer[position - 1]))
        {
            position--;
        }

        return Math.Max(0, position);
    }

    private static int FindNextWord(IReadOnlyList<char> buffer, int position)
    {
        while (position < buffer.Count && !char.IsWhiteSpace(buffer[position]))
        {
            position++;
        }

        while (position < buffer.Count && char.IsWhiteSpace(buffer[position]))
        {
            position++;
        }

        return position;
    }

    private void PrintHeader()
    {
        Console.WriteLine("============================================================");
        Console.WriteLine("               Code Generation Engine");
        Console.WriteLine("============================================================");
        Console.WriteLine();

        Console.WriteLine("Type 'help' to see the complete command reference.");
        Console.WriteLine("Press TAB for command suggestions.");
        Console.WriteLine("Type 'exit' to quit the application.");
        Console.WriteLine();
    }

    internal (string code, ParsedCommand parsed) BuildDemo()
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
            Methods = new List<MethodDefinition>
            {
                new MethodDefinition { Name = "Save", ReturnType = "void" },
                new MethodDefinition { Name = "Validate", ReturnType = "bool" }
            }
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

    private void LoadConfiguredRules()
    {
        var rulesJsonPath = Path.Combine(_workspaceRoot, "rules.json");
        var rulesTxtPath = Path.Combine(_workspaceRoot, "rules.txt");

        try
        {
            if (File.Exists(rulesJsonPath))
            {
                var json = File.ReadAllText(rulesJsonPath);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var loaded = JsonSerializer.Deserialize<List<PropertyRule>>(json, opts);
                if (loaded != null)
                    _configuredRules = loaded;
                Console.WriteLine($"Loaded {_configuredRules.Count} property rules from rules.json");
            }
            else if (File.Exists(rulesTxtPath))
            {
                var lines = File.ReadAllLines(rulesTxtPath).Where(l => !string.IsNullOrWhiteSpace(l));
                _configuredRules = RuleParser.Parse(lines).ToList();
                Console.WriteLine($"Parsed {_configuredRules.Count} property rules from rules.txt");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load rules: {ex.Message}");
            _configuredRules = new List<PropertyRule>();
        }
    }

    private void InitializeExistingProjects()
    {
        try
        {
            var projects = Directory.GetFiles(_workspaceRoot, "*.csproj",
                SearchOption.AllDirectories);

            foreach (var project in projects)
            {
                var projectDir = Path.GetDirectoryName(project);
                ProjectManager.Register(new ProjectMetaData
                {
                    Success = true,
                    ProjectPath = projectDir ?? Path.GetDirectoryName(_workspaceRoot) ?? _workspaceRoot,
                    CsProjPath = project,
                    ProjectName = Path.GetFileNameWithoutExtension(project),
                    Message = "Loaded"
                });
            }

            // Do NOT auto-select a project at startup. A leftover selection from
            // the persisted registry (or the first project that happened to be
            // registered) would silently capture every generated class and hide
            // it in an unexpected folder. New classes go to the workspace until
            // the user explicitly runs "use project X".
            ProjectManager.CurrentProject = null;

            Console.WriteLine(projects.Length > 0
                ? $"Found {projects.Length} project(s). No project selected — " +
                  "use 'use project <name>' to target one, or new classes save to the workspace."
                : "No projects found. New classes save to the workspace.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to initialize existing projects: {ex.Message}");
        }
    }

    private void LoadCommandHistory()
    {
        _commandHistory.Clear();
        _commandHistory.AddRange(CommandHistoryFile.ReadLines(_historyPath));
    }
}
