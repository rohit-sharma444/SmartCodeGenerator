# Fix Build Errors of a Generated App Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When the build of a generated app fails, ask "Fix the N build errors?" (default Yes); on yes, fix with narrow rules first and the local Ollama model second, rebuild, and report what was fixed and what remains. Console and browser.

**Architecture:** A new folder `CodeGenerator/BuildFix` (namespace `CodeGenerator.BuildFix`, internal; tests see it through the existing InternalsVisibleTo) holds a diagnostics parser, a `FixSession` (path guard + per-round backups), three rules, an `IBuildFixAi` with an Ollama implementation, and a `BuildFixLoop`. `BuildFixer` wires them for production. The console (`BuildAppCommand.BuildAndOfferToRun`) and the Generate stage (`GenerateStage.razor`) call the same `IBuildFixer`.

**Tech Stack:** C# / .NET 10, MSTest, Roslyn 5.3.0 (already referenced by CodeGenerator), Blazor Server + bUnit, `System.Net.Http`.

**Spec:** `docs/superpowers/specs/2026-10-07-fix-build-errors-design.md`

## Global Constraints

- Branch `fix-build-errors-prompt`. Never work on `main`.
- Edits only inside the generated project folder; every write goes through `FixSession`.
- Default answer to the prompt is **Yes**. Piped / redirected stdin: no prompt, no fix.
- At most **3** rounds; at most **5** AI asks per round; AI edits only to `.cs` files.
- The report never says "all fixed" unless the rebuild is green; it says "N fixed, M remain".
- CRLF checkout: preserve each edited file's line endings and BOM. Use no heredocs for code with backslashes.
- CI command: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror --no-incremental -m:1`. The CodeGenerator Debug build hides analyzer errors.
- MSTEST0037: use `Assert.HasCount(n, items)`, never `Assert.AreEqual(n, items.Count)`.
- Per task: build + focused filter (`dotnet test CodeGenerator.Tests -m:1 --filter ...`). Full suite only at the end (Task 11), with `-m:1`.
- Kill processes by PID, only your own. Never `taskkill` by image name.
- Every new test must be seen RED for the right reason before its code is written.

## Review Focus

- A diagnostic whose file is outside the project (a NuGet or SDK path) → no edit, no crash.
- The same error repeated per target framework / per project → counted once.
- A rule "fixes" a file but the rebuild still shows the same error → the loop stops after 3 rounds, never loops forever.
- Ollama down, model missing, or timeout → rules still run; the report says the AI step was skipped.
- The AI returns prose, empty text, a changed-nothing answer, or code that does not parse → edit rejected, file unchanged.
- The AI edit makes the error count go up or stay the same → reverted; the file is byte-identical to before.
- CRLF file and a file with a BOM stay CRLF / BOM after an edit.
- A non-empty stale `Validator.Custom.cs` body is never overwritten by the rule.
- Browser: the user reloads the page after a failed build → no crash (the offer is not repeated; stated in the report text).

---

## File map

| File | Responsibility |
|---|---|
| `CodeGenerator/BuildFix/BuildDiagnostic.cs` | record + parser of `dotnet build` output |
| `CodeGenerator/BuildFix/FixSession.cs` | path guard, read/write, per-round backups, revert, edit log |
| `CodeGenerator/BuildFix/IBuildFixRule.cs` | rule interface |
| `CodeGenerator/BuildFix/DuplicatePartialRule.cs` | CS0260 |
| `CodeGenerator/BuildFix/StaleCustomHookRule.cs` | CS0759 / CS8795 |
| `CodeGenerator/BuildFix/NoLoginAuthUsingRule.cs` | CS0234 |
| `CodeGenerator/BuildFix/IBuildFixAi.cs` | AI seam |
| `CodeGenerator/BuildFix/OllamaBuildFixAi.cs` | Ollama implementation |
| `CodeGenerator/BuildFix/BuildFixLoop.cs` | the rounds; `FixOutcome` |
| `CodeGenerator/BuildFix/BuildFixer.cs` | `IBuildFixer` + production wiring |
| `CodeGenerator/BuildFix/BuildFixReport.cs` | prompt text + report lines (pure) |
| `CodeGenerator/App/BuildAppCommand.cs` | console prompt (modify) |
| `CodeGenerator/Web/Components/Stages/GenerateStage.razor` | browser offer (modify) |
| `CodeGenerator/Web/BuildAppWebHost.cs` | register `IBuildFixer` (modify) |
| `CodeGenerator.Tests/BuildFix/*` | tests |

---

### Task 1: Diagnostics parser

**Files:**
- Create: `CodeGenerator/BuildFix/BuildDiagnostic.cs`
- Create: `CodeGenerator.Tests/BuildFix/BuildDiagnosticsTests.cs`
- Create: `CodeGenerator.Tests/BuildFix/FixTestSupport.cs`

**Interfaces:**
- Produces:
  - `internal sealed record BuildDiagnostic(string File, int Line, int Column, string Code, string Message)`
  - `internal static class BuildDiagnostics { public static IReadOnlyList<BuildDiagnostic> Parse(ProcessResult result, string projectPath); }`
  - test support: `FixTestSupport.Result(string output)`, `FixTestSupport.TempProject()`, `FixTestSupport.Write(string root, string rel, string text)`, `FixTestSupport.CompileErrors(params string[] sources)`.

- [ ] **Step 1: Write the test support and failing tests**

`CodeGenerator.Tests/BuildFix/FixTestSupport.cs`:

```csharp
using CodeGenerationEngine.Helper;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGenerator.Tests.BuildFix;

internal static class FixTestSupport
{
    public static ProcessResult Result(string output, int exit = 1)
        => new(exit, output, string.Empty, false);

    /// <summary>A short temp folder: a long one trips MAX_PATH in builds.</summary>
    public static string TempProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "cgfix-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        return root;
    }

    public static string Write(string root, string rel, string text)
    {
        var path = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>The error messages Roslyn reports for these sources, compiled together.</summary>
    public static IReadOnlyList<string> CompileErrors(params string[] sources)
    {
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(p => MetadataReference.CreateFromFile(p));

        var compilation = CSharpCompilation.Create(
            "fixcheck",
            sources.Select(s => CSharpSyntaxTree.ParseText(s)),
            trusted,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        return compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Id + ": " + d.GetMessage())
            .ToList();
    }
}
```

`CodeGenerator.Tests/BuildFix/BuildDiagnosticsTests.cs`:

```csharp
using CodeGenerator.BuildFix;

namespace CodeGenerator.Tests.BuildFix;

[TestClass]
public class BuildDiagnosticsTests
{
    private const string Root = @"C:\p";

    [TestMethod]
    public void Parse_ReadsFileLineColumnCodeAndMessage()
    {
        var output = @"C:\p\Models\Order.cs(12,5): error CS0260: Missing partial modifier on declaration of type 'Order'; another partial declaration of this type exists [C:\p\App.csproj]";

        var d = BuildDiagnostics.Parse(FixTestSupport.Result(output), Root).Single();

        Assert.AreEqual(@"C:\p\Models\Order.cs", d.File);
        Assert.AreEqual(12, d.Line);
        Assert.AreEqual(5, d.Column);
        Assert.AreEqual("CS0260", d.Code);
        Assert.AreEqual(
            "Missing partial modifier on declaration of type 'Order'; another partial declaration of this type exists",
            d.Message);
    }

    [TestMethod]
    public void Parse_DropsRepeatedLines()
    {
        var line = @"C:\p\A.cs(1,1): error CS0103: The name 'x' does not exist in the current context [C:\p\App.csproj]";

        var all = BuildDiagnostics.Parse(FixTestSupport.Result(line + "\n" + line), Root);

        Assert.HasCount(1, all);
    }

    [TestMethod]
    public void Parse_IgnoresWarnings()
    {
        var output = @"C:\p\A.cs(1,1): warning CS0108: 'A.ToString()' hides inherited member [C:\p\App.csproj]";

        Assert.HasCount(0, BuildDiagnostics.Parse(FixTestSupport.Result(output), Root));
    }

    [TestMethod]
    public void Parse_ResolvesARelativePathAgainstTheProject()
    {
        var output = @"Models\A.cs(3,2): error CS1002: ; expected";

        var d = BuildDiagnostics.Parse(FixTestSupport.Result(output), Root).Single();

        Assert.AreEqual(Path.GetFullPath(@"C:\p\Models\A.cs"), d.File);
    }

    [TestMethod]
    public void Parse_KeepsAnErrorThatHasNoLocation()
    {
        var output = "CSC : error CS5001: Program does not contain a static 'Main' method suitable for an entry point";

        var d = BuildDiagnostics.Parse(FixTestSupport.Result(output), Root).Single();

        Assert.AreEqual(string.Empty, d.File);
        Assert.AreEqual(0, d.Line);
        Assert.AreEqual("CS5001", d.Code);
    }

    [TestMethod]
    public void Parse_ReadsStandardErrorToo()
    {
        var result = new CodeGenerationEngine.Helper.ProcessResult(
            1, string.Empty, @"C:\p\A.cs(1,1): error CS1002: ; expected", false);

        Assert.HasCount(1, BuildDiagnostics.Parse(result, Root));
    }
}
```

- [ ] **Step 2: Run to verify RED**

Run: `dotnet test CodeGenerator.Tests -m:1 --filter "FullyQualifiedName~BuildDiagnosticsTests"`
Expected: compile error "The type or namespace name 'BuildFix' does not exist" (RED).

- [ ] **Step 3: Implement**

`CodeGenerator/BuildFix/BuildDiagnostic.cs`:

```csharp
using System.Text.RegularExpressions;
using CodeGenerationEngine.Helper;

namespace CodeGenerator.BuildFix;

/// <summary>One compiler error from <c>dotnet build</c>.</summary>
/// <param name="File">Absolute path, or empty when the compiler gave no location.</param>
internal sealed record BuildDiagnostic(string File, int Line, int Column, string Code, string Message);

/// <summary>Reads <c>dotnet build</c> output into <see cref="BuildDiagnostic"/>s.</summary>
/// <remarks>
/// MSBuild repeats an error once per target framework and per referencing project, so equal
/// diagnostics are dropped: "2 errors" for one mistake sends the user hunting for a second.
/// Warnings are ignored; only the lines MSBuild classes as errors are read.
/// </remarks>
internal static class BuildDiagnostics
{
    private const string Suffix = @"(?:\s*\[[^\]]*\.csproj\])?\s*$";

    private static readonly Regex Located = new(
        @"^(?<file>.+?)\((?<line>\d+),(?<col>\d+)\):\s*error\s+(?<code>[A-Za-z]+\d+):\s*(?<msg>.*?)" + Suffix,
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Unlocated = new(
        @"^(?:.*?:\s*)?error\s+(?<code>[A-Za-z]+\d+):\s*(?<msg>.*?)" + Suffix,
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<BuildDiagnostic> Parse(ProcessResult result, string projectPath)
    {
        var seen = new HashSet<BuildDiagnostic>();
        var list = new List<BuildDiagnostic>();

        foreach (var raw in (result.StandardOutput + "\n" + result.StandardError)
                 .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();

            if (line.IndexOf(": error ", StringComparison.OrdinalIgnoreCase) < 0
                && !line.StartsWith("error ", StringComparison.OrdinalIgnoreCase))
                continue;

            BuildDiagnostic? d = null;

            if (Located.Match(line) is { Success: true } m)
            {
                var file = m.Groups["file"].Value.Trim();
                if (!Path.IsPathRooted(file))
                    file = Path.GetFullPath(Path.Combine(projectPath, file));

                d = new BuildDiagnostic(
                    file, int.Parse(m.Groups["line"].Value), int.Parse(m.Groups["col"].Value),
                    m.Groups["code"].Value.ToUpperInvariant(), m.Groups["msg"].Value.Trim());
            }
            else if (Unlocated.Match(line) is { Success: true } u)
            {
                d = new BuildDiagnostic(
                    string.Empty, 0, 0, u.Groups["code"].Value.ToUpperInvariant(), u.Groups["msg"].Value.Trim());
            }

            if (d is not null && seen.Add(d))
                list.Add(d);
        }

        return list;
    }
}
```

- [ ] **Step 4: Run to verify GREEN**

Run: same filter. Expected: 6 passed.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/BuildFix CodeGenerator.Tests/BuildFix
git commit -m "feat(build-fix): parse dotnet build output into diagnostics"
```

---

### Task 2: FixSession (path guard, backups, revert)

**Files:**
- Create: `CodeGenerator/BuildFix/FixSession.cs`
- Create: `CodeGenerator/BuildFix/IBuildFixRule.cs`
- Test: `CodeGenerator.Tests/BuildFix/FixSessionTests.cs`

**Interfaces:**
- Consumes: Task 1 `BuildDiagnostic`.
- Produces:
  - `internal sealed class FixEdit { string File; string By; int Round; bool Reverted; }` (public get; `Reverted` settable)
  - `internal sealed class FixSession(string projectRoot)`: `string Root`, `IReadOnlyList<FixEdit> Edits`, `void BeginRound(int round)`, `string Read(string path)`, `void Write(string path, string text, string by)`, `void RevertRound(int round)`, `string Relative(string path)`.
  - `internal interface IBuildFixRule { string Name { get; } bool Fix(BuildDiagnostic d, FixSession session); }`

- [ ] **Step 1: Write the failing tests**

```csharp
using CodeGenerator.BuildFix;

namespace CodeGenerator.Tests.BuildFix;

[TestClass]
public class FixSessionTests
{
    [TestMethod]
    public void Write_ChangesTheFileAndLogsTheEdit()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "class A {}");
        var session = new FixSession(root);
        session.BeginRound(1);

        session.Write(file, "partial class A {}", "test");

        Assert.AreEqual("partial class A {}", File.ReadAllText(file));
        Assert.HasCount(1, session.Edits);
        Assert.AreEqual("test", session.Edits[0].By);
    }

    [TestMethod]
    public void Write_RefusesAPathOutsideTheProject()
    {
        var root = FixTestSupport.TempProject();
        var outside = FixTestSupport.Write(FixTestSupport.TempProject(), "B.cs", "class B {}");
        var session = new FixSession(root);
        session.BeginRound(1);

        Assert.Throws<InvalidOperationException>(() => session.Write(outside, "x", "test"));
        Assert.AreEqual("class B {}", File.ReadAllText(outside));
    }

    [TestMethod]
    public void Write_RefusesASiblingFolderThatSharesThePrefix()
    {
        var root = FixTestSupport.TempProject();
        var sibling = FixTestSupport.Write(root + "-other", "C.cs", "class C {}");
        var session = new FixSession(root);
        session.BeginRound(1);

        Assert.Throws<InvalidOperationException>(() => session.Write(sibling, "x", "test"));
    }

    [TestMethod]
    public void RevertRound_RestoresTheExactBytes()
    {
        var root = FixTestSupport.TempProject();
        var file = Path.Combine(root, "A.cs");
        var original = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'a', 13, 10, (byte)'b' };
        File.WriteAllBytes(file, original);
        var session = new FixSession(root);
        session.BeginRound(1);
        session.Write(file, "changed", "test");

        session.RevertRound(1);

        CollectionAssert.AreEqual(original, File.ReadAllBytes(file));
        Assert.IsTrue(session.Edits[0].Reverted);
    }

    [TestMethod]
    public void Write_KeepsTheBomAndTheLineEndings()
    {
        var root = FixTestSupport.TempProject();
        var file = Path.Combine(root, "A.cs");
        File.WriteAllBytes(file, [0xEF, 0xBB, 0xBF, (byte)'x']);
        var session = new FixSession(root);
        session.BeginRound(1);

        session.Write(file, "a\r\nb", "test");

        var bytes = File.ReadAllBytes(file);
        Assert.AreEqual(0xEF, bytes[0]);
        Assert.IsTrue(System.Text.Encoding.UTF8.GetString(bytes).Contains("a\r\nb"));
    }

    [TestMethod]
    public void RevertRound_LeavesAnEarlierRoundsEditAlone()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "v0");
        var session = new FixSession(root);
        session.BeginRound(1);
        session.Write(file, "v1", "rule");
        session.BeginRound(2);
        session.Write(file, "v2", "ai");

        session.RevertRound(2);

        Assert.AreEqual("v1", File.ReadAllText(file));
    }
}
```

- [ ] **Step 2: Run to verify RED**

Run: `dotnet test CodeGenerator.Tests -m:1 --filter "FullyQualifiedName~FixSessionTests"` → compile error (`FixSession` missing).

- [ ] **Step 3: Implement**

`CodeGenerator/BuildFix/IBuildFixRule.cs`:

```csharp
namespace CodeGenerator.BuildFix;

/// <summary>One narrow, safe fix for one kind of build error.</summary>
internal interface IBuildFixRule
{
    string Name { get; }

    /// <summary>
    /// Fixes <paramref name="d"/> when this rule knows how and it is safe; returns true only when it
    /// changed a file. Returns false — and changes nothing — for everything else.
    /// </summary>
    bool Fix(BuildDiagnostic d, FixSession session);
}
```

`CodeGenerator/BuildFix/FixSession.cs`:

```csharp
using System.Text;

namespace CodeGenerator.BuildFix;

internal sealed class FixEdit(string file, string by, int round)
{
    public string File { get; } = file;
    public string By { get; } = by;
    public int Round { get; } = round;
    public bool Reverted { get; set; }
}

/// <summary>
/// The only way a fix touches a file: every path is checked to be inside the generated project,
/// and the first write to a file in a round copies the original aside so the round can be undone.
/// </summary>
internal sealed class FixSession
{
    private readonly string _backupRoot;
    private readonly Dictionary<int, Dictionary<string, string>> _backups = [];
    private readonly List<FixEdit> _edits = [];
    private int _round;

    public FixSession(string projectRoot)
    {
        Root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _backupRoot = Path.Combine(Root, ".codegen", "fix-backups", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
    }

    public string Root { get; }

    public IReadOnlyList<FixEdit> Edits => _edits;

    public void BeginRound(int round)
    {
        _round = round;
        _backups.TryAdd(round, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    public string Relative(string path)
        => Path.GetRelativePath(Root, Path.GetFullPath(path));

    public string Read(string path)
    {
        var bytes = File.ReadAllBytes(Guard(path));
        var skip = HasBom(bytes) ? 3 : 0;
        return Encoding.UTF8.GetString(bytes, skip, bytes.Length - skip);
    }

    public void Write(string path, string text, string by)
    {
        var full = Guard(path);
        var old = File.ReadAllBytes(full);

        var backups = _backups[_round];
        if (!backups.ContainsKey(full))
        {
            Directory.CreateDirectory(Path.Combine(_backupRoot, "r" + _round));
            var bak = Path.Combine(_backupRoot, "r" + _round, backups.Count + ".bak");
            File.WriteAllBytes(bak, old);
            backups[full] = bak;
        }

        File.WriteAllText(full, text, new UTF8Encoding(HasBom(old)));
        _edits.Add(new FixEdit(full, by, _round));
    }

    public void RevertRound(int round)
    {
        if (_backups.TryGetValue(round, out var backups))
            foreach (var (file, bak) in backups)
                File.WriteAllBytes(file, File.ReadAllBytes(bak));

        foreach (var edit in _edits.Where(e => e.Round == round))
            edit.Reverted = true;
    }

    private string Guard(string path)
    {
        var full = Path.GetFullPath(path);

        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing to edit '{path}': it is outside the generated project.");

        return full;
    }

    private static bool HasBom(byte[] b) => b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF;
}
```

- [ ] **Step 4: Run to verify GREEN** — same filter, 6 passed.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/BuildFix CodeGenerator.Tests/BuildFix
git commit -m "feat(build-fix): FixSession guards paths and backs up each round"
```

---

### Task 3: Rule — CS0260 missing `partial`

**Files:**
- Create: `CodeGenerator/BuildFix/DuplicatePartialRule.cs`
- Test: `CodeGenerator.Tests/BuildFix/DuplicatePartialRuleTests.cs`

**Interfaces:**
- Consumes: `IBuildFixRule`, `FixSession`, `BuildDiagnostic`, `FixTestSupport`.
- Produces: `internal sealed class DuplicatePartialRule : IBuildFixRule` (parameterless ctor).

- [ ] **Step 1: Write the failing tests**

```csharp
using CodeGenerator.BuildFix;

namespace CodeGenerator.Tests.BuildFix;

[TestClass]
public class DuplicatePartialRuleTests
{
    private const string Plain = "namespace Shop;\n\npublic class Order\n{\n    public int Id { get; set; }\n}\n";
    private const string Half = "namespace Shop;\n\npublic partial class Order\n{\n    public bool Ok() => Id > 0;\n}\n";

    private static BuildDiagnostic Diag(string file) => new(
        file, 3, 14, "CS0260",
        "Missing partial modifier on declaration of type 'Order'; another partial declaration of this type exists");

    [TestMethod]
    public void Fix_MakesTheDeclarationPartial_AndTheProjectCompiles()
    {
        var root = FixTestSupport.TempProject();
        var plain = FixTestSupport.Write(root, "Models/Order.cs", Plain);
        FixTestSupport.Write(root, "Models/Order.Validation.cs", Half);
        Assert.IsTrue(FixTestSupport.CompileErrors(Plain, Half).Any(e => e.StartsWith("CS0260")));   // RED first
        var session = new FixSession(root);
        session.BeginRound(1);

        var fixedIt = new DuplicatePartialRule().Fix(Diag(plain), session);

        Assert.IsTrue(fixedIt);
        var after = File.ReadAllText(plain);
        StringAssert.Contains(after, "public partial class Order");
        Assert.HasCount(0, FixTestSupport.CompileErrors(after, Half));
    }

    [TestMethod]
    public void Fix_KeepsOtherModifiers()
    {
        var root = FixTestSupport.TempProject();
        var plain = FixTestSupport.Write(root, "Models/Order.cs", "public sealed class Order { }\n");
        var session = new FixSession(root);
        session.BeginRound(1);

        new DuplicatePartialRule().Fix(Diag(plain), session);

        StringAssert.Contains(File.ReadAllText(plain), "public sealed partial class Order");
    }

    [TestMethod]
    public void Fix_ReturnsFalse_WhenAlreadyPartial()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Models/Order.cs", Half);
        var session = new FixSession(root);
        session.BeginRound(1);

        Assert.IsFalse(new DuplicatePartialRule().Fix(Diag(file), session));
        Assert.AreEqual(Half, File.ReadAllText(file));
    }

    [TestMethod]
    public void Fix_ReturnsFalse_ForAnotherCode()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Models/Order.cs", Plain);
        var session = new FixSession(root);
        session.BeginRound(1);

        var other = Diag(file) with { Code = "CS0103" };

        Assert.IsFalse(new DuplicatePartialRule().Fix(other, session));
    }

    [TestMethod]
    public void Fix_ReturnsFalse_WhenTheTypeIsNotInTheFile()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Models/Other.cs", "public class Other { }\n");
        var session = new FixSession(root);
        session.BeginRound(1);

        Assert.IsFalse(new DuplicatePartialRule().Fix(Diag(file), session));
    }
}
```

- [ ] **Step 2: Run to verify RED** — `--filter "FullyQualifiedName~DuplicatePartialRuleTests"` → compile error (`DuplicatePartialRule` missing).

- [ ] **Step 3: Implement**

```csharp
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerator.BuildFix;

/// <summary>
/// CS0260: a type is declared without <c>partial</c> beside a partial half. <c>create class</c>
/// writes <c>public class X</c>; the validator half is <c>public partial class X</c>. Current
/// output marks the declaring half partial already (<c>ClassValidationWriter.EnsurePartial</c>),
/// so this repairs folders made earlier.
/// </summary>
internal sealed class DuplicatePartialRule : IBuildFixRule
{
    private static readonly Regex TypeName = new("type '(?<n>[^']+)'", RegexOptions.Compiled);

    public string Name => "make the class partial";

    public bool Fix(BuildDiagnostic d, FixSession session)
    {
        if (d.Code != "CS0260" || d.File.Length == 0 || !File.Exists(d.File))
            return false;

        if (TypeName.Match(d.Message) is not { Success: true } m)
            return false;

        var source = session.Read(d.File);

        var type = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot()
            .DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => t.Identifier.ValueText == m.Groups["n"].Value
                                 && !t.Modifiers.Any(x => x.IsKind(SyntaxKind.PartialKeyword)));

        if (type is null)
            return false;

        session.Write(d.File, source.Insert(type.Keyword.SpanStart, "partial "), Name);

        return true;
    }
}
```

- [ ] **Step 4: Run to verify GREEN** — 5 passed.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/BuildFix CodeGenerator.Tests/BuildFix
git commit -m "feat(build-fix): rule for CS0260 (missing partial)"
```

---

### Task 4: Rule — stale `Validator.Custom.cs` (CS0759 / CS8795)

**Files:**
- Create: `CodeGenerator/BuildFix/StaleCustomHookRule.cs`
- Test: `CodeGenerator.Tests/BuildFix/StaleCustomHookRuleTests.cs`

**Interfaces:**
- Consumes: `ValidatorTemplate.HasOutdatedCustomHook(string)` and `ValidatorTemplate.CustomStub(string rootNamespace, string className)` (both `public static`, in `AppScaffolding/ValidatorTemplate.cs`; check its namespace with `grep -n "^namespace" AppScaffolding/ValidatorTemplate.cs` and add the matching `using`).
- Produces: `internal sealed class StaleCustomHookRule : IBuildFixRule`.

- [ ] **Step 1: Write the failing tests**

```csharp
using CodeGenerator.BuildFix;

namespace CodeGenerator.Tests.BuildFix;

[TestClass]
public class StaleCustomHookRuleTests
{
    private const string OldEmpty = """
        using System.Collections.Generic;
        using Shop.Models;

        namespace Shop.Validation;

        public sealed partial class OrderValidator
        {
            partial void ValidateCustom(Order item, List<string> errors)
            {
            }
        }
        """;

    private const string OldWithCode = """
        using System.Collections.Generic;
        using Shop.Models;

        namespace Shop.Validation;

        public sealed partial class OrderValidator
        {
            partial void ValidateCustom(Order item, List<string> errors)
            {
                if (item.Id < 0) errors.Add("negative");
            }
        }
        """;

    private static BuildDiagnostic Diag(string file, string code = "CS0759") => new(file, 9, 18, code, "No defining declaration");

    [TestMethod]
    public void Fix_RewritesAnEmptyOutdatedStub()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Validation/OrderValidator.Custom.cs", OldEmpty);
        var session = new FixSession(root);
        session.BeginRound(1);

        Assert.IsTrue(new StaleCustomHookRule().Fix(Diag(file), session));

        var after = File.ReadAllText(file);
        StringAssert.Contains(after, "private static partial void ValidateCustom(Order item, List<ValidationError> errors)");
        StringAssert.Contains(after, "namespace Shop.Validation;");
    }

    [TestMethod]
    public void Fix_NeverOverwritesHandWrittenCode()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Validation/OrderValidator.Custom.cs", OldWithCode);
        var session = new FixSession(root);
        session.BeginRound(1);

        Assert.IsFalse(new StaleCustomHookRule().Fix(Diag(file), session));
        Assert.AreEqual(OldWithCode, File.ReadAllText(file));
    }

    [TestMethod]
    public void Fix_FindsTheCustomFile_FromAnErrorInTheGeneratedHalf()
    {
        var root = FixTestSupport.TempProject();
        FixTestSupport.Write(root, "Validation/OrderValidator.Custom.cs", OldEmpty);
        var generated = FixTestSupport.Write(root, "Validation/OrderValidator.Generated.cs", "// generated");
        var session = new FixSession(root);
        session.BeginRound(1);

        Assert.IsTrue(new StaleCustomHookRule().Fix(Diag(generated, "CS8795"), session));
    }

    [TestMethod]
    public void Fix_ReturnsFalse_ForACurrentStub()
    {
        var root = FixTestSupport.TempProject();
        var current = AppScaffolding.ValidatorTemplate.CustomStub("Shop", "Order");
        var file = FixTestSupport.Write(root, "Validation/OrderValidator.Custom.cs", current);
        var session = new FixSession(root);
        session.BeginRound(1);

        Assert.IsFalse(new StaleCustomHookRule().Fix(Diag(file), session));
    }

    [TestMethod]
    public void Fix_ReturnsFalse_ForAnotherCode()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Validation/OrderValidator.Custom.cs", OldEmpty);
        var session = new FixSession(root);
        session.BeginRound(1);

        Assert.IsFalse(new StaleCustomHookRule().Fix(Diag(file, "CS0103"), session));
    }
}
```

(If `ValidatorTemplate` is in a namespace other than `AppScaffolding`, change the fully qualified call in the fourth test.)

- [ ] **Step 2: Run to verify RED** — `--filter "FullyQualifiedName~StaleCustomHookRuleTests"` → compile error.

- [ ] **Step 3: Implement**

```csharp
using System.Text.RegularExpressions;
using AppScaffolding;

namespace CodeGenerator.BuildFix;

/// <summary>
/// CS0759 / CS8795: a write-once <c>&lt;Class&gt;Validator.Custom.cs</c> still declares the hook in the
/// shape emitted before the validation pipeline. <see cref="ValidatorTemplate.HasOutdatedCustomHook"/>
/// documents why the tool does not rewrite a hand-written body. This rule rewrites the file only when
/// the hook body is EMPTY, which is an untouched stub: nothing of the user's is lost.
/// </summary>
internal sealed class StaleCustomHookRule : IBuildFixRule
{
    private const string CustomSuffix = "Validator.Custom.cs";
    private const string GeneratedSuffix = "Validator.Generated.cs";

    private static readonly Regex EmptyHook = new(
        @"void\s+ValidateCustom\s*\([^)]*\)\s*\{\s*\}", RegexOptions.Compiled);

    private static readonly Regex Namespace = new(
        @"namespace\s+(?<ns>[\w.]+)\s*[;{]", RegexOptions.Compiled);

    public string Name => "regenerate the empty validator stub";

    public bool Fix(BuildDiagnostic d, FixSession session)
    {
        if (d.Code is not ("CS0759" or "CS8795") || d.File.Length == 0)
            return false;

        var custom = d.File.EndsWith(GeneratedSuffix, StringComparison.OrdinalIgnoreCase)
            ? d.File[..^GeneratedSuffix.Length] + CustomSuffix
            : d.File;

        if (!custom.EndsWith(CustomSuffix, StringComparison.OrdinalIgnoreCase) || !File.Exists(custom))
            return false;

        var source = session.Read(custom);

        if (!ValidatorTemplate.HasOutdatedCustomHook(source) || !EmptyHook.IsMatch(source))
            return false;

        var className = Path.GetFileName(custom)[..^CustomSuffix.Length];

        if (className.Length == 0 || Namespace.Match(source) is not { Success: true } ns)
            return false;

        // The stub lives in <Root>.Validation; the root is what CustomStub wants.
        var full = ns.Groups["ns"].Value;
        var root = full.EndsWith(".Validation", StringComparison.Ordinal) ? full[..^".Validation".Length] : full;

        session.Write(custom, ValidatorTemplate.CustomStub(root, className), Name);

        return true;
    }
}
```

- [ ] **Step 4: Run to verify GREEN** — 5 passed.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/BuildFix CodeGenerator.Tests/BuildFix
git commit -m "feat(build-fix): rule for a stale empty Validator.Custom.cs"
```

---

### Task 5: Rule — `using <Root>.Auth;` without an Auth folder (CS0234)

**Files:**
- Create: `CodeGenerator/BuildFix/NoLoginAuthUsingRule.cs`
- Test: `CodeGenerator.Tests/BuildFix/NoLoginAuthUsingRuleTests.cs`

**Interfaces:**
- Produces: `internal sealed class NoLoginAuthUsingRule : IBuildFixRule`.

- [ ] **Step 1: Write the failing tests**

```csharp
using CodeGenerator.BuildFix;

namespace CodeGenerator.Tests.BuildFix;

[TestClass]
public class NoLoginAuthUsingRuleTests
{
    private const string Source = "using System;\nusing Shop.Auth;\nusing Shop.Models;\n\nnamespace Shop;\n\npublic class MainViewModel { }\n";

    private static BuildDiagnostic Diag(string file, int line = 2) => new(
        file, line, 7, "CS0234", "The type or namespace name 'Auth' does not exist in the namespace 'Shop' (are you missing an assembly reference?)");

    [TestMethod]
    public void Fix_RemovesTheUsing_WhenThereIsNoAuthFolder()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "ViewModels/MainViewModel.cs", Source);
        var session = new FixSession(root);
        session.BeginRound(1);

        Assert.IsTrue(new NoLoginAuthUsingRule().Fix(Diag(file), session));

        var after = File.ReadAllText(file);
        Assert.DoesNotContain("Shop.Auth", after);
        StringAssert.Contains(after, "using Shop.Models;");
        Assert.HasCount(0, FixTestSupport.CompileErrors(after));
    }

    [TestMethod]
    public void Fix_ReturnsFalse_WhenTheAuthFolderExists()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "ViewModels/MainViewModel.cs", Source);
        Directory.CreateDirectory(Path.Combine(root, "Auth"));
        var session = new FixSession(root);
        session.BeginRound(1);

        Assert.IsFalse(new NoLoginAuthUsingRule().Fix(Diag(file), session));
        Assert.AreEqual(Source, File.ReadAllText(file));
    }

    [TestMethod]
    public void Fix_ReturnsFalse_WhenTheErrorLineIsNotAnAuthUsing()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "ViewModels/MainViewModel.cs", Source);
        var session = new FixSession(root);
        session.BeginRound(1);

        Assert.IsFalse(new NoLoginAuthUsingRule().Fix(Diag(file, line: 3), session));
    }

    [TestMethod]
    public void Fix_ReturnsFalse_ForAMessageThatIsNotAboutAuth()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "ViewModels/MainViewModel.cs", Source);
        var session = new FixSession(root);
        session.BeginRound(1);

        var other = Diag(file) with { Message = "The type or namespace name 'Models' does not exist" };

        Assert.IsFalse(new NoLoginAuthUsingRule().Fix(other, session));
    }
}
```

- [ ] **Step 2: Run to verify RED** — `--filter "FullyQualifiedName~NoLoginAuthUsingRuleTests"`.

- [ ] **Step 3: Implement**

```csharp
using System.Text.RegularExpressions;

namespace CodeGenerator.BuildFix;

/// <summary>
/// CS0234 for a nologin app: a file names <c>using &lt;Root&gt;.Auth;</c> but the app has no
/// <c>Auth</c> folder. Current templates emit that using only when login is on; this repairs older
/// output. Deletes exactly the one using line the compiler pointed at.
/// </summary>
internal sealed class NoLoginAuthUsingRule : IBuildFixRule
{
    private static readonly Regex AuthUsing = new(@"^\s*using\s+[\w.]+\.Auth\s*;\s*$", RegexOptions.Compiled);

    public string Name => "remove the using for the missing Auth folder";

    public bool Fix(BuildDiagnostic d, FixSession session)
    {
        if (d.Code != "CS0234" || d.Line < 1 || d.File.Length == 0 || !File.Exists(d.File)
            || !d.Message.Contains("'Auth'", StringComparison.Ordinal)
            || Directory.Exists(Path.Combine(session.Root, "Auth")))
            return false;

        var source = session.Read(d.File);
        var lines = source.Split('\n').ToList();

        if (d.Line > lines.Count || !AuthUsing.IsMatch(lines[d.Line - 1]))
            return false;

        lines.RemoveAt(d.Line - 1);
        session.Write(d.File, string.Join('\n', lines), Name);

        return true;
    }
}
```

- [ ] **Step 4: Run to verify GREEN** — 4 passed.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/BuildFix CodeGenerator.Tests/BuildFix
git commit -m "feat(build-fix): rule for a using of a missing Auth folder"
```

---

### Task 6: The AI seam and the Ollama client

**Files:**
- Create: `CodeGenerator/BuildFix/IBuildFixAi.cs`
- Create: `CodeGenerator/BuildFix/OllamaBuildFixAi.cs`
- Test: `CodeGenerator.Tests/BuildFix/OllamaBuildFixAiTests.cs`

**Interfaces:**
- Consumes: `DomainDiscovery.DiscoveryConfig` (`Endpoint`, `Model`, `TimeoutMinutes`), `DomainDiscovery.Llm.OllamaClient(DiscoveryConfig).IsAvailable()`.
- Produces:
  - `internal interface IBuildFixAi { bool IsAvailable(); string? Propose(BuildDiagnostic d, string excerpt); }` — `excerpt` is the file text around the error (no line numbers); the return value is the corrected text for exactly that excerpt, or null.
  - `internal sealed class OllamaBuildFixAi(DiscoveryConfig config, HttpClient? http = null, Func<bool>? available = null) : IBuildFixAi, IDisposable`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using System.Text;
using CodeGenerator.BuildFix;
using DomainDiscovery;

namespace CodeGenerator.Tests.BuildFix;

[TestClass]
public class OllamaBuildFixAiTests
{
    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastBody = request.Content?.ReadAsStringAsync(ct).Result;
            return Task.FromResult(reply(request));
        }
    }

    private static HttpResponseMessage Ok(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(new { message = new { role = "assistant", content } }),
            Encoding.UTF8, "application/json")
    };

    private static BuildDiagnostic Diag() => new(@"C:\p\A.cs", 3, 5, "CS1002", "; expected");

    private static OllamaBuildFixAi Make(Stub stub)
        => new(new DiscoveryConfig(), new HttpClient(stub), available: () => true);

    [TestMethod]
    public void Propose_ReturnsTheFencedBlock()
    {
        var ai = Make(new Stub(_ => Ok("Here you go:\n```csharp\nint a = 1;\n```\nDone.")));

        Assert.AreEqual("int a = 1;", ai.Propose(Diag(), "int a = 1"));
    }

    [TestMethod]
    public void Propose_ReturnsNull_WhenThereIsNoFencedBlock()
    {
        var ai = Make(new Stub(_ => Ok("Just add a semicolon.")));

        Assert.IsNull(ai.Propose(Diag(), "int a = 1"));
    }

    [TestMethod]
    public void Propose_ReturnsNull_ForAnEmptyBlock()
    {
        var ai = Make(new Stub(_ => Ok("```csharp\n\n```")));

        Assert.IsNull(ai.Propose(Diag(), "int a = 1"));
    }

    [TestMethod]
    public void Propose_ReturnsNull_OnAServerError()
    {
        var ai = Make(new Stub(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        Assert.IsNull(ai.Propose(Diag(), "int a = 1"));
    }

    [TestMethod]
    public void Propose_ReturnsNull_WhenTheCallThrows()
    {
        var ai = Make(new Stub(_ => throw new HttpRequestException("down")));

        Assert.IsNull(ai.Propose(Diag(), "int a = 1"));
    }

    [TestMethod]
    public void Propose_SendsTheErrorTheCodeAndTheConfiguredModel_Unstreamed()
    {
        var stub = new Stub(_ => Ok("```\nx\n```"));
        var ai = Make(stub);

        ai.Propose(Diag(), "int a = 1");

        StringAssert.Contains(stub.LastBody!, "CS1002");
        StringAssert.Contains(stub.LastBody!, "int a = 1");
        StringAssert.Contains(stub.LastBody!, DiscoveryConfig.DefaultModel);
        StringAssert.Contains(stub.LastBody!, "\"stream\":false");
    }

    [TestMethod]
    public void IsAvailable_UsesTheInjectedProbe()
    {
        var ai = new OllamaBuildFixAi(new DiscoveryConfig(), new HttpClient(new Stub(_ => Ok(""))), available: () => false);

        Assert.IsFalse(ai.IsAvailable());
    }
}
```

- [ ] **Step 2: Run to verify RED** — `--filter "FullyQualifiedName~OllamaBuildFixAiTests"` → compile error.

- [ ] **Step 3: Implement**

`IBuildFixAi.cs`:

```csharp
namespace CodeGenerator.BuildFix;

/// <summary>Proposes a fix for an error no rule knows. Never throws; null means "no proposal".</summary>
internal interface IBuildFixAi
{
    /// <summary>Whether the model can be asked at all (a quick probe).</summary>
    bool IsAvailable();

    /// <summary>
    /// The corrected text for exactly <paramref name="excerpt"/> (the lines around the error), or
    /// null. The caller checks the result before it is written.
    /// </summary>
    string? Propose(BuildDiagnostic d, string excerpt);
}
```

`OllamaBuildFixAi.cs`:

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using DomainDiscovery;
using DomainDiscovery.Llm;

namespace CodeGenerator.BuildFix;

/// <summary>
/// Asks the local Ollama model for a fix. A small non-streaming <c>/api/chat</c> call: the existing
/// <see cref="OllamaClient"/> is shaped around domain models and streams classes, which is the wrong
/// tool for "rewrite these 30 lines". Same endpoint, model and timeout settings (<c>discovery.json</c>).
/// </summary>
internal sealed class OllamaBuildFixAi : IBuildFixAi, IDisposable
{
    private const string System =
        "You fix C# compiler errors. You are given a compiler error and the lines of a file around it. "
        + "Reply with the corrected version of EXACTLY those lines, in one ```csharp fenced block, and nothing else. "
        + "Change as little as possible. Do not add explanations.";

    private static readonly Regex Fenced = new(
        @"```[A-Za-z#]*\r?\n(?<code>.*?)\r?\n?```", RegexOptions.Singleline | RegexOptions.Compiled);

    private readonly DiscoveryConfig _config;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Func<bool> _available;

    public OllamaBuildFixAi(DiscoveryConfig config, HttpClient? http = null, Func<bool>? available = null)
    {
        _config = config;
        _ownsHttp = http is null;
        _http = http ?? new HttpClient();

        if (_ownsHttp)
            _http.Timeout = TimeSpan.FromMinutes(Math.Clamp(config.TimeoutMinutes, 1, 3));

        _available = available ?? (() =>
        {
            using var client = new OllamaClient(config);
            return client.IsAvailable();
        });
    }

    public void Dispose()
    {
        if (_ownsHttp)
            _http.Dispose();
    }

    public bool IsAvailable() => _available();

    public string? Propose(BuildDiagnostic d, string excerpt)
    {
        try
        {
            var body = new
            {
                model = _config.Model,
                stream = false,
                options = new { temperature = 0, num_ctx = 4096 },
                messages = new object[]
                {
                    new { role = "system", content = System },
                    new { role = "user", content = $"Error {d.Code}: {d.Message}\n\nLines:\n```csharp\n{excerpt}\n```" },
                },
            };

            using var response = _http.PostAsJsonAsync($"{_config.Endpoint}/api/chat", body).GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
                return null;

            using var doc = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            var content = doc.RootElement.GetProperty("message").GetProperty("content").GetString();

            if (content is null || Fenced.Match(content) is not { Success: true } m)
                return null;

            var code = m.Groups["code"].Value.Trim('\r', '\n');

            return code.Trim().Length == 0 ? null : code;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       or KeyNotFoundException or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}
```

Note: the field named `System` shadows the `System` namespace inside the class; rename it `SystemPrompt` if the compiler complains (it will for `System.Text` uses — there are none after the usings, but rename anyway to be safe).

- [ ] **Step 4: Run to verify GREEN** — 7 passed. Then `dotnet build CodeGenerator -c Release -warnaserror --no-incremental` and fix any analyzer error.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/BuildFix CodeGenerator.Tests/BuildFix
git commit -m "feat(build-fix): Ollama client that proposes a fix for one error"
```

---

### Task 7: The fix loop

**Files:**
- Create: `CodeGenerator/BuildFix/BuildFixLoop.cs`
- Test: `CodeGenerator.Tests/BuildFix/BuildFixLoopTests.cs`

**Interfaces:**
- Consumes: Tasks 1-6.
- Produces:
  - `internal sealed record FixOutcome(bool Green, int Rounds, IReadOnlyList<FixEdit> Edits, IReadOnlyList<BuildDiagnostic> Remaining, IReadOnlyList<string> Notes, ProcessResult LastBuild)`
  - `internal sealed class BuildFixLoop(IReadOnlyList<IBuildFixRule> rules, IBuildFixAi? ai)` with `const int MaxRounds = 3`, `const int MaxAiAsksPerRound = 5`, and `FixOutcome Run(string projectPath, ProcessResult firstBuild, Func<ProcessResult> rebuild, Action<string>? progress = null)`.
  - Behaviour: round = rules over every diagnostic; if no rule fixed anything → AI pass; rebuild; an AI round whose rebuild does not LOWER the error count is reverted and the loop stops; `Win32Exception` from `rebuild` stops the loop with a note.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.ComponentModel;
using CodeGenerationEngine.Helper;
using CodeGenerator.BuildFix;

namespace CodeGenerator.Tests.BuildFix;

[TestClass]
public class BuildFixLoopTests
{
    private static string Err(string file, int line, string code, string msg = "msg")
        => $"{file}({line},1): error {code}: {msg}";

    private static readonly ProcessResult Green = new(0, "Build succeeded.", "", false);

    /// <summary>A rule that fixes CS0001 by deleting the marker "BAD" from the file.</summary>
    private sealed class StripBad : IBuildFixRule
    {
        public string Name => "strip";

        public bool Fix(BuildDiagnostic d, FixSession s)
        {
            if (d.Code != "CS0001") return false;
            var text = s.Read(d.File);
            if (!text.Contains("BAD")) return false;
            s.Write(d.File, text.Replace("BAD", ""), Name);
            return true;
        }
    }

    private sealed class FakeAi(Func<BuildDiagnostic, string, string?> propose, bool up = true) : IBuildFixAi
    {
        public int Asks;
        public bool IsAvailable() => up;
        public string? Propose(BuildDiagnostic d, string excerpt) { Asks++; return propose(d, excerpt); }
    }

    private sealed class OutsideWriter(string outside) : IBuildFixRule
    {
        public string Name => "bad";
        public bool Fix(BuildDiagnostic d, FixSession s) { s.Write(outside, "x", Name); return true; }
    }

    [TestMethod]
    public void Run_AppliesARule_Rebuilds_AndReportsGreen()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int BAD;\n");
        var first = FixTestSupport.Result(Err(file, 1, "CS0001"));

        var outcome = new BuildFixLoop([new StripBad()], null).Run(root, first, () => Green);

        Assert.IsTrue(outcome.Green);
        Assert.AreEqual(1, outcome.Rounds);
        Assert.HasCount(1, outcome.Edits);
        Assert.AreEqual("int ;\n", File.ReadAllText(file));
    }

    [TestMethod]
    public void Run_StopsAtThreeRounds_WhenARuleKeepsFiringButTheErrorStays()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "BAD");
        var red = FixTestSupport.Result(Err(file, 1, "CS0001"));
        var rebuilds = 0;

        // The rule fires only while the file still has BAD; re-add it each rebuild so it always fires.
        var outcome = new BuildFixLoop([new StripBad()], null).Run(root, red, () =>
        {
            rebuilds++;
            File.WriteAllText(file, "BAD");
            return red;
        });

        Assert.IsFalse(outcome.Green);
        Assert.AreEqual(BuildFixLoop.MaxRounds, outcome.Rounds);
        Assert.AreEqual(BuildFixLoop.MaxRounds, rebuilds);
        Assert.HasCount(1, outcome.Remaining);
    }

    [TestMethod]
    public void Run_UsesTheAi_WhenNoRuleApplies_AndKeepsAnEditThatLowersTheErrors()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int a = 1\n");
        var first = FixTestSupport.Result(Err(file, 1, "CS1002", "; expected"));
        var ai = new FakeAi((d, ex) => "int a = 1;");

        var outcome = new BuildFixLoop([], ai).Run(root, first, () => Green);

        Assert.IsTrue(outcome.Green);
        Assert.AreEqual("int a = 1;\n", File.ReadAllText(file));
        Assert.AreEqual("AI", outcome.Edits[0].By);
    }

    [TestMethod]
    public void Run_RevertsAnAiEdit_ThatDoesNotLowerTheErrorCount()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int a = 1\n");
        var red = FixTestSupport.Result(Err(file, 1, "CS1002", "; expected"));
        var ai = new FakeAi((d, ex) => "int a = 1; // changed");

        var outcome = new BuildFixLoop([], ai).Run(root, red, () => red);

        Assert.IsFalse(outcome.Green);
        Assert.AreEqual("int a = 1\n", File.ReadAllText(file));
        Assert.IsTrue(outcome.Edits.All(e => e.Reverted));
        Assert.IsTrue(outcome.Notes.Any(n => n.Contains("reverted")));
    }

    [TestMethod]
    public void Run_RejectsAnAiEdit_ThatDoesNotParse()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int a = 1\n");
        var red = FixTestSupport.Result(Err(file, 1, "CS1002"));
        var ai = new FakeAi((d, ex) => "int a = = ;;; {{{");
        var rebuilt = false;

        var outcome = new BuildFixLoop([], ai).Run(root, red, () => { rebuilt = true; return red; });

        Assert.IsFalse(rebuilt);
        Assert.AreEqual("int a = 1\n", File.ReadAllText(file));
        Assert.HasCount(0, outcome.Edits);
    }

    [TestMethod]
    public void Run_RejectsAnAiAnswerThatChangesNothing()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "int a = 1\n");
        var red = FixTestSupport.Result(Err(file, 1, "CS1002"));
        var ai = new FakeAi((d, ex) => ex);

        var outcome = new BuildFixLoop([], ai).Run(root, red, () => red);

        Assert.HasCount(0, outcome.Edits);
    }

    [TestMethod]
    public void Run_SkipsTheAi_AndSaysSo_WhenOllamaIsDown()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "x");
        var red = FixTestSupport.Result(Err(file, 1, "CS1002"));
        var ai = new FakeAi((d, ex) => "y", up: false);

        var outcome = new BuildFixLoop([], ai).Run(root, red, () => red);

        Assert.AreEqual(0, ai.Asks);
        Assert.IsTrue(outcome.Notes.Any(n => n.Contains("Ollama is not running")));
    }

    [TestMethod]
    public void Run_AsksTheAiForAtMostFiveErrorsPerRound()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", string.Join("\n", Enumerable.Repeat("x", 20)));
        var red = FixTestSupport.Result(string.Join("\n", Enumerable.Range(1, 9).Select(i => Err(file, i, "CS1002"))));
        var ai = new FakeAi((d, ex) => null);

        new BuildFixLoop([], ai).Run(root, red, () => red);

        Assert.AreEqual(BuildFixLoop.MaxAiAsksPerRound, ai.Asks);
    }

    [TestMethod]
    public void Run_NeverAsksTheAiToEditANonCsFile()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Main.xaml", "<Grid/>");
        var red = FixTestSupport.Result(Err(file, 1, "MC3000"));
        var ai = new FakeAi((d, ex) => "<Grid></Grid>");

        new BuildFixLoop([], ai).Run(root, red, () => red);

        Assert.AreEqual(0, ai.Asks);
    }

    [TestMethod]
    public void Run_SurvivesARuleThatWritesOutsideTheProject()
    {
        var root = FixTestSupport.TempProject();
        var outside = FixTestSupport.Write(FixTestSupport.TempProject(), "B.cs", "keep");
        var file = FixTestSupport.Write(root, "A.cs", "x");
        var red = FixTestSupport.Result(Err(file, 1, "CS0001"));

        var outcome = new BuildFixLoop([new OutsideWriter(outside)], null).Run(root, red, () => red);

        Assert.AreEqual("keep", File.ReadAllText(outside));
        Assert.IsFalse(outcome.Green);
    }

    [TestMethod]
    public void Run_StopsWithANote_WhenDotnetCannotStart()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "A.cs", "BAD");
        var red = FixTestSupport.Result(Err(file, 1, "CS0001"));

        var outcome = new BuildFixLoop([new StripBad()], null).Run(root, red, () => throw new Win32Exception());

        Assert.IsFalse(outcome.Green);
        Assert.IsTrue(outcome.Notes.Any(n => n.Contains("dotnet")));
    }

    [TestMethod]
    public void Run_DoesNothing_ForAGreenBuild()
    {
        var root = FixTestSupport.TempProject();

        var outcome = new BuildFixLoop([new StripBad()], null).Run(root, Green, () => Green);

        Assert.IsTrue(outcome.Green);
        Assert.AreEqual(0, outcome.Rounds);
    }
}
```

- [ ] **Step 2: Run to verify RED** — `--filter "FullyQualifiedName~BuildFixLoopTests"` → compile error.

- [ ] **Step 3: Implement**

```csharp
using System.ComponentModel;
using CodeGenerationEngine.Helper;
using CodeGenerator.App;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGenerator.BuildFix;

internal sealed record FixOutcome(
    bool Green,
    int Rounds,
    IReadOnlyList<FixEdit> Edits,
    IReadOnlyList<BuildDiagnostic> Remaining,
    IReadOnlyList<string> Notes,
    ProcessResult LastBuild);

/// <summary>
/// Fix, rebuild, repeat. Rules first; the AI only when no rule fixed anything in the round. An AI
/// round that does not LOWER the error count is undone, because a model that cannot be shown to
/// help must not be left editing the user's code.
/// </summary>
internal sealed class BuildFixLoop(IReadOnlyList<IBuildFixRule> rules, IBuildFixAi? ai)
{
    public const int MaxRounds = 3;
    public const int MaxAiAsksPerRound = 5;
    private const int ContextLines = 15;

    public FixOutcome Run(
        string projectPath, ProcessResult firstBuild, Func<ProcessResult> rebuild, Action<string>? progress = null)
    {
        progress ??= _ => { };

        var session = new FixSession(projectPath);
        var notes = new List<string>();
        var build = firstBuild;
        var diagnostics = BuildDiagnostics.Parse(build, projectPath);
        var rounds = 0;
        bool? aiUp = null;

        while (!BuildReport.Succeeded(build) && rounds < MaxRounds && diagnostics.Count > 0)
        {
            rounds++;
            session.BeginRound(rounds);

            var before = build;
            var beforeDiagnostics = diagnostics;
            var aiRound = false;

            if (ApplyRules(diagnostics, session, progress) == 0)
            {
                if (ai is null)
                {
                    notes.Add("No local AI is set up, so only the fix rules ran.");
                    break;
                }

                aiUp ??= ai.IsAvailable();

                if (aiUp != true)
                {
                    notes.Add("Ollama is not running, so only the fix rules ran.");
                    break;
                }

                if (ApplyAi(diagnostics, session, progress) == 0)
                {
                    notes.Add("The AI had no usable fix for the remaining errors.");
                    break;
                }

                aiRound = true;
            }

            try
            {
                progress("Building again");
                build = rebuild();
            }
            catch (Win32Exception)
            {
                notes.Add("Could not start `dotnet` to build again.");
                break;
            }

            diagnostics = BuildDiagnostics.Parse(build, projectPath);

            if (aiRound && !BuildReport.Succeeded(build) && diagnostics.Count >= beforeDiagnostics.Count)
            {
                session.RevertRound(rounds);
                notes.Add("The AI edits did not reduce the errors, so they were reverted.");
                build = before;
                diagnostics = beforeDiagnostics;
                break;
            }
        }

        return new FixOutcome(
            BuildReport.Succeeded(build), rounds, session.Edits, diagnostics, notes, build);
    }

    private int ApplyRules(IReadOnlyList<BuildDiagnostic> diagnostics, FixSession session, Action<string> progress)
    {
        var fixedCount = 0;

        foreach (var d in diagnostics)
            foreach (var rule in rules)
            {
                try
                {
                    if (!rule.Fix(d, session))
                        continue;

                    fixedCount++;
                    progress($"Fixed {d.Code} ({rule.Name})");
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    // A rule that cannot do its job safely is skipped, not allowed to stop the loop.
                }
            }

        return fixedCount;
    }

    private int ApplyAi(IReadOnlyList<BuildDiagnostic> diagnostics, FixSession session, Action<string> progress)
    {
        var applied = 0;
        var asked = 0;

        foreach (var d in diagnostics)
        {
            if (asked >= MaxAiAsksPerRound)
                break;

            if (d.Line < 1 || !d.File.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || !File.Exists(d.File))
                continue;

            string text;
            try
            {
                text = session.Read(d.File);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                continue;
            }

            var lines = text.Split('\n');

            if (d.Line > lines.Length)
                continue;

            var lo = Math.Max(0, d.Line - 1 - ContextLines);
            var hi = Math.Min(lines.Length - 1, d.Line - 1 + ContextLines);
            var crlf = text.Contains("\r\n", StringComparison.Ordinal);
            var excerpt = string.Join("\n", lines[lo..(hi + 1)].Select(l => l.TrimEnd('\r')));

            asked++;
            progress($"Asking the local AI about {d.Code} in {session.Relative(d.File)}");

            var reply = ai!.Propose(d, excerpt);

            if (string.IsNullOrWhiteSpace(reply) || reply.Replace("\r", "") == excerpt)
                continue;

            var replacement = reply.Replace("\r\n", "\n").Split('\n')
                .Select(l => crlf ? l + "\r" : l).ToArray();

            var rebuilt = string.Join(
                "\n", lines[..lo].Concat(replacement).Concat(lines[(hi + 1)..]));

            if (CSharpSyntaxTree.ParseText(rebuilt).GetDiagnostics().Any(x => x.Severity == DiagnosticSeverity.Error))
                continue;

            try
            {
                session.Write(d.File, rebuilt, "AI");
                applied++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
            }
        }

        return applied;
    }
}
```

Known wrinkle to verify in the test run: if the excerpt ends the file and the file has no trailing newline, the last replacement line gets a stray `\r`; if `Run_UsesTheAi_...` (CRLF-free) passes, add one more test with a CRLF file and fix the last line if it shows.

- [ ] **Step 4: Run to verify GREEN** — 11 passed. If a test fails, fix the loop, not the test.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/BuildFix CodeGenerator.Tests/BuildFix
git commit -m "feat(build-fix): the fix loop - rules, then AI, rebuild, revert if no gain"
```

---

### Task 8: Report text, `IBuildFixer` and production wiring

**Files:**
- Create: `CodeGenerator/BuildFix/BuildFixReport.cs`
- Create: `CodeGenerator/BuildFix/BuildFixer.cs`
- Test: `CodeGenerator.Tests/BuildFix/BuildFixReportTests.cs`

**Interfaces:**
- Consumes: `FixOutcome`, `BuildFixLoop`, `DiscoveryConfig.Load()`, `DiscoveryConfig.OllamaSource`.
- Produces:
  - `internal static class BuildFixReport { IReadOnlyList<string> Options; string Title(int errors); bool Accepted(int? choice); IReadOnlyList<string> Lines(FixOutcome outcome, string projectPath); }` — `Options = ["Yes, fix them", "No, leave them"]`, default index 0, `Accepted(c) => c == 0`.
  - `internal interface IBuildFixer { FixOutcome Fix(string projectPath, ProcessResult failedBuild, Func<ProcessResult> rebuild, Action<string> progress); }`
  - `internal sealed class BuildFixer : IBuildFixer` (parameterless; rules = the three rules; AI = `OllamaBuildFixAi` from `DiscoveryConfig.Load()` when `Source == OllamaSource`, else null).

- [ ] **Step 1: Write the failing tests**

```csharp
using CodeGenerator.BuildFix;

namespace CodeGenerator.Tests.BuildFix;

[TestClass]
public class BuildFixReportTests
{
    private static FixOutcome Outcome(bool green, int remaining, int edits, params string[] notes)
    {
        var root = FixTestSupport.TempProject();
        var session = new FixSession(root);
        session.BeginRound(1);
        for (var i = 0; i < edits; i++)
            session.Write(FixTestSupport.Write(root, $"F{i}.cs", "a"), "b", "make the class partial");

        var left = Enumerable.Range(1, remaining)
            .Select(i => new BuildDiagnostic(Path.Combine(root, "A.cs"), i, 1, "CS0103", "The name 'x' does not exist")).ToList();

        return new FixOutcome(green, 1, session.Edits, left, notes, FixTestSupport.Result("", green ? 0 : 1));
    }

    [TestMethod]
    public void Options_AreYesFirst_SoYesIsTheDefault()
    {
        Assert.AreEqual("Yes, fix them", BuildFixReport.Options[0]);
        Assert.IsTrue(BuildFixReport.Accepted(0));
        Assert.IsFalse(BuildFixReport.Accepted(1));
        Assert.IsFalse(BuildFixReport.Accepted(null));
    }

    [TestMethod]
    public void Title_CountsTheErrors_WithTheRightPlural()
    {
        StringAssert.Contains(BuildFixReport.Title(1), "1 error");
        Assert.DoesNotContain("1 errors", BuildFixReport.Title(1));
        StringAssert.Contains(BuildFixReport.Title(4), "4 errors");
    }

    [TestMethod]
    public void Lines_Green_SaysTheBuildSucceeded_AndWhatWasFixed()
    {
        var lines = BuildFixReport.Lines(Outcome(true, 0, 2), @"C:\p");

        StringAssert.Contains(lines[0], "builds now");
        Assert.IsTrue(lines.Any(l => l.Contains("make the class partial")));
    }

    [TestMethod]
    public void Lines_Red_NeverSaysAllFixed_AndListsWhatRemains()
    {
        var lines = BuildFixReport.Lines(Outcome(false, 2, 1, "Ollama is not running, so only the fix rules ran."), @"C:\p");

        StringAssert.Contains(lines[0], "still fails");
        Assert.DoesNotContain("builds now", lines[0]);
        Assert.IsTrue(lines.Any(l => l.Contains("CS0103")));
        Assert.IsTrue(lines.Any(l => l.Contains("Ollama is not running")));
    }

    [TestMethod]
    public void Lines_CapsTheRemainingErrorsShown()
    {
        var lines = BuildFixReport.Lines(Outcome(false, 15, 0), @"C:\p");

        Assert.IsTrue(lines.Any(l => l.Contains("and 5 more")));
    }
}
```

- [ ] **Step 2: Run to verify RED** — `--filter "FullyQualifiedName~BuildFixReportTests"`.

- [ ] **Step 3: Implement**

`BuildFixReport.cs`:

```csharp
namespace CodeGenerator.BuildFix;

/// <summary>The words the user reads about fixing build errors. Pure, so both screens say the same thing.</summary>
internal static class BuildFixReport
{
    internal const int MaxShown = 10;

    internal static IReadOnlyList<string> Options { get; } = ["Yes, fix them", "No, leave them"];

    internal const int DefaultIndex = 0;

    internal static string Title(int errors)
        => $"The build failed with {errors} {(errors == 1 ? "error" : "errors")}. Fix them?";

    internal static bool Accepted(int? choice) => choice == 0;

    public static IReadOnlyList<string> Lines(FixOutcome outcome, string projectPath)
    {
        var applied = outcome.Edits.Where(e => !e.Reverted).ToList();
        var lines = new List<string>();

        if (outcome.Green)
        {
            lines.Add($"The build fixed itself: it builds now ({applied.Count} {(applied.Count == 1 ? "edit" : "edits")}).");
        }
        else
        {
            lines.Add(
                $"The build still fails: {outcome.Remaining.Count} "
                + $"{(outcome.Remaining.Count == 1 ? "error remains" : "errors remain")} after {applied.Count} "
                + $"{(applied.Count == 1 ? "edit" : "edits")}.");
        }

        foreach (var edit in applied)
            lines.Add($"  Fixed in {Path.GetRelativePath(projectPath, edit.File)}: {edit.By}");

        if (!outcome.Green)
        {
            foreach (var d in outcome.Remaining.Take(MaxShown))
            {
                var where = d.File.Length == 0
                    ? string.Empty
                    : $"{Path.GetRelativePath(projectPath, d.File)}({d.Line},{d.Column}): ";
                lines.Add($"  {where}{d.Code}: {d.Message}");
            }

            if (outcome.Remaining.Count > MaxShown)
                lines.Add($"  ... and {outcome.Remaining.Count - MaxShown} more");
        }

        foreach (var note in outcome.Notes)
            lines.Add("  " + note);

        if (applied.Count > 0)
            lines.Add("  The original of each edited file is kept under .codegen\\fix-backups.");

        return lines;
    }
}
```

`BuildFixer.cs`:

```csharp
using CodeGenerationEngine.Helper;
using DomainDiscovery;

namespace CodeGenerator.BuildFix;

internal interface IBuildFixer
{
    FixOutcome Fix(string projectPath, ProcessResult failedBuild, Func<ProcessResult> rebuild, Action<string> progress);
}

/// <summary>Production wiring: the three rules, and the local model when discovery.json uses Ollama.</summary>
internal sealed class BuildFixer : IBuildFixer
{
    public FixOutcome Fix(string projectPath, ProcessResult failedBuild, Func<ProcessResult> rebuild, Action<string> progress)
    {
        IBuildFixRule[] rules = [new DuplicatePartialRule(), new StaleCustomHookRule(), new NoLoginAuthUsingRule()];

        var config = DiscoveryConfig.Load();

        using var ollama = string.Equals(config.Source, DiscoveryConfig.OllamaSource, StringComparison.OrdinalIgnoreCase)
            ? new OllamaBuildFixAi(config)
            : null;

        return new BuildFixLoop(rules, ollama).Run(projectPath, failedBuild, rebuild, progress);
    }
}
```

- [ ] **Step 4: Run to verify GREEN** — 5 passed; then `dotnet build CodeGenerator -c Release -warnaserror --no-incremental`.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/BuildFix CodeGenerator.Tests/BuildFix
git commit -m "feat(build-fix): report text and the production IBuildFixer"
```

---

### Task 9: Console surface

**Files:**
- Modify: `CodeGenerator/App/BuildAppCommand.cs` (`BuildAndOfferToRun`, ~line 1159)
- Test: `CodeGenerator.Tests/BuildFix/ConsoleFixOfferTests.cs`

**Interfaces:**
- Consumes: `BuildFixReport`, `IBuildFixer`/`BuildFixer`, `BuildDiagnostics.Parse`, `SelectionPrompt.Choose(string title, IReadOnlyList<string> labels, int defaultIndex)` returning `int?`.
- Produces: `internal static ProcessResult? BuildAppCommand.OfferToFix(ScaffoldResult scaffold, ProcessResult failed, Func<string, IReadOnlyList<string>, int, int?>? choose = null, IBuildFixer? fixer = null)` — returns the last build result when the user said yes, else null. The seams `choose` and `fixer` default to the real ones; they exist because the console loop cannot be driven from a test (the file's own comment says so for `BuildReport`).

- [ ] **Step 1: Write the failing tests**

```csharp
using CodeGenerationEngine.Helper;
using CodeGenerator.App;
using CodeGenerator.BuildFix;

namespace CodeGenerator.Tests.BuildFix;

[TestClass]
public class ConsoleFixOfferTests
{
    private sealed class FakeFixer(bool green) : IBuildFixer
    {
        public int Calls;

        public FixOutcome Fix(string p, ProcessResult failed, Func<ProcessResult> rebuild, Action<string> progress)
        {
            Calls++;
            var last = new ProcessResult(green ? 0 : 1, "", "", false);
            return new FixOutcome(green, 1, [], [], [], last);
        }
    }

    private static (AppScaffolding.ScaffoldResult, ProcessResult) Failed(string root)
        => (new AppScaffolding.ScaffoldResult(true, root, [], ""),
            FixTestSupport.Result($@"{root}\A.cs(1,1): error CS1002: ; expected"));

    [TestMethod]
    public void OfferToFix_Yes_RunsTheFixer_AndReturnsItsBuild()
    {
        var (scaffold, failed) = Failed(FixTestSupport.TempProject());
        var fixer = new FakeFixer(green: true);

        var result = BuildAppCommand.OfferToFix(scaffold, failed, (t, o, d) => 0, fixer);

        Assert.AreEqual(1, fixer.Calls);
        Assert.IsNotNull(result);
        Assert.IsTrue(BuildReport.Succeeded(result));
    }

    [TestMethod]
    public void OfferToFix_No_DoesNotTouchAnything()
    {
        var (scaffold, failed) = Failed(FixTestSupport.TempProject());
        var fixer = new FakeFixer(green: true);

        var result = BuildAppCommand.OfferToFix(scaffold, failed, (t, o, d) => 1, fixer);

        Assert.AreEqual(0, fixer.Calls);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void OfferToFix_Cancelled_IsNo()
    {
        var (scaffold, failed) = Failed(FixTestSupport.TempProject());
        var fixer = new FakeFixer(green: true);

        Assert.IsNull(BuildAppCommand.OfferToFix(scaffold, failed, (t, o, d) => null, fixer));
        Assert.AreEqual(0, fixer.Calls);
    }

    [TestMethod]
    public void OfferToFix_DefaultsToYes_AndAsksWithTheCount()
    {
        var (scaffold, failed) = Failed(FixTestSupport.TempProject());
        string? title = null;
        int? defaultIndex = null;

        BuildAppCommand.OfferToFix(scaffold, failed, (t, o, d) => { title = t; defaultIndex = d; return 1; }, new FakeFixer(true));

        StringAssert.Contains(title!, "1 error");
        Assert.AreEqual(0, defaultIndex);
    }

    [TestMethod]
    public void OfferToFix_NoParsableErrors_DoesNotAsk()
    {
        var scaffold = new AppScaffolding.ScaffoldResult(true, FixTestSupport.TempProject(), [], "");
        var failed = FixTestSupport.Result("Build FAILED.");
        var asked = false;

        var result = BuildAppCommand.OfferToFix(scaffold, failed, (t, o, d) => { asked = true; return 0; }, new FakeFixer(true));

        Assert.IsFalse(asked);
        Assert.IsNull(result);
    }
}
```

(Check the `ScaffoldResult` constructor: `new ScaffoldResult(success, projectPath, filesWritten, message)` as used in `GenerateStage.razor` line ~245. If the second parameter type is a list of strings, `[]` is fine.)

- [ ] **Step 2: Run to verify RED** — `--filter "FullyQualifiedName~ConsoleFixOfferTests"` → `OfferToFix` missing.

- [ ] **Step 3: Implement** — in `BuildAppCommand.cs` add (near `BuildAndOfferToRun`):

```csharp
/// <summary>
/// Asks whether to fix a failed build, and if so fixes it and rebuilds. Returns the last build, or
/// null when the user said no (or there was nothing parsable to fix).
/// </summary>
/// <remarks>
/// <paramref name="choose"/> and <paramref name="fixer"/> are seams: the console loop around this
/// cannot be driven from a test, so the decision is lifted out and the two things it touches are
/// handed in. Runs outside <c>CommandColour.Run</c> like the rest of <c>build app</c>, so the
/// prompt is visible while it is asked.
/// </remarks>
internal static ProcessResult? OfferToFix(
    ScaffoldResult scaffold,
    ProcessResult failed,
    Func<string, IReadOnlyList<string>, int, int?>? choose = null,
    CodeGenerator.BuildFix.IBuildFixer? fixer = null)
{
    var errors = CodeGenerator.BuildFix.BuildDiagnostics.Parse(failed, scaffold.ProjectPath).Count;

    if (errors == 0)
        return null;

    choose ??= SelectionPrompt.Choose;

    var choice = choose(
        CodeGenerator.BuildFix.BuildFixReport.Title(errors),
        CodeGenerator.BuildFix.BuildFixReport.Options,
        CodeGenerator.BuildFix.BuildFixReport.DefaultIndex);

    if (!CodeGenerator.BuildFix.BuildFixReport.Accepted(choice))
        return null;

    fixer ??= new CodeGenerator.BuildFix.BuildFixer();

    CodeGenerator.BuildFix.FixOutcome outcome;

    using (Spinner.Start("Fixing the build errors"))
    {
        outcome = fixer.Fix(
            scaffold.ProjectPath, failed,
            () => ProcessRunner.Run("dotnet", "build", scaffold.ProjectPath, BuildTimeout),
            _ => { });
    }

    var lines = CodeGenerator.BuildFix.BuildFixReport.Lines(outcome, scaffold.ProjectPath);

    for (var i = 0; i < lines.Count; i++)
    {
        if (i > 0)
            Console.WriteLine(lines[i]);
        else if (outcome.Green)
            Output.Success(lines[i]);
        else
            Output.Error(lines[i]);
    }

    Console.WriteLine("");

    return outcome.LastBuild;
}
```

And in `BuildAndOfferToRun`, after the `Console.WriteLine("");` that follows the report loop and before the `// Nothing to offer for a class library` check, insert:

```csharp
        // A red build the user can act on: offer to fix it, and carry on with the rebuilt result.
        if (!BuildReport.Succeeded(result) && !result.TimedOut
            && OfferToFix(scaffold, result) is { } rebuilt)
            result = rebuilt;
```

If `SelectionPrompt.Choose` is not a method group assignable to the `Func<string, IReadOnlyList<string>, int, int?>` (parameter names/types differ), wrap it: `choose ??= (t, o, d) => SelectionPrompt.Choose(t, o, d);`.

- [ ] **Step 4: Run to verify GREEN** — 5 passed; also run `--filter "FullyQualifiedName~BuildAppWorkflowCommandTests|FullyQualifiedName~AiConfirmationTests"` to make sure the console flow tests still pass.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator CodeGenerator.Tests
git commit -m "feat(build-fix): console asks to fix a failed build"
```

---

### Task 10: Browser surface (Generate stage)

**Files:**
- Modify: `CodeGenerator/Web/Components/Stages/GenerateStage.razor`
- Modify: `CodeGenerator/Web/BuildAppWebHost.cs` (line ~80, `LoopbackHost.TryStart(Route, s => s.AddSingleton(session), ...)`)
- Test: `CodeGenerator.Tests/Web/GenerateFixTests.cs`

**Interfaces:**
- Consumes: `IBuildFixer`, `BuildFixReport`, `BuildDiagnostics`, `GenerateStep.Build`, `Session.SetGenerated`, `Navigation.Offer`.
- Produces: markup classes `fix-offer`, `fix-question`, `fix-yes`, `fix-no`; the stage resolves `IBuildFixer` with `Services.GetService<IBuildFixer>()` so that stage tests that register none see no offer and are unchanged.

- [ ] **Step 1: Write the failing tests**

Follow the shape of `CodeGenerator.Tests/Web/GenerateAddOnlyTests.cs` (read it first: how it builds a `BuildSession`, a target, renders `GenerateStage` and clicks its buttons). Register a fake fixer with `ctx.Services.AddSingleton<IBuildFixer>(fake)`. To get a real red build, give the session a model whose class has a property named like the class (CS0542, which the generator does not guard when the model is built by hand):

```csharp
[TestClass]
public class GenerateFixTests : BunitContext
{
    [TestMethod]
    [TestCategory("Integration")]   // runs a real dotnet build, about a minute
    public async Task A_failed_build_offers_the_fix_and_No_leaves_it()
    {
        var fixer = new FakeFixer(green: true);
        Services.AddSingleton<IBuildFixer>(fixer);
        var cut = RenderGenerateStageWithBrokenModel();          // helper copied from GenerateAddOnlyTests
        await cut.Find("button.write").ClickAsync(new());        // the stage's existing "write" button; use the real class name from the file

        cut.WaitForAssertion(() => Assert.IsNotNull(cut.Find(".fix-offer")), TimeSpan.FromMinutes(3));
        StringAssert.Contains(cut.Find(".fix-question").TextContent, "error");

        await cut.Find(".fix-no").ClickAsync(new());

        Assert.AreEqual(0, fixer.Calls);
        Assert.HasCount(0, cut.FindAll(".fix-offer"));
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task Yes_runs_the_fixer_and_a_green_rebuild_opens_the_run_stage()
    {
        var fixer = new FakeFixer(green: true);
        Services.AddSingleton<IBuildFixer>(fixer);
        var cut = RenderGenerateStageWithBrokenModel(out var session);
        await cut.Find("button.write").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.IsNotNull(cut.Find(".fix-yes")), TimeSpan.FromMinutes(3));

        await cut.Find(".fix-yes").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.AreEqual(1, fixer.Calls));
        cut.WaitForAssertion(() => Assert.IsTrue(session.Generated!.BuildSucceeded));
        Assert.HasCount(0, cut.FindAll(".fix-offer"));
    }

    [TestMethod]
    public void A_reloaded_page_with_a_red_build_shows_no_offer_and_does_not_throw()
    {
        Services.AddSingleton<IBuildFixer>(new FakeFixer(green: true));
        var session = NewSession();                       // as in GenerateAddOnlyTests
        session.SetGenerated(new GeneratedApp(
            new ScaffoldResult(true, FixTestSupport.TempProject(), [], ""), "log", BuildSucceeded: false));

        var cut = Render<GenerateStage>(p => p.Add(x => x.Session, session).Add(x => x.Navigation, new StageNavigation()));

        Assert.HasCount(0, cut.FindAll(".fix-offer"));
        StringAssert.Contains(cut.Markup, "Run the build again to be offered a fix");
    }

    [TestMethod]
    public void No_fixer_registered_means_no_offer()
    {
        var session = NewSession();
        session.SetGenerated(new GeneratedApp(
            new ScaffoldResult(true, FixTestSupport.TempProject(), [], ""), "log", BuildSucceeded: false));

        var cut = Render<GenerateStage>(p => p.Add(x => x.Session, session).Add(x => x.Navigation, new StageNavigation()));

        Assert.HasCount(0, cut.FindAll(".fix-offer"));
    }
}
```

`FakeFixer` is the same shape as in Task 9 (`Fix` returns `FixOutcome(green, 1, [], [], [], new ProcessResult(green ? 0 : 1, "", "", false))`). `NewSession`, `RenderGenerateStageWithBrokenModel` and the `.write` button selector are written to match the helpers and class names you find in `GenerateAddOnlyTests`; read that file first and use its real names. The two integration tests build a real app (about a minute each); the last two tests are fast.

- [ ] **Step 2: Run to verify RED** — `--filter "FullyQualifiedName~GenerateFixTests"`; expected: the offer markup is not found.

- [ ] **Step 3: Implement**

In `GenerateStage.razor`:

1. Add at the top of the file: `@using CodeGenerator.BuildFix` and `@inject IServiceProvider Services`.
2. In the `else` branch, after the `@foreach (var line in _report)` loop and before the log `<pre>`:

```razor
    @if (_phase == Phase.Done && _failedBuild is not null && !_fixDeclined && Fixer is not null)
    {
        <div class="fix-offer">
            <p class="fix-question">@BuildFixReport.Title(_failedErrors)</p>
            <div class="actions">
                <button type="button" class="fix-yes" @onclick="FixErrors">@BuildFixReport.Options[0]</button>
                <button type="button" class="fix-no" @onclick="DeclineFix">@BuildFixReport.Options[1]</button>
            </div>
        </div>
    }
```

3. In `@code`: fields and methods.

```csharp
    private ProcessResult? _failedBuild;
    private int _failedErrors;
    private bool _fixDeclined;

    private IBuildFixer? Fixer => Services.GetService(typeof(IBuildFixer)) as IBuildFixer;

    private void DeclineFix() => _fixDeclined = true;

    private async Task FixErrors()
    {
        var failed = _failedBuild!;
        var scaffold = _scaffold!;
        var fixer = Fixer!;

        _failedBuild = null;
        _phase = Phase.Working;
        Step("Fixing the build errors");
        _log.Add("--- fixing the build errors ---");
        StateHasChanged();

        var outcome = await Task.Run(() => fixer.Fix(
            scaffold.ProjectPath,
            failed,
            () => GenerateStep.Build(
                      scaffold.ProjectPath,
                      line => InvokeAsync(() => { _log.Add(line); StateHasChanged(); })).Result
                  ?? throw new System.ComponentModel.Win32Exception(),
            line => InvokeAsync(() => { _report.Add(line); StateHasChanged(); })));

        Finished(outcome.Green ? "Compiled" : "The compiler still reports problems — the log is below");

        _report.AddRange(BuildFixReport.Lines(outcome, scaffold.ProjectPath));

        // Handed to the session again: the run stage needs the NEW build log to find the assembly,
        // and BuildSucceeded decides whether it opens at all.
        Session.SetGenerated(new GeneratedApp(
            scaffold,
            string.Join(Environment.NewLine, _log),
            outcome.Green,
            Persistence: _persistence));

        // Red again: the offer comes back, so the user can try another round or leave it.
        _failedBuild = outcome.Green ? null : outcome.LastBuild;
        _failedErrors = _failedBuild is null ? 0 : BuildDiagnostics.Parse(_failedBuild, scaffold.ProjectPath).Count;
        _fixDeclined = false;
        _phase = Phase.Done;

        Navigation.Offer(Continue);
        StateHasChanged();
    }
```

4. In `Go`, right after `_report.AddRange(BuildReport.Lines(result, built.Elapsed, scaffold.ProjectPath));` add:

```csharp
        _failedBuild = BuildReport.Succeeded(result) || result.TimedOut ? null : result;
        _failedErrors = _failedBuild is null ? 0 : BuildDiagnostics.Parse(_failedBuild, scaffold.ProjectPath).Count;
        _fixDeclined = false;
        if (_failedErrors == 0)
            _failedBuild = null;
```

5. In `OnInitialized` (reload path) change the report line for a red build to: `"It did not compile — the log is above the last time it ran. Run the build again to be offered a fix."` (the offer needs the compiler output, which went with the circuit).

In `BuildAppWebHost.cs` change `s => s.AddSingleton(session)` to:

```csharp
s => { s.AddSingleton(session); s.AddSingleton<IBuildFixer>(new BuildFixer()); }
```
(add `using CodeGenerator.BuildFix;`).

Add minimal CSS next to the other stage styles (find the file that styles `.overwrite-rename`, with `grep -rn "overwrite-rename" CodeGenerator --include=*.css`): reuse the existing `.actions` button styles; give `.fix-offer` the same margin as `.report-line`. Do not rename any existing class (27 class names are asserted by bUnit).

- [ ] **Step 4: Run to verify GREEN**

Run: `dotnet test CodeGenerator.Tests -m:1 --filter "FullyQualifiedName~GenerateFixTests|FullyQualifiedName~GenerateAddOnlyTests|FullyQualifiedName~WorkflowPageTests"`
Expected: all pass. Then LOOK at the page: start `build app` with the CLI, break a model, open the page, screenshot the offer and the result card (browser-automation skill). A passing bUnit test cannot see a wrapped or hidden button.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator CodeGenerator.Tests
git commit -m "feat(build-fix): the Generate stage offers to fix a failed build"
```

---

### Task 11: Live check, docs, final gate

**Files:**
- Modify: `tools/app-check/` (add a case), `docs/user-manual-source.html` (one subsection in the build-app chapter), `README.md` (one line), `docs/commands.md` is NOT touched (no new command).

- [ ] **Step 1: Live console run with a real failure**

Generate an app (`build app: ...`, `approve console`) under `D:\SampleProjects\FixCheck` with the built CLI (`dotnet build CodeGenerator -c Release` first; `CG_NO_WEB=1`). Corrupt it: in `Models/<Class>.cs` remove `partial` from one class that has a `.Validation.cs` half, or put `partial void ValidateCustom(...)` (old form) into an empty `Validator.Custom.cs`. Run `dotnet build` to see the red build; then drive the CLI interactively through the offer (type `y`/Enter). Check: the question appears, the fix applies, the rebuild is green, the "Run it now?" question follows. Record what you saw.

- [ ] **Step 2: Live browser run**

Same failure through `build app` with the page (no `CG_NO_WEB`): open the Generate stage, take screenshots of the offer, the Working phase, and the result card; click "Yes, fix them"; confirm the Run stage opens after a green rebuild. Look at the screenshots; fix any wrapped or hidden text.

- [ ] **Step 3: Live AI check (only if Ollama runs)**

Create a break no rule knows (for example `int x = ;` in a model file). Check that the AI step is asked, and that either the edit is kept and the build is green, or the edit is reverted and the report says so. If Ollama is not running, check the report says "Ollama is not running".

- [ ] **Step 4: No app-check case (decision)**

`tools/app-check` pipes `build app` and `approve` into the CLI, and a piped CLI never builds (`BuildAndOfferToRun` returns on `Console.IsInputRedirected`). The offer cannot be reached from that harness, so no case is added. The live runs in Steps 1-3 and the integration tests in Task 10 are the end-to-end check; say this in the PR.

- [ ] **Step 5: Docs**

Add to the manual (`docs/user-manual-source.html`, the chapter that describes `build app` output and the build step) a short subsection "When the build fails": the question, the default Yes, what the rules fix, that the local AI is used if Ollama runs, the 3-round limit, the backups under `.codegen\fix-backups`, and that the report lists what remains. Add one line to `README.md` next to the `build app` description. Then regenerate and verify: `UPDATE_MANUAL_APPENDIX=1 dotnet test CodeGenerator.Tests -m:1 --filter "FullyQualifiedName~ManualAppendixDoc"`, `powershell -File tools\build-user-manual.ps1`, `tools\verify-user-manual.ps1` (run from PowerShell with `&`, not `-File` through bash).

- [ ] **Step 6: Final gate**

1. `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror --no-incremental -m:1` → 0 warnings.
2. Full suite once: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -m:1` (low memory kills a parallel run).
3. Dispatch one fresh reviewer (a new agent that has not seen this plan's reasoning) on `git diff main...HEAD` with the Review Focus list. Fix what it finds with a test first.

- [ ] **Step 7: Commit**

```bash
git add -A -- CodeGenerator CodeGenerator.Tests docs README.md tools
git commit -m "docs(build-fix): manual and README for fixing a failed build"
```
Then push, open the PR with the screenshots, and wait for CI. Merge only when the user says so.
