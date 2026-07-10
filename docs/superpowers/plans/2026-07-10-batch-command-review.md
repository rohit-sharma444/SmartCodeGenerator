# Batch Command Detection & Review Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `batch` command to the CodeGenerator CLI that detects the commands in a block of user-entered sentences, presents them for per-item approval, executes only the approved ones, and rolls the whole workspace back if any of them fails.

**Architecture:** Detection reconstructs `CommandHandlingService.ParseInput`'s private chain from its three public stages, so nothing in the execution path changes. Approved sentences are replayed through the existing `NaturalLanguagePipeline.Handle`, which re-parses them against live state. Rollback is a whole-workspace snapshot taken before the first approved action and restored exactly on failure.

**Tech Stack:** C# / .NET 10 (`net10.0`), MSTest 4, Roslyn (existing), no new package references.

**Spec:** `docs/superpowers/specs/2026-07-10-batch-command-review-design.md`

## Global Constraints

- Target framework `net10.0`; `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>` in every project. Write null-annotated code.
- **No existing file in `CodeGenerationEngine/` or `NaturalLanguageEngine/` may be modified.** New files may be added to `CodeGenerationEngine/Core/`. This protects the 40+ intent handlers from regressing.
- No new NuGet package references.
- Tests are MSTest: `[TestClass]`, `[TestMethod]`, `[DataRow]`, `Assert.*` / `StringAssert.*`. Test attributes resolve via implicit usings — do **not** add `using Microsoft.VisualStudio.TestTools.UnitTesting;`.
- All commands run from the repository root `D:\SmartCodeGenerator-Repository`.
- Full test command: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx`
- Every new public type and public member gets an XML `///` doc comment (issue #29 item 7). Inline comments only where they state a constraint the code cannot show.
- Temp directories in tests follow the existing convention: `Path.Combine(Path.GetTempPath(), "<prefix>_" + Path.GetRandomFileName())`.

## File Structure

| File | Responsibility |
|---|---|
| `NaturalLanguageBridge/ApprovalReply.cs` | Parse a y/n answer. Pure. |
| `NaturalLanguageBridge/DetectedAction.cs` | The structured action record. |
| `NaturalLanguageBridge/BatchCommandDetector.cs` | Sentences → `DetectedAction`s. No console, no disk. |
| `NaturalLanguageBridge/ActionFormatter.cs` | `DetectedAction` → one display line. |
| `CodeGenerationEngine/Core/WorkspaceSnapshot.cs` | Capture/restore workspace, registry, in-memory context. |
| `CodeGenerator/App/BatchCommand.cs` | Console glue: read block, review, snapshot, execute, roll back. |
| `CodeGenerator/App/CodeGeneratorStarter.cs` | *Modified:* dispatch `batch`. |
| `CodeGenerator.Tests/Bridge/ApprovalReplyTests.cs` | |
| `CodeGenerator.Tests/Bridge/BatchCommandDetectorTests.cs` | |
| `CodeGenerator.Tests/Bridge/ActionFormatterTests.cs` | |
| `CodeGenerator.Tests/Core/WorkspaceSnapshotTests.cs` | |

Task order is dependency order: 1 → 2 → 3 → 4 are independent of each other except that 3 consumes the record from 2. Task 5 consumes all of them.

---

### Task 1: ApprovalReply

**Files:**
- Create: `NaturalLanguageBridge/ApprovalReply.cs`
- Test: `CodeGenerator.Tests/Bridge/ApprovalReplyTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `NaturalLanguageBridge.ApprovalReply` (enum: `Approve`, `Reject`, `Cancel`, `Reprompt`) and `NaturalLanguageBridge.ApprovalReplyParser.Parse(string? line) -> ApprovalReply`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/Bridge/ApprovalReplyTests.cs`:

```csharp
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

[TestClass]
public class ApprovalReplyTests
{
    [TestMethod]
    [DataRow("y")]
    [DataRow("Y")]
    [DataRow("yes")]
    [DataRow("YES")]
    [DataRow("  y  ")]
    public void Parse_Yes_Approves(string line)
    {
        Assert.AreEqual(ApprovalReply.Approve, ApprovalReplyParser.Parse(line));
    }

    [TestMethod]
    [DataRow("n")]
    [DataRow("N")]
    [DataRow("no")]
    [DataRow("NO")]
    [DataRow("  n  ")]
    public void Parse_No_Rejects(string line)
    {
        Assert.AreEqual(ApprovalReply.Reject, ApprovalReplyParser.Parse(line));
    }

    [TestMethod]
    public void Parse_Null_Cancels()
    {
        // null means EOF / closed stdin, not "no answer".
        Assert.AreEqual(ApprovalReply.Cancel, ApprovalReplyParser.Parse(null));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Parse_Empty_Reprompts(string line)
    {
        // A bare Enter must never approve: a held-down Return key would
        // otherwise walk straight through a DeleteClass.
        Assert.AreEqual(ApprovalReply.Reprompt, ApprovalReplyParser.Parse(line));
    }

    [TestMethod]
    [DataRow("maybe")]
    [DataRow("yep")]
    [DataRow("1")]
    public void Parse_Garbage_Reprompts(string line)
    {
        Assert.AreEqual(ApprovalReply.Reprompt, ApprovalReplyParser.Parse(line));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~ApprovalReplyTests"`

Expected: build FAILS with `CS0246: The type or namespace name 'ApprovalReply' could not be found`.

- [ ] **Step 3: Write minimal implementation**

Create `NaturalLanguageBridge/ApprovalReply.cs`:

```csharp
namespace NaturalLanguageBridge;

/// <summary>
/// The meaning of one answer at a batch approval prompt.
/// </summary>
public enum ApprovalReply
{
    /// <summary>Run this action.</summary>
    Approve,

    /// <summary>Skip this action, continue to the next.</summary>
    Reject,

    /// <summary>Abandon the whole batch without executing anything.</summary>
    Cancel,

    /// <summary>The answer was not understood; ask again.</summary>
    Reprompt
}

/// <summary>
/// Parses the user's answer at a batch approval prompt. Kept out of the CLI
/// project so it can be unit-tested: CodeGenerator.Tests cannot reference the
/// CodeGenerator executable.
/// </summary>
public static class ApprovalReplyParser
{
    /// <summary>
    /// Maps a raw console line to an <see cref="ApprovalReply"/>.
    /// </summary>
    /// <param name="line">
    /// The line read from the console, or <see langword="null"/> for EOF.
    /// </param>
    public static ApprovalReply Parse(string? line)
    {
        // null is EOF / closed stdin, which must abandon the batch rather than
        // spin the prompt forever.
        if (line is null)
            return ApprovalReply.Cancel;

        var trimmed = line.Trim();

        // An empty line re-prompts. It must never mean "approve".
        if (trimmed.Length == 0)
            return ApprovalReply.Reprompt;

        return trimmed.ToLowerInvariant() switch
        {
            "y" or "yes" => ApprovalReply.Approve,
            "n" or "no" => ApprovalReply.Reject,
            _ => ApprovalReply.Reprompt
        };
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~ApprovalReplyTests"`

