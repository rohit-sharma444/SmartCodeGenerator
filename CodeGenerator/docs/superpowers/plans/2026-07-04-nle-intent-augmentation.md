# NLE Intent Augmentation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Use NaturalLanguageEngine's POS-tagged action verb as an additive signal that strengthens CodeGenerator's rule-based intent detection, without changing entity extraction.

**Architecture:** Extend NLE's English verb lexicon so command verbs are recognized (prerequisite), reference NLE from CodeGenerationEngine, isolate NLE behind a single `NlpActionProvider` adapter, and add one purely additive scoring contribution to `IntentAnalyzer`. Additive-only means the change cannot regress existing intent detection.

**Tech Stack:** C# / .NET 10.0, Roslyn (existing), NaturalLanguageEngine (existing project), MSTest 4.0.2 (CodeGenerator.Tests), xUnit (NaturalLanguageEngine.Tests).

## Global Constraints

- Repo root for all commands: `D:\PERSONAL-REPOSITORY`. Run every `dotnet` / `git` command from there.
- Target framework everywhere: `net10.0`. `Nullable` and `ImplicitUsings` are `enable`d in all touched projects.
- The **only** permitted NLE change is the verb-lexicon data addition in Task 1. Do not alter NLE pipeline, stages, or public API.
- NLE stays `<OutputType>Exe</OutputType>`; reference it as-is (a `NETSDK` warning about referencing an Exe is acceptable).
- The NLE action signal is **additive only**: it may raise a matching rule's score, never override or rescue. Bonus value: **+25**.
- `NlpActionProvider` is the only file in CodeGenerationEngine allowed to reference NLE types. Everything else sees a `string?`.
- CodeGenerator.Tests uses MSTest (`[TestClass]`/`[TestMethod]`/`[DataTestMethod]`, global using is already configured). NaturalLanguageEngine.Tests uses xUnit (`[Fact]`/`[Theory]`/`using Xunit;`).

---

### Task 1: Extend NLE verb lexicon (prerequisite)

**Files:**
- Modify: `NaturalLanguageEngine/NaturalLanguageEngine/Knowledge/Providers/EnglishLanguageProvider.cs` (the "Common verbs" array inside `LoadWords()`, around line 36)
- Test: `NaturalLanguageEngine/NaturalLanguageEngine.Tests/Semantics/CommandVerbLexiconTests.cs` (new)

**Interfaces:**
- Consumes: `EngineBuilder.BuildDefault().Analyzer` (`ISemanticAnalyzer`, global namespace `EngineBuilder`), `SemanticResult.Intent.Name`, `Entity.Type == EntityType.Action`, `Entity.Text`.
- Produces: NLE now tags `add/list/generate/delete/remove/rename/analyze/validate/use/restore` (and their -s/-ed forms) as verbs. Later tasks rely on `NlpActionProvider.GetPrimaryAction` returning these verbs.

- [ ] **Step 1: Write the failing test**

Create `NaturalLanguageEngine/NaturalLanguageEngine.Tests/Semantics/CommandVerbLexiconTests.cs`:

```csharp
using NaturalLanguageEngine.Abstractions.Semantics;
using NaturalLanguageEngine.Core;
using NaturalLanguageEngine.Models.Semantics;
using Xunit;

namespace NaturalLanguageEngine.Tests.Semantics;

/// <summary>
/// Locks in that CodeGenerator's imperative command verbs are present in the
/// English lexicon and are therefore tagged as Action verbs by the engine.
/// </summary>
public class CommandVerbLexiconTests
{
    private readonly ISemanticAnalyzer _engine = EngineBuilder.BuildDefault().Analyzer;

    [Theory]
    [InlineData("add method Save to Order", "AddIntent", "add")]
    [InlineData("list projects", "ListIntent", "list")]
    [InlineData("generate a service for Payment", "GenerateIntent", "generate")]
    [InlineData("delete class Foo", "DeleteIntent", "delete")]
    [InlineData("rename class A to B", "RenameIntent", "rename")]
    [InlineData("analyze class Foo", "AnalyzeIntent", "analyze")]
    public void Analyze_CommandVerb_TaggedAsActionWithIntent(
        string input, string expectedIntent, string expectedVerb)
    {
        var result = _engine.Analyze(input);

        Assert.Equal(expectedIntent, result.Intent?.Name);
        Assert.Contains(result.Entities,
            e => e.Type == EntityType.Action && e.Text == expectedVerb);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test NaturalLanguageEngine/NaturalLanguageEngine.Tests/NaturalLanguageEngine.Tests.csproj --filter "FullyQualifiedName~CommandVerbLexiconTests"`
