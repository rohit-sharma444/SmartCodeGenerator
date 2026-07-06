# NLE-Led Front Orchestration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make NaturalLanguageEngine (NLE) the front of the flow — analyze each command with NLE first, then feed its action verb into CodeGenerator to drive code generation — by inverting the project dependency through a new `NaturalLanguageBridge` library.

**Architecture:** CodeGenerationEngine stops referencing NLE and instead accepts an injected `string? nlpAction`. A new `NaturalLanguageBridge` library references both NLE and CodeGenerationEngine, runs NLE once, and calls CodeGenerator's `ProcessInput` with the extracted verb. The existing `CodeGenerator.exe` REPL becomes the NLE-led front by delegating to the bridge.

**Tech Stack:** C# / .NET 10.0, Roslyn (existing), NaturalLanguageEngine (existing), MSTest 4.0.2 (CodeGenerator.Tests).

## Global Constraints

- Repo root for all commands: `D:\PERSONAL-REPOSITORY`. Run every `dotnet`/`git` command from there.
- Target framework everywhere: `net10.0`; `Nullable` and `ImplicitUsings` are `enable`d in all touched projects.
- **No circular references.** After this work: `CodeGenerationEngine` references **no** NLE. `NaturalLanguageBridge` references NLE + CodeGenerationEngine. `CodeGenerator.exe` references CodeGenerationEngine + NaturalLanguageBridge. NLE references nothing.
- The injected signal is a plain `string? nlpAction`; a `null` signal must leave CodeGenerator's scoring identical to keyword-only behavior (graceful degradation, never throws).
- The +25 `GetNlpActionScore` logic in `IntentAnalyzer` is unchanged — only the *source* of the verb changes (injected, not self-fetched).
- NLE's pipeline is **not thread-safe** for concurrent `Analyze()`; the bridge's `NlpActionProvider` serializes calls with a lock (preserved from the moved code).
- CodeGenerator.Tests uses MSTest (`[TestClass]`/`[TestMethod]`/`[DataTestMethod]`/`[DataRow]`; the `Microsoft.VisualStudio.TestTools.UnitTesting` global using is configured).
- The production `IntentAnalyzer` under test must be referenced as `CodeGenerationEngine.Core.IntentAnalyzer` in tests (a pre-existing unrelated `CodeGenerationEngine.Models.IntentAnalyzer` exists).

---

### Task 1: Inject `nlpAction` through CodeGenerationEngine

Invert the internal coupling: `IntentAnalyzer` stops calling NLE itself and instead receives the verb as a parameter, threaded from `ProcessInput`. (The old NLE-facing `NlpActionProvider` in CodeGenerationEngine is left in place, now unused; Task 2 removes it and drops the NLE reference.)

**Files:**
- Modify: `CodeGenerator/CodeGenerationEngine/Core/IntentAnalyzer.cs`
- Modify: `CodeGenerator/CodeGenerationEngine/Core/SemanticAnalyzer.cs`
- Modify: `CodeGenerator/CodeGenerationEngine/Service/CommandHandlingService.cs`
- Test: `CodeGenerator/CodeGenerator.Tests/Core/IntentAnalyzerNlpTests.cs` (replace contents)

**Interfaces:**
- Produces: `IntentAnalyzer.Detect(string sentence, string? nlpAction = null) : IntentMatch`; `SemanticAnalyzer.Analyze(string input, string? nlpAction = null) : SemanticModel`; `CommandHandlingService.ProcessInput(string, ref ProjectMetaData?, List<PropertyRule>, string, Action<ProjectMetaData?>, string? nlpAction = null) : bool`. Task 3's bridge calls the `ProcessInput` overload.

- [ ] **Step 1: Replace the test file with injection-based tests**

Replace the entire contents of `CodeGenerator/CodeGenerator.Tests/Core/IntentAnalyzerNlpTests.cs`:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core;