Expected: PASS, 16 tests.

- [ ] **Step 5: Commit**

```bash
git add NaturalLanguageBridge/ApprovalReply.cs CodeGenerator.Tests/Bridge/ApprovalReplyTests.cs
git commit -m "Parse batch approval answers

Enter re-prompts rather than approving, and EOF cancels the batch."
```

---

### Task 2: DetectedAction and BatchCommandDetector

**Files:**
- Create: `NaturalLanguageBridge/DetectedAction.cs`
- Create: `NaturalLanguageBridge/BatchCommandDetector.cs`
- Test: `CodeGenerator.Tests/Bridge/BatchCommandDetectorTests.cs`

**Interfaces:**
- Consumes: `NlpActionProvider.GetPrimaryAction(string) -> string?` (existing); `CodeGenerationEngine.Core.SemanticAnalyzer.Analyze(string, string?) -> SemanticModel`; `CodeGenerationEngine.Core.IntentAnalyzer.DetectWithConfidence(SemanticModel) -> IntentMatch`; `CodeGenerationEngine.Core.EntityExtractor.Extract(SemanticModel, IntentType, string) -> ParsedCommand`; `CodeGenerationEngine.Core.ContextManager.Current -> SemanticContext`.
- Produces: `NaturalLanguageBridge.DetectedAction(string Sentence, IntentType Intent, double Confidence, ParsedCommand? Preview, string? Note)` with `bool IsActionable`; and `NaturalLanguageBridge.BatchCommandDetector.Detect(IEnumerable<string> lines, string workspaceRoot) -> IReadOnlyList<DetectedAction>`.

Background the implementer needs: `SemanticAnalyzer.Analyze` internally calls `ContextResolver.Resolve`, which writes `LastEntity` on the process-wide `ContextManager.Current`. `Detect` must therefore save and restore that context, or previewing a batch changes how the batch subsequently parses.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/Bridge/BatchCommandDetectorTests.cs`:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

[TestClass]
public class BatchCommandDetectorTests
{
    private static string NewWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "batch_" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        return root;
    }

    [TestMethod]
    public void Detect_ActionableSentences_ResolveInInputOrder()
    {
        var root = NewWorkspace();

        var actions = BatchCommandDetector.Detect(
            ["create a Customer class", "delete class Foo", "list projects"], root);

        Assert.AreEqual(3, actions.Count);
        Assert.AreEqual(IntentType.CreateClass, actions[0].Intent);
        Assert.AreEqual(IntentType.DeleteClass, actions[1].Intent);
        Assert.AreEqual(IntentType.ListProjects, actions[2].Intent);
        Assert.IsTrue(actions.All(a => a.IsActionable));
        Assert.IsTrue(actions.All(a => a.Preview is not null));
    }

    [TestMethod]
    public void Detect_ProseLine_IsUnactionableWithNoteAndNoPreview()
    {
        var root = NewWorkspace();

        var actions = BatchCommandDetector.Detect(["the big red car"], root);

        Assert.AreEqual(1, actions.Count);
        Assert.IsFalse(actions[0].IsActionable);
        Assert.AreEqual(IntentType.Unknown, actions[0].Intent);
        Assert.IsNull(actions[0].Preview);
        Assert.IsNotNull(actions[0].Note);
    }

    [TestMethod]
    public void Detect_BlankLines_AreDropped()
    {
        var root = NewWorkspace();

        var actions = BatchCommandDetector.Detect(
            ["", "   ", "create a Customer class", ""], root);

        Assert.AreEqual(1, actions.Count);
        Assert.AreEqual("create a Customer class", actions[0].Sentence);
    }

    [TestMethod]
    public void Detect_SentenceIsPreservedVerbatimForReplay()
    {
        var root = NewWorkspace();

        var actions = BatchCommandDetector.Detect(["  create a Customer class  "], root);

        // Trimmed, but otherwise untouched: this string is re-parsed at execution.
        Assert.AreEqual("create a Customer class", actions[0].Sentence);
    }

    [TestMethod]
    public void Detect_MixedBatch_CountsActionableCorrectly()
    {
        var root = NewWorkspace();

        var actions = BatchCommandDetector.Detect(
            ["create a Customer class", "the big red car", "delete class Foo"], root);

        Assert.AreEqual(3, actions.Count);
        Assert.AreEqual(2, actions.Count(a => a.IsActionable));
    }

    [TestMethod]
    public void Detect_WritesNothingToTheWorkspace()
    {
        var root = NewWorkspace();

        BatchCommandDetector.Detect(
            ["create a Customer class", "delete class Foo", "create a service for Payment"], root);

        // The whole point of "present before execute": detection must not act.
        Assert.AreEqual(0, Directory.GetFileSystemEntries(root).Length);
    }

    [TestMethod]
    public void Detect_LeavesSharedSemanticContextUnchanged()
    {
        var root = NewWorkspace();

        ContextManager.Current.LastEntity = "SENTINEL";
        ContextManager.Current.CurrentClass = "SentinelClass";

        BatchCommandDetector.Detect(
            ["create a Customer class", "add property Email to Customer"], root);

        // SemanticAnalyzer.Analyze mutates the process-wide context. If detection
        // leaked, execution would re-parse line 1 against line 2's entity.
        Assert.AreEqual("SENTINEL", ContextManager.Current.LastEntity);
        Assert.AreEqual("SentinelClass", ContextManager.Current.CurrentClass);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~BatchCommandDetectorTests"`

Expected: build FAILS with `CS0103: The name 'BatchCommandDetector' does not exist`.

- [ ] **Step 3: Write minimal implementation**

Create `NaturalLanguageBridge/DetectedAction.cs`:

```csharp
using CodeGenerationEngine.Models;

namespace NaturalLanguageBridge;

/// <summary>
/// One line of a batch, together with the command it was understood to be.
/// </summary>
/// <param name="Sentence">
/// The raw text, trimmed. This — not <paramref name="Preview"/> — is what gets
/// executed, by being re-parsed against live state at execution time.
/// </param>
/// <param name="Intent">
/// The detected intent. <see cref="IntentType.Unknown"/> means unactionable.
/// </param>
/// <param name="Confidence">Detection confidence, 0..1.</param>
/// <param name="Preview">
/// The structured action shown to the user, or <see langword="null"/> when the
/// line is unactionable or parsing threw. For display only.
/// </param>
/// <param name="Note">Why the line is unactionable, when it is.</param>
public sealed record DetectedAction(
    string Sentence,
    IntentType Intent,
    double Confidence,
    ParsedCommand? Preview,
    string? Note)
{
    /// <summary>Whether this line may be approved and executed.</summary>
    public bool IsActionable => Intent != IntentType.Unknown;
}
```

