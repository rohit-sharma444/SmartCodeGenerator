using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using CodeGenerationEngine.Service;
using Microsoft.CodeAnalysis;
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
    private int _suggestionIndex = -1;
    private readonly List<string> _commandHistory = [];
    private int _historyIndex = -1;
    private readonly string _encryptedHistoryPath;
    private const string HISTORY_FILENAME = ".codegen_history";

    internal CodeGeneratorStarter()
    {
        _workspaceRoot = ResolveWorkspaceRoot();

        _encryptedHistoryPath = Path.Combine(_workspaceRoot, HISTORY_FILENAME);

        LoadConfiguredRules();

        ProjectManager.LoadProject();

        // At startup, prefer an existing project in the workspace.
        InitializeExistingProjects();

        // Load encrypted command history from file
        LoadEncryptedCommandHistory();
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

        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            try
            {
                return Path.GetFullPath(projectPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Invalid PROJECTPATH '{projectPath}': {ex.Message}");
            }
        }

        return Path.GetFullPath(Directory.GetCurrentDirectory());
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
                _commandHistory, _encryptedHistoryPath);


            var input = editor.ReadCommand();

            //if (string.IsNullOrWhiteSpace(input))
            //    continue;

            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                break;

            // FIX #9: Improved exception handling with specific catch blocks
            try
            {
                // delegate to the static CommandHandlingService
                CommandHandlingService.ProcessInput(input, ref currentProject,
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
        _suggestionIndex = -1;
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

            if (ProjectManager.CurrentProject != null)
            {
                Console.WriteLine($"Current Project: " +
                    $"{Path.GetFileNameWithoutExtension(ProjectManager.CurrentProject.ProjectPath)}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to initialize existing projects: {ex.Message}");
        }
    }

    private void LoadEncryptedCommandHistory()
    {
        try
        {
            var decrypted = FileEncryption.ReadDecryptedLines(_encryptedHistoryPath);
            _commandHistory.Clear();
            _commandHistory.AddRange(decrypted);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to load command history: {ex.Message}");
            _commandHistory.Clear();
        }
    }
}