Expected: FAIL — e.g. `Assert.Equal() Failure: Expected "AddIntent", Actual "DescribeIntent"` (and the `list projects` case has a null intent).

- [ ] **Step 3: Extend the lexicon**

In `EnglishLanguageProvider.cs`, find the "Common verbs" block (starts `// Common verbs`, the array ending `"run", "runs", "ran", "jumps" })`). Replace that array's closing so it reads:

```csharp
        // Common verbs
        foreach (var w in new[] { "go", "goes", "went", "make", "makes", "made", "take", "takes", "took", "see", "saw", "come", "came", "want", "wants", "wanted", "send", "sends", "sent", "give", "gives", "gave", "book", "books", "booked", "buy", "buys", "bought", "find", "found", "show", "shows", "tell", "told", "ask", "asks", "asked", "need", "needs", "needed", "like", "likes", "liked", "call", "calls", "called", "open", "opens", "opened", "close", "closes", "set", "schedule", "schedules", "scheduled", "play", "plays", "played", "create", "creates", "created", "build", "builds", "built", "run", "runs", "ran", "jumps",
            "add", "adds", "added", "list", "lists", "listed", "generate", "generates", "generated",
            "delete", "deletes", "deleted", "remove", "removes", "removed", "rename", "renames", "renamed",
            "analyze", "analyzes", "analyzed", "validate", "validates", "validated", "use", "uses", "used",
            "restore", "restores", "restored" })
            yield return Make(w, "verb");
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test NaturalLanguageEngine/NaturalLanguageEngine.Tests/NaturalLanguageEngine.Tests.csproj --filter "FullyQualifiedName~CommandVerbLexiconTests"`
Expected: PASS (6 cases).

- [ ] **Step 5: Run the full NLE suite to confirm no regression**

Run: `dotnet test NaturalLanguageEngine/NaturalLanguageEngine.Tests/NaturalLanguageEngine.Tests.csproj`
Expected: PASS (all pre-existing tests plus the new ones). If any pre-existing test now fails, stop and report — a lexicon addition should not break them.

- [ ] **Step 6: Commit**

```bash
git add NaturalLanguageEngine/NaturalLanguageEngine/Knowledge/Providers/EnglishLanguageProvider.cs NaturalLanguageEngine/NaturalLanguageEngine.Tests/Semantics/CommandVerbLexiconTests.cs
git commit -m "Add CodeGenerator command verbs to NLE English lexicon

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: NlpActionProvider adapter + project references

**Files:**
- Modify: `CodeGenerator/CodeGenerationEngine/CodeGenerationEngine.csproj` (add `ProjectReference` to NLE)
- Modify: `CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj` (add `ProjectReference` to CodeGenerationEngine — none exists yet)
- Create: `CodeGenerator/CodeGenerationEngine/Core/Nlp/NlpActionProvider.cs`
- Test: `CodeGenerator/CodeGenerator.Tests/Core/NlpActionProviderTests.cs` (new)

**Interfaces:**
- Consumes: `EngineBuilder.BuildDefault().Analyzer` (`ISemanticAnalyzer`), `SemanticResult.Entities` (`Entity` with `.Type`/`.Text`), `EntityType.Action`, `SemanticResult.Intent?.Name`.
- Produces: `CodeGenerationEngine.Core.Nlp.NlpActionProvider.GetPrimaryAction(string input) : string?` — returns the lowercased POS-tagged action verb, or `null` for empty/whitespace/non-actionable input or on any NLE failure. Task 3 consumes this.

- [ ] **Step 1: Add the project references**

In `CodeGenerator/CodeGenerationEngine/CodeGenerationEngine.csproj`, add a new `ItemGroup` (a `ProjectReference` cannot share an `ItemGroup` with `PackageReference` cleanly here — add its own):

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\NaturalLanguageEngine\NaturalLanguageEngine\NaturalLanguageEngine.csproj" />
  </ItemGroup>
```

In `CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj`, add:

```xml
  <ItemGroup>
    <ProjectReference Include="..\CodeGenerationEngine\CodeGenerationEngine.csproj" />
  </ItemGroup>
```

- [ ] **Step 2: Write the failing test**

Create `CodeGenerator/CodeGenerator.Tests/Core/NlpActionProviderTests.cs`:

```csharp
using CodeGenerationEngine.Core.Nlp;

namespace CodeGenerator.Tests.Core;

[TestClass]
public class NlpActionProviderTests
{
    [DataTestMethod]
    [DataRow("create a Customer class", "create")]
    [DataRow("add method Save to Order", "add")]
    [DataRow("list projects", "list")]
    [DataRow("delete class Foo", "delete")]
    [DataRow("rename class A to B", "rename")]
    [DataRow("analyze class Foo", "analyze")]
    public void GetPrimaryAction_CommandVerb_ReturnsLowercasedVerb(
        string input, string expected)
    {
        Assert.AreEqual(expected, NlpActionProvider.GetPrimaryAction(input));
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("the big red car")]
    public void GetPrimaryAction_EmptyOrNonActionable_ReturnsNull(string input)
    {
        Assert.IsNull(NlpActionProvider.GetPrimaryAction(input));
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~NlpActionProviderTests"`
Expected: FAIL to build — `NlpActionProvider` / namespace `CodeGenerationEngine.Core.Nlp` does not exist.

- [ ] **Step 4: Write the adapter**

Create `CodeGenerator/CodeGenerationEngine/Core/Nlp/NlpActionProvider.cs`:

```csharp
using NaturalLanguageEngine.Abstractions.Semantics;
using NaturalLanguageEngine.Models.Semantics;

namespace CodeGenerationEngine.Core.Nlp;

/// <summary>
/// Isolates the NaturalLanguageEngine dependency. Exposes only the POS-tagged
/// primary action verb of a command so the rest of CodeGenerationEngine never
/// sees an NLE type. The engine is built once (lazily) and reused; the console
/// REPL is single-threaded so the default Lazy thread-safety is sufficient.
/// </summary>
public static class NlpActionProvider
{
    private static readonly Lazy<ISemanticAnalyzer> Analyzer =
        new(() => EngineBuilder.BuildDefault().Analyzer);

    /// <summary>
    /// Returns the lowercased action verb NLE identified for <paramref name="input"/>,
    /// or null when the input is blank, has no recognized action, or NLE fails.
    /// Never throws.
    /// </summary>
    public static string? GetPrimaryAction(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        try
        {
            var result = Analyzer.Value.Analyze(input);

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
        catch
        {
            // NLE throws on null/blank input and could throw on unexpected
            // shapes; a missing signal must never break command parsing.
            return null;
        }
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~NlpActionProviderTests"`
Expected: PASS (9 cases: 6 verbs + 3 null).

- [ ] **Step 6: Commit**

```bash
git add CodeGenerator/CodeGenerationEngine/CodeGenerationEngine.csproj CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj CodeGenerator/CodeGenerationEngine/Core/Nlp/NlpActionProvider.cs CodeGenerator/CodeGenerator.Tests/Core/NlpActionProviderTests.cs
git commit -m "Add NlpActionProvider adapter wrapping NaturalLanguageEngine

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: Blend the NLE action signal into IntentAnalyzer

**Files:**
- Modify: `CodeGenerator/CodeGenerationEngine/Core/IntentAnalyzer.cs` (`DetectWithConfidence(string)`, `CalculateScore`, plus a new `GetNlpActionScore`)
- Test: `CodeGenerator/CodeGenerator.Tests/Core/IntentAnalyzerNlpTests.cs` (new)

**Interfaces:**
- Consumes: `NlpActionProvider.GetPrimaryAction(string) : string?` (Task 2), `IntentRule.Actions`, `IntentAnalyzer.Detect(string) : IntentMatch`, `IntentMatch.Intent : IntentType`.
- Produces: no new public surface; `IntentAnalyzer.Detect(string)` behavior gains the additive NLE bonus.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator/CodeGenerator.Tests/Core/IntentAnalyzerNlpTests.cs`:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core;

[TestClass]
public class IntentAnalyzerNlpTests
{
    // --- Regression: canonical commands must keep resolving as before. ---
    [DataTestMethod]
    [DataRow("create a Customer class", IntentType.CreateClass)]
    [DataRow("add method Save to Order", IntentType.CreateMethod)]
    [DataRow("list projects", IntentType.ListProjects)]
    [DataRow("delete class Foo", IntentType.DeleteClass)]
    [DataRow("rename class A to B", IntentType.RenameClass)]
    [DataRow("create a service for Payment", IntentType.CreateService)]
    public void Detect_CanonicalCommands_ResolveToExpectedIntent(
        string input, IntentType expected)
    {
        Assert.AreEqual(expected, IntentAnalyzer.Detect(input).Intent);
    }