Create `NaturalLanguageBridge/BatchCommandDetector.cs`:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace NaturalLanguageBridge;

/// <summary>
/// Turns the lines of a batch into structured actions without executing any of
/// them. Reconstructs the chain <c>CommandHandlingService.ParseInput</c> runs
/// privately, from the three stages it composes — all of which are public.
/// </summary>
public static class BatchCommandDetector
{
    /// <summary>
    /// Detects the command, if any, on each non-blank line. Input order is
    /// preserved. Never throws: a line that fails to parse comes back
    /// unactionable, carrying the reason in <see cref="DetectedAction.Note"/>.
    /// </summary>
    public static IReadOnlyList<DetectedAction> Detect(
        IEnumerable<string> lines, string workspaceRoot)
    {
        // SemanticAnalyzer.Analyze writes LastEntity on the process-wide
        // ContextManager.Current. Previewing must leave the process exactly as
        // it found it, or execution re-parses line 1 against line N's entity.
        var saved = Capture(ContextManager.Current);

        try
        {
            var actions = new List<DetectedAction>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                actions.Add(DetectOne(line.Trim(), workspaceRoot));
            }

            return actions;
        }
        finally
        {
            Restore(ContextManager.Current, saved);
        }
    }

    private static DetectedAction DetectOne(string sentence, string workspaceRoot)
    {
        try
        {
            var nlpAction = NlpActionProvider.GetPrimaryAction(sentence);
            var semantic = SemanticAnalyzer.Analyze(sentence, nlpAction);
            var match = IntentAnalyzer.DetectWithConfidence(semantic);

            if (match.Intent == IntentType.Unknown)
            {
                return new DetectedAction(
                    sentence, IntentType.Unknown, match.Confidence, null, "not understood");
            }

            var preview = EntityExtractor.Extract(semantic, match.Intent, workspaceRoot);
            return new DetectedAction(sentence, match.Intent, match.Confidence, preview, null);
        }
        catch (Exception ex)
        {
            // One malformed line must not destroy the batch.
            return new DetectedAction(
                sentence, IntentType.Unknown, 0, null, $"parse failed: {ex.Message}");
        }
    }

    private static SemanticContext Capture(SemanticContext context) => new()
    {
        CurrentProject = context.CurrentProject,
        CurrentClass = context.CurrentClass,
        CurrentNamespace = context.CurrentNamespace,
        CurrentFile = context.CurrentFile,
        LastEntity = context.LastEntity
    };

    private static void Restore(SemanticContext context, SemanticContext saved)
    {
        context.CurrentProject = saved.CurrentProject;
        context.CurrentClass = saved.CurrentClass;
        context.CurrentNamespace = saved.CurrentNamespace;
        context.CurrentFile = saved.CurrentFile;
        context.LastEntity = saved.LastEntity;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~BatchCommandDetectorTests"`

Expected: PASS, 7 tests.

If `Detect_ActionableSentences_ResolveInInputOrder` fails on an intent value, do **not** change the implementation to match. Check the expectation against `CodeGenerator.Tests/Core/IntentAnalyzerNlpTests.cs`, which pins the canonical sentence→intent mapping, and fix the test's expectation.

- [ ] **Step 5: Commit**

```bash
git add NaturalLanguageBridge/DetectedAction.cs NaturalLanguageBridge/BatchCommandDetector.cs CodeGenerator.Tests/Bridge/BatchCommandDetectorTests.cs
git commit -m "Detect the commands in a batch without executing them

Detection saves and restores ContextManager.Current, because
SemanticAnalyzer.Analyze mutates that singleton and would otherwise make a
batch parse differently than the same commands typed one at a time."
```

---

### Task 3: ActionFormatter

**Files:**
- Create: `NaturalLanguageBridge/ActionFormatter.cs`
- Test: `CodeGenerator.Tests/Bridge/ActionFormatterTests.cs`

**Interfaces:**
- Consumes: `DetectedAction` from Task 2.
- Produces: `NaturalLanguageBridge.ActionFormatter.Format(DetectedAction) -> string`.

`ParsedCommand` carries ~25 fields, nearly all empty for any given intent. The formatter picks the salient one per intent family. Note `ParsedCommand` has `NewClassName` but there is **no** `NewPropertyName` or `NewMethodName`, so renames of those fall through to the default.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/Bridge/ActionFormatterTests.cs`:

```csharp
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

[TestClass]
public class ActionFormatterTests
{
    private static DetectedAction Actionable(IntentType intent, ParsedCommand command)
        => new("some sentence", intent, 0.9, command, null);

    [TestMethod]
    public void Format_ProjectIntent_ShowsProjectName()
    {
        var result = ActionFormatter.Format(
            Actionable(IntentType.CreateProject, new ParsedCommand { ProjectName = "Foo" }));

        StringAssert.StartsWith(result, "CreateProject");
        StringAssert.Contains(result, "Foo");
    }

    [TestMethod]
    public void Format_ClassIntent_ShowsClassName()
    {
        var result = ActionFormatter.Format(
            Actionable(IntentType.CreateClass, new ParsedCommand { ClassName = "Customer" }));

        StringAssert.StartsWith(result, "CreateClass");
        StringAssert.Contains(result, "Customer");
    }

    [TestMethod]
    public void Format_RenameClass_ShowsBothNames()
    {
        var result = ActionFormatter.Format(
            Actionable(IntentType.RenameClass,
                new ParsedCommand { ClassName = "Old", NewClassName = "New" }));

        StringAssert.Contains(result, "Old");
        StringAssert.Contains(result, "New");
    }

    [TestMethod]
    public void Format_PropertyIntent_ShowsNameAndType()
    {
        var result = ActionFormatter.Format(
            Actionable(IntentType.CreateProperty,
                new ParsedCommand { PropertyName = "Email", PropertyType = "string" }));

        StringAssert.Contains(result, "Email");
        StringAssert.Contains(result, "string");
    }

    [TestMethod]
    public void Format_MethodIntent_ShowsMethodName()
    {
        var result = ActionFormatter.Format(
            Actionable(IntentType.CreateMethod, new ParsedCommand { MethodName = "Save" }));

        StringAssert.Contains(result, "Save");
    }

    [TestMethod]
    public void Format_IntentWithNoSalientField_ShowsIntentNameAlone()
    {
        var result = ActionFormatter.Format(
            Actionable(IntentType.ListProjects, new ParsedCommand()));

        Assert.AreEqual("ListProjects", result.Trim());
    }

    [TestMethod]
    public void Format_Unactionable_ShowsSentenceAndNote()
    {
        var action = new DetectedAction(
            "the big red car", IntentType.Unknown, 0, null, "not understood");

        var result = ActionFormatter.Format(action);

        StringAssert.Contains(result, "the big red car");
        StringAssert.Contains(result, "not understood");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~ActionFormatterTests"`

Expected: build FAILS with `CS0103: The name 'ActionFormatter' does not exist`.

- [ ] **Step 3: Write minimal implementation**

Create `NaturalLanguageBridge/ActionFormatter.cs`:

```csharp
using CodeGenerationEngine.Models;

namespace NaturalLanguageBridge;

/// <summary>
/// Renders a <see cref="DetectedAction"/> as a single line for the review list.
/// Presentation only; kept apart from <see cref="BatchCommandDetector"/> because
/// it is the part most likely to churn.
/// </summary>
public static class ActionFormatter
{
    private const int IntentColumnWidth = 14;

    /// <summary>
    /// Formats one detected action. Unactionable lines show the original
    /// sentence and the reason it was skipped.
    /// </summary>
    public static string Format(DetectedAction action)
    {
        if (!action.IsActionable)
            return $"(skipped)  \"{action.Sentence}\"  — {action.Note}";

        // Preview is non-null whenever IsActionable is true; see DetectOne.
        var command = action.Preview!;

        var detail = action.Intent switch
        {
            IntentType.CreateProject
                or IntentType.UseProject
                or IntentType.AnalyzeProject => command.ProjectName,

            IntentType.RenameClass => $"{command.ClassName} -> {command.NewClassName}",

            IntentType.CreateClass
                or IntentType.DeleteClass
                or IntentType.ShowClass
                or IntentType.RestoreClass
                or IntentType.AnalyzeClass
                or IntentType.ClassInherit => command.ClassName,

            IntentType.CreateProperty
                or IntentType.DeleteProperty => $"{command.PropertyName} : {command.PropertyType}",

            IntentType.CreateMethod
                or IntentType.DeleteMethod => command.MethodName,

            _ => string.Empty
        };

        return detail.Length == 0
            ? action.Intent.ToString()
            : $"{action.Intent.ToString().PadRight(IntentColumnWidth)} {detail}";
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~ActionFormatterTests"`

Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add NaturalLanguageBridge/ActionFormatter.cs CodeGenerator.Tests/Bridge/ActionFormatterTests.cs
git commit -m "Render a detected action as one review-list line"
```

---

### Task 4: WorkspaceSnapshot

**Files:**
- Create: `CodeGenerationEngine/Core/WorkspaceSnapshot.cs`
- Test: `CodeGenerator.Tests/Core/WorkspaceSnapshotTests.cs`

**Interfaces:**
- Consumes: `ProjectManager.CurrentProject` (get/set), `ProjectManager.LoadProject()`, `ContextManager.Current` — all existing, all in `CodeGenerationEngine.Core`.
- Produces: `CodeGenerationEngine.Core.SnapshotSizing(int FileCount, long Bytes)`; `CodeGenerationEngine.Core.WorkspaceSnapshot` with `static SnapshotSizing Measure(string)`, `static WorkspaceSnapshot Capture(string)`, `string SnapshotPath { get; }`, `void Restore()`, `void Dispose()`.

This is a **new file** in `CodeGenerationEngine/Core/`; no existing file there changes. It lives here rather than in the bridge because restoring means touching `ProjectManager` and its registry.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/Core/WorkspaceSnapshotTests.cs`:

```csharp
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.Core;

[TestClass]
public class WorkspaceSnapshotTests
{
    private static string NewWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "snap_" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        return root;
    }

    [TestMethod]
    public void Restore_NoChanges_IsANoOp()
    {
        var root = NewWorkspace();
        File.WriteAllText(Path.Combine(root, "A.cs"), "original");

        using var snapshot = WorkspaceSnapshot.Capture(root);
        snapshot.Restore();

        Assert.AreEqual("original", File.ReadAllText(Path.Combine(root, "A.cs")));
    }

    [TestMethod]
    public void Restore_FileCreatedAfterCapture_IsDeleted()
    {
        var root = NewWorkspace();

        using var snapshot = WorkspaceSnapshot.Capture(root);
        File.WriteAllText(Path.Combine(root, "New.cs"), "added");
        snapshot.Restore();

        Assert.IsFalse(File.Exists(Path.Combine(root, "New.cs")));
    }

    [TestMethod]
    public void Restore_FileModifiedAfterCapture_HasOriginalContents()
    {
        var root = NewWorkspace();
        var file = Path.Combine(root, "A.cs");
        File.WriteAllText(file, "original");

        using var snapshot = WorkspaceSnapshot.Capture(root);
        File.WriteAllText(file, "clobbered");
        snapshot.Restore();

        Assert.AreEqual("original", File.ReadAllText(file));
    }

    [TestMethod]
    public void Restore_FileDeletedAfterCapture_IsRecreated()
    {
        var root = NewWorkspace();
        var file = Path.Combine(root, "A.cs");
        File.WriteAllText(file, "original");

        using var snapshot = WorkspaceSnapshot.Capture(root);
        File.Delete(file);
        snapshot.Restore();

        Assert.AreEqual("original", File.ReadAllText(file));
    }

    [TestMethod]
    public void Restore_DirectoryCreatedAfterCapture_IsRemoved()
    {
        var root = NewWorkspace();

        using var snapshot = WorkspaceSnapshot.Capture(root);
        var projectDir = Path.Combine(root, "Foo");
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(Path.Combine(projectDir, "Foo.csproj"), "<Project/>");
        snapshot.Restore();

        // This is what removes a `dotnet new` project tree.
        Assert.IsFalse(Directory.Exists(projectDir));
    }

    [TestMethod]
    public void Capture_ExcludesBuildOutputAndGit()
    {
        var root = NewWorkspace();
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        Directory.CreateDirectory(Path.Combine(root, "obj"));
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        File.WriteAllText(Path.Combine(root, "bin", "app.dll"), "binary");
        File.WriteAllText(Path.Combine(root, "obj", "tmp.cache"), "cache");
        File.WriteAllText(Path.Combine(root, ".git", "HEAD"), "ref");
        File.WriteAllText(Path.Combine(root, "A.cs"), "source");

        using var snapshot = WorkspaceSnapshot.Capture(root);
        File.WriteAllText(Path.Combine(root, "bin", "app.dll"), "rebuilt");
        snapshot.Restore();

        // Excluded directories are neither captured nor disturbed by restore:
        // the rebuilt dll survives, and bin/ is not deleted as "not in snapshot".
        Assert.AreEqual("rebuilt", File.ReadAllText(Path.Combine(root, "bin", "app.dll")));
        Assert.IsTrue(File.Exists(Path.Combine(root, "obj", "tmp.cache")));
        Assert.IsTrue(File.Exists(Path.Combine(root, ".git", "HEAD")));
    }

    [TestMethod]
    public void Measure_CountsOnlyIncludedFiles()
    {
        var root = NewWorkspace();
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        File.WriteAllText(Path.Combine(root, "bin", "app.dll"), "binary");
        File.WriteAllText(Path.Combine(root, "A.cs"), "12345");
        File.WriteAllText(Path.Combine(root, "B.cs"), "12345");

        var sizing = WorkspaceSnapshot.Measure(root);

        Assert.AreEqual(2, sizing.FileCount);
        Assert.AreEqual(10, sizing.Bytes);
    }

    [TestMethod]
    public void Restore_RestoresInMemorySemanticContext()
    {
        var root = NewWorkspace();
        ContextManager.Current.CurrentClass = "Before";
        ContextManager.Current.LastEntity = "BeforeEntity";

        using var snapshot = WorkspaceSnapshot.Capture(root);
        ContextManager.SetCurrentClass("After");
        ContextManager.Current.LastEntity = "AfterEntity";
        snapshot.Restore();

        Assert.AreEqual("Before", ContextManager.Current.CurrentClass);
        Assert.AreEqual("BeforeEntity", ContextManager.Current.LastEntity);
    }

    [TestMethod]
    public void Dispose_AfterSuccessfulRestore_DeletesSnapshot()
    {
        var root = NewWorkspace();
        var snapshot = WorkspaceSnapshot.Capture(root);
        var path = snapshot.SnapshotPath;

        snapshot.Restore();
        snapshot.Dispose();

        Assert.IsFalse(Directory.Exists(path));
    }

    [TestMethod]
    public void Dispose_AfterFailedRestore_KeepsSnapshot()
    {
        var root = NewWorkspace();
        var snapshot = WorkspaceSnapshot.Capture(root);
        var path = snapshot.SnapshotPath;

        // Force Restore to throw by destroying the captured tree.
        Directory.Delete(Path.Combine(path, "workspace"), recursive: true);
        Assert.ThrowsExactly<DirectoryNotFoundException>(() => snapshot.Restore());

        snapshot.Dispose();

        // Losing the only copy of the user's code on a failed restore is the
        // worst thing this feature could do.
        Assert.IsTrue(Directory.Exists(path));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~WorkspaceSnapshotTests"`

Expected: build FAILS with `CS0103: The name 'WorkspaceSnapshot' does not exist`.

- [ ] **Step 3: Write minimal implementation**

Create `CodeGenerationEngine/Core/WorkspaceSnapshot.cs`:

```csharp
using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core;

/// <summary>How much a workspace snapshot would copy.</summary>
/// <param name="FileCount">Files that would be captured, after exclusions.</param>
/// <param name="Bytes">Their total size in bytes.</param>
public sealed record SnapshotSizing(int FileCount, long Bytes);

/// <summary>
/// A point-in-time copy of the workspace, the project registry, and the
/// in-memory state a command can mutate. Used to make a batch of commands
/// all-or-nothing: capture before the first action, restore if any fails.
/// </summary>
/// <remarks>
/// Every command handler bottoms out in the filesystem, so one snapshot is
/// generically correct for all of them — present and future — where an undo log
/// would need a separate inverse per intent.
/// </remarks>
public sealed class WorkspaceSnapshot : IDisposable
{
    private static readonly string[] ExcludedDirectories =
        ["bin", "obj", ".git", "TestResults", "node_modules"];

    private const string WorkspaceFolder = "workspace";
    private const string RegistryFileName = "ProjectManager.dat";
    private const string RegistryBackupName = "ProjectManager.dat.bak";

    private readonly string _workspaceRoot;
    private readonly ProjectMetaData? _currentProject;
    private readonly SemanticContext _context;
    private bool _restoreFailed;

    /// <summary>The temp directory holding the captured copy.</summary>
    public string SnapshotPath { get; }

    private WorkspaceSnapshot(
        string workspaceRoot,
        string snapshotPath,
        ProjectMetaData? currentProject,
        SemanticContext context)
    {
        _workspaceRoot = workspaceRoot;
        SnapshotPath = snapshotPath;
        _currentProject = currentProject;
        _context = context;
    }

    /// <summary>
    /// Counts the files a capture would copy, without copying them.
    /// </summary>
    public static SnapshotSizing Measure(string workspaceRoot)
    {
        int count = 0;
        long bytes = 0;

        foreach (var file in EnumerateIncludedFiles(workspaceRoot))
        {
            count++;
            bytes += new FileInfo(file).Length;
        }

        return new SnapshotSizing(count, bytes);
    }

    /// <summary>
    /// Copies the workspace, the project registry, and the in-memory context to
    /// a temp directory. Throws if the copy cannot be made; the caller must then
    /// abort before executing anything.
    /// </summary>
    public static WorkspaceSnapshot Capture(string workspaceRoot)
    {
        var snapshotPath = Path.Combine(
            Path.GetTempPath(), "codegen_snapshot_" + Path.GetRandomFileName());

        try
        {
            Directory.CreateDirectory(Path.Combine(snapshotPath, WorkspaceFolder));

            foreach (var file in EnumerateIncludedFiles(workspaceRoot))
            {
                var relative = Path.GetRelativePath(workspaceRoot, file);
                var destination = Path.Combine(snapshotPath, WorkspaceFolder, relative);

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, overwrite: true);
            }

            // The project registry lives next to the exe, not in the workspace.
            var registry = Path.Combine(AppContext.BaseDirectory, RegistryFileName);
            if (File.Exists(registry))
                File.Copy(registry, Path.Combine(snapshotPath, RegistryBackupName), overwrite: true);

            return new WorkspaceSnapshot(
                workspaceRoot,
                snapshotPath,
                ProjectManager.CurrentProject,
                CaptureContext(ContextManager.Current));
        }
        catch
        {
            TryDelete(snapshotPath);
            throw;
        }
    }

    /// <summary>
    /// Puts the workspace back exactly as it was. On failure the snapshot
    /// directory is retained — see <see cref="Dispose"/>.
    /// </summary>
    public void Restore()
    {
        try
        {
            var capturedRoot = Path.Combine(SnapshotPath, WorkspaceFolder);

            var captured = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in EnumerateIncludedFiles(capturedRoot))
                captured[Path.GetRelativePath(capturedRoot, file)] = file;

            // 1. Delete what the batch added. Removes `dotnet new` project trees.
            foreach (var file in EnumerateIncludedFiles(_workspaceRoot).ToList())
            {
                var relative = Path.GetRelativePath(_workspaceRoot, file);
                if (!captured.ContainsKey(relative))
                    File.Delete(file);
            }

            // 2 & 3. Overwrite what changed, recreate what was deleted.
            foreach (var (relative, source) in captured)
            {
                var destination = Path.Combine(_workspaceRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: true);
            }

            // 4. Directories the batch created are now empty.
            RemoveEmptyDirectories(_workspaceRoot, isRoot: true);

            // 5. Registry file, then reload it into memory.
            var registry = Path.Combine(AppContext.BaseDirectory, RegistryFileName);
            var backup = Path.Combine(SnapshotPath, RegistryBackupName);

            if (File.Exists(backup))
                File.Copy(backup, registry, overwrite: true);
            else if (File.Exists(registry))
                File.Delete(registry);

            ProjectManager.LoadProject();

            // 6 & 7. In-memory state a handler may have set.
            ProjectManager.CurrentProject = _currentProject;
            RestoreContext(ContextManager.Current, _context);
        }
        catch
        {
            _restoreFailed = true;
            throw;
        }
    }

    /// <summary>
    /// Deletes the captured copy — unless <see cref="Restore"/> failed, in which
    /// case it is kept so the user can recover by hand.
    /// </summary>
    public void Dispose()
    {
        if (_restoreFailed)
            return;

        TryDelete(SnapshotPath);
    }

    private static IEnumerable<string> EnumerateIncludedFiles(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
            yield return file;

        foreach (var subdirectory in Directory.EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(subdirectory);

            if (ExcludedDirectories.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;

            foreach (var file in EnumerateIncludedFiles(subdirectory))
                yield return file;
        }
    }

    private static void RemoveEmptyDirectories(string directory, bool isRoot)
    {
        foreach (var subdirectory in Directory.EnumerateDirectories(directory).ToList())
        {
            var name = Path.GetFileName(subdirectory);

            if (ExcludedDirectories.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;

            RemoveEmptyDirectories(subdirectory, isRoot: false);
        }

        if (!isRoot && !Directory.EnumerateFileSystemEntries(directory).Any())
            Directory.Delete(directory);
    }

    private static SemanticContext CaptureContext(SemanticContext context) => new()
    {
        CurrentProject = context.CurrentProject,
        CurrentClass = context.CurrentClass,
        CurrentNamespace = context.CurrentNamespace,
        CurrentFile = context.CurrentFile,
        LastEntity = context.LastEntity
    };

    private static void RestoreContext(SemanticContext context, SemanticContext saved)
    {
        context.CurrentProject = saved.CurrentProject;
        context.CurrentClass = saved.CurrentClass;
        context.CurrentNamespace = saved.CurrentNamespace;
        context.CurrentFile = saved.CurrentFile;
        context.LastEntity = saved.LastEntity;
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // A leaked temp directory is not worth failing a batch over.
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx --filter "FullyQualifiedName~WorkspaceSnapshotTests"`

Expected: PASS, 10 tests.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Core/WorkspaceSnapshot.cs CodeGenerator.Tests/Core/WorkspaceSnapshotTests.cs
git commit -m "Capture and restore the workspace for batch rollback

Captures the project registry from next to the exe and the in-memory
semantic context alongside the file tree; a failed restore keeps the
snapshot directory rather than destroying the only copy."
```

---

### Task 5: BatchCommand and CLI wiring

**Files:**
- Create: `CodeGenerator/App/BatchCommand.cs`
- Modify: `CodeGenerator/App/CodeGeneratorStarter.cs` (add field near line 23, add dispatch inside `Run()` near line 127)

**Interfaces:**
- Consumes: `BatchCommandDetector.Detect`, `ActionFormatter.Format`, `ApprovalReplyParser.Parse`, `ApprovalReply`, `DetectedAction` (Tasks 1–3); `WorkspaceSnapshot.Measure/Capture/Restore/Dispose`, `SnapshotSizing` (Task 4); existing `NaturalLanguagePipeline.Handle(string, ref ProjectMetaData?, List<PropertyRule>, string, Action<ProjectMetaData?>, Action<SemanticResult?>?) -> PipelineOutcome` and `PipelineOutcome.Handled`.
- Produces: `CodeGenerator.App.BatchCommand.IsBatch(string) -> bool` and its instance `Handle(...)`.

No unit tests: `CodeGenerator.Tests` cannot reference the `CodeGenerator` executable. The file is kept nearly logic-free for that reason, and is verified end-to-end in Step 4.

- [ ] **Step 1: Write `BatchCommand.cs`**

Create `CodeGenerator/App/BatchCommand.cs`:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.App;

/// <summary>
/// The <c>batch</c> command: reads a block of sentences, shows the commands
/// detected in them, executes only those the user approves, and rolls the
/// workspace back if any approved command fails.
/// </summary>
internal sealed class BatchCommand
{
    private const string Keyword = "batch";
    private const int MaxSnapshotFiles = 5_000;
    private const long MaxSnapshotBytes = 200L * 1024 * 1024;

    /// <summary>Whether <paramref name="input"/> opens a batch.</summary>
    internal static bool IsBatch(string input)
        => input.Trim().Equals(Keyword, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Runs one batch from start to finish.
    /// </summary>
    /// <param name="currentProject">
    /// Threaded by reference so that <c>use project Foo</c> on one line is
    /// visible to <c>create class Bar</c> on the next, and so rollback can put
    /// it back.
    /// </param>
    internal void Handle(
        ref ProjectMetaData? currentProject,
        List<PropertyRule> configuredRules,
        string workspaceRoot,
        Action<ProjectMetaData?> setLastCreatedProject,
        NaturalLanguagePipeline pipeline)
    {
        var lines = ReadBlock();
        var detected = BatchCommandDetector.Detect(lines, workspaceRoot);

        if (detected.Count == 0)
        {
            Console.WriteLine("Nothing to do.");
            return;
        }

        PrintDetected(detected);

        var actionable = detected.Where(a => a.IsActionable).ToList();
        if (actionable.Count == 0)
        {
            Console.WriteLine("No actionable commands — nothing was executed.");
            return;
        }

        var approved = CollectApprovals(actionable);
        if (approved is null)
        {
            Console.WriteLine("Cancelled — nothing was executed.");
            return;
        }

        if (approved.Count == 0)
        {
            Console.WriteLine("Nothing approved — no snapshot taken, nothing was executed.");
            return;
        }

        if (!ConfirmSnapshotSize(workspaceRoot))
        {
            Console.WriteLine("Aborted — nothing was executed.");
            return;
        }

        WorkspaceSnapshot snapshot;
        try
        {
            snapshot = WorkspaceSnapshot.Capture(workspaceRoot);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not snapshot the workspace: {ex.Message}");
            Console.WriteLine("Aborted — nothing was executed.");
            return;
        }

        Execute(snapshot, detected, actionable.Count, approved,
            ref currentProject, configuredRules, workspaceRoot, setLastCreatedProject, pipeline);
    }

    private static List<string> ReadBlock()
    {
        Console.WriteLine("Enter one command per line. A blank line ends the batch.");

        var lines = new List<string>();

        while (true)
        {
            Console.Write("... ");
            var line = Console.ReadLine();

            // null is EOF / closed stdin: end the block rather than busy-loop.
            if (line is null || line.Trim().Length == 0)
                break;

            lines.Add(line);
        }

        return lines;
    }

    private static void PrintDetected(IReadOnlyList<DetectedAction> detected)
    {
        var actionableCount = detected.Count(a => a.IsActionable);

        Console.WriteLine();
        Console.WriteLine($"Detected {detected.Count} line(s), {actionableCount} actionable:");
        Console.WriteLine();

        int number = 0;
        foreach (var action in detected)
        {
            if (action.IsActionable)
                Console.WriteLine($"  [{++number}] {ActionFormatter.Format(action)}");
            else
                Console.WriteLine($"   -  {ActionFormatter.Format(action)}");
        }

        Console.WriteLine();
    }

    /// <summary>
    /// Walks the approval prompts. Returns null when the user cancels the batch.
    /// </summary>
    private static List<DetectedAction>? CollectApprovals(IReadOnlyList<DetectedAction> actionable)
    {
        var approved = new List<DetectedAction>();

        for (int i = 0; i < actionable.Count; i++)
        {
            while (true)
            {
                Console.Write($"  [{i + 1}] {ActionFormatter.Format(actionable[i])}   approve? (y/n) ");

                var reply = ApprovalReplyParser.Parse(Console.ReadLine());

                if (reply == ApprovalReply.Cancel)
                    return null;

                if (reply == ApprovalReply.Reprompt)
                    continue;

                if (reply == ApprovalReply.Approve)
                    approved.Add(actionable[i]);

                break;
            }
        }

        return approved;
    }

    private static bool ConfirmSnapshotSize(string workspaceRoot)
    {
        var sizing = WorkspaceSnapshot.Measure(workspaceRoot);

        if (sizing.FileCount <= MaxSnapshotFiles && sizing.Bytes <= MaxSnapshotBytes)
            return true;

        var megabytes = sizing.Bytes / (1024.0 * 1024.0);

        Console.WriteLine($"Workspace is {sizing.FileCount:N0} files ({megabytes:N0} MB) after exclusions.");
        Console.WriteLine("Snapshotting may take a while.");
        Console.WriteLine("  [p]roceed with snapshot   [a]bort   (Enter = abort)");
        Console.Write("> ");

        var reply = Console.ReadLine();

        // Anything but an explicit "p" aborts, including Enter and EOF.
        return reply is not null
            && reply.Trim().Equals("p", StringComparison.OrdinalIgnoreCase);
    }

    private static void Execute(
        WorkspaceSnapshot snapshot,
        IReadOnlyList<DetectedAction> detected,
        int actionableCount,
        List<DetectedAction> approved,
        ref ProjectMetaData? currentProject,
        List<PropertyRule> configuredRules,
        string workspaceRoot,
        Action<ProjectMetaData?> setLastCreatedProject,
        NaturalLanguagePipeline pipeline)
    {
        int unactionable = detected.Count - actionableCount;
        int rejected = actionableCount - approved.Count;

        Console.WriteLine($"Executing {approved.Count} approved action(s)...");

        int succeeded = 0;
        DetectedAction? failedAction = null;
        string failureReason = string.Empty;

        foreach (var action in approved)
        {
            try
            {
                var outcome = pipeline.Handle(
                    action.Sentence, ref currentProject, configuredRules,
                    workspaceRoot, setLastCreatedProject);

                if (!outcome.Handled)
                {
                    failedAction = action;
                    failureReason = "command was not handled";
                    break;
                }

                succeeded++;
            }
            catch (Exception ex)
            {
                failedAction = action;
                failureReason = $"{ex.GetType().Name}: {ex.Message}";
                break;
            }
        }

        if (failedAction is null)
        {
            snapshot.Dispose();

            Console.WriteLine();
            Console.WriteLine($"Done. {succeeded} applied, 0 failed, 0 rolled back, " +
                              $"0 not attempted, {rejected} rejected, {unactionable} unactionable.");
            return;
        }

        Console.WriteLine($"  FAILED: {failureReason}");
        Console.WriteLine();
        Console.WriteLine("Rolling back...");

        try
        {
            snapshot.Restore();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ROLLBACK FAILED: {ex.Message}");
            Console.WriteLine("  The workspace is in an unknown state.");
            Console.WriteLine($"  Your pre-batch snapshot is preserved at: {snapshot.SnapshotPath}");
            return;
        }

        // Restore reset ProjectManager.CurrentProject; keep the caller's copy in step.
        currentProject = ProjectManager.CurrentProject;

        Console.WriteLine("  workspace restored to pre-batch state");
        snapshot.Dispose();

        int notAttempted = approved.Count - succeeded - 1;

        Console.WriteLine();
        Console.WriteLine($"Aborted. 0 applied, 1 failed, {succeeded} rolled back, " +
                          $"{notAttempted} not attempted, {rejected} rejected, {unactionable} unactionable.");
    }
}
```

- [ ] **Step 2: Wire it into the CLI**

In `CodeGenerator/App/CodeGeneratorStarter.cs`, add the field beside `_buildApp` (currently line 23):

```csharp
    private readonly NaturalLanguagePipeline _pipeline = new();
    private readonly BuildAppCommand _buildApp = new();
    private readonly BatchCommand _batch = new();
```

And in `Run()`, add the dispatch immediately after the existing `IsBuildApp` block (currently lines 127–131), inside the `try`:

```csharp
                if (BuildAppCommand.IsBuildApp(input))
                {
                    _buildApp.Handle(input, _workspaceRoot);
                    continue;
                }

                if (BatchCommand.IsBatch(input))
                {
                    _batch.Handle(ref currentProject, _configuredRules, _workspaceRoot,
                        r => ProjectManager.CurrentProject = r, _pipeline);
                    continue;
                }
```

Add a line to `PrintHeader()` after the TAB-suggestions line:

```csharp
        Console.WriteLine("Type 'batch' to enter several commands and review them before they run.");
```

Do **not** touch `RunOnce()`. It is the non-interactive scripting path, and a batch that prompts for approval is meaningless there.

- [ ] **Step 3: Build and run the full suite**

Run: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx`

Expected: PASS. Every pre-existing test still passes — nothing in the execution path changed.

- [ ] **Step 4: Verify end to end against the real CLI**

`dotnet test` does **not** refresh the CLI's `bin`, so the executable would still be the pre-change build. Rebuild first:

```bash
dotnet build CodeGenerator/CodeGenerator.csproj
```

Then drive the happy path in a scratch workspace:

```bash
mkdir -p /tmp/cg_ok && cd /tmp/cg_ok
PROJECTPATH=/tmp/cg_ok dotnet run --project D:/SmartCodeGenerator-Repository/CodeGenerator/CodeGenerator.csproj
```

At the prompt type `batch`, then:

```
create a Customer class
the big red car
create a Order class
<blank line>
```

Expected: two numbered actions, one `(skipped)` line for the prose. Answer `y`, `n`. Expected summary: `1 applied, 0 failed, 0 rolled back, 0 not attempted, 1 rejected, 1 unactionable.` Confirm `Customer.cs` exists and `Order.cs` does not. Type `exit`.

- [ ] **Step 5: Verify rollback end to end**

This is the check no unit test can give. In a fresh scratch workspace:

```bash
mkdir -p /tmp/cg_rollback && cd /tmp/cg_rollback
PROJECTPATH=/tmp/cg_rollback dotnet run --project D:/SmartCodeGenerator-Repository/CodeGenerator/CodeGenerator.csproj
```

Type `batch`, then:

```
create a Customer class
delete class Nonexistent
<blank line>
```

Approve both with `y`, `y`.

Expected: action 1 succeeds, action 2 fails, `Rolling back...`, `workspace restored to pre-batch state`, and the summary `0 applied, 1 failed, 1 rolled back, 0 not attempted, 0 rejected, 0 unactionable.`

Then assert the workspace is exactly as it was — **`Customer.cs` must not exist**:

```bash
ls -la /tmp/cg_rollback
```

Expected: no `Customer.cs`. If it is there, rollback is broken; do not proceed.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerator/App/BatchCommand.cs CodeGenerator/App/CodeGeneratorStarter.cs
git commit -m "Add the batch command to the CLI

Reads a block of sentences, shows what was detected, executes only the
approved actions, and restores the workspace if any of them fails."
```

---

### Task 6: Documentation comments and follow-up issue

**Files:**
- Modify: `README.md` (command reference)
- Modify: the five files created in Tasks 1–5, only if any public member lacks a `///` comment

Issue #29 item 7 asks for comments on all classes, functions, and projects. Per the spec's decision 8 that is scoped to the files this branch touches; the remaining sweep becomes a follow-up.

- [ ] **Step 1: Audit the new files for undocumented public members**

Run:

```bash
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -warnaserror:CS1591 -p:GenerateDocumentationFile=true 2>&1 | grep -E "NaturalLanguageBridge|WorkspaceSnapshot|BatchCommand" | head -20
```

Expected: no output. The compiler flags any public member missing a `///` comment. If a member is flagged, document it.

This is a check, not a permanent project setting — do not commit a `GenerateDocumentationFile` property to any `.csproj`. Turning it on repo-wide would fail the build on the 147 files the sweep has not reached yet.

- [ ] **Step 2: Document `batch` in the README**

In `README.md`, add to the command reference:

```markdown
### `batch`

Enter several commands at once, one per line, ending with a blank line. The CLI
shows every command it detected — marking any line it could not understand —
and asks you to approve each one. Only approved commands run.

If any approved command fails, the whole batch is rolled back and the workspace
is restored to its pre-batch state.
```

- [ ] **Step 3: Verify the README claim is true**

Re-read Task 5 Step 5's result. The README says the workspace is restored; that must be what was observed, not what was intended. If rollback left anything behind, fix the code or the README — not the memory of the test.

- [ ] **Step 4: Open the follow-up issue for the docs sweep**

```bash
gh issue create \
  --title "Add XML doc comments across the remaining source files" \
  --body "Follow-up to #29, which scoped its 'add comments' item to the files that branch touched.

147 of 284 source files have no \`///\` doc comment at all:

- CodeGenerationEngine — 137 files
- NaturalLanguageEngine — 109 files
- DomainDiscovery — 23 files
- AppScaffolding — 9 files
- CodeGenerator — 4 files
- NaturalLanguageBridge — 2 files

Suggested approach: one project per PR, public API surface first, enforced with
\`<GenerateDocumentationFile>\` and CS1591 once a project is clean.

Bundling this with #29's behaviour change would have made both unreviewable."
```

- [ ] **Step 5: Commit**

```bash
git add README.md
git commit -m "Document the batch command"
```

---

## Self-Review

**Spec coverage:**

| Spec requirement | Task |
|---|---|
| Multi-line block input, blank line / EOF ends | 5 (`ReadBlock`) |
| Unrecognized lines listed, not droppable/approvable | 2 (`Note`, `IsActionable`), 5 (`PrintDetected`) |
| Per-item y/n walk | 1, 5 (`CollectApprovals`) |
| Enter re-prompts, never approves | 1 |
| EOF cancels the batch | 1, 5 |
| Re-parse before executing each | 5 (`pipeline.Handle(action.Sentence, ...)`) |
| Stop at first failure | 5 (`break` in `Execute`) |
| Rollback via snapshot + restore | 4, 5 |
| `ProjectManager.dat` captured | 4 |
| `ContextManager.Current` saved/restored in `Detect` | 2 |
| `ContextManager.Current` captured by snapshot | 4 |
| Size cap warns, defaults to abort | 5 (`ConfirmSnapshotSize`) |
| `Capture` failure aborts before executing | 5 |
| `Restore` failure keeps snapshot, prints path | 4 (`_restoreFailed`), 5 |
| Six-bucket summary, sums to line count | 5 (`Execute`) |
| Detection writes nothing to workspace | 2 (test) |
| No batch support in `RunOnce()` | 5 Step 2 |
| Comments on new files | 1–5 inline, 6 audit |
| Follow-up docs issue | 6 |

**Type consistency:** `ApprovalReplyParser.Parse` (not `ApprovalReply.Parse` — the enum owns that name); `ActionFormatter.Format`; `BatchCommandDetector.Detect`; `WorkspaceSnapshot.Measure/Capture/Restore/Dispose`; `SnapshotSizing.FileCount/Bytes`; `DetectedAction.Sentence/Intent/Confidence/Preview/Note/IsActionable`. `PipelineOutcome.Handled` is the existing property name. `ParsedCommand` has `NewClassName` but no `NewPropertyName`, which is why Task 3 renders property renames with the default branch.

**Placeholder scan:** none. Every step contains the code or the exact command.
