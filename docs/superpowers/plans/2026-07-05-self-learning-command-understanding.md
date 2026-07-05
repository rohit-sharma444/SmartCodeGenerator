# Self-Learning Command Understanding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a usage-driven learning layer that improves command-intent classification over time without corrupting the base rules.

**Architecture:** A separate `LearnedIntentMemory` records `(signature, intent)` success/failure counts. `IntentAnalyzer` consults it *after* the deterministic guards and base scoring, adding only a small capped bonus so it can tip borderline cases but never override a guard. A `LearningCoordinator` captures success/failure (and quick-rephrase) feedback from the command pipeline. Data persists per-workspace under `<workspace>/.codegen/learned.json`.

**Tech Stack:** C# / .NET 10, MSTest, System.Text.Json.

## Global Constraints

- Target framework: `net10.0`; `Nullable` enabled; `ImplicitUsings` enabled (match existing projects).
- Learning is **on by default**; disabled when env var `CG_NO_LEARN` equals `1`.
- Learned influence is **capped** at `LearnedIntentMemory.BonusCap = 35`, strictly below the create-class guard weight (120) and context weights; learning must never override a deterministic guard.
- Deterministic guards (`create … class`, `list/show classes`, inheritance) and the two new learning commands are detected **before** learning is consulted.
- Base `intents.json` rules are never modified by learning.
- New source files live in `CodeGenerator/CodeGenerationEngine/Core`; new models in `CodeGenerator/CodeGenerationEngine/Models`; tests in `CodeGenerator/CodeGenerator.Tests/Core`.
- Test classes that touch static learning state use `[DoNotParallelize]`.
- Commit after every task.

---

## File Structure

- Create `CodeGenerator/CodeGenerationEngine/Models/LearnedIntentEntry.cs` — one learned `(signature, intent)` record + confidence math.
- Create `CodeGenerator/CodeGenerationEngine/Core/LearnedIntentMemory.cs` — in-memory learning logic + JSON persistence.
- Create `CodeGenerator/CodeGenerationEngine/Core/LearnedIntentStore.cs` — static singleton holder, enable flag, workspace-path init.
- Create `CodeGenerator/CodeGenerationEngine/Core/LearningCoordinator.cs` — outcome capture, quick-rephrase, vocabulary learning.
- Modify `CodeGenerator/CodeGenerationEngine/Core/IntentAnalyzer.cs` — learned nudge + two exact-match learning intents.
- Modify `CodeGenerator/CodeGenerationEngine/Models/IntentType.cs` — add `ShowLearning`, `ResetLearning`.
- Modify `CodeGenerator/CodeGenerationEngine/Service/CommandHandlingService.cs` — feedback hook + two handlers.
- Modify `CodeGenerator/CodeGenerator/App/CodeGeneratorStarter.cs` — initialize the store with the workspace root.
- Tests under `CodeGenerator/CodeGenerator.Tests/Core/`.

---

### Task 1: LearnedIntentEntry with confidence math

**Files:**
- Create: `CodeGenerator/CodeGenerationEngine/Models/LearnedIntentEntry.cs`
- Test: `CodeGenerator/CodeGenerator.Tests/Core/LearnedIntentEntryTests.cs`

**Interfaces:**
- Produces: `LearnedIntentEntry { string Signature; IntentType Intent; int SuccessCount; int FailureCount; DateTime LastUsedUtc; double Confidence (computed) }`

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator/CodeGenerator.Tests/Core/LearnedIntentEntryTests.cs`:

```csharp
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core;

[TestClass]
public sealed class LearnedIntentEntryTests
{
    [TestMethod]
    public void Confidence_NoObservations_IsZero()
    {
        var e = new LearnedIntentEntry { Intent = IntentType.ListClasses };
        Assert.AreEqual(0d, e.Confidence);
    }

    [TestMethod]
    public void Confidence_AllSuccesses_GrowsButClampedBelowOne()
    {
        var e = new LearnedIntentEntry
        {
            Intent = IntentType.ListClasses,
            SuccessCount = 5,
            FailureCount = 0
        };
        Assert.IsTrue(e.Confidence > 0.6 && e.Confidence <= 1.0,
            $"confidence was {e.Confidence}");
    }

