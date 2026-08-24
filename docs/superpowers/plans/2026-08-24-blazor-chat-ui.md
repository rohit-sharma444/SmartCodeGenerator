# Blazor Chat UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A `chat` command that opens a loopback Blazor page where a user loads a .NET solution or project and runs every command in the catalog except `build app`, `batch` and `demo`.

**Architecture:** In-process Blazor Server on `127.0.0.1:0` behind a per-session token, the same shape `build app` already uses — its host mechanics are extracted into a shared `LoopbackHost` so `/build` and `/chat` share one token guard. Each chat message is classified by a `CommandRoute` shared with the console REPL, then run through the existing `NaturalLanguagePipeline` with `Console.Out` captured and colour suppressed, so the transcript is the same text the console would have printed.

**Tech Stack:** .NET (`Microsoft.NET.Sdk.Web`), Blazor Server with `InteractiveServer` render mode, MSTest 4, bUnit 2.9.

**Spec:** `docs/superpowers/specs/2026-08-24-blazor-chat-ui-design.md` — read it before Task 1. Every task argues from it.

## Global Constraints

- **Branch:** `blazor-chat-ui`. Never commit to `main`.
- **Never change what a command prints.** Not one `Console.WriteLine` in `CodeGenerationEngine` moves. If a renderer needs different output, the renderer is wrong.
- **`build app`, `batch` and `demo` are declined in the chat**, never attempted. They keep working in the console byte for byte.
- **Blazor-facing types are `public`.** A `public` component with a `[Parameter]` or `[Inject]` of an `internal` type is a compile error (CS0051/CS0053). Existing precedent: `BuildSession`, `BuildStage`, `StageState` are all `public`; `CircuitWatch`, which no component touches, is `internal`.
- **The page is self-contained.** No CDN, no web font, no external stylesheet. Same constraint `wwwroot/workflow.css` already works under.
- **Test framework:** MSTest 4 with bUnit 2.9, in the existing `CodeGenerator.Tests` project. Do not add a test project.
- **MSTEST0037:** `Assert.AreEqual(n, x.Count)` is an analyzer error. Use `Assert.HasCount(n, x)`.
- **Tests that touch `ProjectManager` need `[DoNotParallelize]`** — it is static, process-wide state. Tests that start a Kestrel need it too; see the remarks on `BuildAppWebHostTests`.
- **CI** builds `SmartCodeGenerator/SmartCodeGenerator.slnx` in Release with `-warnaserror`. Analyzer checks need `--no-incremental`, or a clean build prints "Build succeeded" without rerunning them.
- **Repo root is `D:\SmartCodeGenerator-Repository`**, the parent of the `SmartCodeGenerator` folder that holds the solution. All paths below are relative to the repo root.
- **Files written by tooling land LF-only in this CRLF checkout.** After creating a file, normalize: `sed -i 's/$/\r/' <path>` (only on files that are LF-only).
- **Local test command:** `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj`. Add `--filter FullyQualifiedName~Name` to run one class. Rebuild `CodeGenerator` before verifying CLI behaviour by hand — `dotnet test` leaves its `bin` stale.

---

## File Structure

**New — engine (`CodeGenerationEngine`)**

| File | Responsibility |
| --- | --- |
| `Core/SolutionFile.cs` | Read `.sln` and `.slnx`; find the root a set of project files implies. Pure, no console. |
| `Helper/OutputCapture.cs` | Swap `Console.Out` for a buffer and suppress colour for the duration. |

**New — CLI (`CodeGenerator`)**

| File | Responsibility |
| --- | --- |
| `App/CommandKind.cs` | The kinds of input the tool recognises. `public` — a `ChatTurn` carries one. |
| `App/CommandRoute.cs` | Classification, in one place, shared by `Run`, `RunOnce` and the chat. |
| `App/Workspace.cs` | Root, rules, project count, history path, and re-rooting. |
| `App/ChatCommand.cs` | The `chat` command: start the host, print the URL, wait. |
| `Web/LoopbackHost.cs` | Bind, token, static assets, antiforgery, the guard, circuits, dispose. |
| `Web/Chat/ChatWebHost.cs` | `/chat` over `LoopbackHost`, plus the wait-and-abandon loop. |
| `Web/Chat/ChatSession.cs` | The transcript, dispatch, serialization, the AI question. |
| `Web/Chat/ChatTurn.cs` | One exchange, and what it turned out to be. |
| `Web/Chat/OutputBlocks.cs` | Captured text to blocks — three known shapes, everything else text. |
| `Web/Chat/CSharpTokens.cs` | Lossless C# scanner for the code block. |
| `Web/Chat/CommandCatalog.cs` | Autocomplete over `CommandDictionary.Commands`. |
| `Web/Chat/FolderListing.cs` | What the picker shows for a directory. |
| `Web/Components/ChatPage.razor` | `@page "/chat"` — picker, strip, transcript, composer. |
| `Web/Components/ChatTranscript.razor` | Turns and their blocks. |
| `Web/Components/ChatComposer.razor` | Input, dropdown, the Enter rule. |
| `Web/Components/ContextStrip.razor` | What the next command will act on. |
| `Web/Components/SolutionPicker.razor` | Path box and directory listing. |
| `wwwroot/chat.css` | Styling. Self-contained. |

**Modified**

| File | Change |
| --- | --- |
| `CodeGenerationEngine/Helper/Output.cs` | `Write` consults a suppression flag through a pure `TransportFor`. |
| `CodeGenerationEngine/Core/CommandDictionary.cs` | The `chat` entry. |
| `CodeGenerator/Web/BuildAppWebHost.cs` | Thin caller over `LoopbackHost`. |
| `CodeGenerator/Web/Components/App.razor` | Per-page title. |
| `CodeGenerator/App/CodeGeneratorStarter.cs` | `Workspace`, `CommandRoute`, the `chat` branch. |
| `docs/commands.md` | Regenerated. |

---

## Task 1: `SolutionFile` — read a solution, find its root

**Files:**
- Create: `CodeGenerationEngine/Core/SolutionFile.cs`
- Test: `CodeGenerator.Tests/Core/SolutionFileTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  ```csharp
  public sealed record SolutionProjects(
      string Root, IReadOnlyList<string> ProjectFiles, string? SolutionPath);

  public static class SolutionFile
  {
      public static bool IsSolution(string? path);
      public static SolutionProjects Read(string solutionPath);
      public static string CommonAncestorDirectory(IReadOnlyList<string> filePaths);
  }
  ```

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/Core/SolutionFileTests.cs`:

```csharp
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// Reading a solution, and deciding where the workspace it implies actually starts.
/// </summary>
/// <remarks>
/// The root is the interesting half. This repository's own .slnx points every path at `../`, so a
/// root taken from the solution file's own folder contains none of its projects — and every
/// command afterwards would be right about a wrong tree.
/// </remarks>
[TestClass]
public class SolutionFileTests
{
    [TestMethod]
    public void Recognizes_both_solution_extensions()
    {
        Assert.IsTrue(SolutionFile.IsSolution(@"C:\x\App.sln"));
        Assert.IsTrue(SolutionFile.IsSolution(@"C:\x\App.slnx"));
        Assert.IsFalse(SolutionFile.IsSolution(@"C:\x\App.csproj"));
        Assert.IsFalse(SolutionFile.IsSolution(@"C:\x"));
        Assert.IsFalse(SolutionFile.IsSolution(null));
    }

    [TestMethod]
    public void Reads_an_slnx_and_resolves_its_paths()
    {
        var dir = NewDir();
        var path = Path.Combine(dir, "App.slnx");

        File.WriteAllText(path, """
            <Solution>
              <Project Path="Api/Api.csproj" />
              <Project Path="Core/Core.csproj" />
            </Solution>
            """);

        var read = SolutionFile.Read(path);

        Assert.HasCount(2, read.ProjectFiles);
        Assert.Contains(Path.Combine(dir, "Api", "Api.csproj"), read.ProjectFiles);
        Assert.AreEqual(path, read.SolutionPath);
    }

    [TestMethod]
    public void Reads_a_classic_sln_and_skips_solution_folders()
    {
        var dir = NewDir();
        var path = Path.Combine(dir, "App.sln");

        // A solution folder is a Project line whose path is not a project file. It must not
        // become a registered project.
        File.WriteAllText(path, """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Api", "Api\Api.csproj", "{1}"
            EndProject
            Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "Docs", "Docs", "{2}"
            EndProject
            """);

        var read = SolutionFile.Read(path);

        Assert.HasCount(1, read.ProjectFiles);
        Assert.AreEqual(Path.Combine(dir, "Api", "Api.csproj"), read.ProjectFiles[0]);
    }

    [TestMethod]
    public void Roots_at_the_common_ancestor_not_the_solution_folder()
    {
        // The shape of this repository's own SmartCodeGenerator.slnx: the solution sits in a
        // folder of its own and every project is a sibling of that folder.
        var repo = NewDir();
        var solutionDir = Path.Combine(repo, "Solution");
        Directory.CreateDirectory(solutionDir);

        var path = Path.Combine(solutionDir, "App.slnx");

        File.WriteAllText(path, """
            <Solution>
              <Project Path="../Api/Api.csproj" />
              <Project Path="../Core/Core.csproj" />
            </Solution>
            """);

        var read = SolutionFile.Read(path);

        Assert.AreEqual(repo, read.Root);
        Assert.AreNotEqual(solutionDir, read.Root);
    }

    [TestMethod]
    public void A_single_project_roots_at_its_own_folder()
    {
        var dir = NewDir();
        var path = Path.Combine(dir, "App.slnx");

        File.WriteAllText(path, """
            <Solution>
              <Project Path="Api/Api.csproj" />
            </Solution>
            """);

        Assert.AreEqual(Path.Combine(dir, "Api"), SolutionFile.Read(path).Root);
    }

    [TestMethod]
    public void An_unreadable_solution_reports_no_projects_rather_than_throwing()
    {
        var dir = NewDir();
        var path = Path.Combine(dir, "Broken.slnx");

        File.WriteAllText(path, "<Solution><Project Path=");

        var read = SolutionFile.Read(path);

        Assert.IsEmpty(read.ProjectFiles);
        Assert.AreEqual(dir, read.Root);
    }

    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sln-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~SolutionFileTests`

Expected: FAIL — `SolutionFile` does not exist (CS0246).

- [ ] **Step 3: Write the implementation**

Create `CodeGenerationEngine/Core/SolutionFile.cs`:

```csharp
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CodeGenerationEngine.Core;

/// <summary>What a solution file declares, and where the workspace it implies starts.</summary>
public sealed record SolutionProjects(
    string Root, IReadOnlyList<string> ProjectFiles, string? SolutionPath);

/// <summary>
/// Reads a .sln or .slnx and works out the workspace root it implies.
/// </summary>
/// <remarks>
/// Nothing in this tool read a solution file before this. The workspace root was resolved once
/// from <c>PROJECTPATH</c> or the launch directory, and the "projects" were every .csproj under it.
/// <para>
/// The root is the part worth being careful about, and it is <b>not</b> the solution file's own
/// folder. This repository's own <c>SmartCodeGenerator.slnx</c> points every path at <c>../</c>,
/// so that reading would root a workspace at a folder containing none of the seventeen projects,
/// and every command afterwards would be correct about the wrong tree.
/// </para>
/// </remarks>
public static class SolutionFile
{
    /// <summary>The `Project(...) = "Name", "Path", "{Guid}"` line of a classic .sln.</summary>
    private static readonly Regex ClassicProject = new(
        """^Project\("\{[^}]*\}"\)\s*=\s*"[^"]*",\s*"([^"]*)",""",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public static bool IsSolution(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var extension = Path.GetExtension(path);

        return extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The projects a solution declares, and the root they imply.
    /// </summary>
    /// <remarks>
    /// A malformed or unreadable file reports no projects rather than throwing: the caller is a
    /// picker in a browser, and a path the user typed being wrong is an ordinary situation with a
    /// remedy, not a fault to report.
    /// </remarks>
    public static SolutionProjects Read(string solutionPath)
    {
        var full = Path.GetFullPath(solutionPath);
        var folder = Path.GetDirectoryName(full) ?? full;

        IReadOnlyList<string> projects;

        try
        {
            projects = Path.GetExtension(full).Equals(".slnx", StringComparison.OrdinalIgnoreCase)
                ? ReadSlnx(full, folder)
                : ReadClassic(full, folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or System.Xml.XmlException or ArgumentException)
        {
            projects = [];
        }

        var root = projects.Count == 0 ? folder : CommonAncestorDirectory(projects);

        return new SolutionProjects(root, projects, full);
    }

    private static List<string> ReadSlnx(string full, string folder)
        => [.. XDocument.Load(full)
            .Descendants("Project")
            .Select(x => (string?)x.Attribute("Path"))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Resolve(folder, p!))
            .Where(IsProjectFile)];

    private static List<string> ReadClassic(string full, string folder)
        => [.. ClassicProject.Matches(File.ReadAllText(full))
            .Select(m => Resolve(folder, m.Groups[1].Value))
            .Where(IsProjectFile)];

    /// <summary>
    /// Solution folders appear as Project lines too, with a path that is not a project file. They
    /// are not projects and must not be registered as any.
    /// </summary>
    private static bool IsProjectFile(string path)
        => Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase);

    private static string Resolve(string folder, string relative)
        => Path.GetFullPath(Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>
    /// The deepest directory that contains every one of <paramref name="filePaths"/>.
    /// </summary>
    /// <remarks>
    /// Compared segment by segment rather than by string prefix: a prefix comparison makes
    /// <c>C:\App</c> an ancestor of <c>C:\AppData</c>, which is how a workspace ends up rooted one
    /// directory too high and quietly scanning a sibling tree.
    /// </remarks>
    public static string CommonAncestorDirectory(IReadOnlyList<string> filePaths)
    {
        if (filePaths.Count == 0)
            return string.Empty;

        var folders = filePaths
            .Select(p => Path.GetDirectoryName(Path.GetFullPath(p)) ?? p)
            .ToList();

        if (folders.Count == 1)
            return folders[0];

        var split = folders
            .Select(f => f.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            .ToList();

        var shared = new List<string>();

        for (var i = 0; i < split.Min(s => s.Length); i++)
        {
            var segment = split[0][i];

            if (split.Any(s => !s[i].Equals(segment, StringComparison.OrdinalIgnoreCase)))
                break;

            shared.Add(segment);
        }

        // Rebuilt from the first path so the drive keeps its separator: joining segments alone
        // turns "C:" into a relative path.
        var rebuilt = string.Join(Path.DirectorySeparatorChar, shared);

        return Path.IsPathRooted(folders[0]) && !rebuilt.EndsWith(Path.DirectorySeparatorChar)
            ? rebuilt + Path.DirectorySeparatorChar
            : rebuilt;
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~SolutionFileTests`

Expected: PASS, 6 tests.