[TestClass]
public class IntentAnalyzerNlpTests
{
    // Regression: canonical commands still resolve, with the NLE verb injected
    // exactly as the runtime pipeline supplies it.
    [DataTestMethod]
    [DataRow("create a Customer class", "create", IntentType.CreateClass)]
    [DataRow("add method Save to Order", "add", IntentType.CreateMethod)]
    [DataRow("list projects", "list", IntentType.ListProjects)]
    [DataRow("delete class Foo", "delete", IntentType.DeleteClass)]
    [DataRow("rename class A to B", "rename", IntentType.RenameClass)]
    [DataRow("create a service for Payment", "create", IntentType.CreateService)]
    [DataRow("remove the class Foo", "remove", IntentType.DeleteClass)]
    [DataRow("add property Email to Customer", "add", IntentType.CreateProperty)]
    [DataRow("generate a repository for Order", "generate", IntentType.CreateRepository)]
    public void Detect_CanonicalCommands_WithInjectedVerb_ResolveToExpectedIntent(
        string input, string nlpAction, IntentType expected)
    {
        Assert.AreEqual(expected,
            CodeGenerationEngine.Core.IntentAnalyzer.Detect(input, nlpAction).Intent);
    }

    // Value-add: "validate Foo" scores 30 on keywords (< 40 => Unknown); the
    // injected "validate" verb (+25, unique to ValidateProject) lifts it past 40.
    [TestMethod]
    public void Detect_TerseCommandWithInjectedVerb_ResolvedViaNlpSignal()
    {
        Assert.AreEqual(IntentType.ValidateProject,
            CodeGenerationEngine.Core.IntentAnalyzer.Detect("validate Foo", "validate").Intent);
    }

    // Without the signal, the same terse command stays Unknown -> proves the
    // injected verb is what does the work.
    [TestMethod]
    public void Detect_TerseCommandWithoutSignal_IsUnknown()
    {
        Assert.AreEqual(IntentType.Unknown,
            CodeGenerationEngine.Core.IntentAnalyzer.Detect("validate Foo", null).Intent);
    }