    [TestMethod]
    public void Confidence_MoreFailuresThanSuccesses_IsLow()
    {
        var e = new LearnedIntentEntry
        {
            Intent = IntentType.ListClasses,
            SuccessCount = 1,
            FailureCount = 4
        };
        Assert.IsTrue(e.Confidence < 0.3, $"confidence was {e.Confidence}");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~LearnedIntentEntryTests"`
Expected: FAIL to compile — `LearnedIntentEntry` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `CodeGenerator/CodeGenerationEngine/Models/LearnedIntentEntry.cs`:

```csharp
namespace CodeGenerationEngine.Models;

/// <summary>
/// One learned association between a command signature and an intent, with the
/// success/failure tally that drives its confidence.
/// </summary>
public sealed class LearnedIntentEntry
{
    public string Signature { get; set; } = string.Empty;

    public IntentType Intent { get; set; }

    public int SuccessCount { get; set; }

    public int FailureCount { get; set; }

    public DateTime LastUsedUtc { get; set; }

    /// <summary>
    /// Success ratio, damped by how few observations we have so a single
    /// success does not immediately read as certainty. Clamped to [0, 1].
    /// </summary>
    public double Confidence
    {
        get
        {
            var total = SuccessCount + FailureCount;
            if (total == 0)
                return 0d;

            var ratio = (double)SuccessCount / total;
            var observationWeight = total / (total + 2.0);
            return Math.Clamp(ratio * observationWeight, 0d, 1d);
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~LearnedIntentEntryTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/CodeGenerationEngine/Models/LearnedIntentEntry.cs CodeGenerator/CodeGenerator.Tests/Core/LearnedIntentEntryTests.cs
git commit -m "Add LearnedIntentEntry with damped confidence"
```

---

### Task 2: LearnedIntentMemory core (signature, reinforce, weaken, suggest)

**Files:**
- Create: `CodeGenerator/CodeGenerationEngine/Core/LearnedIntentMemory.cs`
- Test: `CodeGenerator/CodeGenerator.Tests/Core/LearnedIntentMemoryTests.cs`

**Interfaces:**
- Consumes: `LearnedIntentEntry` (Task 1); `TextTokenizer.Tokenize(string)` returning `IReadOnlyList<Token>` with `.Normalized`.
- Produces:
  - `const int LearnedIntentMemory.BonusCap = 35;`
  - `const double LearnedIntentMemory.SuggestThreshold = 0.5;`
  - `static string LearnedIntentMemory.Signature(string sentence)`
  - `void Reinforce(string sentence, IntentType intent)`
  - `void Weaken(string sentence, IntentType intent)`
  - `IntentType? Suggest(string sentence, out double confidence)`
  - `IReadOnlyCollection<LearnedIntentEntry> Entries { get; }`
  - `void Reset()`

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator/CodeGenerator.Tests/Core/LearnedIntentMemoryTests.cs`:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core;

[TestClass]
public sealed class LearnedIntentMemoryTests
{
    [TestMethod]
    public void Signature_IsWordOrderIndependent()
    {
        Assert.AreEqual(
            LearnedIntentMemory.Signature("class list"),
            LearnedIntentMemory.Signature("list class"));
    }

    [TestMethod]
    public void Suggest_UnknownSignature_ReturnsNull()
    {
        var mem = new LearnedIntentMemory();
        Assert.IsNull(mem.Suggest("list classes", out _));
    }

    [TestMethod]
    public void Reinforce_ThenSuggest_ReturnsReinforcedIntent()
    {
        var mem = new LearnedIntentMemory();
        for (var i = 0; i < 4; i++)
            mem.Reinforce("show my widgets", IntentType.ListClasses);

        var intent = mem.Suggest("show my widgets", out var confidence);

        Assert.AreEqual(IntentType.ListClasses, intent);
        Assert.IsTrue(confidence >= LearnedIntentMemory.SuggestThreshold,
            $"confidence {confidence}");
    }

    [TestMethod]
    public void Weaken_DrivesConfidenceBelowThreshold()
    {
        var mem = new LearnedIntentMemory();
        mem.Reinforce("show my widgets", IntentType.ListClasses);
        for (var i = 0; i < 5; i++)
            mem.Weaken("show my widgets", IntentType.ListClasses);

        mem.Suggest("show my widgets", out var confidence);
        Assert.IsTrue(confidence < LearnedIntentMemory.SuggestThreshold,
            $"confidence {confidence}");
    }

    [TestMethod]
    public void Suggest_PicksHighestConfidenceIntentForSignature()
    {
        var mem = new LearnedIntentMemory();
        mem.Reinforce("do the thing", IntentType.CreateMethod);
        for (var i = 0; i < 5; i++)
            mem.Reinforce("do the thing", IntentType.ListClasses);

        var intent = mem.Suggest("do the thing", out _);
        Assert.AreEqual(IntentType.ListClasses, intent);
    }

    [TestMethod]
    public void Reset_ClearsEntries()
    {
        var mem = new LearnedIntentMemory();
        mem.Reinforce("x y z", IntentType.ListClasses);
        mem.Reset();
        Assert.AreEqual(0, mem.Entries.Count);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~LearnedIntentMemoryTests"`
Expected: FAIL to compile — `LearnedIntentMemory` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `CodeGenerator/CodeGenerationEngine/Core/LearnedIntentMemory.cs`:

```csharp
using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core;

/// <summary>
/// A separate, usage-driven memory that associates a coarse command signature
/// with the intents that have succeeded for it. It only ever nudges intent
/// scoring (see <see cref="BonusCap"/>); it never mutates the base rules.
/// </summary>
public sealed class LearnedIntentMemory
{
    /// <summary>Maximum score bonus a learned suggestion may contribute.</summary>
    public const int BonusCap = 35;

    /// <summary>Minimum confidence before a suggestion is applied at all.</summary>
    public const double SuggestThreshold = 0.5;

    private readonly Dictionary<(string Signature, IntentType Intent), LearnedIntentEntry> _entries = new();

    public IReadOnlyCollection<LearnedIntentEntry> Entries => _entries.Values;

    /// <summary>
    /// Canonical signature: distinct normalized tokens, lower-cased, sorted, so
    /// phrasings that differ only in word order or duplicates collapse together.
    /// </summary>
    public static string Signature(string sentence)
    {
        var tokens = TextTokenizer.Tokenize(sentence ?? string.Empty)
            .Select(t => t.Normalized)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal);

        return string.Join(' ', tokens);
    }

    public void Reinforce(string sentence, IntentType intent)
        => Record(sentence, intent, success: true);

    public void Weaken(string sentence, IntentType intent)
        => Record(sentence, intent, success: false);

    public IntentType? Suggest(string sentence, out double confidence)
    {
        confidence = 0d;

        var signature = Signature(sentence);
        if (string.IsNullOrEmpty(signature))
            return null;

        LearnedIntentEntry? best = null;

        foreach (var entry in _entries.Values)
        {
            if (!entry.Signature.Equals(signature, StringComparison.Ordinal))
                continue;

            if (best == null || entry.Confidence > best.Confidence)
                best = entry;
        }

        if (best == null || best.Confidence < SuggestThreshold)
            return null;

        confidence = best.Confidence;
        return best.Intent;
    }

    public void Reset() => _entries.Clear();

    private void Record(string sentence, IntentType intent, bool success)
    {
        if (intent == IntentType.Unknown)
            return;

        var signature = Signature(sentence);
        if (string.IsNullOrEmpty(signature))
            return;

        var key = (signature, intent);
        if (!_entries.TryGetValue(key, out var entry))
        {
            entry = new LearnedIntentEntry { Signature = signature, Intent = intent };
            _entries[key] = entry;
        }

        if (success)
            entry.SuccessCount++;
        else
            entry.FailureCount++;

        entry.LastUsedUtc = DateTime.UtcNow;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~LearnedIntentMemoryTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/CodeGenerationEngine/Core/LearnedIntentMemory.cs CodeGenerator/CodeGenerator.Tests/Core/LearnedIntentMemoryTests.cs
git commit -m "Add LearnedIntentMemory core (signature, reinforce, weaken, suggest)"
```

---

### Task 3: LearnedIntentMemory persistence + decay

**Files:**
- Modify: `CodeGenerator/CodeGenerationEngine/Core/LearnedIntentMemory.cs`
- Test: `CodeGenerator/CodeGenerator.Tests/Core/LearnedIntentMemoryPersistenceTests.cs`

**Interfaces:**
- Produces (added to `LearnedIntentMemory`):
  - `void Save(string filePath)`
  - `void Load(string filePath)`
  - `void Decay(DateTime nowUtc)`
  - `const int LearnedIntentMemory.DecayAfterDays = 30;`

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator/CodeGenerator.Tests/Core/LearnedIntentMemoryPersistenceTests.cs`:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core;

[TestClass]
public sealed class LearnedIntentMemoryPersistenceTests
{
    private static string NewTempFile()
        => Path.Combine(Path.GetTempPath(),
            "learned_" + Guid.NewGuid().ToString("N") + ".json");

    [TestMethod]
    public void SaveThenLoad_RoundTripsEntries()
    {
        var file = NewTempFile();
        var mem = new LearnedIntentMemory();
        mem.Reinforce("show my widgets", IntentType.ListClasses);
        mem.Reinforce("show my widgets", IntentType.ListClasses);
        mem.Save(file);

        var loaded = new LearnedIntentMemory();
        loaded.Load(file);

        var intent = loaded.Suggest("show my widgets", out _);
        Assert.AreEqual(IntentType.ListClasses, intent);
    }

    [TestMethod]
    public void Load_MissingFile_IsEmpty()
    {
        var mem = new LearnedIntentMemory();
        mem.Load(NewTempFile());
        Assert.AreEqual(0, mem.Entries.Count);
    }

    [TestMethod]
    public void Decay_HalvesCountsForStaleEntries()
    {
        var mem = new LearnedIntentMemory();
        for (var i = 0; i < 8; i++)
            mem.Reinforce("show my widgets", IntentType.ListClasses);

        // Force the entry to look old, then decay relative to "now".
        var entry = mem.Entries.Single();
        entry.LastUsedUtc = DateTime.UtcNow.AddDays(-90);

        mem.Decay(DateTime.UtcNow);

        Assert.AreEqual(4, mem.Entries.Single().SuccessCount);
    }

    [TestMethod]
    public void Decay_LeavesRecentEntriesUntouched()
    {
        var mem = new LearnedIntentMemory();
        for (var i = 0; i < 8; i++)
            mem.Reinforce("show my widgets", IntentType.ListClasses);

        mem.Decay(DateTime.UtcNow);

        Assert.AreEqual(8, mem.Entries.Single().SuccessCount);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~LearnedIntentMemoryPersistenceTests"`
Expected: FAIL to compile — `Save`/`Load`/`Decay` do not exist.

- [ ] **Step 3: Write minimal implementation**

In `CodeGenerator/CodeGenerationEngine/Core/LearnedIntentMemory.cs`, add `using System.Text.Json;` at the top and add these members inside the class (after `Reset`):

```csharp
    public const int DecayAfterDays = 30;

    public void Save(string filePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(
            _entries.Values.ToList(),
            new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(filePath, json);
    }

    public void Load(string filePath)
    {
        _entries.Clear();

        if (!File.Exists(filePath))
            return;

        var json = File.ReadAllText(filePath);
        if (string.IsNullOrWhiteSpace(json))
            return;

        var entries = JsonSerializer.Deserialize<List<LearnedIntentEntry>>(json)
                      ?? new List<LearnedIntentEntry>();

        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.Signature) || entry.Intent == IntentType.Unknown)
                continue;

            _entries[(entry.Signature, entry.Intent)] = entry;
        }
    }

    /// <summary>
    /// Halves the tallies of entries not used within <see cref="DecayAfterDays"/>
    /// so stale/bad associations fade instead of compounding.
    /// </summary>
    public void Decay(DateTime nowUtc)
    {
        foreach (var entry in _entries.Values)
        {
            if ((nowUtc - entry.LastUsedUtc).TotalDays < DecayAfterDays)
                continue;

            entry.SuccessCount /= 2;
            entry.FailureCount /= 2;
        }
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~LearnedIntentMemoryPersistenceTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/CodeGenerationEngine/Core/LearnedIntentMemory.cs CodeGenerator/CodeGenerator.Tests/Core/LearnedIntentMemoryPersistenceTests.cs
git commit -m "Add LearnedIntentMemory persistence and decay"
```

---

### Task 4: LearnedIntentStore static holder + enable flag

**Files:**
- Create: `CodeGenerator/CodeGenerationEngine/Core/LearnedIntentStore.cs`
- Test: `CodeGenerator/CodeGenerator.Tests/Core/LearnedIntentStoreTests.cs`

**Interfaces:**
- Consumes: `LearnedIntentMemory` (Tasks 2-3).
- Produces:
  - `static LearnedIntentMemory LearnedIntentStore.Current { get; }`
  - `static bool LearnedIntentStore.Enabled { get; }` (false when env `CG_NO_LEARN == "1"`)
  - `static void LearnedIntentStore.Initialize(string workspaceRoot)` — sets file path to `<workspaceRoot>/.codegen/learned.json`, loads, decays.
  - `static void LearnedIntentStore.Save()`
  - `static void LearnedIntentStore.UseInMemory()` — resets to a fresh, non-persisted memory (for tests).

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator/CodeGenerator.Tests/Core/LearnedIntentStoreTests.cs`:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core;

[TestClass]
[DoNotParallelize]
public sealed class LearnedIntentStoreTests
{
    [TestMethod]
    public void Initialize_LoadsAndSavesUnderWorkspaceCodegenFolder()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "cg_lis_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        LearnedIntentStore.Initialize(workspace);
        LearnedIntentStore.Current.Reinforce("show my widgets", IntentType.ListClasses);
        LearnedIntentStore.Save();

        var expected = Path.Combine(workspace, ".codegen", "learned.json");
        Assert.IsTrue(File.Exists(expected), $"expected learned.json at {expected}");

        // Re-initialize from the same workspace: data survives.
        LearnedIntentStore.Initialize(workspace);
        Assert.AreEqual(IntentType.ListClasses,
            LearnedIntentStore.Current.Suggest("show my widgets", out _));
    }

    [TestMethod]
    public void Enabled_FollowsEnvVar()
    {
        Environment.SetEnvironmentVariable("CG_NO_LEARN", "1");
        try
        {
            Assert.IsFalse(LearnedIntentStore.Enabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CG_NO_LEARN", null);
        }

        Assert.IsTrue(LearnedIntentStore.Enabled);
    }

    [TestMethod]
    public void UseInMemory_GivesFreshNonPersistedMemory()
    {
        LearnedIntentStore.UseInMemory();
        Assert.AreEqual(0, LearnedIntentStore.Current.Entries.Count);
        // Save is a no-op with no path; must not throw.
        LearnedIntentStore.Save();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~LearnedIntentStoreTests"`
Expected: FAIL to compile — `LearnedIntentStore` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `CodeGenerator/CodeGenerationEngine/Core/LearnedIntentStore.cs`:

```csharp
namespace CodeGenerationEngine.Core;

/// <summary>
/// Process-wide holder for the active <see cref="LearnedIntentMemory"/>. Learning
/// is on by default and disabled when the environment variable
/// <c>CG_NO_LEARN</c> is "1". Persistence is scoped to the current workspace.
/// </summary>
public static class LearnedIntentStore
{
    private static LearnedIntentMemory _current = new();
    private static string? _filePath;

    public static LearnedIntentMemory Current => _current;

    public static bool Enabled =>
        !string.Equals(
            Environment.GetEnvironmentVariable("CG_NO_LEARN"),
            "1",
            StringComparison.Ordinal);

    public static void Initialize(string workspaceRoot)
    {
        var folder = Path.Combine(workspaceRoot, ".codegen");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "learned.json");

        var memory = new LearnedIntentMemory();
        memory.Load(_filePath);
        memory.Decay(DateTime.UtcNow);
        _current = memory;
    }

    public static void Save()
    {
        if (_filePath != null)
            _current.Save(_filePath);
    }

    /// <summary>Test seam: fresh in-memory store with no backing file.</summary>
    public static void UseInMemory()
    {
        _current = new LearnedIntentMemory();
        _filePath = null;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~LearnedIntentStoreTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/CodeGenerationEngine/Core/LearnedIntentStore.cs CodeGenerator/CodeGenerator.Tests/Core/LearnedIntentStoreTests.cs
git commit -m "Add LearnedIntentStore holder with workspace persistence and off-switch"
```

---

### Task 5: IntentAnalyzer learned nudge

**Files:**
- Modify: `CodeGenerator/CodeGenerationEngine/Core/IntentAnalyzer.cs` (inside `DetectWithConfidence(string, string?)`, after the base-rule scoring loop, before the `bestScore < 40` clamp)
- Test: `CodeGenerator/CodeGenerator.Tests/Core/IntentAnalyzerLearningTests.cs`

**Interfaces:**
- Consumes: `LearnedIntentStore.Enabled`, `LearnedIntentStore.Current`, `LearnedIntentMemory.BonusCap`, `LearnedIntentMemory.Suggest`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator/CodeGenerator.Tests/Core/IntentAnalyzerLearningTests.cs`:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core;

[TestClass]
[DoNotParallelize]
public sealed class IntentAnalyzerLearningTests
{
    [TestInitialize]
    public void Setup() => LearnedIntentStore.UseInMemory();

    [TestCleanup]
    public void Cleanup() => LearnedIntentStore.UseInMemory();

    [TestMethod]
    public void LearnedNudge_NeverOverridesCreateClassGuard()
    {
        // Heavily (mis)reinforce CreateMethod for a create-class phrase.
        for (var i = 0; i < 20; i++)
            LearnedIntentStore.Current.Reinforce("create class Foo", IntentType.CreateMethod);

        var semantic = SemanticAnalyzer.Analyze("create class Foo");
        var intent = CodeGenerationEngine.Core.IntentAnalyzer.Detect(semantic);

        Assert.AreEqual(IntentType.CreateClass, intent);
    }

    [TestMethod]
    public void LearnedNudge_TipsABorderlineNonsenseCommand()
    {
        // A phrase the base rules do not confidently classify.
        const string phrase = "wibble the wobble";

        var before = CodeGenerationEngine.Core.IntentAnalyzer.Detect(
            SemanticAnalyzer.Analyze(phrase));
        Assert.AreEqual(IntentType.Unknown, before);

        for (var i = 0; i < 8; i++)
            LearnedIntentStore.Current.Reinforce(phrase, IntentType.ListClasses);

        var after = CodeGenerationEngine.Core.IntentAnalyzer.Detect(
            SemanticAnalyzer.Analyze(phrase));
        Assert.AreEqual(IntentType.ListClasses, after);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~IntentAnalyzerLearningTests"`
Expected: FAIL — `LearnedNudge_TipsABorderlineNonsenseCommand` fails because the learned bonus is not yet applied (after == Unknown).

- [ ] **Step 3: Write minimal implementation**

In `CodeGenerator/CodeGenerationEngine/Core/IntentAnalyzer.cs`, locate this block at the end of `DetectWithConfidence(string, string?)` (after the `foreach (var rule in IntentRules.Rules)` scoring loop):

```csharp
        // Prevent weak accidental matches
        if (bestScore < 40)
        {
            bestIntent = IntentType.Unknown;
        }
```

Replace that **entire block** with the following (the nudge, then a clamp that respects a confident learned suggestion):

```csharp
        // Learned nudge: a usage-driven suggestion may add a small, capped bonus
        // to an intent's score. The cap (LearnedIntentMemory.BonusCap = 35) is far
        // below a deterministic guard's contribution (e.g. the +120 create-class
        // bonus), so this only tips borderline cases and never overrides a
        // confident base decision or an early-return guard.
        var learnedApplied = false;
        if (LearnedIntentStore.Enabled)
        {
            var suggestion = LearnedIntentStore.Current.Suggest(sentence, out var learnedConfidence);
            if (suggestion.HasValue)
            {
                var bonus = (int)Math.Round(
                    LearnedIntentMemory.BonusCap * learnedConfidence);

                var suggestedScore =
                    (suggestion.Value == bestIntent ? bestScore : 0) + bonus;

                if (suggestedScore > bestScore)
                {
                    bestScore = suggestedScore;
                    bestIntent = suggestion.Value;
                    learnedApplied = true;
                }
            }
        }

        // Prevent weak accidental matches, but keep a confident learned suggestion.
        if (bestScore < 40 && !learnedApplied)
        {
            bestIntent = IntentType.Unknown;
        }
```

Why this works: for a strong base decision like `create class Foo` (base score ~160 with the +120 guard), a learned `CreateMethod` bonus of ≤35 can never win. For a phrase the base rules don't recognize (`bestScore == 0`), a confident learned suggestion (8 reinforcements → confidence ≈ 0.8 → bonus ≈ 28) becomes the winner and `learnedApplied` keeps it past the `< 40` clamp.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~IntentAnalyzerLearningTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Run the full suite to confirm no regressions**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj`
Expected: PASS (all existing tests still green).

- [ ] **Step 6: Commit**

```bash
git add CodeGenerator/CodeGenerationEngine/Core/IntentAnalyzer.cs CodeGenerator/CodeGenerator.Tests/Core/IntentAnalyzerLearningTests.cs
git commit -m "Consult learned intent memory as a capped nudge in IntentAnalyzer"
```

---

### Task 6: LearningCoordinator (outcome capture, quick-rephrase, vocabulary)

**Files:**
- Create: `CodeGenerator/CodeGenerationEngine/Core/LearningCoordinator.cs`
- Test: `CodeGenerator/CodeGenerator.Tests/Core/LearningCoordinatorTests.cs`

**Interfaces:**
- Consumes: `LearnedIntentStore`, `LearnedIntentMemory.Signature`, `IntentLearningEngine.Learn(string, ParsedCommand)`.
- Produces:
  - `static void LearningCoordinator.RecordOutcome(string sentence, IntentType intent, bool success, ParsedCommand? command)`
  - `const double LearningCoordinator.RephraseSimilarity = 0.6;`
  - `const int LearningCoordinator.RephraseWindowSeconds = 20;`
  - `static void LearningCoordinator.ResetHistory()` (test seam)

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator/CodeGenerator.Tests/Core/LearningCoordinatorTests.cs`:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core;

[TestClass]
[DoNotParallelize]
public sealed class LearningCoordinatorTests
{
    [TestInitialize]
    public void Setup()
    {
        LearnedIntentStore.UseInMemory();
        LearningCoordinator.ResetHistory();
    }

    [TestMethod]
    public void RecordOutcome_Success_ReinforcesIntent()
    {
        LearningCoordinator.RecordOutcome("show my widgets", IntentType.ListClasses, success: true, command: null);
        LearningCoordinator.RecordOutcome("show my widgets", IntentType.ListClasses, success: true, command: null);
        LearningCoordinator.RecordOutcome("show my widgets", IntentType.ListClasses, success: true, command: null);

        Assert.AreEqual(IntentType.ListClasses,
            LearnedIntentStore.Current.Suggest("show my widgets", out _));
    }

    [TestMethod]
    public void RecordOutcome_Failure_DoesNotReinforce()
    {
        LearningCoordinator.RecordOutcome("show my widgets", IntentType.ListClasses, success: false, command: null);

        Assert.IsNull(LearnedIntentStore.Current.Suggest("show my widgets", out _));
    }

    [TestMethod]
    public void QuickRephrase_WeakensThePriorIntent()
    {
        // First command classified (reinforced by an earlier success elsewhere).
        for (var i = 0; i < 6; i++)
            LearnedIntentStore.Current.Reinforce("show my widget", IntentType.CreateClass);

        // The user runs it (recorded), then immediately rephrases a near-identical
        // command -> the prior interpretation is treated as a misfire.
        LearningCoordinator.RecordOutcome("show my widget", IntentType.CreateClass, success: true, command: null);
        LearningCoordinator.RecordOutcome("show my widgets please", IntentType.ListClasses, success: true, command: null);

        // The prior CreateClass association got a weaken; confidence should have dropped.
        LearnedIntentStore.Current.Suggest("show my widget", out var confidence);
        Assert.IsTrue(confidence < 0.85, $"expected weakened confidence, got {confidence}");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~LearningCoordinatorTests"`
Expected: FAIL to compile — `LearningCoordinator` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `CodeGenerator/CodeGenerationEngine/Core/LearningCoordinator.cs`:

```csharp
using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core;

/// <summary>
/// Single feedback sink for the command pipeline. Turns command outcomes into
/// reinforcement/weakening of the learned intent memory, treats a quick rephrase
/// of a near-identical command as a negative signal for the prior interpretation,
/// and folds successful commands into vocabulary learning.
/// </summary>
public static class LearningCoordinator
{
    public const double RephraseSimilarity = 0.6;
    public const int RephraseWindowSeconds = 20;

    private static string? _lastSentence;
    private static IntentType _lastIntent;
    private static DateTime _lastUtc = DateTime.MinValue;

    public static void RecordOutcome(
        string sentence,
        IntentType intent,
        bool success,
        ParsedCommand? command)
    {
        if (!LearnedIntentStore.Enabled || string.IsNullOrWhiteSpace(sentence))
            return;

        var nowUtc = DateTime.UtcNow;

        // Quick-rephrase: a near-identical follow-up soon after the last command
        // means the last interpretation probably missed - weaken it.
        if (_lastSentence != null
            && _lastIntent != IntentType.Unknown
            && (nowUtc - _lastUtc).TotalSeconds <= RephraseWindowSeconds
            && IsRephrase(_lastSentence, sentence))
        {
            LearnedIntentStore.Current.Weaken(_lastSentence, _lastIntent);
        }

        if (intent == IntentType.Unknown || !success)
        {
            if (intent != IntentType.Unknown)
                LearnedIntentStore.Current.Weaken(sentence, intent);
        }
        else
        {
            LearnedIntentStore.Current.Reinforce(sentence, intent);

            if (command != null)
                IntentLearningEngine.Learn(sentence, command);
        }

        LearnedIntentStore.Save();

        _lastSentence = sentence;
        _lastIntent = intent;
        _lastUtc = nowUtc;
    }

    public static void ResetHistory()
    {
        _lastSentence = null;
        _lastIntent = IntentType.Unknown;
        _lastUtc = DateTime.MinValue;
    }

    private static bool IsRephrase(string a, string b)
    {
        var sa = LearnedIntentMemory.Signature(a);
        var sb = LearnedIntentMemory.Signature(b);

        if (sa.Length == 0 || sb.Length == 0)
            return false;

        if (sa.Equals(sb, StringComparison.Ordinal))
            return false; // identical signature is a repeat, not a rephrase

        var setA = sa.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var setB = sb.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

        var intersection = setA.Count(setB.Contains);
        var union = setA.Count + setB.Count - intersection;

        return union > 0 && (double)intersection / union >= RephraseSimilarity;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~LearningCoordinatorTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/CodeGenerationEngine/Core/LearningCoordinator.cs CodeGenerator/CodeGenerator.Tests/Core/LearningCoordinatorTests.cs
git commit -m "Add LearningCoordinator: outcome capture, quick-rephrase, vocabulary"
```

---

### Task 7: Wire feedback into the pipeline + initialize at startup

**Files:**
- Modify: `CodeGenerator/CodeGenerationEngine/Service/CommandHandlingService.cs` (`ProcessInput`)
- Modify: `CodeGenerator/CodeGenerator/App/CodeGeneratorStarter.cs` (constructor)
- Test: `CodeGenerator/CodeGenerator.Tests/Core/PipelineLearningTests.cs`

**Interfaces:**
- Consumes: `LearningCoordinator.RecordOutcome`, `LearnedIntentStore.Initialize`, `LearnedIntentStore.Current`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator/CodeGenerator.Tests/Core/PipelineLearningTests.cs`:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using CodeGenerationEngine.Service;

namespace CodeGenerator.Tests.Core;

[TestClass]
[DoNotParallelize]
public sealed class PipelineLearningTests
{
    [TestMethod]
    public void ProcessInput_SuccessfulCommand_RecordsReinforcement()
    {
        LearnedIntentStore.UseInMemory();
        LearningCoordinator.ResetHistory();
        ProjectManager.CurrentProject = null;

        var workspace = Path.Combine(Path.GetTempPath(), "cg_pl_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        ProjectMetaData? project = null;
        CommandHandlingService.ProcessInput(
            "create class Widget",
            ref project,
            new List<PropertyRule>(),
            workspace,
            p => ProjectManager.CurrentProject = p);

        Assert.AreEqual(IntentType.CreateClass,
            LearnedIntentStore.Current.Suggest("create class Widget", out _));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~PipelineLearningTests"`
Expected: FAIL — nothing records the outcome yet, so `Suggest` returns null.

- [ ] **Step 3: Wire the feedback hook**

In `CodeGenerator/CodeGenerationEngine/Service/CommandHandlingService.cs`, find `ProcessInput`. It currently ends its body with:

```csharp
        bool result = Dispatch(context);

        // Propagate any project change made by a handler back to the caller.
        currentProject = context.CurrentProject;
        return result;
```

Replace that with:

```csharp
        bool result = Dispatch(context);

        // Feed the outcome back to the learning layer.
        LearningCoordinator.RecordOutcome(input, command.Intent, result, command);

        // Propagate any project change made by a handler back to the caller.
        currentProject = context.CurrentProject;
        return result;
```

- [ ] **Step 4: Initialize the store at startup**

In `CodeGenerator/CodeGenerator/App/CodeGeneratorStarter.cs`, find the constructor block:

```csharp
        ProjectManager.LoadProject();

        // At startup, prefer an existing project in the workspace.
        InitializeExistingProjects();
```

Insert the store initialization right after `InitializeExistingProjects();`:

```csharp
        // Load per-workspace learned intent memory (no-op cost when disabled).
        LearnedIntentStore.Initialize(_workspaceRoot);
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~PipelineLearningTests"`
Expected: PASS (1 test).

- [ ] **Step 6: Run the full suite**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj`
Expected: PASS (all green).

- [ ] **Step 7: Commit**

```bash
git add CodeGenerator/CodeGenerationEngine/Service/CommandHandlingService.cs CodeGenerator/CodeGenerator/App/CodeGeneratorStarter.cs CodeGenerator/CodeGenerator.Tests/Core/PipelineLearningTests.cs
git commit -m "Wire learning feedback into pipeline and initialize store at startup"
```

---

### Task 8: `show learning` and `reset learning` commands

**Files:**
- Modify: `CodeGenerator/CodeGenerationEngine/Models/IntentType.cs` (add two values)
- Modify: `CodeGenerator/CodeGenerationEngine/Core/IntentAnalyzer.cs` (deterministic detection, before the class-listing guard)
- Modify: `CodeGenerator/CodeGenerationEngine/Service/CommandHandlingService.cs` (two handlers + registrations)
- Test: `CodeGenerator/CodeGenerator.Tests/Core/LearningCommandsTests.cs`

**Interfaces:**
- Consumes: `LearnedIntentStore.Current` (`Entries`, `Reset`), `LearnedIntentStore.Save`.
- Produces: `IntentType.ShowLearning`, `IntentType.ResetLearning`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator/CodeGenerator.Tests/Core/LearningCommandsTests.cs`:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using CodeGenerationEngine.Service;

namespace CodeGenerator.Tests.Core;

[TestClass]
[DoNotParallelize]
public sealed class LearningCommandsTests
{
    [DataTestMethod]
    [DataRow("show learning", IntentType.ShowLearning)]
    [DataRow("reset learning", IntentType.ResetLearning)]
    public void LearningCommands_AreDetected(string input, IntentType expected)
    {
        var semantic = SemanticAnalyzer.Analyze(input);
        var intent = CodeGenerationEngine.Core.IntentAnalyzer.Detect(semantic);
        Assert.AreEqual(expected, intent);
    }

    [TestMethod]
    public void ResetLearning_ClearsMemory()
    {
        LearnedIntentStore.UseInMemory();
        LearnedIntentStore.Current.Reinforce("show my widgets", IntentType.ListClasses);

        var workspace = Path.Combine(Path.GetTempPath(), "cg_lc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        ProjectMetaData? project = null;
        CommandHandlingService.ProcessInput(
            "reset learning",
            ref project,
            new List<PropertyRule>(),
            workspace,
            _ => { });

        Assert.AreEqual(0, LearnedIntentStore.Current.Entries.Count);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~LearningCommandsTests"`
Expected: FAIL to compile — `IntentType.ShowLearning` / `ResetLearning` do not exist.

- [ ] **Step 3: Add the intent values**

In `CodeGenerator/CodeGenerationEngine/Models/IntentType.cs`, add two values to the enum (place them after `ValidateProject,`):

```csharp
    // Learning
    ShowLearning,
    ResetLearning,
```

- [ ] **Step 4: Detect the commands deterministically**

In `CodeGenerator/CodeGenerationEngine/Core/IntentAnalyzer.cs`, inside `DetectWithConfidence(string, string?)`, immediately **before** the `TryDetectClassListing` block, add:

```csharp
        if (Regex.IsMatch(sentence, @"^\s*(show|list|view)\s+learning\s*$", RegexOptions.IgnoreCase))
            return new IntentMatch(IntentType.ShowLearning, 999, 1.0, "Show learning command");

        if (Regex.IsMatch(sentence, @"^\s*(reset|clear|forget)\s+learning\s*$", RegexOptions.IgnoreCase))
            return new IntentMatch(IntentType.ResetLearning, 999, 1.0, "Reset learning command");
```

- [ ] **Step 5: Add the handlers**

In `CodeGenerator/CodeGenerationEngine/Service/CommandHandlingService.cs`, register both intents in the `Handlers` dictionary (add after the `[IntentType.ListClasses] = ...` entry):

```csharp
            [IntentType.ShowLearning] = _ => HandleShowLearning(),
            [IntentType.ResetLearning] = _ => HandleResetLearning(),
```

Then add the two handler methods (place them next to the other `Handle*` methods):

```csharp
    private static bool HandleShowLearning()
    {
        var entries = LearnedIntentStore.Current.Entries
            .OrderByDescending(e => e.Confidence)
            .ToList();

        if (entries.Count == 0)
        {
            Console.WriteLine("No learned commands yet.");
            return true;
        }

        Console.WriteLine();
        Console.WriteLine("Learned commands");
        Console.WriteLine("--------------------------------");
        foreach (var entry in entries)
        {
            Console.WriteLine(
                $"{entry.Intent,-22} conf={entry.Confidence:0.00} " +
                $"(+{entry.SuccessCount}/-{entry.FailureCount})  [{entry.Signature}]");
        }
        Console.WriteLine();
        return true;
    }

    private static bool HandleResetLearning()
    {
        LearnedIntentStore.Current.Reset();
        LearnedIntentStore.Save();
        Console.WriteLine("Learned command memory cleared.");
        return true;
    }
```

If `CommandHandlingService.cs` does not already have `using CodeGenerationEngine.Core;`, add it (it references `ProjectManager`, so it does).

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~LearningCommandsTests"`
Expected: PASS (3 tests).

- [ ] **Step 7: Run the full suite**

Run: `dotnet test CodeGenerator/CodeGenerator.Tests/CodeGenerator.Tests.csproj`
Expected: PASS (all green).

- [ ] **Step 8: Commit**

```bash
git add CodeGenerator/CodeGenerationEngine/Models/IntentType.cs CodeGenerator/CodeGenerationEngine/Core/IntentAnalyzer.cs CodeGenerator/CodeGenerationEngine/Service/CommandHandlingService.cs CodeGenerator/CodeGenerator.Tests/Core/LearningCommandsTests.cs
git commit -m "Add 'show learning' and 'reset learning' commands"
```

---

### Task 9: End-to-end verification via the one-shot CLI

**Files:**
- None (manual verification using the `CodeGenerator "<command>"` one-shot mode).

- [ ] **Step 1: Build the console app**

Run: `dotnet build CodeGenerator/CodeGenerator/CodeGenerator.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Exercise learning in a scratch workspace**

Run (PowerShell):

```powershell
$ws = Join-Path $env:TEMP ("cg_learn_" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $ws | Out-Null
$env:PROJECTPATH = $ws
$dll = "CodeGenerator/CodeGenerator/bin/Debug/net10.0/CodeGenerator.dll"
dotnet $dll "create class Alpha"
dotnet $dll "show learning"
```

Expected: the first command saves `Alpha.cs` under `$ws\Generated`; `show learning` lists a `CreateClass` entry with `+1/-0`. A file `$ws\.codegen\learned.json` exists.

- [ ] **Step 3: Verify the off-switch**

Run (PowerShell):

```powershell
$env:CG_NO_LEARN = "1"
dotnet $dll "create class Beta"
dotnet $dll "show learning"
$env:CG_NO_LEARN = $null
```

Expected: `show learning` does not gain a `Beta`/`CreateClass` entry (learning disabled). Clean up: `Remove-Item -Recurse -Force $ws; $env:PROJECTPATH=$null`.

- [ ] **Step 4: Commit (docs only, if any notes added)**

No code change; nothing to commit unless notes were added.

---

## Notes for the implementer

- Run every `dotnet` command from the repo root `D:\PERSONAL-REPOSITORY`.
- The existing test assembly sets `[assembly: Parallelize(Scope = MethodLevel)]`; any test class that touches the static `LearnedIntentStore` / `LearningCoordinator` / `ProjectManager` MUST be marked `[DoNotParallelize]` (already done in the tests above).
- `TextTokenizer.Tokenize` already learns vocabulary during tokenization, so `IntentLearningEngine.Learn` in Task 6 is additive reinforcement of unknown tokens, not the only vocabulary path.
- Do not modify `intents.json` or the base `IntentRules`; all learning lives in the new files.