Note: `CommonAncestorDirectory` returns a trailing separator on a drive root (`C:\`) but not on a deeper path, because `Path.GetDirectoryName` never returns one. If `Roots_at_the_common_ancestor_not_the_solution_folder` fails on a trailing separator, compare with `Path.TrimEndingDirectorySeparator` on both sides in the assertion **and** in `Workspace.LoadFrom` later — do not paper over it in only one place.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Core/SolutionFile.cs CodeGenerator.Tests/Core/SolutionFileTests.cs
git commit -m "feat(solution): read .sln and .slnx, and root at the projects' common ancestor"
```

---

## Task 2: `OutputCapture` — capture a command's output as plain text

**Files:**
- Create: `CodeGenerationEngine/Helper/OutputCapture.cs`
- Modify: `CodeGenerationEngine/Helper/Output.cs` (the `Write` switch, around line 182-200)
- Test: `CodeGenerator.Tests/Core/OutputCaptureTests.cs`

**Interfaces:**
- Consumes: `ColourTransport`, `ColourDelivery.Current`.
- Produces:
  ```csharp
  public sealed class OutputCapture : IDisposable
  {
      public static bool Suppressed { get; }
      public static OutputCapture Start();   // throws InvalidOperationException if already capturing
      public string Text { get; }
      public void Dispose();
  }

  // added to Output
  public static ColourTransport TransportFor(ColourTransport transport, bool suppressed);
  ```

**Why a pure `TransportFor`:** the test host runs with output redirected, so `ColourDelivery.Current` is `None` there and no test can exercise the ANSI path end to end. `OutputColourTests` solves this by testing pure functions that take the transport as an argument (`Output.ColourAllowed`, `Output.Explain`). Follow that precedent — `TransportFor` is the assertable guard, and the round-trip test below covers the wiring.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/Core/OutputCaptureTests.cs`:

```csharp
using CodeGenerationEngine.Helper;

namespace CodeGenerator.Tests.Core;

/// <summary>
/// Capturing what a command printed, as plain text.
/// </summary>
/// <remarks>
/// The chat page shows a command's whole output and tints it by verdict, so the text it captures
/// must carry no colour of its own. Colour is decided once per process
/// (<c>ColourDelivery.Current</c> is a static readonly), so suppression is a flag consulted at the
/// point of writing — otherwise the ANSI transport writes escape sequences into the buffer and the
/// transcript shows them as text.
/// <para>
/// <see cref="DoNotParallelizeAttribute"/> because <c>Console.SetOut</c> is process-global: two of
/// these running at once capture each other's output.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public class OutputCaptureTests
{
    [TestMethod]
    public void Suppression_takes_the_transport_to_none()
    {
        // The pure half, testable because the transport is an argument. The test host is
        // redirected, so ColourDelivery.Current is None here whatever the code does.
        Assert.AreEqual(
            ColourTransport.None, Output.TransportFor(ColourTransport.Ansi, suppressed: true));

        Assert.AreEqual(
            ColourTransport.None,
            Output.TransportFor(ColourTransport.ConsoleAttributes, suppressed: true));

        Assert.AreEqual(
            ColourTransport.Ansi, Output.TransportFor(ColourTransport.Ansi, suppressed: false));
    }

    [TestMethod]
    public void Captures_what_was_written_and_restores_the_previous_writer()
    {
        var before = Console.Out;
        string captured;

        using (var capture = OutputCapture.Start())
        {
            Console.WriteLine("first");
            Output.Success("second");
            captured = capture.Text;
        }

        Assert.AreSame(before, Console.Out);
        Assert.Contains("first", captured);
        Assert.Contains("second", captured);
    }

    [TestMethod]
    public void Suppresses_colour_only_while_capturing()
    {
        Assert.IsFalse(OutputCapture.Suppressed);

        using (OutputCapture.Start())
            Assert.IsTrue(OutputCapture.Suppressed);

        Assert.IsFalse(OutputCapture.Suppressed);
    }

    [TestMethod]
    public void Restores_the_writer_when_the_command_throws()
    {
        // The chat writes the buffer to the turn in a finally for the same reason CommandColour
        // does: a command that throws must still show what it printed before it did. If the
        // writer is not restored, every later command prints into a dead buffer.
        var before = Console.Out;

        try
        {
            using var capture = OutputCapture.Start();
            Console.WriteLine("printed before the throw");
            throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException)
        {
            // expected
        }

        Assert.AreSame(before, Console.Out);
        Assert.IsFalse(OutputCapture.Suppressed);
    }

    [TestMethod]
    public void Refuses_to_nest()
    {
        // Console.SetOut is process-global, so a nested capture would steal the outer one's
        // output and restore the wrong writer. The chat serializes commands; this is the
        // invariant made enforceable rather than assumed.
        using var outer = OutputCapture.Start();

        Assert.ThrowsExactly<InvalidOperationException>(() => OutputCapture.Start());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~OutputCaptureTests`

Expected: FAIL — `OutputCapture` and `Output.TransportFor` do not exist (CS0246, CS0117).

- [ ] **Step 3: Write `OutputCapture`**

Create `CodeGenerationEngine/Helper/OutputCapture.cs`:

```csharp
namespace CodeGenerationEngine.Helper;

/// <summary>
/// Captures everything written to the console for the length of one command, as plain text.
/// </summary>
/// <remarks>
/// For a host that renders a command's output itself rather than letting it reach a terminal — the
/// chat page shows the whole block and tints it by verdict, so colour inside the text would be
/// both wrong and visible as escape sequences.
/// <para>
/// Two things happen together and must be undone together: <c>Console.Out</c> is swapped for a
/// buffer, and colour is suppressed at the point of writing (see
/// <see cref="Output.TransportFor"/>). Suppression cannot be done by reading the environment,
/// because <see cref="ColourDelivery.Current"/> is decided once per process.
/// </para>
/// <para>
/// Both are process-global, so this refuses to nest and its caller is expected to serialize.
/// </para>
/// </remarks>
public sealed class OutputCapture : IDisposable
{
    private readonly StringWriter _buffer = new();
    private readonly TextWriter _previous;

    private OutputCapture(TextWriter previous) => _previous = previous;

    /// <summary>Whether output is being captured, and colour therefore suppressed.</summary>
    public static bool Suppressed { get; private set; }

    public static OutputCapture Start()
    {
        if (Suppressed)
        {
            throw new InvalidOperationException(
                "Output is already being captured. Commands must be run one at a time.");
        }

        var capture = new OutputCapture(Console.Out);

        Console.SetOut(capture._buffer);
        Suppressed = true;

        return capture;
    }

    /// <summary>What has been written so far.</summary>
    public string Text => _buffer.ToString();

    public void Dispose()
    {
        Suppressed = false;
        Console.SetOut(_previous);
        _buffer.Dispose();
    }
}
```

- [ ] **Step 4: Wire suppression into `Output`**

In `CodeGenerationEngine/Helper/Output.cs`, add the pure function next to `ColourAllowed`:

```csharp
    /// <summary>
    /// The transport actually in force, given that output may be being captured.
    /// </summary>
    /// <remarks>
    /// Takes both as parameters for the reason <see cref="ColourAllowed"/> does: the test host
    /// redirects output, so <see cref="ColourDelivery.Current"/> is <c>None</c> there and the
    /// interesting cases are unreachable from a test that reads the process.
    /// </remarks>
    public static ColourTransport TransportFor(ColourTransport transport, bool suppressed)
        => suppressed ? ColourTransport.None : transport;
```

Then change the switch in `Write` (currently `switch (ColourDelivery.Current)`) to:

```csharp
        switch (TransportFor(ColourDelivery.Current, OutputCapture.Suppressed))
```

Nothing else in `Write` changes.

- [ ] **Step 5: Run the tests**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~OutputCaptureTests|FullyQualifiedName~OutputColourTests"`

Expected: PASS. `OutputColourTests` must be green and unchanged — it is the guard that this did not alter how the console prints.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerationEngine/Helper/OutputCapture.cs CodeGenerationEngine/Helper/Output.cs CodeGenerator.Tests/Core/OutputCaptureTests.cs
git commit -m "feat(output): capture a command's output as plain text"
```

---

## Task 3: `CommandRoute` — one classification, three callers

**Files:**
- Create: `CodeGenerator/App/CommandKind.cs`, `CodeGenerator/App/CommandRoute.cs`
- Modify: `CodeGenerator/App/CodeGeneratorStarter.cs` (`Run` at 167-300, `RunOnce` at 96-151)
- Test: `CodeGenerator.Tests/App/CommandRouteTests.cs`

**Interfaces:**
- Consumes: the existing predicates — `ClearCommand.IsClear(string)`, `HistoryCommand.IsHistory(string)`, `HelpCommand.IsHelp(string?)`, `StatusCommand.IsStatus(string?)`, `DemoCommand.IsDemo(string?)`, `AiCommand.IsAiCommand(string)`, `BuildAppCommand.IsBuildApp(string)`, `BatchCommand.IsBatch(string)`.
- Produces:
  ```csharp
  public enum CommandKind
  {
      Exit, Clear, History, Help, Status, Demo, Ai, Chat, BuildApp, Batch, Pipeline
  }

  internal static class CommandRoute
  {
      public static CommandKind Classify(string? input);
  }
  ```

`CommandKind.Chat` is included now and returns `Pipeline` until Task 16 adds `ChatCommand.IsChat`. The enum member exists from the start so `ChatSession` (Task 10) can decline it without a later edit to a public enum.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/App/CommandRouteTests.cs`:

```csharp
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Which handler an input belongs to — asserted against the predicates that used to be an
/// if-ladder in the REPL.
/// </summary>
/// <remarks>
/// There were two ladders before this: one in <c>Run</c> and a shorter one in a different order in
/// <c>RunOnce</c>. The chat would have been a third. The order is behaviour, not tidiness, and
/// `help build app` is the case that proves it — it is a help topic, not a build.
/// </remarks>
[TestClass]
public class CommandRouteTests
{
    [TestMethod]
    public void Exit_is_the_bare_word_only()
    {
        Assert.AreEqual(CommandKind.Exit, CommandRoute.Classify("exit"));
        Assert.AreEqual(CommandKind.Exit, CommandRoute.Classify("EXIT"));
        Assert.AreEqual(CommandKind.Pipeline, CommandRoute.Classify("exit the building"));
    }

    [TestMethod]
    public void Help_about_build_app_is_help_not_build_app()
    {
        // The whole reason the order is fixed here. RunOnce gets this right today only because
        // IsBuildApp is anchored to the front of the input; nothing said so out loud.
        Assert.AreEqual(CommandKind.Help, CommandRoute.Classify("help build app"));
        Assert.AreEqual(CommandKind.BuildApp, CommandRoute.Classify("build app"));
        Assert.AreEqual(CommandKind.BuildApp, CommandRoute.Classify("build app: a shop"));
    }

    [TestMethod]
    public void Each_kind_agrees_with_the_predicate_it_replaced()
    {
        Assert.AreEqual(CommandKind.Clear, CommandRoute.Classify("clear"));
        Assert.AreEqual(CommandKind.Clear, CommandRoute.Classify("cls"));
        Assert.AreEqual(CommandKind.History, CommandRoute.Classify("history"));
        Assert.AreEqual(CommandKind.Help, CommandRoute.Classify("help"));
        Assert.AreEqual(CommandKind.Status, CommandRoute.Classify("status"));
        Assert.AreEqual(CommandKind.Demo, CommandRoute.Classify("demo"));
        Assert.AreEqual(CommandKind.Ai, CommandRoute.Classify("ai status"));
        Assert.AreEqual(CommandKind.Batch, CommandRoute.Classify("batch"));
    }

    [TestMethod]
    public void Anything_else_is_the_pipeline()
    {
        Assert.AreEqual(CommandKind.Pipeline, CommandRoute.Classify("list classes"));
        Assert.AreEqual(CommandKind.Pipeline, CommandRoute.Classify("create class Employee"));
        Assert.AreEqual(CommandKind.Pipeline, CommandRoute.Classify("wibble"));
    }

    [TestMethod]
    public void Nothing_typed_is_the_pipeline_which_is_what_the_repl_did()
    {
        // The REPL passes empty input straight to the pipeline — the blank-input guard is
        // commented out. Classification must not invent a new behaviour here.
        Assert.AreEqual(CommandKind.Pipeline, CommandRoute.Classify(""));
        Assert.AreEqual(CommandKind.Pipeline, CommandRoute.Classify(null));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~CommandRouteTests`

Expected: FAIL — `CommandKind` and `CommandRoute` do not exist.

- [ ] **Step 3: Write the implementation**

Create `CodeGenerator/App/CommandKind.cs`:

```csharp
namespace CodeGenerator.App;

/// <summary>
/// Which handler an input belongs to.
/// </summary>
/// <remarks>
/// Public because a <c>ChatTurn</c> carries one and a Blazor component takes that as a parameter —
/// a public component cannot expose an internal type.
/// </remarks>
public enum CommandKind
{
    /// <summary>Leave. In the console that ends the process; in the chat it ends the session.</summary>
    Exit,

    Clear,

    History,

    Help,

    Status,

    Demo,

    Ai,

    /// <summary>Open the chat page. Declined inside the chat, which is already it.</summary>
    Chat,

    BuildApp,

    Batch,

    /// <summary>Everything else: the natural-language pipeline decides.</summary>
    Pipeline
}
```

Create `CodeGenerator/App/CommandRoute.cs`:

```csharp
namespace CodeGenerator.App;

/// <summary>
/// Which handler an input belongs to, decided once for every caller.
/// </summary>
/// <remarks>
/// This was an if-ladder inside <c>CodeGeneratorStarter.Run</c> and a shorter one, in a different
/// order, inside <c>RunOnce</c>. The chat page would have been a third copy, and three copies of
/// an ordered ladder is how the three come to disagree about what `help build app` means.
/// <para>
/// The order is behaviour. <c>help</c> is tested before <c>build app</c> because
/// <c>LiteralCommandTable</c> keys on the exact word "help" and would not match `help build app`
/// at all; <c>RunOnce</c> happens to get that right because <c>IsBuildApp</c> is anchored, which is
/// a coincidence worth pinning down.
/// </para>
/// </remarks>
internal static class CommandRoute
{
    public static CommandKind Classify(string? input)
    {
        var text = input ?? string.Empty;

        // Not trimmed, deliberately: the REPL compares the raw input to "exit", and this must
        // classify exactly what that ladder classified.
        if (text.Equals("exit", StringComparison.OrdinalIgnoreCase))
            return CommandKind.Exit;

        if (ClearCommand.IsClear(text))
            return CommandKind.Clear;

        if (HistoryCommand.IsHistory(text))
            return CommandKind.History;

        if (HelpCommand.IsHelp(text))
            return CommandKind.Help;

        if (StatusCommand.IsStatus(text))
            return CommandKind.Status;

        if (DemoCommand.IsDemo(text))
            return CommandKind.Demo;

        if (AiCommand.IsAiCommand(text))
            return CommandKind.Ai;

        if (BuildAppCommand.IsBuildApp(text))
            return CommandKind.BuildApp;

        if (BatchCommand.IsBatch(text))
            return CommandKind.Batch;

        return CommandKind.Pipeline;
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~CommandRouteTests`

Expected: PASS, 5 tests.

- [ ] **Step 5: Adopt it in the REPL**

In `CodeGeneratorStarter.Run()`, replace the ladder of `if (X.IsY(input))` guards with a single switch on `CommandRoute.Classify(input)`, keeping **every** body exactly as it is. The shape:

```csharp
            var kind = CommandRoute.Classify(input);

            if (kind == CommandKind.Exit)
                break;

            if (kind == CommandKind.Clear)
            {
                // ... existing body, unchanged, comments included
                continue;
            }

            if (kind == CommandKind.History)
            {
                Console.WriteLine(HistoryView.Format(_commandHistory, 20));
                continue;
            }

            // ... and so on for Help, Status, Demo, Ai
```

Inside the existing `try`, the same for `BuildApp` and `Batch`. Do not move any body, do not reorder, do not delete a comment.

In `RunOnce`, do the same for its three guards (`BuildApp`, `Help`, `Status`), leaving its bodies and its fall-through to the pipeline alone.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj`

Expected: PASS, no test changed. Any red here means a body moved or the order shifted — fix the adoption, not the test.

- [ ] **Step 7: Commit**

```bash
git add CodeGenerator/App/CommandKind.cs CodeGenerator/App/CommandRoute.cs CodeGenerator/App/CodeGeneratorStarter.cs CodeGenerator.Tests/App/CommandRouteTests.cs
git commit -m "refactor(cli): classify a command in one place instead of two ladders"
```

---

## Task 4: `Workspace` — a root that can move

**Files:**
- Create: `CodeGenerator/App/Workspace.cs`
- Modify: `CodeGenerator/App/CodeGeneratorStarter.cs` (fields at 17-45, constructor at 47-68, `LoadConfiguredRules` at 553, `InitializeExistingProjects` at 583)
- Test: `CodeGenerator.Tests/App/WorkspaceTests.cs`

**Interfaces:**
- Consumes: `SolutionFile.Read`, `SolutionFile.IsSolution` (Task 1); `ProjectManager.Register`, `ProjectManager.RetainOnlyWithin`, `ProjectManager.ClearCurrentProjectIfOutside`; `RuleParser.Parse`; `LearnedIntentStore.Initialize`.
- Produces:
  ```csharp
  public sealed record WorkspaceLoad(
      bool Ok, string Root, int ProjectCount, string? SolutionPath, string? Problem);

  public sealed class Workspace
  {
      public Workspace(string root, bool projectPathSet);
      public string Root { get; }
      public bool ProjectPathSet { get; }
      public List<PropertyRule> Rules { get; }
      public int ProjectCount { get; }
      public string HistoryPath { get; }        // Path.Combine(Root, ".codegen_history")
      public WorkspaceLoad LoadFrom(string path);
      public void LoadRules();
  }
  ```

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/App/WorkspaceTests.cs`:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Re-rooting a workspace: what gets registered, what gets dropped, and what follows the root.
/// </summary>
/// <remarks>
/// <see cref="DoNotParallelizeAttribute"/> because <c>ProjectManager</c> is static, process-wide
/// state. Two of these at once register into each other's registry.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class WorkspaceTests
{
    [TestMethod]
    public void A_folder_registers_every_project_under_it()
    {
        var root = NewTree("Api", "Core");

        var workspace = new Workspace(root, projectPathSet: false);
        var load = workspace.LoadFrom(root);

        Assert.IsTrue(load.Ok);
        Assert.AreEqual(2, load.ProjectCount);
        Assert.AreEqual(root, workspace.Root);
        Assert.IsNull(load.SolutionPath);
    }

    [TestMethod]
    public void A_solution_registers_only_the_projects_it_declares()
    {
        var root = NewTree("Api", "Core", "NotInTheSolution");
        var solution = Path.Combine(root, "App.slnx");

        File.WriteAllText(solution, """
            <Solution>
              <Project Path="Api/Api.csproj" />
              <Project Path="Core/Core.csproj" />
            </Solution>
            """);

        var workspace = new Workspace(root, projectPathSet: false);
        var load = workspace.LoadFrom(solution);

        Assert.IsTrue(load.Ok);
        Assert.AreEqual(2, load.ProjectCount);
        Assert.AreEqual(solution, load.SolutionPath);
        Assert.IsFalse(
            ProjectManager.Projects.Any(p => p.ProjectName == "NotInTheSolution"),
            "a project the solution does not declare must not be registered");
    }

    [TestMethod]
    public void Projects_outside_the_new_root_are_dropped()
    {
        var first = NewTree("Old");
        var second = NewTree("New");

        var workspace = new Workspace(first, projectPathSet: false);
        workspace.LoadFrom(first);

        workspace.LoadFrom(second);

        Assert.IsFalse(
            ProjectManager.Projects.Any(p => p.ProjectName == "Old"),
            "the previous root's projects must not leak into the new one");
    }

    [TestMethod]
    public void A_current_project_outside_the_new_root_is_cleared()
    {
        var first = NewTree("Old");
        var second = NewTree("New");

        var workspace = new Workspace(first, projectPathSet: false);
        workspace.LoadFrom(first);
        ProjectManager.Select("Old");

        workspace.LoadFrom(second);

        Assert.IsNull(ProjectManager.CurrentProject);
    }

    [TestMethod]
    public void The_history_file_follows_the_root()
    {
        var first = NewTree("A");
        var second = NewTree("B");

        var workspace = new Workspace(first, projectPathSet: false);

        Assert.AreEqual(Path.Combine(first, ".codegen_history"), workspace.HistoryPath);

        workspace.LoadFrom(second);

        Assert.AreEqual(Path.Combine(second, ".codegen_history"), workspace.HistoryPath);
    }

    [TestMethod]
    public void Rules_are_reloaded_from_the_new_root()
    {
        var first = NewTree("A");
        var second = NewTree("B");

        File.WriteAllText(
            Path.Combine(second, "rules.txt"), "Name should not be blank");

        var workspace = new Workspace(first, projectPathSet: false);
        workspace.LoadFrom(first);

        Assert.IsEmpty(workspace.Rules);

        workspace.LoadFrom(second);

        Assert.IsNotEmpty(workspace.Rules);
    }

    [TestMethod]
    public void A_path_that_does_not_exist_is_reported_not_thrown()
    {
        var root = NewTree("A");
        var workspace = new Workspace(root, projectPathSet: false);

        var load = workspace.LoadFrom(Path.Combine(root, "nowhere"));

        Assert.IsFalse(load.Ok);
        Assert.IsNotNull(load.Problem);
        Assert.AreEqual(root, workspace.Root);
    }

    /// <summary>A temp directory with one folder and one .csproj per name.</summary>
    private static string NewTree(params string[] projects)
    {
        var root = Path.Combine(Path.GetTempPath(), "ws-" + Guid.NewGuid().ToString("N")[..8]);

        foreach (var name in projects)
        {
            var folder = Path.Combine(root, name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(
                Path.Combine(folder, name + ".csproj"),
                """<Project Sdk="Microsoft.NET.Sdk"></Project>""");
        }

        return root;
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~WorkspaceTests`

Expected: FAIL — `Workspace` does not exist.

- [ ] **Step 3: Write the implementation**

Create `CodeGenerator/App/Workspace.cs`:

```csharp
using System.Text.Json;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Helper;
using CodeGenerationEngine.Models;

namespace CodeGenerator.App;

/// <summary>What a re-root did, for the caller to report.</summary>
public sealed record WorkspaceLoad(
    bool Ok, string Root, int ProjectCount, string? SolutionPath, string? Problem);

/// <summary>
/// Where the tool is working: the root, the rules loaded from it, and the projects under it.
/// </summary>
/// <remarks>
/// This was four loose fields on <c>CodeGeneratorStarter</c> with the loading split between its
/// constructor and <c>ProjectManager</c>. It is one object now because the chat page can re-root at
/// runtime, and the REPL has to read whatever the chat left behind.
/// <para>
/// <see cref="HistoryPath"/> follows <see cref="Root"/> rather than being fixed at startup, which
/// needs no further plumbing: <c>Run</c> builds a fresh <c>CommandLineEditor</c> on every
/// iteration.
/// </para>
/// <para>
/// The projects live in <c>ProjectManager</c>, which is static. That is why re-rooting here moves
/// the console session too, and why the chat says so when it happens rather than pretending each
/// surface has its own.
/// </para>
/// </remarks>
public sealed class Workspace
{
    private const string HistoryFilename = ".codegen_history";

    public Workspace(string root, bool projectPathSet)
    {
        Root = root;
        ProjectPathSet = projectPathSet;
    }

    public string Root { get; private set; }

    /// <summary>Whether <c>PROJECTPATH</c> chose the root. Only used to word a notice.</summary>
    public bool ProjectPathSet { get; }

    public List<PropertyRule> Rules { get; private set; } = [];

    public int ProjectCount { get; private set; }

    public string HistoryPath => Path.Combine(Root, HistoryFilename);

    /// <summary>
    /// Re-roots at a solution file, a project file or a folder.
    /// </summary>
    /// <remarks>
    /// Does what startup does, in startup's order: register, drop what is outside, clear a
    /// selection that no longer applies, reload the rules, reinitialize the learned-intent store.
    /// Getting that order wrong leaves a registry holding two workspaces at once.
    /// </remarks>
    public WorkspaceLoad LoadFrom(string path)
    {
        string root;
        string? solution = null;
        IReadOnlyList<string> projects;

        try
        {
            var full = Path.GetFullPath(path);

            if (SolutionFile.IsSolution(full))
            {
                if (!File.Exists(full))
                    return Failed($"There is no solution file at {full}.");

                var read = SolutionFile.Read(full);

                if (read.ProjectFiles.Count == 0)
                    return Failed($"{Path.GetFileName(full)} declares no C# projects.");

                solution = full;
                root = read.Root;
                projects = read.ProjectFiles;
            }
            else if (File.Exists(full)
                     && Path.GetExtension(full).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                root = Path.GetDirectoryName(full) ?? full;
                projects = [full];
            }
            else if (Directory.Exists(full))
            {
                root = full;
                projects = Directory.GetFiles(full, "*.csproj", SearchOption.AllDirectories);
            }
            else
            {
                return Failed($"There is nothing at {full}.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or ArgumentException or NotSupportedException)
        {
            return Failed(ex.Message);
        }

        Root = Path.TrimEndingDirectorySeparator(root);

        foreach (var project in projects)
        {
            ProjectManager.Register(new ProjectMetaData
            {
                Success = true,
                ProjectPath = Path.GetDirectoryName(project) ?? Root,
                CsProjPath = project,
                ProjectName = Path.GetFileNameWithoutExtension(project),
                Message = "Loaded"
            });
        }

        // Order matters: drop the outsiders before clearing the selection, or a selection inside
        // the old root is still in the registry when the check runs.
        ProjectManager.RetainOnlyWithin(Root);
        ProjectManager.ClearCurrentProjectIfOutside(Root);

        LoadRules();
        LearnedIntentStore.Initialize(Root);

        ProjectCount = projects.Count;

        return new WorkspaceLoad(true, Root, ProjectCount, solution, null);
    }

    /// <summary>
    /// Reads rules.json, or rules.txt if there is no json, from the current root.
    /// </summary>
    /// <remarks>
    /// Lifted verbatim from the starter, including the fall-back to an empty list on a bad file:
    /// a malformed rules file must not stop the tool starting.
    /// </remarks>
    public void LoadRules()
    {
        var jsonPath = Path.Combine(Root, "rules.json");
        var textPath = Path.Combine(Root, "rules.txt");

        Rules = [];

        try
        {
            if (File.Exists(jsonPath))
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var loaded = JsonSerializer.Deserialize<List<PropertyRule>>(
                    File.ReadAllText(jsonPath), options);

                if (loaded != null)
                    Rules = loaded;

                Console.WriteLine($"Loaded {Rules.Count} property rules from rules.json");
            }
            else if (File.Exists(textPath))
            {
                var lines = File.ReadAllLines(textPath).Where(l => !string.IsNullOrWhiteSpace(l));

                Rules = [.. RuleParser.Parse(lines)];

                Console.WriteLine($"Parsed {Rules.Count} property rules from rules.txt");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load rules: {ex.Message}");
            Rules = [];
        }
    }

    private WorkspaceLoad Failed(string problem)
        => new(false, Root, ProjectCount, null, problem);
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~WorkspaceTests`

Expected: PASS, 7 tests.

- [ ] **Step 5: Adopt it in the starter**

Replace `_workspaceRoot`, `_configuredRules`, `_projectCount` and `_historyPath` with one `private readonly Workspace _workspace;`, built in the constructor from `ResolveWorkspaceRoot()` and the existing `_projectPathSet`. Then:

- `LoadConfiguredRules()` becomes `_workspace.LoadRules()`. Delete the private method.
- Every `_workspaceRoot` reference becomes `_workspace.Root`.
- Every `_configuredRules` reference becomes `_workspace.Rules`.
- `_projectCount` becomes `_workspace.ProjectCount`; keep the starter's own `InitializeExistingProjects` (it prints the `WorkspaceNotice` lines, which `Workspace` does not) and set `_workspace.ProjectCount` there — add an internal setter or have `InitializeExistingProjects` call `_workspace.LoadFrom(_workspace.Root)` after the notice check. Prefer the latter: one code path for registering projects.
- `_historyPath` becomes `_workspace.HistoryPath`.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj`

Expected: PASS, no existing test changed. `status` output in particular must be identical — `StatusReport` reads the project count and the rules.

- [ ] **Step 7: Commit**

```bash
git add CodeGenerator/App/Workspace.cs CodeGenerator/App/CodeGeneratorStarter.cs CodeGenerator.Tests/App/WorkspaceTests.cs
git commit -m "refactor(cli): one workspace object, and a root that can move"
```

---

## Task 5: `LoopbackHost` — one host, one token guard

**Files:**
- Create: `CodeGenerator/Web/LoopbackHost.cs`
- Modify: `CodeGenerator/Web/BuildAppWebHost.cs` (`TryStart` at 76-153)
- Test: `CodeGenerator.Tests/Web/LoopbackHostTests.cs`

**Interfaces:**
- Consumes: `CircuitWatch`, `WorkflowCircuits` (both already in `Web/Workflow`).
- Produces:
  ```csharp
  internal sealed class LoopbackHost : IDisposable
  {
      public static bool TryStart(
          string route, Action<IServiceCollection> register, out LoopbackHost host);
      public string Url { get; }            // http://127.0.0.1:PORT/{route}?t={token}
      public string SessionToken { get; }
      public CircuitWatch Circuits { get; }
      public IServiceProvider Services { get; }
      public void Dispose();
  }
  ```

**The point of this task:** the token guard is currently `StartsWithSegments("/build")` at `BuildAppWebHost.cs:130`. A `/chat` route added without touching that file would be served to anything on this machine that can reach loopback — where these commands write files and run `dotnet`. After this task the guard exists once and is keyed on the route it was started for.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/Web/LoopbackHostTests.cs`:

```csharp
using CodeGenerator.Web;
using Microsoft.Extensions.DependencyInjection;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// The host mechanics, independent of what page they serve.
/// </summary>
/// <remarks>
/// <see cref="DoNotParallelizeAttribute"/> for the reason given on <c>BuildAppWebHostTests</c>:
/// every test here starts a real Kestrel, and there is one machine.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class LoopbackHostTests
{
    [TestMethod]
    public void Binds_loopback_on_a_free_port()
    {
        using var host = Start("chat");

        StringAssert.StartsWith(host.Url, "http://127.0.0.1:");
    }

    [TestMethod]
    public void The_url_carries_the_route_and_the_token()
    {
        using var host = Start("chat");

        StringAssert.Contains(host.Url, "/chat?t=");
        StringAssert.Contains(host.Url, host.SessionToken);
    }

    [TestMethod]
    public async Task Refuses_the_route_without_the_token()
    {
        using var host = Start("chat");
        using var client = new HttpClient();

        var response = await client.GetAsync(
            new Uri(host.Url).GetLeftPart(UriPartial.Authority) + "/chat");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Refuses_the_route_with_the_wrong_token()
    {
        using var host = Start("chat");
        using var client = new HttpClient();

        var response = await client.GetAsync(
            new Uri(host.Url).GetLeftPart(UriPartial.Authority) + "/chat?t=nonsense");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public void Registers_what_the_caller_asked_for()
    {
        using var host = Start("chat", services => services.AddSingleton(new Marker()));

        Assert.IsNotNull(host.Services.GetService<Marker>());
    }

    [TestMethod]
    public void Guards_the_route_it_was_given_and_not_another()
    {
        // The guard must follow the route argument. Hard-coding it to "/build" is the bug this
        // whole extraction exists to make impossible.
        using var host = Start("chat");

        StringAssert.Contains(host.Url, "/chat");
    }

    private sealed class Marker;

    private static LoopbackHost Start(
        string route, Action<IServiceCollection>? register = null)
    {
        Assert.IsTrue(
            LoopbackHost.TryStart(route, register ?? (_ => { }), out var host),
            "the host must start; a machine that cannot bind loopback cannot run this test");

        return host;
    }
}
```

Add `using System.Net;` for `HttpStatusCode`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~LoopbackHostTests`

Expected: FAIL — `LoopbackHost` does not exist.

- [ ] **Step 3: Write `LoopbackHost`**

Create `CodeGenerator/Web/LoopbackHost.cs`. This is `BuildAppWebHost.TryStart` moved almost verbatim — keep its comments, they record why each line is there:

```csharp
using System.Security.Cryptography;
using CodeGenerator.Web.Workflow;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CodeGenerator.Web;

/// <summary>
/// Serves one Blazor page on a loopback port, for the length of one command.
/// </summary>
/// <remarks>
/// Extracted from <see cref="BuildAppWebHost"/> when the chat page needed the same machinery. The
/// reason it is shared rather than copied is one line — the token guard. Any process on this
/// machine can reach loopback, and these pages write files and run <c>dotnet</c>, so a route
/// served without a token is served to anything running here. Two copies of that check is two
/// places for it to drift out of step, and the second one would be the one nobody remembers.
/// <para>
/// Three things this class exists to get right, unchanged from where they came from. It binds port
/// 0 and reads the assignment back, so a port in use is not a failure mode. It mints a token and
/// 404s without it. And it stops on every path, because a CLI blocked forever on an abandoned page
/// is worse than no feature.
/// </para>
/// </remarks>
internal sealed class LoopbackHost : IDisposable
{
    /// <summary>How the query string's token parameter is spelled.</summary>
    internal const string TokenParameter = "t";

    private readonly WebApplication _app;

    private LoopbackHost(WebApplication app, string url, string token, CircuitWatch circuits)
    {
        _app = app;
        Url = url;
        SessionToken = token;
        Circuits = circuits;
    }

    /// <summary>The tabs connected to this host.</summary>
    public CircuitWatch Circuits { get; }

    /// <summary>What the running host resolves its services from.</summary>
    public IServiceProvider Services => _app.Services;

    /// <summary>The address to open, route and token and all.</summary>
    public string Url { get; }

    public string SessionToken { get; }

    /// <summary>
    /// Starts the host, or reports that it could not.
    /// </summary>
    /// <remarks>
    /// False rather than an exception because a machine that cannot bind loopback has a remedy —
    /// the console — not a fault to report.
    /// </remarks>
    /// <param name="route">
    /// The path segment the page is served on, without a leading slash. The token guard is keyed on
    /// it, so a caller that passes the wrong one gets an unreachable page rather than an
    /// unguarded one.
    /// </param>
    public static bool TryStart(
        string route, Action<IServiceCollection> register, out LoopbackHost host)
    {
        host = null!;

        try
        {
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

            // The application name and content root are pinned to this assembly and its output
            // folder rather than left to the defaults: the defaults are the entry assembly and the
            // current directory, and both are wrong here — the CLI is started from wherever the
            // user happens to be, and under a test host the entry assembly is the test host. Both
            // manifests below are named after this assembly, and are found next to it.
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                ApplicationName = typeof(LoopbackHost).Assembly.GetName().Name,
                ContentRootPath = AppContext.BaseDirectory,
            });

            // What serves wwwroot and _framework/blazor.web.js from a build output that contains
            // neither: the runtime manifest maps them back to the source folders, and the endpoints
            // manifest publishes a route per asset. A published layout has no runtime manifest and
            // its own real wwwroot, and this call then does nothing.
            builder.WebHost.UseStaticWebAssets();

            // Nothing but warnings: this host shares a console with a REPL, and Kestrel's startup
            // banner in the middle of a command reads as output the tool produced.
            builder.Logging.ClearProviders();

            builder.WebHost.UseUrls("http://127.0.0.1:0");

            builder.Services.AddRazorComponents().AddInteractiveServerComponents();

            // The watch is one per host and the handler is one per circuit, which is the whole
            // point of the pair: the handlers come and go with the tabs and the count does not.
            var circuits = new CircuitWatch();

            builder.Services.AddSingleton(circuits);
            builder.Services.AddScoped<CircuitHandler, WorkflowCircuits>();

            register(builder.Services);

            var app = builder.Build();

            app.MapStaticAssets();

            // MapRazorComponents stamps anti-forgery metadata on the endpoint, and the endpoint
            // middleware refuses to run without the matching middleware in the pipeline.
            app.UseAntiforgery();

            var guarded = "/" + route.Trim('/');

            // Before the component pipeline, so an untokened request never reaches a circuit.
            app.Use(async (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments(guarded)
                    && context.Request.Query[TokenParameter] != token)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                await next(context);
            });

            app.MapRazorComponents<Components.App>().AddInteractiveServerRenderMode();

            app.Start();

            var bound = app.Urls.First();

            host = new LoopbackHost(
                app, $"{bound}{guarded}?{TokenParameter}={token}", token, circuits);

            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException
                                      or System.Net.Sockets.SocketException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            _app.StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // Already stopping, or already gone. Either way there is nothing to stop.
        }

        ((IDisposable)_app).Dispose();
    }
}
```

Check the existing `BuildAppWebHost.Dispose` body (it continues past line 250) and move it here verbatim rather than trusting the sketch above.

- [ ] **Step 4: Make `BuildAppWebHost` a caller**

Replace its `TryStart` body with a call, keeping its own class doc, `AbandonGrace`, `PollInterval`, `Run`, `OutcomeFor` and `Repeatable` exactly as they are:

```csharp
    /// <summary>The route the workflow page is served on.</summary>
    private const string Route = "build";

    public static bool TryStart(BuildSession session, out BuildAppWebHost host)
    {
        host = null!;

        // Singleton, so a second tab attaches to the running workflow rather than starting a rival
        // one against the same workspace.
        if (!LoopbackHost.TryStart(Route, s => s.AddSingleton(session), out var loopback))
            return false;

        host = new BuildAppWebHost(loopback);

        return true;
    }
```

`BuildAppWebHost` now holds a `LoopbackHost` and forwards `Url`, `SessionToken`, `Circuits`, `Services` and `Dispose` to it. Its `Run(BuildSession, CancellationToken, TimeSpan?)` is untouched.

- [ ] **Step 5: Run every Web test**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~CodeGenerator.Tests.Web`

Expected: PASS. **No file under `CodeGenerator.Tests/Web` may be edited in this task.** If one has to change, the extraction changed behaviour — fix the extraction.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerator/Web/LoopbackHost.cs CodeGenerator/Web/BuildAppWebHost.cs CodeGenerator.Tests/Web/LoopbackHostTests.cs
git commit -m "refactor(web): one loopback host, so the token guard exists once"
```

---

## Task 6: `CSharpTokens` — a scanner that cannot lose code

**Files:**
- Create: `CodeGenerator/Web/Chat/CSharpTokens.cs`
- Test: `CodeGenerator.Tests/Web/Chat/CSharpTokensTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  ```csharp
  public enum TokenKind { Plain, Keyword, Text, Comment, Number }
  public sealed record Token(TokenKind Kind, string Text);
  public static class CSharpTokens
  {
      public static IReadOnlyList<Token> Scan(string code);
  }
  ```

`TokenKind.Text` rather than `String` — `String` collides with the type name in a `switch` inside a Razor file.

**Why hand-rolled:** the page is self-contained. No CDN, no external highlighter. The constraint is the same one `wwwroot/workflow.css` already works under.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/Web/Chat/CSharpTokensTests.cs`:

```csharp
using CodeGenerator.Web.Chat;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// Scanning C# for the code block.
/// </summary>
/// <remarks>
/// One test here outranks the others: the spans must concatenate back to exactly what went in. A
/// highlighter that drops a character is a highlighter that shows the user code they do not have,
/// and no amount of correct colouring makes up for it.
/// </remarks>
[TestClass]
public class CSharpTokensTests
{
    [TestMethod]
    public void Nothing_is_lost()
    {
        var code = """
            using System;

            namespace Acme.Shop;

            /// <summary>An order.</summary>
            public sealed class Order
            {
                // 42 is not a magic number if you name it
                public const int MaxLines = 42;

                public string Reference { get; set; } = "ORD-000";

                public char Separator = '-';

                public decimal Total(decimal net) => net * 1.20m;
            }
            """;

        var scanned = CSharpTokens.Scan(code);

        Assert.AreEqual(code, string.Concat(scanned.Select(t => t.Text)));
    }

    [TestMethod]
    public void Nothing_is_lost_from_awkward_input()
    {
        foreach (var code in new[]
                 {
                     "",
                     "\"unterminated",
                     "// trailing comment with no newline",
                     "/* unterminated block",
                     "'",
                     "\"escaped \\\" quote\"",
                     "123abc",
                     "\r\n\r\n",
                 })
        {
            Assert.AreEqual(
                code,
                string.Concat(CSharpTokens.Scan(code).Select(t => t.Text)),
                $"lost something scanning: {code}");
        }
    }

    [TestMethod]
    public void Keywords_strings_comments_and_numbers_are_marked()
    {
        var scanned = CSharpTokens.Scan("public int X = 42; // note\n");

        Assert.Contains(new Token(TokenKind.Keyword, "public"), scanned);
        Assert.Contains(new Token(TokenKind.Keyword, "int"), scanned);
        Assert.Contains(new Token(TokenKind.Number, "42"), scanned);
        Assert.IsTrue(scanned.Any(t => t.Kind == TokenKind.Comment && t.Text.Contains("note")));
    }

    [TestMethod]
    public void A_word_that_merely_starts_with_a_keyword_is_not_one()
    {
        // "internalName" is an identifier. Matching keywords by prefix rather than by whole word
        // is the classic way a highlighter starts colouring variable names.
        var scanned = CSharpTokens.Scan("var internalName = 1;");

        Assert.IsFalse(scanned.Any(t => t.Kind == TokenKind.Keyword && t.Text == "internal"));
        Assert.Contains(new Token(TokenKind.Plain, "internalName"), scanned);
    }

    [TestMethod]
    public void A_keyword_inside_a_string_stays_part_of_the_string()
    {
        var scanned = CSharpTokens.Scan("""var s = "public";""");

        Assert.IsFalse(scanned.Any(t => t.Kind == TokenKind.Keyword && t.Text == "public"));
        Assert.IsTrue(scanned.Any(t => t.Kind == TokenKind.Text && t.Text.Contains("public")));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~CSharpTokensTests`

Expected: FAIL — `CSharpTokens` does not exist.

- [ ] **Step 3: Write the implementation**

Create `CodeGenerator/Web/Chat/CSharpTokens.cs`:

```csharp
using System.Text;

namespace CodeGenerator.Web.Chat;

/// <summary>What a run of characters is, for colouring.</summary>
public enum TokenKind
{
    Plain,

    Keyword,

    /// <summary>A string or character literal. Not called String: that collides with the type
    /// name in a switch inside a .razor file.</summary>
    Text,

    Comment,

    Number
}

/// <summary>One run of characters and what it is.</summary>
public sealed record Token(TokenKind Kind, string Text);

/// <summary>
/// Splits C# into coloured runs, losing nothing.
/// </summary>
/// <remarks>
/// Hand-rolled because the page is self-contained — no CDN, no external highlighter, the same
/// constraint the workflow page already works under.
/// <para>
/// The invariant that matters is not correct colouring, it is that the runs concatenate back to
/// the input exactly. A highlighter that drops a character shows a user source they do not have,
/// and that is worse than showing them no colour at all. Every unterminated literal, every
/// trailing comment and every stray quote below is handled by running to the end of the input
/// rather than by giving up.
/// </para>
/// </remarks>
public static class CSharpTokens
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "class",
        "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum",
        "event", "explicit", "false", "finally", "fixed", "float", "for", "foreach", "get",
        "if", "implicit", "in", "init", "int", "interface", "internal", "is", "lock", "long",
        "namespace", "new", "null", "object", "operator", "out", "override", "params", "private",
        "protected", "public", "readonly", "record", "ref", "return", "sealed", "set", "short",
        "sizeof", "static", "string", "struct", "switch", "this", "throw", "true", "try",
        "typeof", "uint", "ulong", "ushort", "using", "var", "virtual", "void", "while", "yield"
    };

    public static IReadOnlyList<Token> Scan(string code)
    {
        var tokens = new List<Token>();
        var plain = new StringBuilder();
        var i = 0;

        void FlushPlain()
        {
            if (plain.Length == 0)
                return;

            tokens.Add(new Token(TokenKind.Plain, plain.ToString()));
            plain.Clear();
        }

        while (i < code.Length)
        {
            var c = code[i];

            // Comments. A line comment runs to the newline or the end; a block comment to its
            // terminator or the end. Neither gives up on an unterminated one.
            if (c == '/' && i + 1 < code.Length && code[i + 1] == '/')
            {
                FlushPlain();

                var end = code.IndexOf('\n', i);
                end = end < 0 ? code.Length : end;

                tokens.Add(new Token(TokenKind.Comment, code[i..end]));
                i = end;
                continue;
            }

            if (c == '/' && i + 1 < code.Length && code[i + 1] == '*')
            {
                FlushPlain();

                var close = code.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var end = close < 0 ? code.Length : close + 2;

                tokens.Add(new Token(TokenKind.Comment, code[i..end]));
                i = end;
                continue;
            }

            // String and character literals, including the escape that would otherwise end them
            // early.
            if (c is '"' or '\'')
            {
                FlushPlain();

                var j = i + 1;

                while (j < code.Length)
                {
                    if (code[j] == '\\')
                    {
                        j += 2;
                        continue;
                    }

                    if (code[j] == c)
                    {
                        j++;
                        break;
                    }

                    j++;
                }

                var end = Math.Min(j, code.Length);

                tokens.Add(new Token(TokenKind.Text, code[i..end]));
                i = end;
                continue;
            }

            // Numbers, including a decimal point and a type suffix, so "1.20m" is one token.
            if (char.IsAsciiDigit(c) && (i == 0 || !IsWordChar(code[i - 1])))
            {
                FlushPlain();

                var j = i;

                while (j < code.Length && (char.IsAsciiDigit(code[j]) || code[j] == '.'
                                           || code[j] is 'm' or 'M' or 'd' or 'D' or 'f' or 'F'
                                           or 'L' or 'l' or 'u' or 'U'))
                {
                    j++;
                }

                // A word that merely starts with digits is not a number: 123abc is one plain run.
                if (j < code.Length && IsWordChar(code[j]))
                {
                    while (j < code.Length && IsWordChar(code[j]))
                        j++;

                    plain.Append(code[i..j]);
                    i = j;
                    continue;
                }

                tokens.Add(new Token(TokenKind.Number, code[i..j]));
                i = j;
                continue;
            }

            // Words. Matched whole, so an identifier beginning with a keyword is not coloured as
            // one — the classic way a highlighter starts painting variable names.
            if (IsWordStart(c))
            {
                var j = i;

                while (j < code.Length && IsWordChar(code[j]))
                    j++;

                var word = code[i..j];

                if (Keywords.Contains(word))
                {
                    FlushPlain();
                    tokens.Add(new Token(TokenKind.Keyword, word));
                }
                else
                {
                    plain.Append(word);
                }

                i = j;
                continue;
            }

            plain.Append(c);
            i++;
        }

        FlushPlain();

        return tokens;
    }

    private static bool IsWordStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~CSharpTokensTests`

Expected: PASS, 5 tests. If `Nothing_is_lost` fails, fix the scanner — never the assertion.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/Web/Chat/CSharpTokens.cs CodeGenerator.Tests/Web/Chat/CSharpTokensTests.cs
git commit -m "feat(chat): a C# scanner that concatenates back to its input"
```

---

## Task 7: `OutputBlocks` — three known shapes, everything else text

**Files:**
- Create: `CodeGenerator/Web/Chat/OutputBlocks.cs`
- Test: `CodeGenerator.Tests/Web/Chat/OutputBlocksTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  ```csharp
  public abstract record OutputBlock;
  public sealed record TextBlock(string Text) : OutputBlock;
  public sealed record CodeBlock(string Heading, string Code) : OutputBlock;
  public sealed record TableBlock(
      string Heading, IReadOnlyList<string> Headers,
      IReadOnlyList<IReadOnlyList<string>> Rows) : OutputBlock;
  public sealed record ListBlock(string Heading, IReadOnlyList<string> Items) : OutputBlock;

  public static class OutputBlocks
  {
      public static IReadOnlyList<OutputBlock> Parse(string input, string text);
  }
  ```

**The three shapes, read out of the code:**

| Command | Printed by | Shape |
| --- | --- | --- |
| `show class X` | `ClassValidator.cs:84` | blank, `----- X -----`, blank, the whole file |
| `list properties X` | `CommandHandlingService.cs:396` | `Properties in X:` then `  <Type> <Name>` per line |
| `list classes` | `ProjectManager.cs:256` | `Classes in <scope>`, a line of dashes, one name per line |

**The rule:** anything that does not match its shape exactly returns a single `TextBlock`. These shapes are one refactor away from changing, and a renderer that half-recognises output and mangles the rest is worse than a monospace block.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/Web/Chat/OutputBlocksTests.cs`:

```csharp
using CodeGenerator.Web.Chat;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// Turning a command's captured output into blocks the page can render properly.
/// </summary>
/// <remarks>
/// Three shapes are recognised, each read out of the handler that prints it rather than assumed.
/// The other half of every case below is the one that matters more: output one line off its
/// expected shape comes back as plain text. Nothing here is allowed to guess.
/// </remarks>
[TestClass]
public class OutputBlocksTests
{
    [TestMethod]
    public void Show_class_becomes_a_code_block()
    {
        var text = "\r\n----- Order -----\r\n\r\npublic class Order\r\n{\r\n}\r\n\r\n";

        var blocks = OutputBlocks.Parse("show class Order", text);

        Assert.HasCount(1, blocks);
        var code = (CodeBlock)blocks[0];
        Assert.AreEqual("Order", code.Heading);
        StringAssert.StartsWith(code.Code, "public class Order");
        Assert.DoesNotContain("-----", code.Code);
    }

    [TestMethod]
    public void Show_class_that_did_not_find_the_class_stays_text()
    {
        // The failure path prints a warning and no header. There is no code to show.
        var blocks = OutputBlocks.Parse("show class Nope", "Class 'Nope' not found.\r\n");

        Assert.IsInstanceOfType<TextBlock>(blocks[0]);
    }

    [TestMethod]
    public void List_properties_becomes_a_table()
    {
        var text = "Properties in Order:\r\n  int Id\r\n  string Reference\r\n";

        var blocks = OutputBlocks.Parse("list properties of Order", text);

        var table = (TableBlock)blocks[0];
        Assert.AreEqual("Properties in Order", table.Heading);
        Assert.HasCount(2, table.Headers);
        Assert.AreEqual("Type", table.Headers[0]);
        Assert.AreEqual("Name", table.Headers[1]);
        Assert.HasCount(2, table.Rows);
        Assert.AreEqual("int", table.Rows[0][0]);
        Assert.AreEqual("Id", table.Rows[0][1]);
    }

    [TestMethod]
    public void List_properties_with_no_properties_stays_text()
    {
        var blocks = OutputBlocks.Parse(
            "list properties of Order", "No properties found in 'Order'.\r\n");

        Assert.IsInstanceOfType<TextBlock>(blocks[0]);
    }

    [TestMethod]
    public void A_property_line_of_the_wrong_shape_makes_the_whole_thing_text()
    {
        // One line without the two-space indent, or with three words, and the table would be
        // wrong in a way the user cannot see. Fall back rather than guess.
        var text = "Properties in Order:\r\n  int Id\r\nsomething else entirely\r\n";

        Assert.IsInstanceOfType<TextBlock>(
            OutputBlocks.Parse("list properties of Order", text)[0]);
    }

    [TestMethod]
    public void List_classes_becomes_a_list()
    {
        var text = "\r\nClasses in Shop\r\n--------------------------------\r\nCustomer\r\nOrder\r\n\r\n";

        var blocks = OutputBlocks.Parse("list classes", text);

        var list = (ListBlock)blocks[0];
        Assert.AreEqual("Classes in Shop", list.Heading);
        Assert.HasCount(2, list.Items);
        Assert.Contains("Order", list.Items);
    }

    [TestMethod]
    public void List_classes_with_none_found_stays_text()
    {
        Assert.IsInstanceOfType<TextBlock>(
            OutputBlocks.Parse("list classes", "No classes found.\r\n")[0]);
    }

    [TestMethod]
    public void List_classes_without_the_rule_of_dashes_stays_text()
    {
        var text = "\r\nClasses in Shop\r\nCustomer\r\nOrder\r\n";

        Assert.IsInstanceOfType<TextBlock>(OutputBlocks.Parse("list classes", text)[0]);
    }

    [TestMethod]
    public void Anything_unrecognised_is_one_text_block()
    {
        foreach (var input in new[] { "status", "help", "validate project", "show validation Order" })
        {
            var blocks = OutputBlocks.Parse(input, "whatever it printed\r\n");

            Assert.HasCount(1, blocks);
            Assert.IsInstanceOfType<TextBlock>(blocks[0]);
        }
    }

    [TestMethod]
    public void Empty_output_is_no_blocks()
    {
        Assert.IsEmpty(OutputBlocks.Parse("list classes", ""));
        Assert.IsEmpty(OutputBlocks.Parse("list classes", "   \r\n"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~OutputBlocksTests`

Expected: FAIL — `OutputBlocks` does not exist.

- [ ] **Step 3: Write the implementation**

Create `CodeGenerator/Web/Chat/OutputBlocks.cs`:

```csharp
using System.Text.RegularExpressions;

namespace CodeGenerator.Web.Chat;

/// <summary>One part of a command's output, and how the page should draw it.</summary>
public abstract record OutputBlock;

/// <summary>Preformatted text, exactly as the command printed it.</summary>
public sealed record TextBlock(string Text) : OutputBlock;

public sealed record CodeBlock(string Heading, string Code) : OutputBlock;

public sealed record TableBlock(
    string Heading,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows) : OutputBlock;

public sealed record ListBlock(string Heading, IReadOnlyList<string> Items) : OutputBlock;

/// <summary>
/// Reads a command's captured output and says how to draw it.
/// </summary>
/// <remarks>
/// Three shapes are recognised, each taken from the code that prints it: <c>show class</c>
/// (<c>ClassValidator.ShowClass</c>), <c>list properties</c>
/// (<c>CommandHandlingService.HandleListProperties</c>) and <c>list classes</c>
/// (<c>ProjectManager.ListClasses</c>).
/// <para>
/// Everything else is one <see cref="TextBlock"/>, and so is anything that does not match its
/// shape <b>exactly</b>. That is the rule this class is built around rather than an oversight:
/// these shapes are one refactor of an unrelated handler away from changing, and output that is
/// half-recognised and half-mangled is worse than a monospace block. A changed shape degrades to
/// preformatted text, which is what the console shows anyway.
/// </para>
/// </remarks>
public static class OutputBlocks
{
    /// <summary>`----- Order -----`, the header ShowClass prints before the file.</summary>
    private static readonly Regex ClassHeader = new(
        @"^-{5}\s(?<name>.+?)\s-{5}$", RegexOptions.Compiled);

    /// <summary>`  int Id` — two spaces, a type, one space, a name.</summary>
    private static readonly Regex PropertyLine = new(
        @"^\s{2}(?<type>\S+)\s(?<name>\S+)$", RegexOptions.Compiled);

    public static IReadOnlyList<OutputBlock> Parse(string input, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

        return AsCode(lines)
            ?? AsPropertyTable(lines)
            ?? AsClassList(lines)
            ?? [new TextBlock(text.TrimEnd('\r', '\n'))];
    }

    /// <summary>
    /// The whole file, minus the header ShowClass printed above it. Null when the header is not
    /// there — which is every failure path, all of which print a warning and no source.
    /// </summary>
    private static IReadOnlyList<OutputBlock>? AsCode(string[] lines)
    {
        var header = -1;

        for (var i = 0; i < lines.Length && i < 3; i++)
        {
            if (ClassHeader.IsMatch(lines[i].Trim()))
            {
                header = i;
                break;
            }
        }

        if (header < 0)
            return null;

        var name = ClassHeader.Match(lines[header].Trim()).Groups["name"].Value;
        var code = string.Join("\n", lines.Skip(header + 1)).Trim('\n');

        return code.Length == 0 ? null : [new CodeBlock(name, code)];
    }

    private static IReadOnlyList<OutputBlock>? AsPropertyTable(string[] lines)
    {
        if (lines.Length < 2 || !lines[0].StartsWith("Properties in ", StringComparison.Ordinal))
            return null;

        var rows = new List<IReadOnlyList<string>>();

        foreach (var line in lines.Skip(1).Where(l => l.Length > 0))
        {
            var match = PropertyLine.Match(line);

            // One line off the shape and the whole thing is text. A table with a row missing is a
            // table that lies about the class.
            if (!match.Success)
                return null;

            rows.Add([match.Groups["type"].Value, match.Groups["name"].Value]);
        }

        return rows.Count == 0
            ? null
            : [new TableBlock(lines[0].TrimEnd(':'), ["Type", "Name"], rows)];
    }

    private static IReadOnlyList<OutputBlock>? AsClassList(string[] lines)
    {
        var kept = lines.Where(l => l.Trim().Length > 0).ToList();

        if (kept.Count < 3 || !kept[0].StartsWith("Classes in ", StringComparison.Ordinal))
            return null;

        // The rule of dashes is part of the shape. Without it this is some other output that
        // happens to start with the same words.
        if (kept[1].Trim('-').Length != 0 || kept[1].Length < 3)
            return null;

        return [new ListBlock(kept[0].Trim(), [.. kept.Skip(2).Select(l => l.Trim())])];
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~OutputBlocksTests`

Expected: PASS, 10 tests.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/Web/Chat/OutputBlocks.cs CodeGenerator.Tests/Web/Chat/OutputBlocksTests.cs
git commit -m "feat(chat): render three known output shapes, and fall back to text"
```

---

## Task 8: `CommandCatalog` — autocomplete over the real catalog

**Files:**
- Create: `CodeGenerator/Web/Chat/CommandCatalog.cs`
- Test: `CodeGenerator.Tests/Web/Chat/CommandCatalogTests.cs`

**Interfaces:**
- Consumes: `CommandDictionary.Commands` (`IReadOnlyList<CommandInfo>` with `Category`, `Command`, `Description`, `Example`).
- Produces:
  ```csharp
  public static class CommandCatalog
  {
      public static IReadOnlyList<CommandInfo> Matches(string typed, int limit = 8);
  }
  ```

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/Web/Chat/CommandCatalogTests.cs`:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerator.Web.Chat;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// What the composer's dropdown offers.
/// </summary>
/// <remarks>
/// Filtered from <c>CommandDictionary.Commands</c> — the same list <c>help</c> and
/// <c>docs/commands.md</c> are generated from, so the dropdown cannot drift from the real catalog.
/// </remarks>
[TestClass]
public class CommandCatalogTests
{
    [TestMethod]
    public void Nothing_typed_offers_nothing()
    {
        Assert.IsEmpty(CommandCatalog.Matches(""));
        Assert.IsEmpty(CommandCatalog.Matches("   "));
    }

    [TestMethod]
    public void Offers_commands_that_start_with_what_was_typed()
    {
        var matches = CommandCatalog.Matches("list");

        Assert.IsNotEmpty(matches);
        Assert.IsTrue(
            matches.All(m => m.Command.StartsWith("list", StringComparison.OrdinalIgnoreCase)
                             || m.Command.Contains("list", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void A_prefix_match_is_offered_before_a_contains_match()
    {
        // Typing "class" should reach `create class` before `list classes in <project>`: the
        // command that begins with what you typed is the one you were most likely reaching for.
        var matches = CommandCatalog.Matches("show class");

        Assert.IsNotEmpty(matches);
        StringAssert.StartsWith(matches[0].Command, "show class");
    }

    [TestMethod]
    public void Matching_is_case_insensitive()
    {
        Assert.IsNotEmpty(CommandCatalog.Matches("LIST CLASSES"));
    }

    [TestMethod]
    public void The_limit_is_respected()
    {
        Assert.IsTrue(CommandCatalog.Matches("a", limit: 3).Count <= 3);
    }

    [TestMethod]
    public void Build_app_is_never_offered()
    {
        // The chat declines it. Offering a command that cannot be run there is an invitation to
        // a dead end.
        var matches = CommandCatalog.Matches("build", limit: 50);

        Assert.IsFalse(
            matches.Any(m => m.Command.StartsWith("build app", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Every_offer_carries_its_description_and_example()
    {
        foreach (var match in CommandCatalog.Matches("list", limit: 50))
        {
            Assert.IsNotEmpty(match.Description, $"{match.Command} has no description");
            Assert.IsNotEmpty(match.Example, $"{match.Command} has no example");
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~CommandCatalogTests`

Expected: FAIL — `CommandCatalog` does not exist.

- [ ] **Step 3: Write the implementation**

Create `CodeGenerator/Web/Chat/CommandCatalog.cs`:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Web.Chat;

/// <summary>
/// What the composer offers as you type.
/// </summary>
/// <remarks>
/// Filtered from <see cref="CommandDictionary.Commands"/> rather than a list of its own: that is
/// the list <c>help</c> renders and <c>docs/commands.md</c> is generated from, so the dropdown
/// cannot fall behind the catalog. A separate list would.
/// <para>
/// The three commands the chat declines are filtered out. Offering something that cannot be run
/// where it is offered is worse than offering nothing.
/// </para>
/// </remarks>
public static class CommandCatalog
{
    private static readonly string[] Declined = ["build app", "batch", "demo"];

    public static IReadOnlyList<CommandInfo> Matches(string typed, int limit = 8)
    {
        var text = (typed ?? string.Empty).Trim();

        if (text.Length == 0)
            return [];

        var runnable = CommandDictionary.Commands.Where(Runnable);

        // Prefix first, then contains: the command that begins with what was typed is the one
        // being reached for, and a "contains" hit is a second-best guess rather than an equal one.
        var prefix = runnable
            .Where(c => c.Command.StartsWith(text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Command.Length)
            .ToList();

        var contains = runnable
            .Where(c => !prefix.Contains(c)
                        && c.Command.Contains(text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Command.Length);

        return [.. prefix.Concat(contains).Take(limit)];
    }

    private static bool Runnable(CommandInfo command)
        => !Declined.Any(d => command.Command.StartsWith(d, StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~CommandCatalogTests`

Expected: PASS, 7 tests. `A_prefix_match_is_offered_before_a_contains_match` may need the assertion loosened if no catalog entry starts with `show class` — check `CommandDictionary.cs:144` first; it is `show class`, so it should hold.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/Web/Chat/CommandCatalog.cs CodeGenerator.Tests/Web/Chat/CommandCatalogTests.cs
git commit -m "feat(chat): autocomplete from the real command catalog"
```

---

## Task 9: `FolderListing` — what the picker shows

**Files:**
- Create: `CodeGenerator/Web/Chat/FolderListing.cs`
- Test: `CodeGenerator.Tests/Web/Chat/FolderListingTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  ```csharp
  public sealed record FolderEntry(string Name, string Path, bool IsDirectory);
  public static class FolderListing
  {
      public static IReadOnlyList<FolderEntry> For(string directory);
      public static string? ParentOf(string directory);
  }
  ```

**Why server-side:** a browser `<input type="file">` never hands the server a real path, and a native dialog opened from the host process appears behind the browser. The host is local, so it lists directories itself.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/Web/Chat/FolderListingTests.cs`:

```csharp
using CodeGenerator.Web.Chat;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// What the solution picker shows for a directory.
/// </summary>
/// <remarks>
/// Listed by the host process rather than the browser: an <c>&lt;input type="file"&gt;</c> never
/// gives the server a real path, and a native dialog would open behind the browser.
/// </remarks>
[TestClass]
public class FolderListingTests
{
    [TestMethod]
    public void Lists_subfolders_and_solution_files_only()
    {
        var dir = NewDir();
        Directory.CreateDirectory(Path.Combine(dir, "Api"));
        File.WriteAllText(Path.Combine(dir, "App.slnx"), "<Solution />");
        File.WriteAllText(Path.Combine(dir, "Old.sln"), "");
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "");

        var entries = FolderListing.For(dir);

        Assert.IsTrue(entries.Any(e => e.Name == "Api" && e.IsDirectory));
        Assert.IsTrue(entries.Any(e => e.Name == "App.slnx" && !e.IsDirectory));
        Assert.IsTrue(entries.Any(e => e.Name == "Old.sln"));
        Assert.IsFalse(entries.Any(e => e.Name == "notes.txt"));
    }

    [TestMethod]
    public void Solution_files_come_before_folders()
    {
        // The thing you came to click is the thing at the top.
        var dir = NewDir();
        Directory.CreateDirectory(Path.Combine(dir, "Api"));
        File.WriteAllText(Path.Combine(dir, "App.slnx"), "<Solution />");

        Assert.IsFalse(FolderListing.For(dir)[0].IsDirectory);
    }

    [TestMethod]
    public void Build_output_folders_are_not_offered()
    {
        // bin and obj are never where a solution is, and they are the two folders every project
        // has. Offering them is offering noise.
        var dir = NewDir();
        Directory.CreateDirectory(Path.Combine(dir, "bin"));
        Directory.CreateDirectory(Path.Combine(dir, "obj"));
        Directory.CreateDirectory(Path.Combine(dir, "src"));

        var names = FolderListing.For(dir).Select(e => e.Name).ToList();

        Assert.Contains("src", names);
        Assert.DoesNotContain("bin", names);
        Assert.DoesNotContain("obj", names);
    }

    [TestMethod]
    public void A_directory_that_is_gone_lists_nothing_rather_than_throwing()
    {
        Assert.IsEmpty(FolderListing.For(Path.Combine(Path.GetTempPath(), "not-here-at-all")));
    }

    [TestMethod]
    public void The_parent_of_a_drive_root_is_null()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;

        Assert.IsNull(FolderListing.ParentOf(root));
        Assert.IsNotNull(FolderListing.ParentOf(NewDir()));
    }

    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fl-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~FolderListingTests`

Expected: FAIL — `FolderListing` does not exist.

- [ ] **Step 3: Write the implementation**

Create `CodeGenerator/Web/Chat/FolderListing.cs`:

```csharp
using CodeGenerationEngine.Core;

namespace CodeGenerator.Web.Chat;

/// <summary>One thing the picker can click.</summary>
public sealed record FolderEntry(string Name, string Path, bool IsDirectory);

/// <summary>
/// What the solution picker shows for a directory.
/// </summary>
/// <remarks>
/// Listed by the host process, not the browser. A browser file input hands over a name and no
/// path, and a native dialog started from here would open behind the browser window — so the page
/// walks the filesystem through this instead.
/// <para>
/// Solution files first, then folders: the file is what the user came to click, and a listing that
/// buries it under twenty directories makes them type the path anyway.
/// </para>
/// </remarks>
public static class FolderListing
{
    /// <summary>Folders never worth offering: every project has both, and neither holds a
    /// solution.</summary>
    private static readonly string[] Skipped = ["bin", "obj", ".git", ".vs"];

    public static IReadOnlyList<FolderEntry> For(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
                return [];

            var solutions = Directory.EnumerateFiles(directory)
                .Where(SolutionFile.IsSolution)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Select(f => new FolderEntry(Path.GetFileName(f), f, false));

            var folders = Directory.EnumerateDirectories(directory)
                .Where(d => !Skipped.Contains(
                    Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Select(d => new FolderEntry(Path.GetFileName(d), d, true));

            return [.. solutions, .. folders];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be read is a folder with nothing to offer. The picker keeps
            // working; the path box is still there.
            return [];
        }
    }

    public static string? ParentOf(string directory)
    {
        try
        {
            return Directory.GetParent(Path.TrimEndingDirectorySeparator(directory))?.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or ArgumentException)
        {
            return null;
        }
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~FolderListingTests`

Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/Web/Chat/FolderListing.cs CodeGenerator.Tests/Web/Chat/FolderListingTests.cs
git commit -m "feat(chat): list folders and solution files for the picker"
```

---

## Task 10: `ChatSession` — the transcript and the dispatch

**Files:**
- Create: `CodeGenerator/Web/Chat/ChatTurn.cs`, `CodeGenerator/Web/Chat/ChatSession.cs`
- Test: `CodeGenerator.Tests/Web/Chat/ChatSessionTests.cs`

**Interfaces:**
- Consumes: `CommandRoute.Classify` and `CommandKind` (Task 3); `Workspace` and `WorkspaceLoad` (Task 4); `OutputBlocks.Parse` (Task 7); `OutputCapture` (Task 2); `CommandColour.Verdict(bool, bool)`; `NaturalLanguagePipeline.Handle`; `HelpCommand.Render(string)`; `HistoryView.Format(List<string>, int)`; `AiCommand.Handle(string, AiLayer)`; `StatusReport`; `ProjectManager`.
- Produces:
  ```csharp
  public enum ChatVerdict { Succeeded, Failed, Unrecognized, Declined }

  public sealed record ChatTurn(
      string Input, CommandKind Kind, ChatVerdict Verdict, IReadOnlyList<OutputBlock> Blocks);

  public sealed class ChatSession
  {
      public ChatSession(Workspace workspace, AiLayer ai, List<string> history);
      public IReadOnlyList<ChatTurn> Turns { get; }
      public bool Running { get; }
      public bool Ended { get; }
      public string? InFlight { get; }
      public event Action? Changed;
      public string Root { get; }
      public int ProjectCount { get; }
      public string? CurrentProject { get; }
      public int RuleCount { get; }
      public string AiState { get; }
      public IReadOnlyList<string> History { get; }
      public Task Send(string input);
      public void ClearTranscript();
      public void End();
      public void Cancel();
      public WorkspaceLoad Load(string path);
  }
  ```

`Question`/`Answer` arrive in Task 11 — do not write them here.

- [ ] **Step 1: Write `ChatTurn`**

Create `CodeGenerator/Web/Chat/ChatTurn.cs`:

```csharp
using CodeGenerator.App;

namespace CodeGenerator.Web.Chat;

/// <summary>
/// How a chat turn turned out.
/// </summary>
/// <remarks>
/// The first three are <c>CommandVerdict</c> under another name, so green, red and amber mean in
/// the browser exactly what they mean in the console: it worked, it was understood and could not be
/// done, the words were not understood.
/// <para>
/// <see cref="Declined"/> is the fourth because a decline is none of those — nothing was attempted.
/// It draws the same amber as <see cref="Unrecognized"/> and is a separate value anyway, because a
/// model that calls a decline "unrecognized" is a model that will eventually print that word.
/// </para>
/// </remarks>
public enum ChatVerdict
{
    Succeeded,

    Failed,

    Unrecognized,

    Declined
}

/// <summary>One exchange: what was typed, what it was, and what came back.</summary>
public sealed record ChatTurn(
    string Input,
    CommandKind Kind,
    ChatVerdict Verdict,
    IReadOnlyList<OutputBlock> Blocks);
```

- [ ] **Step 2: Write the failing tests**

Create `CodeGenerator.Tests/Web/Chat/ChatSessionTests.cs`:

```csharp
using CodeGenerator.App;
using CodeGenerator.Web.Chat;
using CodeIntelligence;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// One message in, one turn out.
/// </summary>
/// <remarks>
/// <see cref="DoNotParallelizeAttribute"/> for two reasons that both bite: <c>OutputCapture</c>
/// swaps <c>Console.Out</c>, which is process-global and refuses to nest, and every command runs
/// against the static <c>ProjectManager</c>.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class ChatSessionTests
{
    [TestMethod]
    public async Task A_command_that_worked_reads_as_succeeded()
    {
        var session = NewSession(out _);

        await session.Send("help");

        Assert.HasCount(1, session.Turns);
        Assert.AreEqual(ChatVerdict.Succeeded, session.Turns[0].Verdict);
        Assert.AreEqual(CommandKind.Help, session.Turns[0].Kind);
        Assert.IsNotEmpty(session.Turns[0].Blocks);
    }

    [TestMethod]
    public async Task Words_the_tool_does_not_know_read_as_unrecognized()
    {
        var session = NewSession(out _);

        await session.Send("wibble the frobnicator");

        Assert.AreEqual(ChatVerdict.Unrecognized, session.Turns[0].Verdict);
    }

    [TestMethod]
    public async Task The_input_is_kept_exactly_as_it_was_typed()
    {
        var session = NewSession(out _);

        await session.Send("  Show Class Order  ");

        Assert.AreEqual("  Show Class Order  ", session.Turns[0].Input);
    }

    [TestMethod]
    public async Task Build_app_batch_and_demo_are_declined_and_never_attempted()
    {
        var session = NewSession(out _);

        foreach (var input in new[] { "build app", "build app: a shop", "batch", "demo" })
        {
            await session.Send(input);

            var turn = session.Turns[^1];

            Assert.AreEqual(ChatVerdict.Declined, turn.Verdict, $"{input} should be declined");
            Assert.IsInstanceOfType<TextBlock>(turn.Blocks[0]);
            StringAssert.Contains(
                ((TextBlock)turn.Blocks[0]).Text, "console",
                $"the decline for {input} must say where to run it");
        }
    }

    [TestMethod]
    public async Task Chat_inside_the_chat_is_declined()
    {
        var session = NewSession(out _);

        await session.Send("chat");

        Assert.AreEqual(ChatVerdict.Declined, session.Turns[0].Verdict);
    }

    [TestMethod]
    public async Task Exit_ends_the_session_without_leaving_a_turn()
    {
        var session = NewSession(out _);

        await session.Send("exit");

        Assert.IsTrue(session.Ended);
        Assert.IsEmpty(session.Turns);
    }

    [TestMethod]
    public async Task Clear_empties_the_transcript_but_not_the_history()
    {
        var session = NewSession(out _);

        await session.Send("help");
        await session.Send("clear");

        Assert.IsEmpty(session.Turns);
        Assert.Contains("help", session.History);
    }

    [TestMethod]
    public async Task A_command_is_recorded_in_history_and_clear_is_not()
    {
        var session = NewSession(out _);

        await session.Send("help");
        await session.Send("clear");
        await session.Send("cls");

        Assert.HasCount(1, session.History);
        Assert.AreEqual("help", session.History[0]);
    }

    [TestMethod]
    public async Task Every_send_raises_changed_so_both_tabs_repaint()
    {
        var session = NewSession(out _);
        var raised = 0;

        session.Changed += () => raised++;

        await session.Send("help");

        Assert.IsGreaterThan(0, raised);
    }

    [TestMethod]
    public async Task A_command_that_throws_becomes_a_failed_turn_rather_than_a_dead_session()
    {
        // Nothing in the catalog is guaranteed to throw, so this asserts the property that
        // matters: whatever happens, Console.Out comes back and the session still works.
        var session = NewSession(out _);
        var before = Console.Out;

        await session.Send("show class \0illegal");

        Assert.AreSame(before, Console.Out);
        Assert.HasCount(1, session.Turns);

        await session.Send("help");

        Assert.HasCount(2, session.Turns);
    }

    [TestMethod]
    public async Task Loading_a_folder_records_a_turn_that_names_the_new_root()
    {
        var session = NewSession(out _);
        var other = Path.Combine(Path.GetTempPath(), "cs-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(other, "Api"));
        File.WriteAllText(
            Path.Combine(other, "Api", "Api.csproj"),
            """<Project Sdk="Microsoft.NET.Sdk"></Project>""");

        var load = session.Load(other);

        Assert.IsTrue(load.Ok);
        Assert.AreEqual(other, session.Root);
        Assert.HasCount(1, session.Turns);
        StringAssert.Contains(((TextBlock)session.Turns[0].Blocks[0]).Text, other);
    }

    [TestMethod]
    public async Task Loading_a_path_that_is_not_there_reports_it_and_keeps_the_root()
    {
        var session = NewSession(out var workspace);
        var before = workspace.Root;

        var load = session.Load(Path.Combine(before, "nowhere-at-all"));

        Assert.IsFalse(load.Ok);
        Assert.AreEqual(before, session.Root);
        Assert.AreEqual(ChatVerdict.Failed, session.Turns[0].Verdict);
    }

    private static ChatSession NewSession(out Workspace workspace)
    {
        var root = Path.Combine(Path.GetTempPath(), "cs-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        workspace = new Workspace(root, projectPathSet: false);

        return new ChatSession(workspace, AiLayer.Create(), []);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~ChatSessionTests`

Expected: FAIL — `ChatSession` does not exist.

- [ ] **Step 4: Write `ChatSession`**

Create `CodeGenerator/Web/Chat/ChatSession.cs`:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Helper;
using CodeGenerationEngine.Models;
using CodeGenerator.App;
using CodeIntelligence;
using NaturalLanguageBridge;

namespace CodeGenerator.Web.Chat;

/// <summary>
/// One conversation with the tool: the transcript, and the running of what is typed into it.
/// </summary>
/// <remarks>
/// A singleton per host, so two tabs looking at the same session see one conversation — the same
/// arrangement <c>BuildSession</c> uses, and for the same reason: a second tab must join the
/// session rather than start a rival one against the same workspace.
/// <para>
/// Commands run one at a time. <c>OutputCapture</c> swaps <c>Console.Out</c>, which is
/// process-global, and the engine's state is static — so a second command starting while the first
/// is mid-flight would capture into the wrong buffer and act on a tree the first one is changing.
/// The page disables its composer in every tab while <see cref="Running"/> is true; nothing is
/// queued, because a queued command is aimed at a workspace that no longer exists by the time it
/// runs.
/// </para>
/// <para>
/// Every command goes through the same pipeline the console REPL uses, with the same
/// classification (<see cref="CommandRoute"/>) and the same verdict
/// (<see cref="CommandColour.Verdict"/>). Nothing here changes what a command prints.
/// </para>
/// </remarks>
public sealed class ChatSession
{
    private readonly Workspace _workspace;
    private readonly AiLayer _ai;
    private readonly List<string> _history;
    private readonly List<ChatTurn> _turns = [];
    private readonly NaturalLanguagePipeline _pipeline = new();
    private readonly Lock _gate = new();

    /// <summary>Carried between commands the way the REPL carries it: `use project` in one
    /// message has to still be in effect in the next.</summary>
    private ProjectMetaData? _currentProject;

    private CancellationTokenSource? _cancellation;

    public ChatSession(Workspace workspace, AiLayer ai, List<string> history)
    {
        _workspace = workspace;
        _ai = ai;
        _history = history;
    }

    public IReadOnlyList<ChatTurn> Turns => _turns;

    /// <summary>Whether a command is in flight. The composer is disabled while it is.</summary>
    public bool Running { get; private set; }

    /// <summary>Whether the session is over — `exit`, or every tab gone.</summary>
    public bool Ended { get; private set; }

    /// <summary>What is running, for the indicator to name.</summary>
    public string? InFlight { get; private set; }

    /// <summary>Raised whenever anything a tab draws has changed.</summary>
    public event Action? Changed;

    public string Root => _workspace.Root;

    public int ProjectCount => _workspace.ProjectCount;

    public string? CurrentProject => ProjectManager.CurrentProject?.ProjectName;

    public int RuleCount => _workspace.Rules.Count;

    public string AiState => _ai.Resolver is not null ? "on" : "off";

    public IReadOnlyList<string> History => _history;

    /// <summary>
    /// Runs one message and adds its turn.
    /// </summary>
    /// <remarks>
    /// The command itself is synchronous and some of it is slow, so it runs on a worker rather than
    /// on the circuit — a page that stops repainting while a command runs cannot show that a
    /// command is running.
    /// </remarks>
    public async Task Send(string input)
    {
        lock (_gate)
        {
            if (Running || Ended)
                return;

            Running = true;
            InFlight = input;
        }

        Raise();

        try
        {
            var kind = CommandRoute.Classify(input);

            switch (kind)
            {
                case CommandKind.Exit:
                    End();
                    return;

                case CommandKind.Clear:
                    ClearTranscript();
                    return;
            }

            Record(input);

            _cancellation = new CancellationTokenSource();

            var turn = await Task.Run(() => Run(input, kind, _cancellation.Token));

            _turns.Add(turn);
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;

            lock (_gate)
            {
                Running = false;
                InFlight = null;
            }

            Raise();
        }
    }

    /// <summary>Best-effort stop. Most of what runs below honours no token, which is why the page
    /// says "stopping" and never says it stopped.</summary>
    public void Cancel() => _cancellation?.Cancel();

    public void ClearTranscript()
    {
        _turns.Clear();
        Raise();
    }

    public void End()
    {
        Ended = true;
        Raise();
    }

    /// <summary>
    /// Re-roots the workspace at a solution, a project or a folder.
    /// </summary>
    /// <remarks>
    /// Recorded as a turn, and worded to say what it did to the whole session: the project
    /// registry is static, so this moved the console the chat was started from too. A page that
    /// quietly changed what the next command acts on would be worse than one that cannot change
    /// it at all.
    /// </remarks>
    public WorkspaceLoad Load(string path)
    {
        var load = _workspace.LoadFrom(path);

        var text = load.Ok
            ? load.SolutionPath is null
                ? $"Loaded {load.Root} — {load.ProjectCount} project(s). "
                  + "The console session is now working here too."
                : $"Loaded {Path.GetFileName(load.SolutionPath)} — {load.ProjectCount} "
                  + $"project(s), rooted at {load.Root}. "
                  + "The console session is now working here too."
            : $"Could not load that: {load.Problem}";

        _turns.Add(new ChatTurn(
            path,
            CommandKind.Pipeline,
            load.Ok ? ChatVerdict.Succeeded : ChatVerdict.Failed,
            [new TextBlock(text)]));

        Raise();

        return load;
    }

    /// <summary>
    /// Runs one classified input and turns what it printed into a turn.
    /// </summary>
    /// <remarks>
    /// The capture is written to the turn whatever happens, including a throw — the same
    /// discipline <c>CommandColour.Run</c> keeps in its finally. A command that crashed after
    /// printing half its work is much easier to diagnose with that half in front of you.
    /// </remarks>
    private ChatTurn Run(string input, CommandKind kind, CancellationToken ct)
    {
        if (Declined(kind) is { } decline)
        {
            return new ChatTurn(
                input, kind, ChatVerdict.Declined, [new TextBlock(decline)]);
        }

        var verdict = ChatVerdict.Failed;
        var captured = string.Empty;

        using var capture = OutputCapture.Start();

        try
        {
            verdict = Execute(input, kind, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ErrorText.For(ex));
            verdict = ChatVerdict.Failed;
        }
        finally
        {
            captured = capture.Text;
        }

        return new ChatTurn(input, kind, verdict, OutputBlocks.Parse(input, captured));
    }

    /// <summary>
    /// The reason a kind cannot be run here, or null if it can.
    /// </summary>
    /// <remarks>
    /// All three of these prompt mid-flight, which is the same reason <c>CommandColour</c> does not
    /// buffer them — a buffered question is a question nobody can see. `chat` is declined too,
    /// because this is it.
    /// </remarks>
    private static string? Declined(CommandKind kind) => kind switch
    {
        CommandKind.BuildApp =>
            "`build app` asks questions as it goes and opens a workflow page of its own. "
            + "Run it from the console.",

        CommandKind.Batch =>
            "`batch` asks you to review what it detected before it runs anything. "
            + "Run it from the console.",

        CommandKind.Demo =>
            "`demo` asks before it writes anything. Run it from the console.",

        CommandKind.Chat => "You are already in the chat.",

        _ => null
    };

    private ChatVerdict Execute(string input, CommandKind kind, CancellationToken ct)
    {
        switch (kind)
        {
            case CommandKind.History:
                Console.WriteLine(HistoryView.Format([.. _history], 20));
                return ChatVerdict.Succeeded;

            case CommandKind.Help:
                // No pager: the page scrolls, and Pager needs a console to measure.
                Console.WriteLine(HelpCommand.Render(input));
                return ChatVerdict.Succeeded;

            case CommandKind.Status:
                Console.WriteLine(StatusReport.Render(
                    _workspace.Root, _workspace.ProjectCount, _workspace.Rules.Count, _ai));
                return ChatVerdict.Succeeded;

            case CommandKind.Ai:
                Console.WriteLine(AiCommand.Handle(input, _ai));
                return ChatVerdict.Succeeded;

            default:
                return Pipeline(input, ct);
        }
    }

    private ChatVerdict Pipeline(string input, CancellationToken ct)
    {
        var project = _currentProject;

        var outcome = _pipeline.Handle(
            input, ref project, _workspace.Rules, _workspace.Root,
            r => ProjectManager.CurrentProject = r,
            onAnalyzed: null,
            aiResolver: null,
            confirmAi: null,
            ct: ct);

        _currentProject = project;

        if (!outcome.Recognized && !string.IsNullOrWhiteSpace(input))
        {
            Console.WriteLine(
                UnrecognizedCommand.Describe(input, CommandSuggester.Closest(input)));
        }

        return CommandColour.Verdict(outcome.Recognized, outcome.Handled) switch
        {
            CommandVerdict.Succeeded => ChatVerdict.Succeeded,
            CommandVerdict.Failed => ChatVerdict.Failed,
            _ => ChatVerdict.Unrecognized
        };
    }

    /// <summary>
    /// Adds the input to the shared history, minus what the console never records.
    /// </summary>
    /// <remarks>
    /// The same list and the same file the console's up-arrow walks, so a command typed here is
    /// found there afterwards. <c>clear</c> and <c>cls</c> are excluded for the reason
    /// <c>CommandLineEditor.ShouldRecordInHistory</c> excludes them: clearing the screen is not a
    /// command you want to repeat by pressing up.
    /// </remarks>
    private void Record(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || ClearCommand.IsClear(input))
            return;

        _history.Add(input);
    }

    private void Raise() => Changed?.Invoke();
}
```

**Two signatures to check before writing this**, because the sketch above guesses at them:

1. `StatusReport.Render(...)` — read `CodeGenerator/App/StatusReport.cs` and call it with whatever it actually takes. If it reads the starter's private state, add an overload that takes the values instead; do not duplicate its text.
2. `ErrorText.For(Exception)` — read `CodeGenerator/App/ErrorText.cs`. If there is no such method, use the starter's `ReportUnexpected` wording rather than inventing new phrasing for a crash.

`aiResolver` and `confirmAi` are `null` here deliberately — Task 11 fills them in.

- [ ] **Step 5: Run the tests**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~ChatSessionTests`

Expected: PASS, 12 tests.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerator/Web/Chat/ChatTurn.cs CodeGenerator/Web/Chat/ChatSession.cs CodeGenerator.Tests/Web/Chat/ChatSessionTests.cs
git commit -m "feat(chat): run a command and turn what it printed into a turn"
```

---

## Task 11: The AI confirmation, asked in the browser

**Files:**
- Modify: `CodeGenerator/Web/Chat/ChatSession.cs`
- Test: `CodeGenerator.Tests/Web/Chat/AiConfirmationTests.cs`

**Interfaces:**
- Consumes: `AiLayer.Resolver` (`IIntentResolver?`), `AiResolution`, `AiDecisionRecorder.ConfirmAndRecord`.
- Produces, added to `ChatSession`:
  ```csharp
  public sealed record AiQuestion(string Text);
  public AiQuestion? Question { get; }
  public void Answer(bool yes);
  ```

**The problem this solves:** `confirmAi` is a `Func<AiResolution, bool>` the pipeline calls **synchronously, from inside itself**. In the console it is a `SelectionPrompt`. Left as it is, a chat session would prompt on a console nobody is looking at and block until someone noticed.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/Web/Chat/AiConfirmationTests.cs`:

```csharp
using CodeGenerator.App;
using CodeGenerator.Web.Chat;
using CodeIntelligence;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// Asking the AI question in the browser instead of on a console nobody is watching.
/// </summary>
/// <remarks>
/// The pipeline calls its confirm delegate synchronously from inside itself, so the answer has to
/// travel back to a blocked worker thread. Every test here is about that thread being released —
/// by an answer, or by the session ending. A path that does not release it hangs the command and
/// the console behind it.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class AiConfirmationTests
{
    [TestMethod]
    public void An_answer_releases_the_waiting_command()
    {
        var session = NewSession();
        var asked = new AiQuestion("Did you mean `list classes`?");

        var wait = Task.Run(() => session.Ask(asked));

        WaitUntil(() => session.Question is not null);

        session.Answer(true);

        Assert.IsTrue(wait.Wait(TimeSpan.FromSeconds(5)), "the answer must release the thread");
        Assert.IsTrue(wait.Result);
        Assert.IsNull(session.Question, "the question must be cleared once answered");
    }

    [TestMethod]
    public void Declining_releases_it_too()
    {
        var session = NewSession();

        var wait = Task.Run(() => session.Ask(new AiQuestion("Did you mean something?")));

        WaitUntil(() => session.Question is not null);

        session.Answer(false);

        Assert.IsTrue(wait.Wait(TimeSpan.FromSeconds(5)));
        Assert.IsFalse(wait.Result);
    }

    [TestMethod]
    public void Ending_the_session_releases_it_with_no()
    {
        // The path that would otherwise hang forever: the tab is closed while the question is on
        // screen, the host abandons the session, and nobody is left to answer.
        var session = NewSession();

        var wait = Task.Run(() => session.Ask(new AiQuestion("Did you mean something?")));

        WaitUntil(() => session.Question is not null);

        session.End();

        Assert.IsTrue(
            wait.Wait(TimeSpan.FromSeconds(5)),
            "abandoning the session must release the thread, or the console waits forever");

        Assert.IsFalse(wait.Result, "an unanswered question is a no");
    }

    [TestMethod]
    public void Answering_when_nothing_was_asked_does_nothing()
    {
        var session = NewSession();

        session.Answer(true);

        Assert.IsNull(session.Question);
    }

    [TestMethod]
    public void The_question_raises_changed_so_every_tab_shows_it()
    {
        var session = NewSession();
        var raised = 0;

        session.Changed += () => raised++;

        var wait = Task.Run(() => session.Ask(new AiQuestion("Did you mean something?")));

        WaitUntil(() => session.Question is not null);

        Assert.IsGreaterThan(0, raised);

        session.Answer(false);
        wait.Wait(TimeSpan.FromSeconds(5));
    }

    private static void WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (!condition() && DateTime.UtcNow < deadline)
            Thread.Sleep(10);

        Assert.IsTrue(condition(), "the condition never became true");
    }

    private static ChatSession NewSession()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        return new ChatSession(
            new Workspace(root, projectPathSet: false), AiLayer.Create(), []);
    }
}
```

`Ask` is `internal` on `ChatSession` and reachable from the tests through the existing `InternalsVisibleTo CodeGenerator.Tests` in `CodeGenerator.csproj`. Testing it directly is deliberate: driving it through the pipeline would need the AI model loaded and an input it fails to recognise, which makes the test about the model rather than about the thread being released.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~AiConfirmationTests`

Expected: FAIL — `AiQuestion`, `Ask`, `Answer` and `Question` do not exist.

- [ ] **Step 3: Add the question to `ChatSession`**

Add to `CodeGenerator/Web/Chat/ChatTurn.cs` (it is the file that already holds the small chat records):

```csharp
/// <summary>A question the session is waiting on an answer to.</summary>
public sealed record AiQuestion(string Text);
```

Add to `ChatSession`:

```csharp
    private readonly Lock _questionGate = new();

    private TaskCompletionSource<bool>? _answer;

    /// <summary>The question every tab is showing, or null.</summary>
    public AiQuestion? Question { get; private set; }

    /// <summary>
    /// Puts a question on every tab and blocks until one of them answers.
    /// </summary>
    /// <remarks>
    /// Called from inside the pipeline, on the worker thread, because that is how
    /// <c>confirmAi</c> is shaped — a synchronous <c>Func&lt;AiResolution, bool&gt;</c> invoked
    /// from deep inside the command. Blocking here is the point: the command genuinely cannot
    /// continue until the answer arrives.
    /// <para>
    /// The one thing that must never happen is this thread waiting forever. A tab closed while the
    /// question is on screen ends the session (the host abandons it after its grace period), and
    /// <see cref="End"/> releases this with a no. Without that, the command hangs and the console
    /// that started the chat hangs behind it.
    /// </para>
    /// </remarks>
    internal bool Ask(AiQuestion question)
    {
        TaskCompletionSource<bool> pending;

        lock (_questionGate)
        {
            if (Ended)
                return false;

            pending = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _answer = pending;
            Question = question;
        }

        Raise();

        var answered = pending.Task.GetAwaiter().GetResult();

        lock (_questionGate)
        {
            Question = null;
            _answer = null;
        }

        Raise();

        return answered;
    }

    /// <summary>Answers the question on screen. Does nothing if there is none.</summary>
    public void Answer(bool yes)
    {
        lock (_questionGate)
            _answer?.TrySetResult(yes);
    }
```

Change `End` so it releases a waiting question:

```csharp
    public void End()
    {
        Ended = true;

        // Before the repaint, and with a no: a question nobody is left to answer must not hold
        // the command — and the console behind it — open.
        lock (_questionGate)
            _answer?.TrySetResult(false);

        Raise();
    }
```

Then wire it into `Pipeline`, replacing the two nulls:

```csharp
        var outcome = _pipeline.Handle(
            input, ref project, _workspace.Rules, _workspace.Root,
            r => ProjectManager.CurrentProject = r,
            onAnalyzed: null,
            aiResolver: _ai.Resolver,
            confirmAi: resolution => AiDecisionRecorder.ConfirmAndRecord(
                resolution, input, _ai.DecisionLog,
                r => Ask(new AiQuestion(AiConfirmPrompt.Title(r)))),
            ct: ct);
```

Check `AiConfirmPrompt.Title(AiResolution)` exists and is reachable (it is used at `AiConfirmPrompt.cs:35`); if it is private, add an internal accessor rather than re-wording the question — the console and the page must ask the same thing.

- [ ] **Step 4: Run the tests**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~AiConfirmationTests|FullyQualifiedName~ChatSessionTests"`

Expected: PASS, 17 tests.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/Web/Chat/ChatSession.cs CodeGenerator/Web/Chat/ChatTurn.cs CodeGenerator.Tests/Web/Chat/AiConfirmationTests.cs
git commit -m "feat(chat): ask the AI question in the browser, and always release the thread"
```

---

## Task 12: `ChatWebHost` — `/chat`, and a wait that always ends

**Files:**
- Create: `CodeGenerator/Web/Chat/ChatWebHost.cs`
- Test: `CodeGenerator.Tests/Web/Chat/ChatWebHostTests.cs`

**Interfaces:**
- Consumes: `LoopbackHost` (Task 5), `ChatSession` (Task 10), `CircuitWatch`.
- Produces:
  ```csharp
  internal sealed class ChatWebHost : IDisposable
  {
      public static bool TryStart(ChatSession session, out ChatWebHost host);
      public string Url { get; }
      public string SessionToken { get; }
      public CircuitWatch Circuits { get; }
      public IServiceProvider Services { get; }
      public void Run(ChatSession session, CancellationToken ct, TimeSpan? grace = null);
      public void Dispose();
  }
  ```

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/Web/Chat/ChatWebHostTests.cs`:

```csharp
using System.Net;
using CodeGenerator.App;
using CodeGenerator.Web.Chat;
using CodeIntelligence;
using Microsoft.Extensions.DependencyInjection;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// The chat host, without a browser.
/// </summary>
/// <remarks>
/// The two properties that matter are the same two the build host has: the route is not served to
/// a visitor without the token, and the wait always ends. A CLI blocked forever on a page nobody
/// has open is the worst failure this feature can have.
/// <para>
/// <see cref="DoNotParallelizeAttribute"/>: every test here starts a real Kestrel.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public class ChatWebHostTests
{
    private static readonly TimeSpan ReturnsWithin = TimeSpan.FromSeconds(60);

    [TestMethod]
    public async Task Refuses_a_visitor_with_no_token()
    {
        using var host = Start(out _);
        using var client = new HttpClient();

        var response = await client.GetAsync(
            new Uri(host.Url).GetLeftPart(UriPartial.Authority) + "/chat");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Serves_the_page_to_a_visitor_carrying_the_token()
    {
        using var host = Start(out _);
        using var client = new HttpClient();

        var response = await client.GetAsync(host.Url);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public void The_session_is_a_singleton_so_a_second_tab_joins_it()
    {
        using var host = Start(out var session);

        Assert.AreSame(session, host.Services.GetRequiredService<ChatSession>());
    }

    [TestMethod]
    public void Returns_when_the_session_ends()
    {
        using var host = Start(out var session);

        var wait = Task.Run(() => host.Run(session, CancellationToken.None));

        session.End();

        Assert.IsTrue(wait.Wait(ReturnsWithin), "the wait must end when the session does");
    }

    [TestMethod]
    public void Returns_when_cancelled()
    {
        using var host = Start(out var session);
        using var cancellation = new CancellationTokenSource();

        var wait = Task.Run(() => host.Run(session, cancellation.Token));

        cancellation.Cancel();

        Assert.IsTrue(wait.Wait(ReturnsWithin), "Ctrl-C must end the wait");
        Assert.IsTrue(session.Ended, "and it must end the session, not just the wait");
    }

    [TestMethod]
    public void Returns_when_every_tab_has_been_gone_for_the_grace_period()
    {
        using var host = Start(out var session);

        // A tab arrived and left. With a zero grace the next poll treats it as abandoned.
        host.Circuits.Opened(DateTimeOffset.UtcNow);
        host.Circuits.Closed(DateTimeOffset.UtcNow);

        var wait = Task.Run(() => host.Run(session, CancellationToken.None, TimeSpan.Zero));

        Assert.IsTrue(wait.Wait(ReturnsWithin), "an abandoned page must end the command");
        Assert.IsTrue(session.Ended);
    }

    private static ChatWebHost Start(out ChatSession session)
    {
        var root = Path.Combine(Path.GetTempPath(), "cw-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        session = new ChatSession(
            new Workspace(root, projectPathSet: false), AiLayer.Create(), []);

        Assert.IsTrue(ChatWebHost.TryStart(session, out var host));

        return host;
    }
}
```

`Serves_the_page_to_a_visitor_carrying_the_token` needs `ChatPage.razor` to exist. Either write this test last in the task, after Task 13, or expect it red until then — say which in the commit.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~ChatWebHostTests`

Expected: FAIL — `ChatWebHost` does not exist.

- [ ] **Step 3: Write the implementation**

Create `CodeGenerator/Web/Chat/ChatWebHost.cs`:

```csharp
using CodeGenerator.Web.Workflow;
using Microsoft.Extensions.DependencyInjection;

namespace CodeGenerator.Web.Chat;

/// <summary>
/// Serves the chat page for the length of one <c>chat</c> command.
/// </summary>
/// <remarks>
/// The mechanics are <see cref="LoopbackHost"/>'s; what is here is the waiting. It polls rather
/// than awaiting an event because the session is mutated from a Blazor circuit on another thread,
/// and a poll costs nothing next to a conversation someone is typing into.
/// <para>
/// Every exit is bounded, including the one where the person walked away: a tab closed and never
/// replaced ends the session, or the console would wait on a page nobody has open.
/// </para>
/// </remarks>
internal sealed class ChatWebHost : IDisposable
{
    /// <summary>The route the chat page is served on.</summary>
    private const string Route = "chat";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// How long every tab must have been gone before the session is treated as abandoned.
    /// </summary>
    /// <remarks>
    /// Long enough that a refresh — circuit down, new circuit up about a second later — is not
    /// mistaken for a closed tab, and short enough that someone who closed the tab is not left
    /// wondering why their prompt has not come back. Same number as the build workflow, for the
    /// same reasons.
    /// </remarks>
    private static readonly TimeSpan AbandonGrace = TimeSpan.FromSeconds(10);

    private readonly LoopbackHost _host;

    private ChatWebHost(LoopbackHost host) => _host = host;

    public string Url => _host.Url;

    public string SessionToken => _host.SessionToken;

    public CircuitWatch Circuits => _host.Circuits;

    public IServiceProvider Services => _host.Services;

    public static bool TryStart(ChatSession session, out ChatWebHost host)
    {
        host = null!;

        // Singleton, so a second tab joins the conversation rather than starting a rival one
        // against the same workspace.
        if (!LoopbackHost.TryStart(Route, s => s.AddSingleton(session), out var loopback))
            return false;

        host = new ChatWebHost(loopback);

        return true;
    }

    public void Run(ChatSession session, CancellationToken ct, TimeSpan? grace = null)
    {
        var window = grace ?? AbandonGrace;

        while (!session.Ended)
        {
            if (ct.IsCancellationRequested)
            {
                session.End();
                break;
            }

            if (Circuits.ShouldAbandon(window, DateTimeOffset.UtcNow))
            {
                session.End();
                break;
            }

            Thread.Sleep(PollInterval);
        }
    }

    public void Dispose() => _host.Dispose();
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~ChatWebHostTests`

Expected: PASS except `Serves_the_page_to_a_visitor_carrying_the_token`, which needs Task 13.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/Web/Chat/ChatWebHost.cs CodeGenerator.Tests/Web/Chat/ChatWebHostTests.cs
git commit -m "feat(chat): serve /chat behind a token, and always stop waiting"
```

---

## Task 13: The page and the transcript

**Files:**
- Create: `CodeGenerator/Web/Components/ChatPage.razor`, `CodeGenerator/Web/Components/ChatTranscript.razor`, `CodeGenerator/wwwroot/chat.css`
- Modify: `CodeGenerator/Web/Components/App.razor`
- Test: `CodeGenerator.Tests/Web/Chat/ChatPageTests.cs`

**Interfaces:**
- Consumes: `ChatSession`, `ChatTurn`, `ChatVerdict`, `OutputBlock` and its four subtypes, `CSharpTokens.Scan`.
- Produces: `ChatTranscript` with `[Parameter] public IReadOnlyList<ChatTurn> Turns { get; set; }`.

**Naming:** the page is `ChatPage.razor`, not `Chat.razor`. A `Chat` component class next to the `CodeGenerator.Web.Chat` namespace is an ambiguity inside `Components`.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/Web/Chat/ChatPageTests.cs`:

```csharp
using Bunit;
using CodeGenerator.App;
using CodeGenerator.Web.Chat;
using CodeGenerator.Web.Components;
using CodeIntelligence;
using Microsoft.Extensions.DependencyInjection;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// The page, as a page: that a turn is drawn with the verdict it earned, and that two tabs stay in
/// step.
/// </summary>
/// <remarks>
/// What the session decides is asserted in <c>ChatSessionTests</c> without a renderer. What is
/// here is only what needs one.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class ChatPageTests
{
    [TestMethod]
    public void A_turn_is_drawn_with_its_verdict_class()
    {
        using var context = new Bunit.TestContext();

        var turns = new List<ChatTurn>
        {
            new("list classes", CommandKind.Pipeline, ChatVerdict.Succeeded,
                [new TextBlock("Customer")]),
            new("wibble", CommandKind.Pipeline, ChatVerdict.Unrecognized,
                [new TextBlock("not understood")]),
            new("build app", CommandKind.BuildApp, ChatVerdict.Declined,
                [new TextBlock("Run it from the console.")]),
        };

        var rendered = context.Render<ChatTranscript>(
            p => p.Add(c => c.Turns, turns));

        Assert.IsNotNull(rendered.Find(".turn-succeeded"));
        Assert.IsNotNull(rendered.Find(".turn-unrecognized"));
        Assert.IsNotNull(rendered.Find(".turn-declined"));
    }

    [TestMethod]
    public void The_input_is_shown_as_it_was_typed()
    {
        using var context = new Bunit.TestContext();

        var rendered = context.Render<ChatTranscript>(p => p.Add(
            c => c.Turns,
            new List<ChatTurn>
            {
                new("Show Class Order", CommandKind.Pipeline, ChatVerdict.Succeeded, [])
            }));

        StringAssert.Contains(rendered.Find(".turn-said").TextContent, "Show Class Order");
    }

    [TestMethod]
    public void A_code_block_is_drawn_as_code_with_a_copy_button()
    {
        using var context = new Bunit.TestContext();

        var rendered = context.Render<ChatTranscript>(p => p.Add(
            c => c.Turns,
            new List<ChatTurn>
            {
                new("show class Order", CommandKind.Pipeline, ChatVerdict.Succeeded,
                    [new CodeBlock("Order", "public class Order { }")])
            }));

        Assert.IsNotNull(rendered.Find("pre.block-code"));
        Assert.IsNotNull(rendered.Find(".block-copy"));
        StringAssert.Contains(rendered.Markup, "public");
    }

    [TestMethod]
    public void A_table_block_is_drawn_as_a_table()
    {
        using var context = new Bunit.TestContext();

        var rendered = context.Render<ChatTranscript>(p => p.Add(
            c => c.Turns,
            new List<ChatTurn>
            {
                new("list properties of Order", CommandKind.Pipeline, ChatVerdict.Succeeded,
                    [new TableBlock(
                        "Properties in Order", ["Type", "Name"], [["int", "Id"]])])
            }));

        Assert.HasCount(2, rendered.FindAll("th"));
        Assert.HasCount(2, rendered.FindAll("td"));
    }

    [TestMethod]
    public void A_second_circuit_repaints_when_the_first_one_sends()
    {
        // Two tabs on one conversation. The session raises Changed; both pages must redraw, or the
        // second tab shows a transcript that stopped growing.
        using var first = NewContext(out var session);
        using var second = NewContext(session);

        var one = first.Render<ChatPage>();
        var two = second.Render<ChatPage>();

        session.Send("help").Wait();

        one.WaitForAssertion(() => Assert.IsNotNull(one.Find(".turn")));
        two.WaitForAssertion(() => Assert.IsNotNull(two.Find(".turn")));
    }

    private static Bunit.TestContext NewContext(out ChatSession session)
    {
        var root = Path.Combine(Path.GetTempPath(), "cp-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        session = new ChatSession(
            new Workspace(root, projectPathSet: false), AiLayer.Create(), []);

        return NewContext(session);
    }

    private static Bunit.TestContext NewContext(ChatSession session)
    {
        var context = new Bunit.TestContext();
        context.Services.AddSingleton(session);
        return context;
    }
}
```

Check how the existing `WorkflowPageTests` builds its bUnit context (`PageFor(...)`) and follow it exactly — the namespace of `TestContext` and the render API differ between bUnit versions, and that file is the working reference for 2.9.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~ChatPageTests`

Expected: FAIL — no `ChatPage`, no `ChatTranscript`.

- [ ] **Step 3: Write `ChatTranscript.razor`**

```razor
@* One turn per exchange: what was typed, then what came back, tinted by how it turned out. *@

<div class="transcript">
    @foreach (var turn in Turns)
    {
        <div class="turn turn-@Class(turn.Verdict)">
            <div class="turn-said">@turn.Input</div>

            @foreach (var block in turn.Blocks)
            {
                @switch (block)
                {
                    case CodeBlock code:
                        <div class="block-heading">
                            @code.Heading
                            <button class="block-copy" @onclick="() => Copy(code.Code)">Copy</button>
                        </div>
                        <pre class="block-code">@foreach (var token in CSharpTokens.Scan(code.Code))
{<span class="tok-@token.Kind.ToString().ToLowerInvariant()">@token.Text</span>}</pre>
                        break;

                    case TableBlock table:
                        <div class="block-heading">@table.Heading</div>
                        <div class="block-scroll">
                            <table class="block-table">
                                <thead>
                                    <tr>
                                        @foreach (var header in table.Headers)
                                        {
                                            <th>@header</th>
                                        }
                                    </tr>
                                </thead>
                                <tbody>
                                    @foreach (var row in table.Rows)
                                    {
                                        <tr>
                                            @foreach (var cell in row)
                                            {
                                                <td>@cell</td>
                                            }
                                        </tr>
                                    }
                                </tbody>
                            </table>
                        </div>
                        break;

                    case ListBlock list:
                        <div class="block-heading">@list.Heading</div>
                        <ul class="block-list">
                            @foreach (var item in list.Items)
                            {
                                <li>@item</li>
                            }
                        </ul>
                        break;

                    case TextBlock text:
                        <pre class="block-text">@text.Text</pre>
                        break;
                }
            }
        </div>
    }
</div>

@code {
    [Parameter, EditorRequired]
    public IReadOnlyList<ChatTurn> Turns { get; set; } = [];

    /// <summary>The verdict as a CSS suffix. Declined and Unrecognized draw the same amber and
    /// stay separate values, because the model must not call a decline "unrecognized".</summary>
    private static string Class(ChatVerdict verdict) => verdict switch
    {
        ChatVerdict.Succeeded => "succeeded",
        ChatVerdict.Failed => "failed",
        ChatVerdict.Declined => "declined",
        _ => "unrecognized"
    };

    private Task Copy(string code) => Task.CompletedTask;
}
```

`Copy` needs `IJSRuntime` and a small inline module; wire it in Step 5. Add `@using CodeGenerator.Web.Chat` and `@using CodeGenerator.App` to `Web/Components/_Imports.razor` rather than to each file.

- [ ] **Step 4: Write `ChatPage.razor`**

```razor
@page "/chat"
@implements IDisposable
@inject ChatSession Session

<PageTitle>Chat — SmartCodeGenerator</PageTitle>

<div class="chat">
    <ChatTranscript Turns="Session.Turns" />

    @if (Session.Running)
    {
        <div class="working">
            <span>@(_stopping ? "Stopping" : "Working on") @Session.InFlight…</span>
            <button class="working-stop" disabled="@_stopping" @onclick="Stop">Stop</button>
        </div>
    }
</div>

@code {
    protected override void OnInitialized() => Session.Changed += Repaint;

    public void Dispose() => Session.Changed -= Repaint;

    /// <summary>
    /// Repaints this circuit when the session changes.
    /// </summary>
    /// <remarks>
    /// InvokeAsync because the event is raised from a worker thread — the command runs off the
    /// circuit so the page can keep drawing while it does. StateHasChanged called directly from
    /// that thread throws.
    /// </remarks>
    private void Repaint() => InvokeAsync(StateHasChanged);

    private bool _stopping;

    /// <summary>
    /// Asks the running command to stop.
    /// </summary>
    /// <remarks>
    /// Best-effort, and the label says so rather than claiming success. Plenty of what runs below
    /// here is synchronous and takes no token; a button that said "Stopped" would be lying about
    /// half the catalog.
    /// </remarks>
    private void Stop()
    {
        _stopping = true;
        Session.Cancel();
    }
}
```

`_stopping` is reset when the next command starts — add `_stopping = false;` at the top of `Send`.

The composer, picker and strip are added by Tasks 14 and 15. Leave placeholders out — add them when they exist.

**Also in this task: collapse long output.** A `TextBlock` or `CodeBlock` over 40 lines renders truncated with a `.block-more` button that expands it. Add to `ChatTranscript.razor` a `HashSet<OutputBlock> _expanded`, render `block.Text.Split('\n')` truncated to 40 when the block is not in it, and a button that adds it. Two tests in `ChatPageTests`:

```csharp
    [TestMethod]
    public void Long_output_is_collapsed_until_asked_for()
    {
        using var context = new Bunit.TestContext();
        var long_ = string.Join("\n", Enumerable.Range(0, 200).Select(i => $"line {i}"));

        var rendered = context.Render<ChatTranscript>(p => p.Add(
            c => c.Turns,
            new List<ChatTurn>
            {
                new("list classes", CommandKind.Pipeline, ChatVerdict.Succeeded,
                    [new TextBlock(long_)])
            }));

        Assert.DoesNotContain("line 199", rendered.Markup);

        rendered.Find(".block-more").Click();

        StringAssert.Contains(rendered.Markup, "line 199");
    }

    [TestMethod]
    public void Short_output_has_no_show_all_button()
    {
        using var context = new Bunit.TestContext();

        var rendered = context.Render<ChatTranscript>(p => p.Add(
            c => c.Turns,
            new List<ChatTurn>
            {
                new("status", CommandKind.Pipeline, ChatVerdict.Succeeded,
                    [new TextBlock("one line")])
            }));

        Assert.IsEmpty(rendered.FindAll(".block-more"));
    }
```

Add `.working-stop` and `.block-more` to `chat.css`.

- [ ] **Step 5: Write `chat.css` and the copy helper**

Create `CodeGenerator/wwwroot/chat.css`. Self-contained, no font import. Reuse the `:root` variables `workflow.css` defines by copying the block — the two pages are served by the same host and both stylesheets are linked. Define at minimum:

- `.chat` — column layout, `max-width: 1100px`, centred.
- `.turn-said` — the typed line, distinct from output.
- `.turn-succeeded`, `.turn-failed`, `.turn-unrecognized`, `.turn-declined` — a left border in `--done`, `--failed`, and amber for the last two.
- `.block-text`, `.block-code` — monospace, `overflow-x: auto`.
- `.block-scroll` — `overflow-x: auto` so a wide table scrolls inside itself and the page body never scrolls sideways.
- `.tok-keyword`, `.tok-text`, `.tok-comment`, `.tok-number` — four colours, nothing for `.tok-plain`.
- `.working` — the indicator.

Then in `App.razor`, link it and give the title per page:

```razor
    <title>SmartCodeGenerator</title>
    <base href="/" />
    <link rel="stylesheet" href="workflow.css" />
    <link rel="stylesheet" href="chat.css" />
```

The existing `<HeadOutlet @rendermode="InteractiveServer" />` stays exactly as it is — it is what lets `<PageTitle>` reach the tab, and a statically rendered outlet never hears the update. Do not remove or change its render mode.

For `Copy`, add an inline script to `App.razor` and call it:

```razor
    <script>
        window.chatCopy = text => navigator.clipboard?.writeText(text);
    </script>
```

and in `ChatTranscript.razor`:

```csharp
    [Inject]
    public IJSRuntime Js { get; set; } = default!;

    private async Task Copy(string code)
    {
        try
        {
            await Js.InvokeVoidAsync("chatCopy", code);
        }
        catch (JSException)
        {
            // No clipboard permission. The code is on screen and selectable; nothing to report.
        }
    }
```

`await`, not a discarded task — a dropped `@onclick` task never repaints and swallows its exception.

- [ ] **Step 6: Run the tests**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ChatPageTests|FullyQualifiedName~ChatWebHostTests"`

Expected: PASS, including `Serves_the_page_to_a_visitor_carrying_the_token` now that the page exists.

- [ ] **Step 7: Commit**

```bash
git add CodeGenerator/Web/Components/ChatPage.razor CodeGenerator/Web/Components/ChatTranscript.razor CodeGenerator/Web/Components/App.razor CodeGenerator/Web/Components/_Imports.razor CodeGenerator/wwwroot/chat.css CodeGenerator.Tests/Web/Chat/ChatPageTests.cs
git commit -m "feat(chat): the page and its transcript"
```

---

## Task 14: The composer — autocomplete, and the Enter rule

**Files:**
- Create: `CodeGenerator/Web/Components/ChatComposer.razor`
- Modify: `CodeGenerator/Web/Components/ChatPage.razor`
- Test: `CodeGenerator.Tests/Web/Chat/ChatComposerTests.cs`

**Interfaces:**
- Consumes: `CommandCatalog.Matches` (Task 8), `CommandSuggester.Closest`.
- Produces: `ChatComposer` with
  ```csharp
  [Parameter] public bool Disabled { get; set; }
  [Parameter] public IReadOnlyList<string> History { get; set; }
  [Parameter] public EventCallback<string> OnSend { get; set; }
  ```

**The one rule to get right:** while the dropdown is open, Enter **inserts** and the arrow keys move the selection. Closed, Enter **sends** and up-arrow recalls history.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/Web/Chat/ChatComposerTests.cs`:

```csharp
using Bunit;
using CodeGenerator.Web.Components;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// Typing into the chat.
/// </summary>
/// <remarks>
/// The Enter rule is the whole point of these tests. A composer where Enter sometimes sends and
/// sometimes completes, and the user cannot tell which, is worse than one with no completion at
/// all.
/// </remarks>
[TestClass]
public class ChatComposerTests
{
    [TestMethod]
    public void Enter_sends_when_no_dropdown_is_open()
    {
        using var context = new Bunit.TestContext();
        string? sent = null;

        var composer = context.Render<ChatComposer>(p => p
            .Add(c => c.OnSend, (string s) => sent = s));

        composer.Find("textarea").Change("status");
        composer.Find("textarea").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.AreEqual("status", sent);
    }

    [TestMethod]
    public void Shift_enter_does_not_send()
    {
        using var context = new Bunit.TestContext();
        var sent = 0;

        var composer = context.Render<ChatComposer>(p => p
            .Add(c => c.OnSend, (string _) => sent++));

        composer.Find("textarea").Change("status");
        composer.Find("textarea").KeyDown(
            new KeyboardEventArgs { Key = "Enter", ShiftKey = true });

        Assert.AreEqual(0, sent);
    }

    [TestMethod]
    public void Typing_a_command_prefix_opens_the_dropdown()
    {
        using var context = new Bunit.TestContext();

        var composer = context.Render<ChatComposer>();

        composer.Find("textarea").Input("list");

        Assert.IsNotEmpty(composer.FindAll(".suggestion"));
    }

    [TestMethod]
    public void Enter_inserts_the_selection_while_the_dropdown_is_open()
    {
        using var context = new Bunit.TestContext();
        var sent = 0;

        var composer = context.Render<ChatComposer>(p => p
            .Add(c => c.OnSend, (string _) => sent++));

        composer.Find("textarea").Input("list");
        composer.Find("textarea").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.AreEqual(0, sent, "Enter must complete, not send, while the dropdown is open");
        Assert.IsEmpty(composer.FindAll(".suggestion"), "and it must close the dropdown");
        StringAssert.StartsWith(composer.Find("textarea").GetAttribute("value") ?? "", "list");
    }

    [TestMethod]
    public void Escape_closes_the_dropdown_and_then_enter_sends()
    {
        using var context = new Bunit.TestContext();
        string? sent = null;

        var composer = context.Render<ChatComposer>(p => p
            .Add(c => c.OnSend, (string s) => sent = s));

        composer.Find("textarea").Input("list");
        composer.Find("textarea").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.IsEmpty(composer.FindAll(".suggestion"));

        composer.Find("textarea").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.AreEqual("list", sent);
    }

    [TestMethod]
    public void Up_recalls_history_when_the_dropdown_is_closed()
    {
        using var context = new Bunit.TestContext();

        var composer = context.Render<ChatComposer>(p => p
            .Add(c => c.History, new List<string> { "status", "list classes" }));

        composer.Find("textarea").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });

        Assert.AreEqual("list classes", composer.Find("textarea").GetAttribute("value"));
    }

    [TestMethod]
    public void The_composer_is_disabled_while_a_command_runs()
    {
        using var context = new Bunit.TestContext();

        var composer = context.Render<ChatComposer>(p => p.Add(c => c.Disabled, true));

        Assert.IsTrue(composer.Find("textarea").HasAttribute("disabled"));
    }

    [TestMethod]
    public void Sending_clears_the_box()
    {
        using var context = new Bunit.TestContext();

        var composer = context.Render<ChatComposer>(p => p
            .Add(c => c.OnSend, (string _) => { }));

        composer.Find("textarea").Change("status");
        composer.Find("textarea").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.AreEqual("", composer.Find("textarea").GetAttribute("value"));
    }
}
```

Add `using Microsoft.AspNetCore.Components.Web;` for `KeyboardEventArgs`.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~ChatComposerTests`

Expected: FAIL — no `ChatComposer`.

- [ ] **Step 3: Write `ChatComposer.razor`**

```razor
@* The input, and the dropdown over the command catalog. *@

<div class="composer">
    @if (_suggestions.Count > 0)
    {
        <ul class="suggestions">
            @for (var i = 0; i < _suggestions.Count; i++)
            {
                var index = i;
                var suggestion = _suggestions[i];

                <li class="suggestion @(index == _selected ? "suggestion-on" : "")"
                    @onclick="() => Insert(suggestion.Command)">
                    <span class="suggestion-command">@suggestion.Command</span>
                    <span class="suggestion-description">@suggestion.Description</span>
                    <span class="suggestion-example">@suggestion.Example</span>
                </li>
            }
        </ul>
    }

    <textarea value="@_text"
              disabled="@Disabled"
              rows="2"
              placeholder="Type a command…"
              @oninput="OnInput"
              @onkeydown="OnKeyDown"></textarea>
</div>

@code {
    private string _text = string.Empty;
    private int _selected;
    private int _historyAt = -1;
    private IReadOnlyList<CommandInfo> _suggestions = [];

    [Parameter]
    public bool Disabled { get; set; }

    [Parameter]
    public IReadOnlyList<string> History { get; set; } = [];

    [Parameter]
    public EventCallback<string> OnSend { get; set; }

    private void OnInput(ChangeEventArgs e)
    {
        _text = e.Value?.ToString() ?? string.Empty;
        _suggestions = CommandCatalog.Matches(_text);
        _selected = 0;
    }

    /// <summary>
    /// The keyboard, and the one rule this component exists to keep.
    /// </summary>
    /// <remarks>
    /// While the dropdown is open, Enter completes and the arrows move the selection. Closed, Enter
    /// sends and Up recalls history. Both meanings for one key is unavoidable — this is the
    /// arrangement every editor uses — but which one applies has to be decided by something the
    /// user can see, and the open dropdown is that thing.
    /// </remarks>
    private async Task OnKeyDown(KeyboardEventArgs e)
    {
        if (Disabled)
            return;

        var open = _suggestions.Count > 0;

        switch (e.Key)
        {
            case "Escape" when open:
                _suggestions = [];
                return;

            case "ArrowDown" when open:
                _selected = Math.Min(_selected + 1, _suggestions.Count - 1);
                return;

            case "ArrowUp" when open:
                _selected = Math.Max(_selected - 1, 0);
                return;

            case "Tab" when open:
            case "Enter" when open && !e.ShiftKey:
                Insert(_suggestions[_selected].Command);
                return;

            case "ArrowUp":
                Recall(-1);
                return;

            case "ArrowDown":
                Recall(1);
                return;

            case "Enter" when !e.ShiftKey:
                await Send();
                return;
        }
    }

    private void Insert(string command)
    {
        _text = command;
        _suggestions = [];
        _selected = 0;
    }

    /// <summary>
    /// Walks the shared history. Newest first going up, which is what every shell does.
    /// </summary>
    private void Recall(int direction)
    {
        if (History.Count == 0)
            return;

        _historyAt = _historyAt < 0
            ? History.Count - 1
            : Math.Clamp(_historyAt - direction, 0, History.Count - 1);

        _text = History[_historyAt];
    }

    private async Task Send()
    {
        var text = _text;

        // Cleared before the callback: the command runs on a worker and the page repaints while it
        // does, so a box still holding the sent text reads as a command that did not go.
        _text = string.Empty;
        _suggestions = [];
        _historyAt = -1;

        await OnSend.InvokeAsync(text);
    }
}
```

- [ ] **Step 4: Add it to the page**

In `ChatPage.razor`, below the transcript:

```razor
    <ChatComposer Disabled="Session.Running || Session.Ended"
                  History="Session.History"
                  OnSend="Send" />
```

and:

```csharp
    /// <summary>
    /// Sends one message.
    /// </summary>
    /// <remarks>
    /// Awaited, not discarded: a dropped task never repaints and swallows whatever it threw.
    /// </remarks>
    private async Task Send(string input) => await Session.Send(input);
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~ChatComposerTests`

Expected: PASS, 8 tests.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerator/Web/Components/ChatComposer.razor CodeGenerator/Web/Components/ChatPage.razor CodeGenerator.Tests/Web/Chat/ChatComposerTests.cs
git commit -m "feat(chat): the composer, its dropdown, and one Enter rule"
```

---

## Task 15: The picker, the context strip, and the AI question

**Files:**
- Create: `CodeGenerator/Web/Components/SolutionPicker.razor`, `CodeGenerator/Web/Components/ContextStrip.razor`
- Modify: `CodeGenerator/Web/Components/ChatPage.razor`, `CodeGenerator/wwwroot/chat.css`
- Test: `CodeGenerator.Tests/Web/Chat/SolutionPickerTests.cs`

**Interfaces:**
- Consumes: `FolderListing.For`, `FolderListing.ParentOf` (Task 9); `ChatSession.Load`, `.Root`, `.ProjectCount`, `.CurrentProject`, `.RuleCount`, `.AiState`, `.Question`, `.Answer`.
- Produces: `SolutionPicker` and `ContextStrip`, both taking `[Parameter] public ChatSession Session { get; set; }`.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/Web/Chat/SolutionPickerTests.cs`:

```csharp
using Bunit;
using CodeGenerator.App;
using CodeGenerator.Web.Chat;
using CodeGenerator.Web.Components;
using CodeIntelligence;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// Choosing what the next command acts on, and being told what that is.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SolutionPickerTests
{
    [TestMethod]
    public void The_strip_names_the_root_and_what_is_selected()
    {
        using var context = new Bunit.TestContext();
        var session = NewSession(out var root);

        var strip = context.Render<ContextStrip>(p => p.Add(c => c.Session, session));

        StringAssert.Contains(strip.Markup, root);
        StringAssert.Contains(strip.Markup, "projects");
    }

    [TestMethod]
    public void Typing_a_path_and_loading_it_re_roots_the_session()
    {
        using var context = new Bunit.TestContext();
        var session = NewSession(out _);
        var other = NewTree();

        var picker = context.Render<SolutionPicker>(p => p.Add(c => c.Session, session));

        picker.Find(".picker-path").Change(other);
        picker.Find(".picker-load").Click();

        Assert.AreEqual(other, session.Root);
    }

    [TestMethod]
    public void A_path_that_is_not_there_is_reported_and_the_root_is_kept()
    {
        using var context = new Bunit.TestContext();
        var session = NewSession(out var root);

        var picker = context.Render<SolutionPicker>(p => p.Add(c => c.Session, session));

        picker.Find(".picker-path").Change(Path.Combine(root, "nowhere"));
        picker.Find(".picker-load").Click();

        Assert.AreEqual(root, session.Root);
        Assert.IsNotNull(picker.Find(".picker-problem"));
    }

    [TestMethod]
    public void The_listing_offers_solution_files_and_folders_to_click()
    {
        using var context = new Bunit.TestContext();
        var session = NewSession(out var root);

        Directory.CreateDirectory(Path.Combine(root, "Api"));
        File.WriteAllText(Path.Combine(root, "App.slnx"), "<Solution />");

        var picker = context.Render<SolutionPicker>(p => p.Add(c => c.Session, session));

        Assert.IsNotEmpty(picker.FindAll(".picker-entry"));
    }

    [TestMethod]
    public void The_ai_question_is_drawn_and_both_answers_are_wired()
    {
        using var context = new Bunit.TestContext();
        var session = NewSession(out _);

        var wait = Task.Run(() => session.Ask(new AiQuestion("Did you mean `list classes`?")));

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (session.Question is null && DateTime.UtcNow < deadline)
            Thread.Sleep(10);

        var page = context.Render<ContextStrip>(p => p.Add(c => c.Session, session));

        StringAssert.Contains(page.Find(".question").TextContent, "list classes");

        page.Find(".question-yes").Click();

        Assert.IsTrue(wait.Wait(TimeSpan.FromSeconds(5)));
        Assert.IsTrue(wait.Result);
    }

    private static ChatSession NewSession(out string root)
    {
        root = NewTree();

        return new ChatSession(
            new Workspace(root, projectPathSet: false), AiLayer.Create(), []);
    }

    private static string NewTree()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pk-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
```

The AI question is drawn by `ContextStrip` because it belongs above the composer, next to what the command was going to act on. If you put it in its own component, rename the test.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~SolutionPickerTests`

Expected: FAIL — neither component exists.

- [ ] **Step 3: Write `ContextStrip.razor`**

```razor
@* What the next command will act on, and any question standing in its way. *@

<div class="strip">
    <span class="strip-root" title="@Session.Root">@Session.Root</span>
    <span class="strip-fact">@Session.ProjectCount projects</span>
    <span class="strip-fact">project: @(Session.CurrentProject ?? "none")</span>
    <span class="strip-fact">@Session.RuleCount rules</span>
    <span class="strip-fact">AI: @Session.AiState</span>
</div>

@if (Session.Question is { } question)
{
    <div class="question">
        <span class="question-text">@question.Text</span>
        <button class="question-yes" @onclick="() => Session.Answer(true)">Yes</button>
        <button class="question-no" @onclick="() => Session.Answer(false)">No</button>
    </div>
}

@code {
    [Parameter, EditorRequired]
    public ChatSession Session { get; set; } = default!;
}
```

No class count, deliberately: counting classes means enumerating `*.cs` across the workspace after every turn, and a count that lags what the transcript just did is worse than no count.

- [ ] **Step 4: Write `SolutionPicker.razor`**

```razor
@* Choosing a solution, a project or a folder. Listed by this process, because a browser file
   input never hands the server a real path and a native dialog opens behind the browser. *@

<div class="picker">
    <div class="picker-row">
        <input class="picker-path" value="@_path" @onchange="OnPathChanged"
               placeholder="Path to a .sln, .slnx, .csproj or folder" />
        <button class="picker-load" @onclick="Load">Load</button>
    </div>

    @if (_problem is not null)
    {
        <div class="picker-problem">@_problem</div>
    }

    <ul class="picker-list">
        @if (FolderListing.ParentOf(_browsing) is { } parent)
        {
            <li class="picker-entry" @onclick="() => Browse(parent)">..</li>
        }

        @foreach (var entry in FolderListing.For(_browsing))
        {
            <li class="picker-entry @(entry.IsDirectory ? "" : "picker-solution")"
                @onclick="() => Choose(entry)">@entry.Name</li>
        }
    </ul>
</div>

@code {
    private string _path = string.Empty;
    private string _browsing = string.Empty;
    private string? _problem;

    [Parameter, EditorRequired]
    public ChatSession Session { get; set; } = default!;

    protected override void OnInitialized()
    {
        _browsing = Session.Root;
        _path = Session.Root;
    }

    private void OnPathChanged(ChangeEventArgs e)
        => _path = e.Value?.ToString() ?? string.Empty;

    private void Browse(string directory)
    {
        _browsing = directory;
        _problem = null;
    }

    private void Choose(FolderEntry entry)
    {
        if (entry.IsDirectory)
        {
            Browse(entry.Path);
            return;
        }

        _path = entry.Path;
        Load();
    }

    /// <summary>
    /// Loads what is in the box.
    /// </summary>
    /// <remarks>
    /// The session records the outcome as a turn either way, so the transcript says what happened;
    /// the problem is repeated here because the box is where the mistake was made.
    /// </remarks>
    private void Load()
    {
        var load = Session.Load(_path);

        _problem = load.Ok ? null : load.Problem;

        if (load.Ok)
            _browsing = load.Root;
    }
}
```

- [ ] **Step 5: Add both to the page**

In `ChatPage.razor`, above the transcript:

```razor
    <ContextStrip Session="Session" />
    <SolutionPicker Session="Session" />
```

- [ ] **Step 6: Style them**

Add `.strip`, `.strip-root`, `.strip-fact`, `.question`, `.question-yes`, `.question-no`, `.picker`, `.picker-row`, `.picker-path`, `.picker-load`, `.picker-problem`, `.picker-list`, `.picker-entry`, `.picker-solution`, `.suggestions`, `.suggestion`, `.suggestion-on`, `.composer` to `chat.css`. The picker list needs a `max-height` and `overflow-y: auto` — a folder with two hundred subdirectories must not push the composer off the screen.

- [ ] **Step 7: Run the tests**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~CodeGenerator.Tests.Web`

Expected: PASS, every Web test including the existing 23 files.

- [ ] **Step 8: Commit**

```bash
git add CodeGenerator/Web/Components/SolutionPicker.razor CodeGenerator/Web/Components/ContextStrip.razor CodeGenerator/Web/Components/ChatPage.razor CodeGenerator/wwwroot/chat.css CodeGenerator.Tests/Web/Chat/SolutionPickerTests.cs
git commit -m "feat(chat): load a solution, and say what the next command acts on"
```

---

## Task 16: The `chat` command

**Files:**
- Create: `CodeGenerator/App/ChatCommand.cs`
- Modify: `CodeGenerator/App/CommandRoute.cs`, `CodeGenerator/App/CodeGeneratorStarter.cs`, `CodeGenerationEngine/Core/CommandDictionary.cs`, `docs/commands.md`
- Test: `CodeGenerator.Tests/App/ChatCommandTests.cs`

**Interfaces:**
- Consumes: `ChatWebHost.TryStart`, `.Run`, `.Url` (Task 12); `ChatSession` (Task 10); `Workspace` (Task 4); `WebAppStart.OpenBrowser`; `Interrupt.Token`.
- Produces:
  ```csharp
  internal sealed class ChatCommand
  {
      internal const string NoWebVariable = "CG_NO_WEB";
      internal static bool IsChat(string? input);
      internal static bool UseWeb(bool inputRedirected, bool noWeb);
      internal void Handle(Workspace workspace, AiLayer ai, List<string> history);
  }
  ```

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/App/ChatCommandTests.cs`:

```csharp
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Recognising `chat`, and knowing when it cannot run.
/// </summary>
/// <remarks>
/// <c>Handle</c> is not driven here: it binds a port, opens a browser and blocks. What can be
/// asserted without any of that is what decides whether it runs at all — the same separation
/// <c>BuildAppCommand.UseWebWorkflow</c> exists for.
/// </remarks>
[TestClass]
public class ChatCommandTests
{
    [TestMethod]
    public void Recognises_the_command_and_its_spellings()
    {
        Assert.IsTrue(ChatCommand.IsChat("chat"));
        Assert.IsTrue(ChatCommand.IsChat("CHAT"));
        Assert.IsTrue(ChatCommand.IsChat("  chat  "));
        Assert.IsTrue(ChatCommand.IsChat("ui"));
    }

    [TestMethod]
    public void Does_not_claim_input_that_merely_mentions_it()
    {
        Assert.IsFalse(ChatCommand.IsChat("chat about the weather"));
        Assert.IsFalse(ChatCommand.IsChat("create class Chat"));
        Assert.IsFalse(ChatCommand.IsChat(null));
        Assert.IsFalse(ChatCommand.IsChat(""));
    }

    [TestMethod]
    public void Needs_a_browser_and_says_so_when_there_is_none()
    {
        Assert.IsTrue(ChatCommand.UseWeb(inputRedirected: false, noWeb: false));
        Assert.IsFalse(ChatCommand.UseWeb(inputRedirected: true, noWeb: false));
        Assert.IsFalse(ChatCommand.UseWeb(inputRedirected: false, noWeb: true));
    }

    [TestMethod]
    public void The_route_classifies_it_as_chat()
    {
        Assert.AreEqual(CommandKind.Chat, CommandRoute.Classify("chat"));
        Assert.AreEqual(CommandKind.Chat, CommandRoute.Classify("ui"));
    }

    [TestMethod]
    public void The_catalog_documents_it()
    {
        // Everything the tool can do is in CommandDictionary, which is what `help` renders and
        // docs/commands.md is generated from. A command that is not there is a command nobody
        // finds.
        Assert.IsTrue(
            CodeGenerationEngine.Core.CommandDictionary.Commands.Any(
                c => c.Command.StartsWith("chat", StringComparison.OrdinalIgnoreCase)));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~ChatCommandTests`

Expected: FAIL — no `ChatCommand`, and `CommandRoute` does not return `Chat`.

- [ ] **Step 3: Write `ChatCommand.cs`**

```csharp
using CodeGenerationEngine.Helper;
using CodeGenerator.Web.Chat;
using CodeIntelligence;

namespace CodeGenerator.App;

/// <summary>
/// Opens the chat page and waits for it.
/// </summary>
/// <remarks>
/// The same shape as the interactive branch of <c>build app</c>: start a loopback host, print the
/// address, open a browser if there is one, and block until the session ends. Everything the page
/// does happens in this process against the same objects the console path uses, so there is
/// nothing to serialize and nothing to marshal back.
/// </remarks>
internal sealed class ChatCommand
{
    /// <summary>Set to 1 to keep the console. Same variable `build app` honours.</summary>
    internal const string NoWebVariable = "CG_NO_WEB";

    internal static bool IsChat(string? input)
    {
        var text = (input ?? string.Empty).Trim();

        return text.Equals("chat", StringComparison.OrdinalIgnoreCase)
            || text.Equals("ui", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether this run can open the page.
    /// </summary>
    /// <remarks>
    /// Pure, and taking its inputs as arguments, for the reason
    /// <c>BuildAppCommand.UseWebWorkflow</c> is: the branch it governs reads the console and
    /// cannot be driven from a test.
    /// <para>
    /// Redirected stdin is the boundary. A piped run has no browser to open and no person to type
    /// into it, and the console prompt it already has is the better answer.
    /// </para>
    /// </remarks>
    internal static bool UseWeb(bool inputRedirected, bool noWeb)
        => !inputRedirected && !noWeb;

    internal void Handle(Workspace workspace, AiLayer ai, List<string> history)
    {
        if (!UseWeb(
                Console.IsInputRedirected,
                Environment.GetEnvironmentVariable(NoWebVariable) == "1"))
        {
            Output.Warning(
                "`chat` opens a page in a browser, which this run has no way to reach. "
                + "The console prompt does everything the page does.");
            return;
        }

        var session = new ChatSession(workspace, ai, history);

        if (!ChatWebHost.TryStart(session, out var host))
        {
            // Could not bind loopback. Say so; the prompt the user already has still works.
            Output.Warning("Could not start the chat page. The console prompt is unaffected.");
            return;
        }

        using (host)
        {
            Console.WriteLine($"  Opening the chat at {host.Url}");

            if (!WebAppStart.OpenBrowser(host.Url))
                Console.WriteLine("  No default browser — open that address yourself.");

            Console.WriteLine("  Ctrl-C here ends the session.");
            Console.WriteLine("");

            host.Run(session, Interrupt.Token);

            Console.WriteLine("  The chat session ended.");
            Console.WriteLine("");
        }
    }
}
```

- [ ] **Step 4: Add `Chat` to the route**

In `CommandRoute.Classify`, after the `Ai` check and before `BuildApp`:

```csharp
        if (ChatCommand.IsChat(text))
            return CommandKind.Chat;
```

- [ ] **Step 5: Wire it into the REPL**

In `CodeGeneratorStarter`, add `private readonly ChatCommand _chat = new();` and a branch in `Run`'s switch:

```csharp
            if (kind == CommandKind.Chat)
            {
                _chat.Handle(_workspace, _ai, _commandHistory);
                continue;
            }
```

Not buffered, and not inside `CommandColour.Run` — the command prints an address and then blocks, and buffering that means printing the address after the session it was for has ended.

In `RunOnce`, decline it: a scripted single-shot run has no browser.

```csharp
            if (ChatCommand.IsChat(command))
            {
                Output.Warning("`chat` needs an interactive console. Start the tool and type it.");
                return false;
            }
```

- [ ] **Step 6: Document it**

Add to `CommandDictionary.Commands`, in the Utility category, alphabetically among its neighbours:

```csharp
        new CommandInfo
        {
            Category = "Utility",
            Command = "chat",
            Description =
                "Opens a page in the browser where every command except 'build app', 'batch' and "
                + "'demo' can be run in a conversation: a command's whole output appears below what "
                + "you typed, tinted green when it worked, red when it failed and amber when the "
                + "words were not understood. The header says which solution is loaded and which "
                + "project the next command will act on, and 'Load' points the whole session — "
                + "console included — at another .sln, .slnx, .csproj or folder. The box completes "
                + "from the command catalog as you type, and shares its history with the prompt. "
                + "Closing the tab ends the session. Alias: ui. Piped runs and CG_NO_WEB=1 keep "
                + "the console.",
            Example = "chat"
        },
```

Then regenerate the reference:

```bash
UPDATE_COMMAND_DOCS=1 dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter FullyQualifiedName~CommandReferenceDocTests
```

Check `git diff docs/commands.md` shows only the new row.

- [ ] **Step 7: Run everything**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj`

Expected: PASS. `CommandDictionaryDataTests` and `CommandReferenceDocTests` in particular — they check every entry has a description and an example, and that the committed markdown matches.

- [ ] **Step 8: Commit**

```bash
git add CodeGenerator/App/ChatCommand.cs CodeGenerator/App/CommandRoute.cs CodeGenerator/App/CodeGeneratorStarter.cs CodeGenerationEngine/Core/CommandDictionary.cs docs/commands.md CodeGenerator.Tests/App/ChatCommandTests.cs
git commit -m "feat(cli): a chat command that opens the page and waits for it"
```

---

## Task 17: Run it, and write it down

**Files:**
- Modify: `docs/user-manual-source.html`, `docs/SmartCodeGenerator-User-Manual.docx`
- No new tests.

**This task is not optional and cannot be done from a passing suite.** On the previous Blazor round, four defects compiled clean with green tests and appeared only in a running application.

- [ ] **Step 1: Build the CLI fresh**

`dotnet test` leaves `CodeGenerator/bin` stale, so a fix can look dead.

```bash
dotnet build CodeGenerator/CodeGenerator.csproj -c Debug --no-incremental
```

- [ ] **Step 2: Run the whole solution the way CI does**

```bash
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror --no-incremental
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release
```

Expected: green. `-warnaserror` with `--no-incremental` is the only way the analyzer rules actually run.

- [ ] **Step 3: Drive the page by hand**

Start the CLI from the repo root and type `chat`. Then, in the page:

1. **Load this repository's own solution** — `SmartCodeGenerator/SmartCodeGenerator.slnx`. Confirm the strip says 17 projects and the root is `D:\SmartCodeGenerator-Repository`, **not** `...\SmartCodeGenerator`. This is the case the spec calls out; if it is wrong, `SolutionFile.CommonAncestorDirectory` is wrong.
2. `use project CodeGenerationEngine`, then `list classes` — a heading and a list, not preformatted text.
3. `show class ProjectManager` — a code block, highlighted, with a copy button. Compare the block against the file on disk: **nothing missing.**
4. `list properties of CommandInfo` — a two-column table.
5. `status` — plain text, and it agrees with the strip.
6. Type `list` and confirm the dropdown appears; press Enter and confirm it **completes** rather than sends; press Enter again and confirm it **sends**.
7. Press up-arrow and confirm history comes back.
8. `wibble` — amber, with a "did you mean" chip that fills the box rather than running.
9. `build app`, `batch`, `demo` — each declined in amber, nothing attempted.
10. `chat` — declined.
11. Open a second tab on the same URL. Send from one; confirm the other repaints. Confirm the composer is disabled in both while a command runs.
12. Visit the URL with the token removed — confirm 404.
13. Close both tabs, wait ten seconds, and confirm the console prompt comes back.
14. Start again, and press Ctrl-C at the console — confirm the session ends.
15. Confirm the console's up-arrow recalls a command typed in the page.

- [ ] **Step 4: Fix what that found, with a test each**

Anything found by hand gets a failing test first, then the fix. A hand-found defect with no test is a defect that comes back.

- [ ] **Step 5: Update the manual**

`docs/SmartCodeGenerator-User-Manual.docx` is generated from `docs/user-manual-source.html` through Word COM — there is no pandoc and no python-docx on this machine. Add a section for `chat` covering: what it opens, loading a solution and that it moves the console session too, the three declined commands, the Enter rule, and the two ways a session ends. Then regenerate the .docx and verify it by unzipping it rather than by opening Word.

The command appendix is **generated** from `CommandDictionary` — do not hand-edit it.

- [ ] **Step 6: Commit and push**

```bash
git add docs/user-manual-source.html docs/SmartCodeGenerator-User-Manual.docx
git commit -m "docs: the manual describes the chat page"
git push -u origin blazor-chat-ui
```

- [ ] **Step 7: Open the PR**

The body says what changed and, more usefully, what was verified by hand — the list in Step 3, with its results. Name the two refactors explicitly (`LoopbackHost`, `CommandRoute`) and say that no existing test under `CodeGenerator.Tests/Web` was edited, because that is the claim a reviewer will want to check first.

```bash
gh pr create --title "A Blazor chat UI for every command but build app" --body-file pr-body.md
```

Watch CI with `gh run watch`, then read the result properly — `gh run watch | tail` reports exit 0 even when CI failed:

```bash
gh run list --branch blazor-chat-ui --limit 1 --json conclusion,status
```

---

## Notes for whoever executes this

**Three signatures this plan guesses at.** Read the file before writing the call, and use what is actually there:

1. `StatusReport.Render(...)` in Task 10 — `CodeGenerator/App/StatusReport.cs`.
2. `ErrorText.For(Exception)` in Task 10 — `CodeGenerator/App/ErrorText.cs`.
3. `AiConfirmPrompt.Title(AiResolution)` in Task 11 — `CodeGenerator/App/AiConfirmPrompt.cs:35` uses it, so it exists; check its accessibility.

**The bUnit API.** `CodeGenerator.Tests/Web/WorkflowPageTests.cs` is the working reference for bUnit 2.9 in this repo. Where the test code in Tasks 13-15 disagrees with it, that file wins.

**Debug-suite load.** Adding tests to `CodeGenerator.Tests` can redden `ProcessRunnerTests` in Debug through CPU starvation, not through anything you wrote. Stash and rerun before debugging it.

**Path length.** Do not put generated-project probes under the scratchpad — it blows `MAX_PATH`, and the tell is MSB3030 "file not found" on a file that exists.

**CS1998 under `-warnaserror`.** Some test methods in this plan are written `async Task` and contain no `await` — `Loading_a_folder_records_a_turn_that_names_the_new_root` and `Loading_a_path_that_is_not_there_reports_it_and_keeps_the_root` in Task 10, and the two collapse tests in Task 13. CI treats CS1998 as an error. Drop `async Task` to `void` on any test that does not await; do not add a pointless `await Task.CompletedTask` to silence it.

**Verdict names.** `CommandVerdict` (the console's, three values) and `ChatVerdict` (the chat's, four) are different types on purpose. `ChatVerdict.Declined` has no counterpart, and mapping a decline onto `Unrecognized` to save a value is how the page ends up telling a user the tool did not understand `build app`.