    // A null signal must not disturb an already-correct keyword-only result.
    [TestMethod]
    public void Detect_CanonicalCommandWithoutSignal_StillResolves()
    {
        Assert.AreEqual(IntentType.CreateClass,
            CodeGenerationEngine.Core.IntentAnalyzer.Detect("create a Customer class", null).Intent);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (compile error)**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~IntentAnalyzerNlpTests"`
Expected: FAIL to build — `Detect` has no overload taking two arguments yet (the current `Detect(string)` self-fetches from NLE).

- [ ] **Step 3: Change `IntentAnalyzer` to accept the injected verb**

In `CodeGenerator/CodeGenerationEngine/Core/IntentAnalyzer.cs`:

3a. Delete the first line `using CodeGenerationEngine.Core.Nlp;`.

3b. Replace:

```csharp
    public static IntentMatch Detect(string sentence)
        => DetectWithConfidence(sentence);

    private static IntentMatch DetectWithConfidence(string sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence))
            return new(IntentType.Unknown, 0, 0, "Empty input");

        sentence = NormalizeSentence(sentence);

        var nlpAction = NlpActionProvider.GetPrimaryAction(sentence);

        var tokens = TextTokenizer.Tokenize(sentence)
```

with:

```csharp
    public static IntentMatch Detect(string sentence, string? nlpAction = null)
        => DetectWithConfidence(sentence, nlpAction);

    private static IntentMatch DetectWithConfidence(string sentence, string? nlpAction)
    {
        if (string.IsNullOrWhiteSpace(sentence))
            return new(IntentType.Unknown, 0, 0, "Empty input");

        sentence = NormalizeSentence(sentence);

        var tokens = TextTokenizer.Tokenize(sentence)
```

(Everything else stays: the loop already calls `CalculateScore(sentence, tokens, rule, nlpAction)`, and `CalculateScore`/`GetNlpActionScore` are unchanged.)

- [ ] **Step 4: Thread `nlpAction` through `SemanticAnalyzer`**

In `CodeGenerator/CodeGenerationEngine/Core/SemanticAnalyzer.cs`, change the signature and the `Detect` call:

```csharp
    public static SemanticModel Analyze(string input, string? nlpAction = null)
```

and change:

```csharp
        // Detect intent
        var intent = IntentAnalyzer.Detect(input);
```

to:

```csharp
        // Detect intent
        var intent = IntentAnalyzer.Detect(input, nlpAction);
```

- [ ] **Step 5: Thread `nlpAction` through `CommandHandlingService`**

In `CodeGenerator/CodeGenerationEngine/Service/CommandHandlingService.cs`:

5a. Add the parameter to `ProcessInput` (append after `setLastCreatedProject`):

```csharp
    public static bool ProcessInput(
        string input,
        ref ProjectMetaData? currentProject,
        List<PropertyRule> configuredRules,
        string workspaceRoot,
        Action<ProjectMetaData?> setLastCreatedProject,
        string? nlpAction = null)
```

5b. Change the `ParseInput` call inside `ProcessInput` from `ParseInput(input, workspaceRoot)` to `ParseInput(input, workspaceRoot, nlpAction)`.

5c. Change `ParseInput` itself:

```csharp
    private static ParsedCommand ParseInput(string input, string workspaceRoot, string? nlpAction)
    {
        var semantic = SemanticAnalyzer.Analyze(input, nlpAction);
        var intentType = Core.IntentAnalyzer.Detect(semantic);
        return EntityExtractor.Extract(semantic, intentType, workspaceRoot);
    }
```

- [ ] **Step 6: Run the focused tests to verify they pass**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~IntentAnalyzerNlpTests"`
Expected: PASS (13 cases: 9 regression rows + 4 methods).

- [ ] **Step 7: Run the full CodeGenerator.Tests suite**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj`
Expected: PASS. (The old NLE-backed `NlpActionProviderTests` still runs against the yet-unremoved provider — that's fine here; it moves in Task 2.)

- [ ] **Step 8: Commit**

```bash
git add CodeGenerator/CodeGenerationEngine/Core/IntentAnalyzer.cs CodeGenerator/CodeGenerationEngine/Core/SemanticAnalyzer.cs CodeGenerator/CodeGenerationEngine/Service/CommandHandlingService.cs CodeGenerator/CodeGenerator.Tests/Core/IntentAnalyzerNlpTests.cs
git commit -m "Invert intent signal: IntentAnalyzer takes injected nlpAction

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: Create `NaturalLanguageBridge` and remove NLE from CodeGenerationEngine

Move the NLE-facing adapter up into a new library, extend its API, then sever CodeGenerationEngine's NLE dependency.

**Files:**
- Create: `CodeGenerator/NaturalLanguageBridge/NaturalLanguageBridge.csproj`
- Create: `CodeGenerator/NaturalLanguageBridge/NlpActionProvider.cs`
- Delete: `CodeGenerator/CodeGenerationEngine/Core/Nlp/NlpActionProvider.cs`
- Modify: `CodeGenerator/CodeGenerationEngine/CodeGenerationEngine.csproj` (remove NLE ProjectReference)
- Modify: `CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj` (add bridge ProjectReference)
- Delete: `CodeGenerator/CodeGenerator.Tests/Core/NlpActionProviderTests.cs` (old location)
- Create: `CodeGenerator/CodeGenerator.Tests/Bridge/NlpActionProviderBridgeTests.cs`
- Modify: `SmartCodeGenerator/SmartCodeGenerator.slnx` (add the bridge project)

**Interfaces:**
- Produces: `NaturalLanguageBridge.NlpActionProvider` with `Analyze(string) : SemanticResult?`, `GetPrimaryAction(SemanticResult?) : string?`, `GetPrimaryAction(string) : string?`. Task 3 consumes all three.

- [ ] **Step 1: Create the bridge project (no provider yet)**

Create `CodeGenerator/NaturalLanguageBridge/NaturalLanguageBridge.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\NaturalLanguageEngine\NaturalLanguageEngine\NaturalLanguageEngine.csproj" />
    <ProjectReference Include="..\CodeGenerationEngine\CodeGenerationEngine.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Point the test project at the bridge and write the failing bridge test**

In `CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj`, add to the existing `ItemGroup` that holds the `CodeGenerationEngine` ProjectReference:

```xml
    <ProjectReference Include="..\NaturalLanguageBridge\NaturalLanguageBridge.csproj" />
```

Delete the old `CodeGenerator/CodeGenerator.Tests/Core/NlpActionProviderTests.cs`.

Create `CodeGenerator/CodeGenerator.Tests/Bridge/NlpActionProviderBridgeTests.cs`:

```csharp
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

[TestClass]
public class NlpActionProviderBridgeTests
{
    [DataTestMethod]
    [DataRow("create a Customer class", "create")]
    [DataRow("add method Save to Order", "add")]
    [DataRow("list projects", "list")]
    [DataRow("delete class Foo", "delete")]
    [DataRow("rename class A to B", "rename")]
    [DataRow("validate foo", "validate")]
    public void GetPrimaryAction_CommandVerb_ReturnsLowercasedVerb(string input, string expected)
    {
        Assert.AreEqual(expected, NlpActionProvider.GetPrimaryAction(input));
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("the big red car")]
    public void GetPrimaryAction_BlankOrNonActionable_ReturnsNull(string input)
    {
        Assert.IsNull(NlpActionProvider.GetPrimaryAction(input));
    }

    [TestMethod]
    public void Analyze_Blank_ReturnsNull()
    {
        Assert.IsNull(NlpActionProvider.Analyze("   "));
    }

    [TestMethod]
    public void Analyze_Command_ReturnsResultWhoseActionIsTheVerb()
    {
        var result = NlpActionProvider.Analyze("create a Customer class");
        Assert.IsNotNull(result);
        Assert.AreEqual("create", NlpActionProvider.GetPrimaryAction(result));
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~NlpActionProviderBridgeTests"`
Expected: FAIL to build — `NaturalLanguageBridge.NlpActionProvider` does not exist yet.

- [ ] **Step 4: Create the extended provider in the bridge**

Create `CodeGenerator/NaturalLanguageBridge/NlpActionProvider.cs`:

```csharp
using NaturalLanguageEngine.Abstractions.Semantics;
using NaturalLanguageEngine.Models.Semantics;

namespace NaturalLanguageBridge;

/// <summary>
/// Wraps NaturalLanguageEngine for the code-generation front. Builds the engine
/// once (lazily) and reuses it. The pipeline returned by <c>EngineBuilder</c>
/// shares mutable stage instances across calls and is not safe for concurrent
/// <c>Analyze</c> invocations, so calls are serialized with a lock.
/// </summary>
public static class NlpActionProvider
{
    private static readonly Lazy<ISemanticAnalyzer> Analyzer =
        new(() => EngineBuilder.BuildDefault().Analyzer);

    private static readonly object AnalyzeLock = new();

    /// <summary>
    /// Runs NLE once over <paramref name="input"/>. Returns null when the input
    /// is blank or NLE throws. Never throws.
    /// </summary>
    public static SemanticResult? Analyze(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        try
        {
            lock (AnalyzeLock)
            {
                return Analyzer.Value.Analyze(input);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts the lowercased primary action verb from an NLE result, or null
    /// when the result is null or has no recognized action.
    /// </summary>
    public static string? GetPrimaryAction(SemanticResult? result)
    {
        if (result is null)
            return null;

        var action = result.Entities
            .FirstOrDefault(e => e.Type == EntityType.Action);

        if (action is not null)
        {
            var verb = action.Text
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault();

            if (!string.IsNullOrWhiteSpace(verb))
                return verb.ToLowerInvariant();
        }

        // Fallback: derive from the intent name ("AddIntent" -> "add"),
        // ignoring the generic no-verb "DescribeIntent".
        var name = result.Intent?.Name;
        if (!string.IsNullOrWhiteSpace(name)
            && name.EndsWith("Intent", StringComparison.Ordinal)
            && !name.Equals("DescribeIntent", StringComparison.Ordinal))
        {
            var verb = name[..^"Intent".Length];
            if (verb.Length > 0)
                return verb.ToLowerInvariant();
        }

        return null;
    }

    /// <summary>Convenience: analyze then extract. Null on blank / no-action / failure.</summary>
    public static string? GetPrimaryAction(string input)
        => GetPrimaryAction(Analyze(input));
}
```

- [ ] **Step 5: Remove NLE from CodeGenerationEngine**

Delete `CodeGenerator/CodeGenerationEngine/Core/Nlp/NlpActionProvider.cs`.

In `CodeGenerator/CodeGenerationEngine/CodeGenerationEngine.csproj`, delete the entire `ItemGroup` that contains the NLE ProjectReference:

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\NaturalLanguageEngine\NaturalLanguageEngine\NaturalLanguageEngine.csproj" />
  </ItemGroup>
```

- [ ] **Step 6: Add the bridge to the solution**

In `SmartCodeGenerator/SmartCodeGenerator.slnx`, add this line inside `<Solution>` alongside the other `<Project>` entries:

```xml
  <Project Path="../../../PERSONAL-REPOSITORY/CodeGenerator/NaturalLanguageBridge/NaturalLanguageBridge.csproj" />
```

- [ ] **Step 7: Verify CodeGenerationEngine no longer references NLE, and tests pass**

Run: `dotnet build CodeGenerator/CodeGenerationEngine/CodeGenerationEngine.csproj`
Expected: build succeeds with no reference to NaturalLanguageEngine (a clean compile proves the dependency is gone).

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj`
Expected: PASS — the new `NlpActionProviderBridgeTests` (10 cases) plus the Task 1 tests; the old `NlpActionProviderTests` is gone.

- [ ] **Step 8: Commit**

```bash
git add CodeGenerator/NaturalLanguageBridge/ CodeGenerator/CodeGenerationEngine/CodeGenerationEngine.csproj CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj CodeGenerator/CodeGenerator.Tests/Bridge/NlpActionProviderBridgeTests.cs SmartCodeGenerator/SmartCodeGenerator.slnx
git add -u CodeGenerator/CodeGenerationEngine/Core/Nlp CodeGenerator/CodeGenerator.Tests/Core/NlpActionProviderTests.cs
git commit -m "Add NaturalLanguageBridge; remove NLE dependency from CodeGenerationEngine

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: `NaturalLanguagePipeline` orchestrator

Add the orchestrator that runs NLE once, surfaces its analysis, and hands the verb to CodeGenerator.

**Files:**
- Create: `CodeGenerator/NaturalLanguageBridge/NaturalLanguagePipeline.cs`
- Test: `CodeGenerator/CodeGenerator.Tests/Bridge/NaturalLanguagePipelineTests.cs`

**Interfaces:**
- Consumes: `NlpActionProvider.Analyze`/`GetPrimaryAction` (Task 2); `CommandHandlingService.ProcessInput(..., string? nlpAction)` (Task 1).
- Produces: `NaturalLanguagePipeline.Handle(string, ref ProjectMetaData?, List<PropertyRule>, string, Action<ProjectMetaData?>, Action<SemanticResult?>?) : PipelineOutcome`; `PipelineOutcome(bool Handled, SemanticResult? Analysis)`. Task 4 consumes these.

- [ ] **Step 1: Write the failing end-to-end test**

Create `CodeGenerator/CodeGenerator.Tests/Bridge/NaturalLanguagePipelineTests.cs`:

```csharp
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;
using NaturalLanguageEngine.Models.Semantics;

namespace CodeGenerator.Tests.Bridge;

[TestClass]
public class NaturalLanguagePipelineTests
{
    [TestMethod]
    public void Handle_CreateClassCommand_RunsNleThenGenerates()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(), "nlbridge_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            ProjectMetaData? project = null;
            SemanticResult? captured = null;
            var pipeline = new NaturalLanguagePipeline();

            var outcome = pipeline.Handle(
                "create a Customer class with Name string",
                ref project,
                new List<PropertyRule>(),
                tempRoot,
                _ => { },
                onAnalyzed: a => captured = a);

            // NLE ran and its analysis was surfaced before generation.
            Assert.IsNotNull(outcome.Analysis);
            Assert.AreSame(outcome.Analysis, captured);
            Assert.AreEqual("create", NlpActionProvider.GetPrimaryAction(outcome.Analysis));

            // The command was handled (code generated) end-to-end.
            Assert.IsTrue(outcome.Handled);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort */ }
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~NaturalLanguagePipelineTests"`
Expected: FAIL to build — `NaturalLanguagePipeline` / `PipelineOutcome` do not exist yet.

- [ ] **Step 3: Implement the pipeline**

Create `CodeGenerator/NaturalLanguageBridge/NaturalLanguagePipeline.cs`:

```csharp
using CodeGenerationEngine.Models;
using CodeGenerationEngine.Service;
using NaturalLanguageEngine.Models.Semantics;

namespace NaturalLanguageBridge;

/// <summary>Result of orchestrating one command: whether CodeGenerator handled
/// it, and the NLE analysis produced for it (null if NLE failed).</summary>
public sealed record PipelineOutcome(bool Handled, SemanticResult? Analysis);

/// <summary>
/// NLE-led entry point for a single command: analyze with NLE once, surface the
/// analysis, then delegate parsing + code generation to CodeGenerator with the
/// extracted action verb.
/// </summary>
public sealed class NaturalLanguagePipeline
{
    public PipelineOutcome Handle(
        string input,
        ref ProjectMetaData? project,
        List<PropertyRule> configuredRules,
        string workspaceRoot,
        Action<ProjectMetaData?> setLastCreatedProject,
        Action<SemanticResult?>? onAnalyzed = null)
    {
        var analysis = NlpActionProvider.Analyze(input);
        onAnalyzed?.Invoke(analysis);

        var nlpAction = NlpActionProvider.GetPrimaryAction(analysis);

        bool handled = CommandHandlingService.ProcessInput(
            input,
            ref project,
            configuredRules,
            workspaceRoot,
            setLastCreatedProject,
            nlpAction);

        return new PipelineOutcome(handled, analysis);
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~NaturalLanguagePipelineTests"`
Expected: PASS.

- [ ] **Step 5: Run the full CodeGenerator.Tests suite**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerator/NaturalLanguageBridge/NaturalLanguagePipeline.cs CodeGenerator/CodeGenerator.Tests/Bridge/NaturalLanguagePipelineTests.cs
git commit -m "Add NaturalLanguagePipeline orchestrator (NLE -> CodeGenerator)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: Wire `CodeGenerator.exe` to lead with NLE

Make the REPL delegate to the bridge pipeline and print NLE's analysis per command.

**Files:**
- Modify: `CodeGenerator/CodeGenerator/CodeGenerator.csproj` (add bridge ProjectReference)
- Modify: `CodeGenerator/CodeGenerator/App/CodeGeneratorStarter.cs`

**Interfaces:**
- Consumes: `NaturalLanguagePipeline.Handle(...)` and `PipelineOutcome` (Task 3).

- [ ] **Step 1: Add the bridge reference to the exe**

In `CodeGenerator/CodeGenerator/CodeGenerator.csproj`, add to the existing `ItemGroup` that holds the `CodeGenerationEngine` ProjectReference:

```xml
    <ProjectReference Include="..\NaturalLanguageBridge\NaturalLanguageBridge.csproj">
      <Private>True</Private>
    </ProjectReference>
```

- [ ] **Step 2: Delegate the REPL to the pipeline**

In `CodeGenerator/CodeGenerator/App/CodeGeneratorStarter.cs`:

2a. Add two usings at the top (after the existing `using` lines):

```csharp
using NaturalLanguageBridge;
using NaturalLanguageEngine.Models.Semantics;
```

2b. Add a pipeline field next to the other private fields (e.g. after `private readonly string _encryptedHistoryPath;`):

```csharp
    private readonly NaturalLanguagePipeline _pipeline = new();
```

2c. In `Run()`, replace the delegation call:

```csharp
                // delegate to the static CommandHandlingService
                CommandHandlingService.ProcessInput(input, ref currentProject,
                     _configuredRules, _workspaceRoot, r => ProjectManager.CurrentProject = r);
```

with:

```csharp
                // NLE analyzes first, then CodeGenerator generates from the result
                _pipeline.Handle(input, ref currentProject,
                     _configuredRules, _workspaceRoot, r => ProjectManager.CurrentProject = r,
                     onAnalyzed: DisplayNleAnalysis);
```

2d. Add this private helper (place it near `PrintHeader`):

```csharp
    private static void DisplayNleAnalysis(SemanticResult? analysis)
    {
        if (analysis is null)
            return;

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"[NLE] intent: {analysis.Intent?.Name ?? "(none)"}");

        if (analysis.Entities.Count > 0)
        {
            var entities = string.Join(", ",
                analysis.Entities.Select(e => $"{e.Type}:\"{e.Text}\""));
            Console.WriteLine($"[NLE] entities: {entities}");
        }

        Console.ResetColor();
    }
```

- [ ] **Step 3: Build the front to verify it compiles**

Run: `dotnet build CodeGenerator/CodeGenerator/CodeGenerator.csproj`
Expected: build succeeds (transitively builds NaturalLanguageBridge, CodeGenerationEngine, and NaturalLanguageEngine). The REPL is interactive (`Console.ReadKey`), so its behavior isn't unit-tested here; the NLE→codegen behavior is covered by Task 3's pipeline test.

- [ ] **Step 4: Run the full test suite to confirm nothing regressed**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/CodeGenerator/CodeGenerator.csproj CodeGenerator/CodeGenerator/App/CodeGeneratorStarter.cs
git commit -m "Make CodeGenerator.exe the NLE-led front via NaturalLanguagePipeline

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Final verification

- [ ] Confirm no circular reference and clean layering — CodeGenerationEngine must not mention NLE:
  Run: `dotnet build CodeGenerator/CodeGenerator/CodeGenerator.csproj`
  Expected: succeeds. Then confirm CodeGenerationEngine is NLE-free — search its folder for any NaturalLanguageEngine usings; expect no matches:
  `grep -rn "NaturalLanguageEngine" CodeGenerator/CodeGenerationEngine --include=*.cs` → no results.
- [ ] Run both test suites:
  - `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj` → all pass.
  - `dotnet test NaturalLanguageEngine/NaturalLanguageEngine.Tests/NaturalLanguageEngine.Tests.csproj` → 171/173 (the 2 `PipelineLoggerTests` timing failures are pre-existing and unrelated).
- [ ] Sanity-drive the front: from `D:\PERSONAL-REPOSITORY`, `dotnet run --project CodeGenerator/CodeGenerator/CodeGenerator.csproj`, type `create a Customer class with Name string, Email string`, confirm a `[NLE] intent: CreateIntent` line prints and a class is generated, then `exit`. (Interactive; run manually.)