    // --- Value-add: a terse command that scores below the keyword threshold
    // (verb present, no target word => 30 < 40 => Unknown without the signal).
    // NLE recognizes "validate" (unique to ValidateProject), adding +25 which
    // lifts it above the threshold. This resolves ONLY because of the NLE signal.
    [TestMethod]
    public void Detect_TerseCommandWithKnownVerb_ResolvedViaNlpSignal()
    {
        Assert.AreEqual(
            IntentType.ValidateProject,
            IntentAnalyzer.Detect("validate Foo").Intent);
    }
}
```

- [ ] **Step 2: Run tests to verify the value-add test fails**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~IntentAnalyzerNlpTests"`
Expected: the 6 regression rows PASS (current behavior already handles them); `Detect_TerseCommandWithKnownVerb_ResolvedViaNlpSignal` FAILS with `Expected:<ValidateProject> Actual:<Unknown>` (keyword score 30 < 40, signal not wired yet).

- [ ] **Step 3: Wire the NLE signal into the scorer**

In `CodeGenerator/CodeGenerationEngine/Core/IntentAnalyzer.cs`:

3a. At the top of `IntentAnalyzer.cs`, add the adapter's namespace to the existing usings (the file currently has `using CodeGenerationEngine.Models;` and `using System.Text.RegularExpressions;`):

```csharp
using CodeGenerationEngine.Core.Nlp;
```

Then in `DetectWithConfidence(string sentence)`, immediately after `sentence = NormalizeSentence(sentence);`, resolve the action once:

```csharp
        sentence = NormalizeSentence(sentence);

        var nlpAction = NlpActionProvider.GetPrimaryAction(sentence);
```

3b. In the same method, pass `nlpAction` into the scorer. Change the loop body call from:

```csharp
            int score = CalculateScore(sentence, tokens, rule);
```

to:

```csharp
            int score = CalculateScore(sentence, tokens, rule, nlpAction);
```

3c. Change the `CalculateScore` signature and add the new contribution:

```csharp
    private static int CalculateScore(string sentence, HashSet<string> tokens, IntentRule rule, string? nlpAction)
    {
        int score = 0;

        score += ScoreActions(sentence, tokens, rule);
        score += ScoreTargets(sentence, tokens, rule);
        score += GetPhraseBonus(sentence, rule);
        score += GetEnglishHintScore(sentence, rule.Intent);
        score += GetContextScore(sentence, rule.Intent);
        score += GetNlpActionScore(nlpAction, rule);

        return score;
    }

    // Additive only: when NLE identified a primary action verb that matches one
    // of this rule's actions, add a fixed bonus. If NLE returned nothing (null),
    // every rule's score is unchanged, so behavior is identical to before.
    private static int GetNlpActionScore(string? nlpAction, IntentRule rule)
    {
        if (string.IsNullOrWhiteSpace(nlpAction))
            return 0;

        return rule.Actions.Any(a =>
            string.Equals(a, nlpAction, StringComparison.OrdinalIgnoreCase))
            ? 25
            : 0;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~IntentAnalyzerNlpTests"`
Expected: PASS (all 7).

- [ ] **Step 5: Run the full CodeGenerator.Tests suite**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj`
Expected: PASS (Task 2 + Task 3 tests all green).

- [ ] **Step 6: Commit**

```bash
git add CodeGenerator/CodeGenerationEngine/Core/IntentAnalyzer.cs CodeGenerator/CodeGenerator.Tests/Core/IntentAnalyzerNlpTests.cs
git commit -m "Blend NLE action-verb signal into intent detection (additive)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Final verification

- [ ] From `D:\PERSONAL-REPOSITORY`, build the whole solution: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx`. Expected: build succeeds (a `NETSDK1150`-style warning about referencing the NLE Exe is acceptable).
- [ ] Run both test suites and confirm green:
  - `dotnet test NaturalLanguageEngine/NaturalLanguageEngine.Tests/NaturalLanguageEngine.Tests.csproj`
  - `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj`
- [ ] Sanity-drive the real app: from `D:\PERSONAL-REPOSITORY`, `dotnet run --project CodeGenerator/CodeGenerator/CodeGenerator.csproj`, then at the `CodeGenerator >>` prompt enter `create a Customer class with Name string, Email string` and confirm a class is generated (intent still resolves correctly with NLE in the loop). Type `exit` to quit.
