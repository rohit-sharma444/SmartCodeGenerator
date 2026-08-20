# `build app` Blazor Workflow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Interactive `build app` opens a Blazor page that shows the whole build pipeline as a clickable flow diagram and carries the user end to end — description, app type, data, look, discovery, model review, generate, build, run.

**Architecture:** Blazor Server hosted **in-process by the CLI**. `CodeGenerator.csproj` switches to `Microsoft.NET.Sdk.Web`; `build app` builds a `WebApplication` on a free loopback port, opens the browser, and blocks until the workflow reaches a terminal state. A headless `BuildSession` state machine holds every decision, so the whole workflow is unit-testable without a browser. The components call the same console-free services the console path calls — nothing is reimplemented.

**Tech Stack:** .NET 10, Blazor Server (`MapRazorComponents` + `AddInteractiveServerRenderMode`), MSTest 4.0.2, bUnit (new), Kestrel on `127.0.0.1`.

**Spec:** `docs/superpowers/specs/2026-08-17-build-app-web-workflow-design.md`

## Global Constraints

- **Branch:** `build-app-web-workflow`. Already created; do not work on `main`.
- **Target framework:** `net10.0`, from `Directory.Build.props`. Do not set it per-project.
- **Nullable and ImplicitUsings:** both `enable`, from `Directory.Build.props`.
- **Redirected stdin must not change, at all.** The `!Console.IsInputRedirected` guard at `CodeGenerator/App/BuildAppCommand.cs:93` stays. If any existing piped/E2E test needs editing, the change is wrong — stop and re-read the spec's Scope section.
- **No new projects.** Production code goes in `CodeGenerator/Web/`; tests go in `CodeGenerator.Tests/Web/`. `SmartCodeGenerator.slnx` is not edited.
- **Do not move `BuildAppCommand`'s internal rules.** `Merge`, `AppNameFor`, `CombineReports`, `LayoutNotice`, `IsPersistenceAllowed`, `WhyLines`, `AddLine` are called where they are. Their seven test files under `CodeGenerator.Tests/App/` must stay untouched and green.
- **Do not change `ProcessRunner.Run`.** Add `RunStreaming` alongside it (Task 8).
- **Verification command** (every task ends with it):
  `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror --no-incremental`
  then `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release`.
  Run from `D:\SmartCodeGenerator-Repository` — **the git root is the parent of the `SmartCodeGenerator` folder.**
- **Known false failure:** adding tests to `CodeGenerator.Tests` can redden `ProcessRunnerTests` in **Debug** through CPU starvation. If that suite alone goes red, `git stash` and re-run before debugging it.
- **MSTest idiom in this repo:** `[TestClass]` / `[TestMethod]`, underscore-separated method names, `Assert.IsNull` / `Assert.IsNotNull` / `Assert.IsTrue` / `StringAssert.Contains`. Analyzer rule MSTEST0037 **rejects** `Assert.AreEqual(n, x.Count)` — use `Assert.HasCount(n, x)`.
- **The `Write` tool emits LF-only in this CRLF checkout.** For new `.cs`/`.razor` files that is fine; never use it to rewrite an existing file wholesale.
- **Commit after every task**, message prefixed `feat:`, `test:`, `refactor:` or `docs:`.

---

## File Structure

**Created — production (all under `CodeGenerator/Web/`):**

| File | Responsibility |
| --- | --- |
| `Workflow/BuildStage.cs` | the seven-node enum |
| `Workflow/StageState.cs` | `Pending` / `Active` / `Done` / `Failed`, and `SessionPhase` |
| `Workflow/StageGraph.cs` | node titles, order, and the invalidation table |
| `Workflow/BuildSession.cs` | the state machine: answers, transitions, revisit invalidation |
| `Workflow/DiscoveryStep.cs` | node 5's work: discover, or read + analyse |
| `Workflow/GenerateStep.cs` | node 7's work: scaffold, build, run |
| `BuildOutcome.cs` | what the console prints when the workflow ends |
| `BuildAppWebHost.cs` | Kestrel, loopback binding, session token, lifetime |
| `Components/App.razor` | root component and `<head>` |
| `Components/Routes.razor` | the single route |
| `Components/BuildWorkflow.razor` | the page: diagram + active panel + sidebar |
| `Components/StageDiagram.razor` | the node graph |
| `Components/Stages/DescribeStage.razor` … `GenerateStage.razor` | one per node (7 files) |
| `wwwroot/workflow.css` | diagram and panel styling |

**Modified:**

| File | Change |
| --- | --- |
| `CodeGenerator/CodeGenerator.csproj` | SDK → `Microsoft.NET.Sdk.Web` |
| `CodeGenerator/App/BuildAppCommand.cs:93-118` | interactive branch calls `BuildAppWebHost.Run` |
| `CodeGenerationEngine/Helper/ProcessRunner.cs` | add `RunStreaming` |
| `CodeGenerator.Tests/CodeGenerator.Tests.csproj` | add bUnit |
| `CodeGenerationEngine/Core/CommandDictionary.cs` | `build app` help text |
| `docs/commands.md`, `docs/architecture.md`, `CodeGenerator/README.md`, `docs/user-manual-source.html` | documentation |

---

### Task 1: Switch the CLI to the Web SDK

The whole design rests on the CLI project being able to host Kestrel. Prove that first, in a commit that changes nothing else.

**Files:**
- Modify: `CodeGenerator/CodeGenerator.csproj:2`

**Interfaces:**
- Consumes: nothing.
- Produces: a `CodeGenerator` project that can call `WebApplication.CreateBuilder` and serve Razor components. No new types.

- [ ] **Step 1: Record the current console behaviour**

Run and keep the output — this is the before-picture that Step 4 compares against:

```powershell
"status", "exit" | dotnet run --project CodeGenerator -c Release
```

- [ ] **Step 2: Change the SDK attribute**

In `CodeGenerator/CodeGenerator.csproj`, change line 2 only:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
```

`<OutputType>Exe</OutputType>` stays. Add nothing else — the Web SDK brings the ASP.NET Core framework reference in on its own.

- [ ] **Step 3: Build the whole solution**

```powershell
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror --no-incremental
```

Expected: PASS. If it fails on a duplicate implicit `using`, the Web SDK has added a namespace that a file already imports — remove the redundant `using`, do not disable `ImplicitUsings`.

- [ ] **Step 4: Prove the REPL still works and the suite is still green**

```powershell
"status", "exit" | dotnet run --project CodeGenerator -c Release
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release
```

Expected: identical console output to Step 1, and all tests pass. The `bin` folder now also contains `CodeGenerator.staticwebassets.endpoints.json` — that is expected and harmless.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/CodeGenerator.csproj
git commit -m "refactor(cli): host on the Web SDK so build app can serve a page"
```

---

### Task 2: The stage graph

The seven nodes, their order, and the invalidation table. Pure data and pure functions — no session, no UI.

**Files:**
- Create: `CodeGenerator/Web/Workflow/BuildStage.cs`
- Create: `CodeGenerator/Web/Workflow/StageState.cs`
- Create: `CodeGenerator/Web/Workflow/StageGraph.cs`
- Test: `CodeGenerator.Tests/Web/StageGraphTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `enum BuildStage { Describe, AppType, Data, Look, Discover, Review, Generate }`
  - `enum StageState { Pending, Active, Done, Failed }`
  - `enum SessionPhase { Running, Cancelled, Generated, Abandoned }`
  - `record StageNode(BuildStage Stage, string Title, string Blurb)`
  - `StageGraph.Nodes` → `IReadOnlyList<StageNode>` in workflow order
  - `StageGraph.Next(BuildStage)` → `BuildStage?`
  - `StageGraph.Previous(BuildStage)` → `BuildStage?`
  - `StageGraph.Invalidates(BuildStage)` → `IReadOnlyList<BuildStage>`

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/Web/StageGraphTests.cs`:

```csharp
using CodeGenerator.Web.Workflow;

namespace CodeGenerator.Tests.Web;

/// <summary>The workflow's shape: seven nodes in order, and what changing one costs.</summary>
[TestClass]
public class StageGraphTests
{
    [TestMethod]
    public void Has_seven_nodes_in_workflow_order()
    {
        Assert.HasCount(7, StageGraph.Nodes);
        Assert.AreEqual(BuildStage.Describe, StageGraph.Nodes[0].Stage);
        Assert.AreEqual(BuildStage.Generate, StageGraph.Nodes[6].Stage);
    }

    [TestMethod]
    public void Every_node_has_a_title_and_a_blurb()
    {
        foreach (var node in StageGraph.Nodes)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(node.Title), $"{node.Stage} has no title");
            Assert.IsFalse(string.IsNullOrWhiteSpace(node.Blurb), $"{node.Stage} has no blurb");
        }
    }

    [TestMethod]
    public void Next_walks_forward_and_stops_at_the_end()
    {
        Assert.AreEqual(BuildStage.AppType, StageGraph.Next(BuildStage.Describe));
        Assert.AreEqual(BuildStage.Review, StageGraph.Next(BuildStage.Discover));
        Assert.IsNull(StageGraph.Next(BuildStage.Generate));
    }

    [TestMethod]
    public void Previous_walks_back_and_stops_at_the_start()
    {
        Assert.AreEqual(BuildStage.Describe, StageGraph.Previous(BuildStage.AppType));
        Assert.IsNull(StageGraph.Previous(BuildStage.Describe));
    }

    // One test per row of the spec's invalidation table. A description or a data source
    // changes what discovery would produce; an app type and a customization are not read
    // until Generate, so changing them costs nothing.
    [TestMethod]
    public void Changing_the_description_invalidates_discovery_onward()
    {
        var invalidated = StageGraph.Invalidates(BuildStage.Describe);

        Assert.HasCount(3, invalidated);
        Assert.Contains(BuildStage.Discover, invalidated);
        Assert.Contains(BuildStage.Review, invalidated);
        Assert.Contains(BuildStage.Generate, invalidated);
    }

    [TestMethod]
    public void Changing_the_data_source_invalidates_discovery_onward()
        => Assert.HasCount(3, StageGraph.Invalidates(BuildStage.Data));

    [TestMethod]
    public void Changing_discovery_invalidates_the_review_onward()
    {
        var invalidated = StageGraph.Invalidates(BuildStage.Discover);

        Assert.HasCount(2, invalidated);
        Assert.Contains(BuildStage.Review, invalidated);
        Assert.Contains(BuildStage.Generate, invalidated);
    }

    [TestMethod]
    public void Changing_an_answer_read_only_at_generate_invalidates_nothing()
    {
        Assert.IsEmpty(StageGraph.Invalidates(BuildStage.AppType));
        Assert.IsEmpty(StageGraph.Invalidates(BuildStage.Look));
        Assert.IsEmpty(StageGraph.Invalidates(BuildStage.Review));
        Assert.IsEmpty(StageGraph.Invalidates(BuildStage.Generate));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
dotnet test CodeGenerator.Tests -c Release --filter StageGraphTests
```

Expected: FAIL to compile — `The type or namespace name 'Web' does not exist in the namespace 'CodeGenerator'`.

- [ ] **Step 3: Write the implementation**

`CodeGenerator/Web/Workflow/BuildStage.cs`:

```csharp
namespace CodeGenerator.Web.Workflow;

/// <summary>
/// The seven nodes of the `build app` workflow, in the order the diagram draws them.
/// </summary>
/// <remarks>
/// The order matters twice: <see cref="StageGraph.Next"/> and
/// <see cref="StageGraph.Previous"/> are declaration-order walks, and the diagram lays the
/// nodes out by index. A stage inserted in the middle therefore needs no other change here —
/// but it does need a row in <see cref="StageGraph.Invalidates"/>, which is deliberately a
/// total switch so the compiler asks for one.
/// </remarks>
internal enum BuildStage
{
    Describe,
    AppType,
    Data,
    Look,
    Discover,
    Review,
    Generate
}
```

`CodeGenerator/Web/Workflow/StageState.cs`:

```csharp
namespace CodeGenerator.Web.Workflow;

/// <summary>How one node draws, and whether it can be revisited.</summary>
internal enum StageState
{
    /// <summary>Not reached, or invalidated by a change upstream.</summary>
    Pending,

    /// <summary>The node whose panel is open.</summary>
    Active,

    /// <summary>Answered. Clickable — this is what makes the workflow revisitable.</summary>
    Done,

    /// <summary>Tried and failed: a fetch that threw, a file that would not read. Carries its
    /// own message, and offers a way back rather than ending the workflow.</summary>
    Failed
}

/// <summary>Whether the workflow is still going, and if not, how it ended.</summary>
/// <remarks>
/// <see cref="Abandoned"/> is distinct from <see cref="Cancelled"/> on purpose: a user who
/// closed the tab did not decide anything, and the console says so differently from one who
/// pressed Cancel.
/// </remarks>
internal enum SessionPhase
{
    Running,
    Cancelled,
    Generated,
    Abandoned
}
```

`CodeGenerator/Web/Workflow/StageGraph.cs`:

```csharp
namespace CodeGenerator.Web.Workflow;

/// <summary>One node as the diagram needs it: what to call it, and one line of what it does.</summary>
internal sealed record StageNode(BuildStage Stage, string Title, string Blurb);

/// <summary>
/// The workflow's shape. Pure: nodes, the walk between them, and what changing an answer costs.
/// </summary>
/// <remarks>
/// Separate from <see cref="BuildSession"/> for the reason <c>SelectionState</c> is separate
/// from <c>SelectionPrompt</c> — everything decidable is lifted out of the thing that holds
/// mutable state, so it can be asserted directly.
/// </remarks>
internal static class StageGraph
{
    public static IReadOnlyList<StageNode> Nodes { get; } =
    [
        new(BuildStage.Describe, "Describe", "What are we building?"),
        new(BuildStage.AppType,  "App type", "Which kind of application"),
        new(BuildStage.Data,     "Data",     "A file to read, or discover from the web"),
        new(BuildStage.Look,     "Look",     "Branding, theme and features"),
        new(BuildStage.Discover, "Discover", "Work out the classes"),
        new(BuildStage.Review,   "Review",   "Change the model before it is written"),
        new(BuildStage.Generate, "Generate", "Write it, build it, run it"),
    ];

    public static BuildStage? Next(BuildStage stage)
    {
        var index = IndexOf(stage);

        return index + 1 < Nodes.Count ? Nodes[index + 1].Stage : null;
    }

    public static BuildStage? Previous(BuildStage stage)
    {
        var index = IndexOf(stage);

        return index > 0 ? Nodes[index - 1].Stage : null;
    }

    /// <summary>
    /// The stages whose results are worthless once <paramref name="changed"/> is answered
    /// differently.
    /// </summary>
    /// <remarks>
    /// Not simply "everything downstream". An application type and a customization are not read
    /// until <see cref="BuildStage.Generate"/>, so changing either leaves a discovered model
    /// perfectly valid — and re-running a Wikipedia fetch because a checkbox moved would be the
    /// worst version of this feature. A description, by contrast, is the discovery input AND the
    /// app name, so changing it invalidates everything discovery produced.
    /// <para>
    /// A total switch, so a stage added to <see cref="BuildStage"/> cannot compile without a
    /// decision being made here.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<BuildStage> Invalidates(BuildStage changed)
        => changed switch
        {
            BuildStage.Describe => [BuildStage.Discover, BuildStage.Review, BuildStage.Generate],
            BuildStage.Data     => [BuildStage.Discover, BuildStage.Review, BuildStage.Generate],
            BuildStage.Discover => [BuildStage.Review, BuildStage.Generate],
            BuildStage.AppType  => [],
            BuildStage.Look     => [],
            BuildStage.Review   => [],
            BuildStage.Generate => [],
            _ => throw new InvalidOperationException($"No invalidation rule for {changed}."),
        };

    /// <summary>Where <paramref name="stage"/> sits in the walk. A miss is a wiring bug: every
    /// member of the enum has a node.</summary>
    private static int IndexOf(BuildStage stage)
    {
        for (var i = 0; i < Nodes.Count; i++)
            if (Nodes[i].Stage == stage)
                return i;

        throw new InvalidOperationException($"No node for stage {stage}.");
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```powershell
dotnet test CodeGenerator.Tests -c Release --filter StageGraphTests
```

Expected: PASS, 8 tests.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/Web/Workflow CodeGenerator.Tests/Web
git commit -m "feat(web): the build-app workflow stage graph"
```

---

### Task 3: `BuildSession` — answers, transitions and invalidation

The state machine. Everything the workflow decides lives here, and nothing here touches a browser.

**Files:**
- Create: `CodeGenerator/Web/Workflow/BuildSession.cs`
- Test: `CodeGenerator.Tests/Web/BuildSessionTests.cs`

**Interfaces:**
- Consumes: `BuildStage`, `StageState`, `SessionPhase`, `StageGraph` (Task 2).
- Produces:
  - `BuildSession(string description, string workspaceRoot, AppCustomization start, DataSourceSpec? source = null)`
  - `Current` → `BuildStage`; `Phase` → `SessionPhase`; `StateOf(BuildStage)` → `StageState`
  - `Description`, `Target`, `Source`, `HasData`, `Customization`, `Model`, `FailureMessage`
  - `SetDescription(string)`, `SetTarget(ITargetScaffolder)`, `SetNoData()`, `SetSource(DataSourceSpec)`, `SetCustomization(AppCustomization)`, `SetModel(CandidateDomainModel)`
  - `Advance()`, `GoTo(BuildStage)`, `Fail(string)`, `Cancel()`, `Abandon()`, `Complete()`
  - `WorkspaceRoot` → `string`

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/Web/BuildSessionTests.cs`:

```csharp
using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerator.Web.Workflow;
using DataIngestion;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// The workflow's state machine. Every rule the page depends on is asserted here rather than
/// through a rendered component: the decisions are the session's, and a browser proves nothing
/// about them.
/// </summary>
[TestClass]
public class BuildSessionTests
{
    private static BuildSession New(string description = "")
        => new(description, @"C:\ws", AppCustomization.Default);

    [TestMethod]
    public void Starts_at_the_first_node_with_nothing_answered()
    {
        var session = New();

        Assert.AreEqual(BuildStage.Describe, session.Current);
        Assert.AreEqual(StageState.Active, session.StateOf(BuildStage.Describe));
        Assert.AreEqual(StageState.Pending, session.StateOf(BuildStage.AppType));
        Assert.AreEqual(SessionPhase.Running, session.Phase);
    }

    // `build app: a library system` has already answered node 1, and re-asking would make the
    // shortcut pointless.
    [TestMethod]
    public void An_inline_description_opens_the_workflow_at_the_app_type()
    {
        var session = New("a library system");

        Assert.AreEqual(BuildStage.AppType, session.Current);
        Assert.AreEqual(StageState.Done, session.StateOf(BuildStage.Describe));
        Assert.AreEqual("a library system", session.Description);
    }

    // `build app from csv orders.csv` has answered node 3 the same way.
    [TestMethod]
    public void An_inline_source_marks_the_data_node_answered()
    {
        var session = new BuildSession(
            "", @"C:\ws", AppCustomization.Default,
            new DataSourceSpec(DataSourceKind.Csv, @"C:\data\orders.csv"));

        Assert.AreEqual(StageState.Done, session.StateOf(BuildStage.Data));
        Assert.IsTrue(session.HasData);
    }

    [TestMethod]
    public void Advancing_marks_the_current_node_done_and_opens_the_next()
    {
        var session = New();

        session.SetDescription("a shop");
        session.Advance();

        Assert.AreEqual(StageState.Done, session.StateOf(BuildStage.Describe));
        Assert.AreEqual(BuildStage.AppType, session.Current);
        Assert.AreEqual(StageState.Active, session.StateOf(BuildStage.AppType));
    }

    [TestMethod]
    public void GoTo_reopens_a_done_node_without_losing_the_answers_after_it()
    {
        var session = New("a shop");
        session.SetTarget(new WpfTarget());
        session.Advance();

        session.GoTo(BuildStage.Describe);

        Assert.AreEqual(BuildStage.Describe, session.Current);
        Assert.AreEqual(StageState.Active, session.StateOf(BuildStage.Describe));
        Assert.AreEqual(StageState.Done, session.StateOf(BuildStage.AppType));
        Assert.IsNotNull(session.Target);
    }

    [TestMethod]
    public void GoTo_a_pending_node_is_refused()
    {
        var session = New();

        Assert.ThrowsExactly<InvalidOperationException>(() => session.GoTo(BuildStage.Review));
    }

    // The rule the whole diagram lives on. A model discovered from one description must not
    // survive a different description.
    [TestMethod]
    public void Changing_the_description_discards_the_discovered_model()
    {
        var session = Discovered();

        session.SetDescription("a hotel");

        Assert.IsNull(session.Model);
        Assert.AreEqual(StageState.Pending, session.StateOf(BuildStage.Discover));
        Assert.AreEqual(StageState.Pending, session.StateOf(BuildStage.Review));
    }

    [TestMethod]
    public void Changing_the_data_source_discards_the_discovered_model()
    {
        var session = Discovered();

        session.SetSource(new DataSourceSpec(DataSourceKind.Csv, @"C:\data\other.csv"));

        Assert.IsNull(session.Model);
        Assert.AreEqual(StageState.Pending, session.StateOf(BuildStage.Discover));
    }

    // The counterpart, and the reason Invalidates is not just "everything downstream": the
    // target is not read until Generate, so a discovered model is still perfectly good.
    [TestMethod]
    public void Changing_the_app_type_keeps_the_discovered_model()
    {
        var session = Discovered();

        session.SetTarget(new WebApiTarget());

        Assert.IsNotNull(session.Model);
        Assert.AreEqual(StageState.Done, session.StateOf(BuildStage.Discover));
    }

    [TestMethod]
    public void Changing_the_customization_keeps_the_discovered_model()
    {
        var session = Discovered();

        session.SetCustomization(AppCustomization.Default with { Company = "Acme" });

        Assert.IsNotNull(session.Model);
        Assert.AreEqual(StageState.Done, session.StateOf(BuildStage.Discover));
    }

    // Re-answering with the SAME value is not a change. Clicking back into a node to look at it
    // and clicking Next must not throw away a fetch.
    [TestMethod]
    public void Re_answering_with_the_same_value_invalidates_nothing()
    {
        var session = Discovered();

        session.SetDescription("a shop");

        Assert.IsNotNull(session.Model);
        Assert.AreEqual(StageState.Done, session.StateOf(BuildStage.Discover));
    }

    [TestMethod]
    public void Choosing_no_data_after_a_source_clears_the_source()
    {
        var session = New("a shop");
        session.SetSource(new DataSourceSpec(DataSourceKind.Csv, @"C:\data\orders.csv"));

        session.SetNoData();

        Assert.IsNull(session.Source);
        Assert.IsFalse(session.HasData);
    }

    [TestMethod]
    public void Failing_a_node_records_the_message_and_leaves_the_workflow_running()
    {
        var session = New("a shop");

        session.Fail("No file at C:\\nope.csv");

        Assert.AreEqual(StageState.Failed, session.StateOf(BuildStage.Describe));
        Assert.AreEqual("No file at C:\\nope.csv", session.FailureMessage);
        Assert.AreEqual(SessionPhase.Running, session.Phase);
    }

    [TestMethod]
    public void Cancelling_and_abandoning_are_different_endings()
    {
        var cancelled = New();
        cancelled.Cancel();
        Assert.AreEqual(SessionPhase.Cancelled, cancelled.Phase);

        var abandoned = New();
        abandoned.Abandon();
        Assert.AreEqual(SessionPhase.Abandoned, abandoned.Phase);
    }

    [TestMethod]
    public void A_terminal_session_refuses_further_answers()
    {
        var session = New();
        session.Cancel();

        Assert.ThrowsExactly<InvalidOperationException>(() => session.SetDescription("too late"));
    }

    /// <summary>A session with a model, parked on the review node — the state most of the
    /// invalidation rules are about.</summary>
    private static BuildSession Discovered()
    {
        var session = New("a shop");
        session.SetTarget(new WpfTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();
        session.SetModel(new DomainDiscovery.Model.CandidateDomainModel());
        session.Advance();

        return session;
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
dotnet test CodeGenerator.Tests -c Release --filter BuildSessionTests
```

Expected: FAIL to compile — `The type or namespace name 'BuildSession' could not be found`.

- [ ] **Step 3: Write the implementation**

`CodeGenerator/Web/Workflow/BuildSession.cs`:

```csharp
using AppScaffolding;
using DataIngestion;
using DomainDiscovery.Model;

namespace CodeGenerator.Web.Workflow;

/// <summary>
/// Every decision a `build app` run makes, and the rules for changing one's mind.
/// </summary>
/// <remarks>
/// Deliberately free of Razor, Kestrel and the console: the components read these properties and
/// call these methods, and the whole workflow is therefore assertable as plain C#. This is the
/// same split <c>SelectionState</c> / <c>SelectionPrompt</c> already uses, one layer up.
/// <para>
/// Registered as a singleton for the host's lifetime, so a second browser tab attaches to the
/// running workflow rather than starting a rival one against the same workspace.
/// </para>
/// </remarks>
internal sealed class BuildSession
{
    private readonly Dictionary<BuildStage, StageState> _states = new();

    /// <param name="description">
    /// What followed `build app:`, or empty. A description typed inline has already answered
    /// node 1, so the workflow opens at node 2.
    /// </param>
    /// <param name="start">
    /// The remembered profile with anything typed on the command line layered over it —
    /// <c>BuildAppCommand.Merge(CustomizationDefaults.Load(root), inline)</c>.
    /// </param>
    /// <param name="source">
    /// A `from &lt;format&gt; &lt;location&gt;` typed inline, which has answered node 3.
    /// </param>
    public BuildSession(
        string description, string workspaceRoot, AppCustomization start,
        DataSourceSpec? source = null)
    {
        WorkspaceRoot = workspaceRoot;
        Description = description ?? string.Empty;
        Customization = start;
        Source = source;

        foreach (var node in StageGraph.Nodes)
            _states[node.Stage] = StageState.Pending;

        // Inline answers arrive already made, so the nodes that hold them open green and the
        // workflow parks on the first thing it does not know.
        if (Description.Length > 0)
            _states[BuildStage.Describe] = StageState.Done;

        if (source is not null)
            _states[BuildStage.Data] = StageState.Done;

        Current = FirstUnanswered();
        _states[Current] = StageState.Active;
    }

    public string WorkspaceRoot { get; }

    public BuildStage Current { get; private set; }

    public SessionPhase Phase { get; private set; } = SessionPhase.Running;

    public string Description { get; private set; }

    public ITargetScaffolder? Target { get; private set; }

    public DataSourceSpec? Source { get; private set; }

    /// <summary>Whether node 3 was answered "I have a file". Distinct from
    /// <c>Source is not null</c> only in intent, and read by node 5 to choose its shape.</summary>
    public bool HasData => Source is not null;

    public AppCustomization Customization { get; private set; }

    public CandidateDomainModel? Model { get; private set; }

    /// <summary>Why the node that failed, failed. Null whenever nothing has.</summary>
    public string? FailureMessage { get; private set; }

    public StageState StateOf(BuildStage stage) => _states[stage];

    public void SetDescription(string text)
    {
        var value = (text ?? string.Empty).Trim();

        Change(BuildStage.Describe, value != Description, () => Description = value);
    }

    public void SetTarget(ITargetScaffolder target)
        => Change(
            BuildStage.AppType,
            !string.Equals(target.Key, Target?.Key, StringComparison.OrdinalIgnoreCase),
            () => Target = target);

    public void SetSource(DataSourceSpec source)
        => Change(BuildStage.Data, source != Source, () => Source = source);

    public void SetNoData()
        => Change(BuildStage.Data, Source is not null, () => Source = null);

    public void SetCustomization(AppCustomization customization)
        => Change(BuildStage.Look, customization != Customization, () => Customization = customization);

    public void SetModel(CandidateDomainModel model)
        => Change(BuildStage.Discover, changed: true, () => Model = model);

    /// <summary>
    /// Applies an answer, and discards whatever it invalidated.
    /// </summary>
    /// <remarks>
    /// <paramref name="changed"/> is what makes clicking back into a node harmless: re-answering
    /// with the same value is not a change, so looking at an answer and pressing Next does not
    /// throw away a Wikipedia fetch.
    /// </remarks>
    private void Change(BuildStage stage, bool changed, Action apply)
    {
        RequireRunning();

        apply();

        if (!changed)
            return;

        FailureMessage = null;

        foreach (var invalidated in StageGraph.Invalidates(stage))
        {
            if (_states[invalidated] == StageState.Active)
                continue;

            _states[invalidated] = StageState.Pending;

            if (invalidated == BuildStage.Discover)
                Model = null;
        }
    }

    /// <summary>Marks the open node answered and opens the next one.</summary>
    public void Advance()
    {
        RequireRunning();

        _states[Current] = StageState.Done;

        var next = StageGraph.Next(Current);

        if (next is null)
            return;

        Current = next.Value;
        _states[Current] = StageState.Active;
    }

    /// <summary>Reopens an answered node. This is what a click on a green node does.</summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="stage"/> has not been reached. A Pending node has nothing to show and no
    /// answer to change, so a click on one is a wiring bug rather than user input.
    /// </exception>
    public void GoTo(BuildStage stage)
    {
        RequireRunning();

        if (_states[stage] == StageState.Pending)
            throw new InvalidOperationException($"{stage} has not been reached yet.");

        // The node being left keeps whatever it had. Marking it Pending would erase an answer
        // the user only navigated away from, and every answer downstream is still valid — that
        // is what Invalidates is for.
        if (_states[Current] == StageState.Active)
            _states[Current] = StageState.Done;

        Current = stage;
        _states[stage] = StageState.Active;
    }

    /// <summary>Records that the open node could not do its work. Not terminal: the panel offers
    /// a way back, because a mistyped path is not a reason to abandon an interview.</summary>
    public void Fail(string message)
    {
        RequireRunning();

        FailureMessage = message;
        _states[Current] = StageState.Failed;
    }

    public void Cancel() => Phase = SessionPhase.Cancelled;

    /// <summary>The tab was closed. Distinct from <see cref="Cancel"/>: nothing was decided.</summary>
    public void Abandon() => Phase = SessionPhase.Abandoned;

    public void Complete()
    {
        _states[BuildStage.Generate] = StageState.Done;
        Phase = SessionPhase.Generated;
    }

    private void RequireRunning()
    {
        if (Phase != SessionPhase.Running)
            throw new InvalidOperationException($"The workflow has already ended ({Phase}).");
    }

    /// <summary>The first node with no answer, which is where an inline-answered run opens.</summary>
    private BuildStage FirstUnanswered()
    {
        foreach (var node in StageGraph.Nodes)
            if (_states[node.Stage] != StageState.Done)
                return node.Stage;

        return BuildStage.Generate;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```powershell
dotnet test CodeGenerator.Tests -c Release --filter BuildSessionTests
```

Expected: PASS, 15 tests.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/Web/Workflow/BuildSession.cs CodeGenerator.Tests/Web/BuildSessionTests.cs
git commit -m "feat(web): the build-app workflow state machine"
```

---

### Task 4: `BuildAppWebHost` — Kestrel, loopback, token, lifetime

The host, serving a stub page. Real components come later; what this task settles is that the CLI can put a page on a loopback port, keep only the intended visitor out, and shut down without hanging.

**Files:**
- Create: `CodeGenerator/Web/BuildAppWebHost.cs`
- Create: `CodeGenerator/Web/BuildOutcome.cs`
- Create: `CodeGenerator/Web/Components/App.razor`
- Create: `CodeGenerator/Web/Components/Routes.razor`
- Create: `CodeGenerator/Web/Components/BuildWorkflow.razor` (stub)
- Create: `CodeGenerator/Web/wwwroot/workflow.css` (empty but present — the `<link>` must not 404)
- Test: `CodeGenerator.Tests/Web/BuildAppWebHostTests.cs`

**Interfaces:**
- Consumes: `BuildSession`, `SessionPhase` (Task 3).
- Produces:
  - `record BuildOutcome(SessionPhase Phase, IReadOnlyList<string> Lines)` with `BuildOutcome.Cancelled`, `BuildOutcome.Abandoned`
  - `BuildAppWebHost.Run(BuildSession session, CancellationToken ct)` → `BuildOutcome`
  - `BuildAppWebHost.TryStart(BuildSession session, out BuildAppWebHost host)` → `bool` (false when the port cannot be bound — Task 10's fallback trigger)
  - `Url` → `string` (includes the token)
  - `SessionToken` → `string`

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/Web/BuildAppWebHostTests.cs`:

```csharp
using AppScaffolding;
using CodeGenerator.Web;
using CodeGenerator.Web.Workflow;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// The host, without a browser. What matters here is not what the page looks like but that it
/// binds loopback, refuses a visitor with no token, and always stops — a CLI left blocked on an
/// abandoned workflow is the worst failure this feature can have.
/// </summary>
[TestClass]
public class BuildAppWebHostTests
{
    private static BuildSession NewSession()
        => new("a shop", Path.GetTempPath(), AppCustomization.Default);

    [TestMethod]
    public void Binds_loopback_only()
    {
        using var host = Start(out _);

        StringAssert.StartsWith(host.Url, "http://127.0.0.1:");
    }

    [TestMethod]
    public void Chooses_a_free_port_rather_than_a_fixed_one()
    {
        using var first = Start(out _);
        using var second = Start(out _);

        Assert.AreNotEqual(new Uri(first.Url).Port, new Uri(second.Url).Port);
    }

    [TestMethod]
    public async Task Serves_the_page_to_a_visitor_carrying_the_token()
    {
        using var host = Start(out _);
        using var client = new HttpClient();

        var response = await client.GetAsync(host.Url);

        Assert.IsTrue(response.IsSuccessStatusCode, $"expected 2xx, got {response.StatusCode}");
    }

    // Any process on this machine can reach a loopback port, and this one writes files and runs
    // `dotnet`. The token is what makes that safe.
    [TestMethod]
    public async Task Refuses_a_visitor_with_no_token()
    {
        using var host = Start(out _);
        using var client = new HttpClient();

        var bare = new Uri(host.Url).GetLeftPart(UriPartial.Path);
        var response = await client.GetAsync(bare);

        Assert.AreEqual(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Refuses_a_visitor_with_the_wrong_token()
    {
        using var host = Start(out _);
        using var client = new HttpClient();

        var bare = new Uri(host.Url).GetLeftPart(UriPartial.Path);
        var response = await client.GetAsync($"{bare}?t=not-the-token");

        Assert.AreEqual(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public void Run_returns_when_the_session_is_cancelled()
    {
        var session = NewSession();

        var task = Task.Run(() => BuildAppWebHost.Run(session, CancellationToken.None));

        session.Cancel();

        Assert.IsTrue(task.Wait(TimeSpan.FromSeconds(20)), "Run did not return on cancel");
        Assert.AreEqual(SessionPhase.Cancelled, task.Result.Phase);
    }

    [TestMethod]
    public void Run_returns_when_the_console_is_interrupted()
    {
        var session = NewSession();
        using var interrupt = new CancellationTokenSource();

        var task = Task.Run(() => BuildAppWebHost.Run(session, interrupt.Token));

        interrupt.Cancel();

        Assert.IsTrue(task.Wait(TimeSpan.FromSeconds(20)), "Run did not return on interrupt");
    }

    private static BuildAppWebHost Start(out BuildSession session)
    {
        session = NewSession();

        Assert.IsTrue(
            BuildAppWebHost.TryStart(session, out var host), "the host could not bind loopback");

        return host;
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
dotnet test CodeGenerator.Tests -c Release --filter BuildAppWebHostTests
```

Expected: FAIL to compile — `The type or namespace name 'BuildAppWebHost' could not be found`.

- [ ] **Step 3: Write the outcome record**

`CodeGenerator/Web/BuildOutcome.cs`:

```csharp
using CodeGenerator.Web.Workflow;

namespace CodeGenerator.Web;

/// <summary>
/// What the console prints once the browser is done with the workflow.
/// </summary>
/// <remarks>
/// Lines rather than a formatted block, for the reason <c>BuildReport</c> and <c>NextSteps</c>
/// return lines: the caller decides the colour, and a test can assert the content without
/// parsing a screen.
/// </remarks>
internal sealed record BuildOutcome(SessionPhase Phase, IReadOnlyList<string> Lines)
{
    public static BuildOutcome Cancelled { get; } =
        new(SessionPhase.Cancelled, ["Cancelled. Nothing was generated."]);

    /// <summary>The tab was closed without a decision — deliberately worded differently from
    /// <see cref="Cancelled"/>, which was one.</summary>
    public static BuildOutcome Abandoned { get; } =
        new(SessionPhase.Abandoned,
            ["The build workflow was closed before it finished. Nothing was generated."]);
}
```

- [ ] **Step 4: Write the root components**

`CodeGenerator/Web/Components/App.razor`:

```razor
@* The root component. Self-contained on purpose: no CDN, no external font. *@
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Build an application</title>
    <base href="/" />
    <link rel="stylesheet" href="workflow.css" />
    <HeadOutlet />
</head>
<body>
    <Routes />
    <script src="_framework/blazor.web.js"></script>
</body>
</html>
```

`CodeGenerator/Web/Components/Routes.razor`:

```razor
@* One route. There is no navigation, so there is no layout and no NavMenu. *@
<Router AppAssembly="typeof(Routes).Assembly">
    <Found Context="routeData">
        <RouteView RouteData="routeData" />
    </Found>
    <NotFound>
        <p>Nothing here.</p>
    </NotFound>
</Router>
```

`CodeGenerator/Web/Components/BuildWorkflow.razor` — the stub this task serves:

```razor
@page "/build"
@rendermode InteractiveServer
@using CodeGenerator.Web.Workflow
@inject BuildSession Session

<h1>Build an application</h1>
<p>Workflow stage: @Session.Current</p>
```

`CodeGenerator/Web/wwwroot/workflow.css` — created empty now so the `<link>` above does not 404; Task 5 fills it in:

```css
/* The workflow diagram and its panels. Filled in with the diagram component. */
```

- [ ] **Step 5: Write the host**

`CodeGenerator/Web/BuildAppWebHost.cs`:

```csharp
using System.Security.Cryptography;
using CodeGenerator.Web.Workflow;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CodeGenerator.Web;

/// <summary>
/// Serves the `build app` workflow on a loopback port for the length of one command.
/// </summary>
/// <remarks>
/// In-process rather than a spawned app: the components call the same services the console path
/// calls, against the same objects, so there is nothing to serialize and nothing to marshal back
/// — the scaffold runs here and the console prints the result directly.
/// <para>
/// Three things this class exists to get right. It binds port 0 and reads the assignment back, so
/// a port in use is not a failure mode. It mints a token and 404s without it, because any process
/// on this machine can reach loopback and this page writes files and runs <c>dotnet</c>. And it
/// stops on every path — a terminal session, a console interrupt, or a tab closed and never
/// reconnected — because a CLI blocked forever on an abandoned workflow is worse than no feature.
/// </para>
/// </remarks>
internal sealed class BuildAppWebHost : IDisposable
{
    /// <summary>How long the query string's token parameter is called.</summary>
    private const string TokenParameter = "t";

    /// <summary>How often <see cref="Run"/> looks at the session.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    private readonly WebApplication _app;

    private BuildAppWebHost(WebApplication app, string url, string token)
    {
        _app = app;
        Url = url;
        SessionToken = token;
    }

    /// <summary>The address to open, token and all.</summary>
    public string Url { get; }

    public string SessionToken { get; }

    /// <summary>
    /// Starts the host, or reports that it could not. False rather than an exception because a
    /// machine that cannot bind loopback is a situation with a remedy — the console wizard — not
    /// a fault to report.
    /// </summary>
    public static bool TryStart(BuildSession session, out BuildAppWebHost host)
    {
        host = null!;

        try
        {
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

            var builder = WebApplication.CreateSlimBuilder();

            // Nothing but warnings: this host shares a console with a REPL, and Kestrel's
            // startup banner in the middle of a command reads as output the tool produced.
            builder.Logging.ClearProviders();

            builder.WebHost.UseUrls("http://127.0.0.1:0");

            builder.Services.AddRazorComponents().AddInteractiveServerComponents();

            // Singleton, so a second tab attaches to the running workflow rather than starting a
            // rival one against the same workspace.
            builder.Services.AddSingleton(session);

            var app = builder.Build();

            app.UseStaticFiles();

            // Before the component pipeline, so an untokened request never reaches a circuit.
            app.Use(async (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments("/build")
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

            host = new BuildAppWebHost(app, $"{bound}/build?{TokenParameter}={token}", token);

            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException
                                      or System.Net.Sockets.SocketException)
        {
            return false;
        }
    }

    /// <summary>
    /// Runs the workflow to its end and returns what the console should print.
    /// </summary>
    /// <remarks>
    /// Polls rather than awaiting an event, for one reason: the session is mutated from a Blazor
    /// circuit on another thread, and a poll costs nothing next to a workflow a person is typing
    /// into. Every exit is bounded — there is no path here that waits forever.
    /// </remarks>
    public static BuildOutcome Run(BuildSession session, CancellationToken ct)
    {
        if (!TryStart(session, out var host))
            return new BuildOutcome(SessionPhase.Cancelled, ["Could not start the build workflow."]);

        using (host)
        {
            while (session.Phase == SessionPhase.Running)
            {
                if (ct.IsCancellationRequested)
                {
                    session.Cancel();
                    break;
                }

                Thread.Sleep(PollInterval);
            }

            return session.Phase switch
            {
                SessionPhase.Abandoned => BuildOutcome.Abandoned,
                SessionPhase.Generated => session.Outcome ?? BuildOutcome.Cancelled,
                _ => BuildOutcome.Cancelled,
            };
        }
    }

    public void Dispose()
    {
        try
        {
            _app.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // Already going down. Nothing to add.
        }

        ((IDisposable)_app).Dispose();
    }
}
```

- [ ] **Step 6: Add `Outcome` to `BuildSession`**

`Run` above reads `session.Outcome`. Add it to `CodeGenerator/Web/Workflow/BuildSession.cs`, beside `FailureMessage`:

```csharp
    /// <summary>What the console prints, set by the generate step. Null until it runs.</summary>
    public BuildOutcome? Outcome { get; private set; }
```

and change `Complete` to take it:

```csharp
    public void Complete(BuildOutcome outcome)
    {
        Outcome = outcome;
        _states[BuildStage.Generate] = StageState.Done;
        Phase = SessionPhase.Generated;
    }
```

Add `using CodeGenerator.Web;` to the file.

- [ ] **Step 7: Run the tests to verify they pass**

```powershell
dotnet test CodeGenerator.Tests -c Release --filter BuildAppWebHostTests
```

Expected: PASS, 7 tests. If `Serves_the_page_to_a_visitor_carrying_the_token` returns 404 rather than 200, the static-asset manifest is not being found — confirm Task 1 actually switched the SDK.

- [ ] **Step 8: Commit**

```bash
git add CodeGenerator/Web CodeGenerator.Tests/Web/BuildAppWebHostTests.cs
git commit -m "feat(web): host the build-app workflow on a tokened loopback port"
```

---

### Task 5: The diagram and the page shell

The thing the user actually asked for: the pipeline drawn as connected nodes, with the active one open beneath it.

**Files:**
- Create: `CodeGenerator/Web/Components/StageDiagram.razor`
- Modify: `CodeGenerator/Web/Components/BuildWorkflow.razor` (replace the stub)
- Modify: `CodeGenerator/Web/wwwroot/workflow.css`
- Modify: `CodeGenerator.Tests/CodeGenerator.Tests.csproj` (add bUnit)
- Test: `CodeGenerator.Tests/Web/StageDiagramTests.cs`

**Interfaces:**
- Consumes: `StageGraph`, `StageState`, `BuildSession` (Tasks 2–3).
- Produces: `StageDiagram` with parameters `Session` (`BuildSession`) and `OnRevisit` (`EventCallback<BuildStage>`).

- [ ] **Step 1: Add bUnit to the test project**

In `CodeGenerator.Tests/CodeGenerator.Tests.csproj`, inside the existing `PackageReference` `ItemGroup`:

```xml
    <PackageReference Include="bunit" Version="1.40.0" />
```

- [ ] **Step 2: Write the failing tests**

Create `CodeGenerator.Tests/Web/StageDiagramTests.cs`:

```csharp
using AppScaffolding;
using AppScaffolding.Targets;
using Bunit;
using CodeGenerator.Web.Components;
using CodeGenerator.Web.Workflow;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// The diagram's rendering only. Which node is active and what a click costs are
/// <see cref="BuildSession"/>'s decisions and are asserted in BuildSessionTests; what is asserted
/// here is that the four states look different and that a green node is clickable.
/// </summary>
[TestClass]
public class StageDiagramTests
{
    [TestMethod]
    public void Draws_every_node()
    {
        using var context = new BunitContext();

        var markup = Render(context, New()).Markup;

        foreach (var node in StageGraph.Nodes)
            StringAssert.Contains(markup, node.Title);
    }

    [TestMethod]
    public void Marks_the_active_node_apart_from_the_pending_ones()
    {
        using var context = new BunitContext();

        var rendered = Render(context, New());

        Assert.HasCount(1, rendered.FindAll(".stage-node.active"));
        Assert.HasCount(6, rendered.FindAll(".stage-node.pending"));
    }

    [TestMethod]
    public void Marks_an_answered_node_done()
    {
        using var context = new BunitContext();
        var session = New("a shop");

        var rendered = Render(context, session);

        Assert.HasCount(1, rendered.FindAll(".stage-node.done"));
    }

    [TestMethod]
    public void Marks_a_failed_node_failed()
    {
        using var context = new BunitContext();
        var session = New();
        session.Fail("No file there.");

        var rendered = Render(context, session);

        Assert.HasCount(1, rendered.FindAll(".stage-node.failed"));
    }

    // Clicking a green node is how the workflow is revisitable, which is the whole point of
    // drawing it as a graph rather than a progress bar.
    [TestMethod]
    public void Clicking_a_done_node_raises_revisit_with_that_stage()
    {
        using var context = new BunitContext();
        var session = New("a shop");
        BuildStage? revisited = null;

        var rendered = context.Render<StageDiagram>(parameters => parameters
            .Add(p => p.Session, session)
            .Add(p => p.OnRevisit, (BuildStage stage) => revisited = stage));

        rendered.Find(".stage-node.done button").Click();

        Assert.AreEqual(BuildStage.Describe, revisited);
    }

    [TestMethod]
    public void A_pending_node_is_not_clickable()
    {
        using var context = new BunitContext();

        var rendered = Render(context, New());

        Assert.IsEmpty(rendered.FindAll(".stage-node.pending button"));
    }

    private static IRenderedComponent<StageDiagram> Render(
        BunitContext context, BuildSession session)
        => context.Render<StageDiagram>(parameters => parameters
            .Add(p => p.Session, session)
            .Add(p => p.OnRevisit, (BuildStage _) => { }));

    private static BuildSession New(string description = "")
        => new(description, @"C:\ws", AppCustomization.Default);
}
```

- [ ] **Step 3: Run the tests to verify they fail**

```powershell
dotnet test CodeGenerator.Tests -c Release --filter StageDiagramTests
```

Expected: FAIL to compile — `The type or namespace name 'StageDiagram' could not be found`.

- [ ] **Step 4: Write the diagram**

`CodeGenerator/Web/Components/StageDiagram.razor`:

```razor
@* The pipeline, drawn. A CSS grid with pseudo-element connectors rather than an SVG with
   computed coordinates: a node whose title wraps must not need the arrows re-measuring. *@
@using CodeGenerator.Web.Workflow

<ol class="stage-diagram">
    @foreach (var node in StageGraph.Nodes)
    {
        var state = Session.StateOf(node.Stage);

        <li class="stage-node @CssFor(state)">
            @if (IsRevisitable(state))
            {
                @* A button, not a div with an onclick: a workflow the keyboard cannot drive is
                   not navigable, and the diagram is the navigation. *@
                <button type="button" @onclick="() => OnRevisit.InvokeAsync(node.Stage)"
                        title="Go back to @node.Title">
                    <span class="stage-title">@node.Title</span>
                    <span class="stage-blurb">@node.Blurb</span>
                </button>
            }
            else
            {
                <span class="stage-title">@node.Title</span>
                <span class="stage-blurb">@node.Blurb</span>
            }
        </li>
    }
</ol>

@code {
    [Parameter, EditorRequired]
    public required BuildSession Session { get; set; }

    /// <summary>Raised when a node the user may go back to is clicked.</summary>
    [Parameter]
    public EventCallback<BuildStage> OnRevisit { get; set; }

    private static string CssFor(StageState state)
        => state switch
        {
            StageState.Active => "active",
            StageState.Done => "done",
            StageState.Failed => "failed",
            _ => "pending",
        };

    /// <summary>
    /// Whether this node can be clicked. Done and Failed can — a failed node is exactly the one
    /// a user needs to get back into. Pending cannot: it has no answer to change, which is why
    /// <see cref="BuildSession.GoTo"/> refuses one.
    /// </summary>
    private static bool IsRevisitable(StageState state)
        => state is StageState.Done or StageState.Failed;
}
```

- [ ] **Step 5: Replace the page stub**

`CodeGenerator/Web/Components/BuildWorkflow.razor`:

```razor
@page "/build"
@rendermode InteractiveServer
@using CodeGenerator.Web.Workflow
@inject BuildSession Session

<main class="workflow">
    <h1>Build an application</h1>

    <StageDiagram Session="Session" OnRevisit="Revisit" />

    @if (Session.FailureMessage is not null)
    {
        <p class="failure" role="alert">@Session.FailureMessage</p>
    }

    <section class="panel">
        @* Stage panels are added by the tasks that build them. Until then the panel names the
           stage, so the diagram and the shell can be worked on independently. *@
        <h2>@StageGraph.Nodes.First(n => n.Stage == Session.Current).Title</h2>
    </section>
</main>

@code {
    private void Revisit(BuildStage stage)
    {
        Session.GoTo(stage);
        StateHasChanged();
    }
}
```

- [ ] **Step 6: Write the stylesheet**

`CodeGenerator/Web/wwwroot/workflow.css`:

```css
/* Self-contained: no CDN, no web font. The tool must work with no network. */
:root {
    --ink: #1c1c1c;
    --paper: #fbfbfa;
    --line: #d3d3cf;
    --pending: #8a8a85;
    --active: #1f6feb;
    --done: #1a7f37;
    --failed: #b3261e;
}

body {
    margin: 0;
    padding: 2rem;
    background: var(--paper);
    color: var(--ink);
    font: 15px/1.5 "Segoe UI", system-ui, sans-serif;
}

.workflow { max-width: 1100px; margin: 0 auto; }

/* A grid, so the seven nodes wrap onto a second row on a narrow window without the
   connectors needing to be re-measured. */
.stage-diagram {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
    gap: 0.75rem 1.75rem;
    margin: 0 0 2rem;
    padding: 0;
    list-style: none;
}

.stage-node {
    position: relative;
    padding: 0.7rem 0.9rem;
    border: 2px solid var(--line);
    border-radius: 6px;
    background: #fff;
}

/* The connector. On the node rather than between nodes, so it cannot get out of step with the
   grid's own wrapping; hidden on the last of a row by the grid overflowing invisibly. */
.stage-node + .stage-node::before {
    content: "";
    position: absolute;
    left: -1.4rem;
    top: 50%;
    width: 1rem;
    border-top: 2px solid var(--line);
}

.stage-node button {
    all: unset;
    cursor: pointer;
    display: block;
    width: 100%;
}

.stage-node.pending { color: var(--pending); }
.stage-node.active  { border-color: var(--active); box-shadow: 0 0 0 3px rgb(31 111 235 / 15%); }
.stage-node.done    { border-color: var(--done); }
.stage-node.failed  { border-color: var(--failed); }

.stage-title { display: block; font-weight: 600; }
.stage-blurb { display: block; font-size: 0.82em; color: var(--pending); }

.panel {
    padding: 1.25rem 1.5rem;
    border: 1px solid var(--line);
    border-radius: 6px;
    background: #fff;
}

.failure {
    padding: 0.6rem 0.9rem;
    border-left: 4px solid var(--failed);
    background: #fdf0ef;
}
```

- [ ] **Step 7: Run the tests to verify they pass**

```powershell
dotnet test CodeGenerator.Tests -c Release --filter StageDiagramTests
```

Expected: PASS, 6 tests.

- [ ] **Step 8: Commit**

```bash
git add CodeGenerator/Web CodeGenerator.Tests
git commit -m "feat(web): draw the build workflow as a revisitable flow diagram"
```

---

### Task 6: Nodes 1–4 — the four setup panels

**Files:**
- Create: `CodeGenerator/Web/Components/Stages/DescribeStage.razor`
- Create: `CodeGenerator/Web/Components/Stages/AppTypeStage.razor`
- Create: `CodeGenerator/Web/Components/Stages/DataStage.razor`
- Create: `CodeGenerator/Web/Components/Stages/LookStage.razor`
- Modify: `CodeGenerator/Web/Components/BuildWorkflow.razor` (dispatch on `Session.Current`)
- Test: `CodeGenerator.Tests/Web/SetupStageTests.cs`

**Interfaces:**
- Consumes: `BuildSession` (Task 3), `ApplicationTypeMenu`, `DataSourceReaders`, `AppCustomization`, `AppLayouts`, `AppLayoutCatalog`, `ThemePalette`, `NamespacePrefix`, `CustomizationDefaults`.
- Produces: four components, each taking `Session` and an `EventCallback OnAdvance`.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/Web/SetupStageTests.cs`:

```csharp
using AppScaffolding;
using Bunit;
using CodeGenerator.Web.Components.Stages;
using CodeGenerator.Web.Workflow;
using DataIngestion.Schema.Readers;

namespace CodeGenerator.Tests.Web;

/// <summary>The four setup panels. Bindings and blocked-Next only — the rules are the session's
/// and the validators'.</summary>
[TestClass]
public class SetupStageTests
{
    private static BuildSession New(string description = "")
        => new(description, Path.GetTempPath(), AppCustomization.Default);

    [TestMethod]
    public void The_describe_panel_opens_with_the_inline_description_in_it()
    {
        using var context = new BunitContext();
        var session = New("a library system");

        var rendered = context.Render<DescribeStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.OnAdvance, () => { }));

        Assert.AreEqual("a library system", rendered.Find("textarea").GetAttribute("value"));
    }

    // Blank is a real answer: `build app from csv orders.csv` with no description is supported.
    [TestMethod]
    public void The_describe_panel_allows_a_blank_description()
    {
        using var context = new BunitContext();
        var session = New();

        var rendered = context.Render<DescribeStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.OnAdvance, () => { }));

        Assert.IsFalse(rendered.Find("button.next").HasAttribute("disabled"));
    }

    [TestMethod]
    public void The_app_type_panel_offers_every_menu_row()
    {
        using var context = new BunitContext();

        var rendered = context.Render<AppTypeStage>(p => p
            .Add(c => c.Session, New("a shop"))
            .Add(c => c.OnAdvance, () => { }));

        Assert.HasCount(
            CodeGenerator.App.ApplicationTypeMenu.Labels.Count,
            rendered.FindAll("input[type=radio]"));
    }

    // A format with no reader in this build is refused before it is chosen, not after — the same
    // courtesy DataSourceInterview.FormatLabel pays.
    [TestMethod]
    public void The_data_panel_disables_a_format_this_build_cannot_read()
    {
        using var context = new BunitContext();

        var rendered = context.Render<DataStage>(p => p
            .Add(c => c.Session, New("a shop"))
            .Add(c => c.OnAdvance, () => { }));

        var unreadable = DataSourceReaders.Keys
            .Where(k => DataSourceReaders.TryParseKind(k, out var kind)
                        && !DataSourceReaders.TryCreate(kind, out _))
            .ToList();

        foreach (var key in unreadable)
            Assert.IsTrue(
                rendered.Find($"option[value={key}]").HasAttribute("disabled"),
                $"{key} has no reader and should not be selectable");
    }

    [TestMethod]
    public void The_data_panel_refuses_a_path_that_is_not_there()
    {
        using var context = new BunitContext();
        var session = New("a shop");

        var rendered = context.Render<DataStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.OnAdvance, () => { }));

        rendered.Find("input[type=radio][value=file]").Change(true);
        rendered.Find("input.path").Change(@"C:\nope\missing.csv");
        rendered.Find("button.next").Click();

        Assert.IsNull(session.Source);
        StringAssert.Contains(rendered.Markup, "missing.csv");
    }

    // The console wizard silently keeps the old value on a bad accent, because trapping someone
    // in a terminal loop over a colour is worse. A form has no such excuse.
    [TestMethod]
    public void The_look_panel_blocks_next_on_an_unusable_accent()
    {
        using var context = new BunitContext();

        var rendered = context.Render<LookStage>(p => p
            .Add(c => c.Session, New("a shop"))
            .Add(c => c.OnAdvance, () => { }));

        rendered.Find("input.customize").Change(true);
        rendered.Find("input.accent").Change("not-a-colour");

        Assert.IsTrue(rendered.Find("button.next").HasAttribute("disabled"));
    }

    [TestMethod]
    public void The_look_panel_blocks_next_on_an_unusable_namespace_prefix()
    {
        using var context = new BunitContext();

        var rendered = context.Render<LookStage>(p => p
            .Add(c => c.Session, New("a shop"))
            .Add(c => c.OnAdvance, () => { }));

        rendered.Find("input.customize").Change(true);
        rendered.Find("input.prefix").Change("9nope-");

        Assert.IsTrue(rendered.Find("button.next").HasAttribute("disabled"));
    }

    // Asking a console app for a window shape is nonsense, and the console wizard already skips
    // the question for such a target.
    [TestMethod]
    public void The_look_panel_hides_the_layout_field_for_a_target_with_no_window()
    {
        using var context = new BunitContext();
        var session = New("a shop");
        session.SetTarget(new AppScaffolding.Targets.ConsoleTarget());

        var rendered = context.Render<LookStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.OnAdvance, () => { }));

        rendered.Find("input.customize").Change(true);

        Assert.IsEmpty(rendered.FindAll("select.layout"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
dotnet test CodeGenerator.Tests -c Release --filter SetupStageTests
```

Expected: FAIL to compile — `DescribeStage` does not exist.

- [ ] **Step 3: Write `DescribeStage`**

`CodeGenerator/Web/Components/Stages/DescribeStage.razor`:

```razor
@using CodeGenerator.Web.Workflow

<p>Describe the application you want to build.</p>
<p class="hint">
    Leave it blank if you will point at a data file instead — that is a supported flow.
</p>

<textarea rows="4" value="@_text" @onchange="e => _text = e.Value?.ToString() ?? string.Empty">
</textarea>

<div class="actions">
    <button type="button" class="next" @onclick="Next">Next</button>
    <button type="button" class="cancel" @onclick="() => Session.Cancel()">Cancel</button>
</div>

@code {
    [Parameter, EditorRequired] public required BuildSession Session { get; set; }
    [Parameter] public EventCallback OnAdvance { get; set; }

    private string _text = string.Empty;

    protected override void OnInitialized() => _text = Session.Description;

    private async Task Next()
    {
        // Blank is allowed here and refused at node 3, where "no description AND no data" is
        // the combination that has genuinely nothing to build from.
        Session.SetDescription(_text);
        Session.Advance();

        await OnAdvance.InvokeAsync();
    }
}
```

- [ ] **Step 4: Write `AppTypeStage`**

`CodeGenerator/Web/Components/Stages/AppTypeStage.razor`:

```razor
@using CodeGenerator.App
@using CodeGenerator.Web.Workflow

@* Rows, order and labels all come from ApplicationTypeMenu, which is already the single
   display-only source. A target added there appears here without this file changing. *@
<p>Which kind of application?</p>

<div class="choices">
    @for (var i = 0; i < ApplicationTypeMenu.Labels.Count; i++)
    {
        var index = i;

        <label class="choice">
            <input type="radio" name="apptype" checked="@(_index == index)"
                   @onchange="() => _index = index" />
            @ApplicationTypeMenu.Labels[index]
        </label>
    }
</div>

<div class="actions">
    <button type="button" class="next" @onclick="Next">Next</button>
    <button type="button" class="cancel" @onclick="() => Session.Cancel()">Cancel</button>
</div>

@code {
    [Parameter, EditorRequired] public required BuildSession Session { get; set; }
    [Parameter] public EventCallback OnAdvance { get; set; }

    private int _index;

    protected override void OnInitialized()
        => _index = Session.Target is null ? 0 : ApplicationTypeMenu.IndexOf(Session.Target);

    private async Task Next()
    {
        Session.SetTarget(ApplicationTypeMenu.Resolve(_index));
        Session.Advance();

        await OnAdvance.InvokeAsync();
    }
}
```

- [ ] **Step 5: Write `DataStage`**

`CodeGenerator/Web/Components/Stages/DataStage.razor`:

```razor
@using CodeGenerator.Web.Workflow
@using DataIngestion
@using DataIngestion.Schema.Readers
@using Microsoft.AspNetCore.Components.Forms

<p>Is there data to read, or should the domain be discovered from the web?</p>

<label class="choice">
    <input type="radio" name="data" value="none" checked="@(!_hasFile)"
           @onchange="() => _hasFile = false" />
    No data — discover the domain from the web
</label>

<label class="choice">
    <input type="radio" name="data" value="file" checked="@_hasFile"
           @onchange="() => _hasFile = true" />
    I have a file
</label>

@if (_hasFile)
{
    <div class="file">
        <label>
            Format
            <select value="@_format" @onchange="e => _format = e.Value?.ToString() ?? _format">
                @foreach (var key in DataSourceReaders.Keys)
                {
                    <option value="@key" disabled="@(!Readable(key))">@Label(key)</option>
                }
            </select>
        </label>

        @* A path box rather than only a file picker: the server IS this machine, and a path is
           what DataSourceSpec.Location holds. The drop zone below is the convenience. *@
        <label>
            Path
            <input class="path" value="@_path"
                   @onchange="e => _path = e.Value?.ToString() ?? string.Empty" />
        </label>

        <InputFile OnChange="Upload" class="drop" />
        <span class="hint">…or drop a file here to copy it into the workspace cache.</span>
    </div>
}

@if (_error is not null)
{
    <p class="failure" role="alert">@_error</p>
}

<div class="actions">
    <button type="button" class="back" @onclick="Back">Back</button>
    <button type="button" class="next" @onclick="Next">Next</button>
    <button type="button" class="cancel" @onclick="() => Session.Cancel()">Cancel</button>
</div>

@code {
    /// <summary>Ten megabytes. The 512 KB default rejects an ordinary spreadsheet.</summary>
    private const long MaxUpload = 10 * 1024 * 1024;

    [Parameter, EditorRequired] public required BuildSession Session { get; set; }
    [Parameter] public EventCallback OnAdvance { get; set; }

    private bool _hasFile;
    private string _format = "csv";
    private string _path = string.Empty;
    private string? _error;

    protected override void OnInitialized()
    {
        if (Session.Source is null)
            return;

        _hasFile = true;
        _path = Session.Source.Location;
        _format = Session.Source.Kind.ToString().ToLowerInvariant();
    }

    private static bool Readable(string key)
        => DataSourceReaders.TryParseKind(key, out var kind)
           && DataSourceReaders.TryCreate(kind, out _);

    private static string Label(string key)
        => Readable(key) ? key : $"{key} — not in this build yet";

    private async Task Upload(InputFileChangeEventArgs args)
    {
        var folder = Path.Combine(Session.WorkspaceRoot, ".dd-cache", "uploads");
        Directory.CreateDirectory(folder);

        var destination = Path.Combine(folder, args.File.Name);

        await using var source = args.File.OpenReadStream(MaxUpload);
        await using var target = File.Create(destination);
        await source.CopyToAsync(target);

        _path = destination;
        _error = null;
    }

    private async Task Next()
    {
        _error = null;

        if (!_hasFile)
        {
            // The one combination with nothing to build from, refused here rather than after a
            // discovery run that would have nothing to discover.
            if (Session.Description.Length == 0)
            {
                _error = "With no description and no data there is nothing to build from — "
                         + "go back and describe the application, or point at a file.";
                return;
            }

            Session.SetNoData();
            Session.Advance();
            await OnAdvance.InvokeAsync();
            return;
        }

        // A path pasted from Explorer arrives quoted, exactly as it does in the console.
        var path = _path.Trim();
        if (path.Length >= 2 && path[0] == '"' && path[^1] == '"')
            path = path[1..^1];

        if (!File.Exists(path))
        {
            _error = $"No file at {path}.";
            return;
        }

        if (!DataSourceReaders.TryParseKind(_format, out var kind))
        {
            _error = $"Unknown format '{_format}'.";
            return;
        }

        if (!DataSourceReaders.TryCreate(kind, out _))
        {
            _error = DataSourceReaders.NotSupportedMessage(kind);
            return;
        }

        Session.SetSource(new DataSourceSpec(kind, path));
        Session.Advance();

        await OnAdvance.InvokeAsync();
    }

    private async Task Back()
    {
        Session.GoTo(BuildStage.AppType);
        await OnAdvance.InvokeAsync();
    }
}
```

- [ ] **Step 6: Write `LookStage`**

`CodeGenerator/Web/Components/Stages/LookStage.razor`:

```razor
@using AppScaffolding
@using AppScaffolding.Layouts
@using CodeGenerator.App
@using CodeGenerator.Web.Workflow

@* Collapsed behind a gate, matching CustomizationWizard.GateTitle: this step is decoration, and
   a user who wants the app they came for should skip it in one click. *@
<label class="choice">
    <input type="checkbox" class="customize" checked="@_customize"
           @onchange="e => _customize = (bool?)e.Value ?? false" />
    Customize appearance and features
</label>

@if (_customize)
{
    <div class="fields">
        @if (AsksLayout)
        {
            <label>
                Layout
                <select class="layout" value="@_layout"
                        @onchange="e => _layout = e.Value?.ToString() ?? _layout">
                    @foreach (var key in AppLayoutCatalog.Keys)
                    {
                        <option value="@key">@key</option>
                    }
                </select>
            </label>
        }

        <label>Company
            <input class="company" value="@_company"
                   @onchange="e => _company = e.Value?.ToString() ?? string.Empty" /></label>

        <label>Theme
            <select class="theme" value="@_theme"
                    @onchange="e => _theme = e.Value?.ToString() ?? _theme">
                <option value="default">none</option>
                <option value="light">light</option>
                <option value="dark">dark</option>
            </select></label>

        <label>Accent colour
            <input class="accent" value="@_accent" placeholder="#rrggbb"
                   @onchange="e => _accent = e.Value?.ToString() ?? string.Empty" />
            @if (AccentError is not null)
            {
                <span class="field-error">@AccentError</span>
            }
        </label>

        <label>Icon path
            <input class="icon" value="@_icon"
                   @onchange="e => _icon = e.Value?.ToString() ?? string.Empty" /></label>

        <label>Window title
            <input class="title" value="@_title"
                   @onchange="e => _title = e.Value?.ToString() ?? string.Empty" /></label>

        <label>Namespace prefix
            <input class="prefix" value="@_prefix"
                   @onchange="e => _prefix = e.Value?.ToString() ?? string.Empty" />
            @if (PrefixError is not null)
            {
                <span class="field-error">@PrefixError</span>
            }
        </label>

        <label class="choice"><input type="checkbox" class="validation" checked="@_validation"
            @onchange="e => _validation = (bool?)e.Value ?? false" /> Validation layer</label>

        <label class="choice"><input type="checkbox" class="logging" checked="@_logging"
            @onchange="e => _logging = (bool?)e.Value ?? false" /> Logging</label>

        <label class="choice"><input type="checkbox" class="seed" checked="@_seed"
            @onchange="e => _seed = (bool?)e.Value ?? false" /> Sample seed data</label>
    </div>
}

<div class="actions">
    <button type="button" class="back" @onclick="Back">Back</button>
    <button type="button" class="next" disabled="@(!Valid)" @onclick="Next">Next</button>
    <button type="button" class="cancel" @onclick="() => Session.Cancel()">Cancel</button>
</div>

@code {
    [Parameter, EditorRequired] public required BuildSession Session { get; set; }
    [Parameter] public EventCallback OnAdvance { get; set; }

    private bool _customize;
    private string _layout = "tabs";
    private string _company = string.Empty;
    private string _theme = "default";
    private string _accent = string.Empty;
    private string _icon = string.Empty;
    private string _title = AppCustomization.AppPlaceholder;
    private string _prefix = string.Empty;
    private bool _validation = true;
    private bool _logging;
    private bool _seed = true;

    /// <summary>Only a target with a window has a layout. The console wizard skips the question
    /// for the same reason.</summary>
    private bool AsksLayout
        => Session.Target is not null && AppLayouts.Supports(Session.Target.Key);

    private string? AccentError
        => _accent.Length == 0 || ThemePalette.IsValidHexColour(_accent)
            ? null
            : "A colour looks like #1f6feb.";

    private string? PrefixError
        => _prefix.Length == 0 || NamespacePrefix.IsValid(_prefix)
            ? null
            : "A prefix must be a valid namespace, like Acme or Acme.Internal.";

    private bool Valid => AccentError is null && PrefixError is null;

    protected override void OnInitialized()
    {
        var start = Session.Customization;

        _layout = AppLayoutCatalog.KeyFor(start.EffectiveLayout);
        _company = start.Company;
        _theme = start.Theme == AppTheme.Default ? "default" : start.Theme.ToString().ToLowerInvariant();
        _accent = start.Accent ?? string.Empty;
        _icon = start.IconPath ?? string.Empty;
        _title = start.TitlePattern;
        _prefix = start.NamespacePrefix;
        _validation = start.Validation;
        _logging = start.Logging;
        _seed = start.SampleSeedData;
    }

    private async Task Next()
    {
        if (_customize)
            Session.SetCustomization(Compose());

        // Saved even when the gate was left off: what the user settled on this time is what the
        // next run should offer, and declining settled it too.
        CustomizationDefaults.Save(Session.WorkspaceRoot, Session.Customization);

        Session.Advance();

        await OnAdvance.InvokeAsync();
    }

    private AppCustomization Compose()
        => Session.Customization with
        {
            Layout = AsksLayout && AppLayoutCatalog.TryResolve(_layout, out var layout)
                ? layout
                : Session.Customization.Layout,
            Company = _company.Trim(),
            Theme = Enum.TryParse<AppTheme>(_theme, ignoreCase: true, out var theme)
                ? theme
                : AppTheme.Default,
            Accent = _accent.Length == 0 ? null : _accent,
            IconPath = _icon.Length == 0 ? null : _icon,
            TitlePattern = _title.Length == 0 ? AppCustomization.AppPlaceholder : _title,
            NamespacePrefix = _prefix.Trim(),
            Validation = _validation,
            Logging = _logging,
            SampleSeedData = _seed,
        };

    private async Task Back()
    {
        Session.GoTo(BuildStage.Data);
        await OnAdvance.InvokeAsync();
    }
}
```

- [ ] **Step 7: Check `AppLayoutCatalog.Keys` exists**

`LookStage` reads `AppLayoutCatalog.Keys`. Confirm it:

```powershell
Select-String -Path AppScaffolding\Layouts\AppLayoutCatalog.cs -Pattern "public static"
```

If there is no `Keys`, add one beside the existing `PromptOptions` (which is already a joined list of the same values) rather than hardcoding layout names in the component.

- [ ] **Step 8: Dispatch the panels from the page**

In `CodeGenerator/Web/Components/BuildWorkflow.razor`, replace the `<section class="panel">` body:

```razor
    <section class="panel">
        <h2>@StageGraph.Nodes.First(n => n.Stage == Session.Current).Title</h2>

        @switch (Session.Current)
        {
            case BuildStage.Describe:
                <DescribeStage Session="Session" OnAdvance="Refresh" />
                break;
            case BuildStage.AppType:
                <AppTypeStage Session="Session" OnAdvance="Refresh" />
                break;
            case BuildStage.Data:
                <DataStage Session="Session" OnAdvance="Refresh" />
                break;
            case BuildStage.Look:
                <LookStage Session="Session" OnAdvance="Refresh" />
                break;
        }
    </section>
```

and add to `@code`, plus `@using CodeGenerator.Web.Components.Stages` at the top:

```csharp
    private void Refresh() => StateHasChanged();
```

- [ ] **Step 9: Run the tests to verify they pass**

```powershell
dotnet test CodeGenerator.Tests -c Release --filter SetupStageTests
```

Expected: PASS, 8 tests.

- [ ] **Step 10: Commit**

```bash
git add CodeGenerator/Web CodeGenerator.Tests/Web/SetupStageTests.cs
git commit -m "feat(web): the four build-app setup panels"
```

---

### Task 7: Node 5 — discovery and normalization

**Files:**
- Create: `CodeGenerator/Web/Workflow/DiscoveryStep.cs`
- Create: `CodeGenerator/Web/Components/Stages/DiscoverStage.razor`
- Modify: `CodeGenerator/Web/Components/BuildWorkflow.razor` (one more case)
- Test: `CodeGenerator.Tests/Web/DiscoveryStepTests.cs`

**Interfaces:**
- Consumes: `BuildSession` (Task 3), `DomainDiscoveryPipeline`, `DataSourceReaders`, `BuildAppCommand.CombineReports`, `NormalizationPlan`, `DataModelBuilder`, `TabularImport`.
- Produces:
  - `record ReadResult(IReadOnlyList<TabularTable> Tables, NormalizationReport Report, string FileLabel, IReadOnlyList<string> Lines)`
  - `DiscoveryStep.Read(DataSourceSpec)` → `ReadResult` (throws nothing; see `TryRead`)
  - `DiscoveryStep.TryRead(DataSourceSpec, out ReadResult, out string error)` → `bool`
  - `DiscoveryStep.BuildFromData(ReadResult, IReadOnlyList<NormalizationFinding> chosen)` → `(CandidateDomainModel Model, ImportedData Imported)`
  - `DiscoveryStep.Discover(string description, string workspaceRoot)` → `(CandidateDomainModel Model, bool TopicRecognized)`

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/Web/DiscoveryStepTests.cs`:

```csharp
using CodeGenerator.Web.Workflow;
using DataIngestion;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// Node 5's work, without a component. The data path only: the discovery path fetches from
/// Wikipedia and belongs in the existing discovery suites, not here.
/// </summary>
[TestClass]
public class DiscoveryStepTests
{
    private string _csv = string.Empty;

    [TestInitialize]
    public void WriteACsv()
    {
        _csv = Path.Combine(Path.GetTempPath(), $"orders-{Guid.NewGuid():N}.csv");

        File.WriteAllText(_csv, "OrderId,Customer,Total\n1,Ada,10.50\n2,Grace,22.00\n");
    }

    [TestCleanup]
    public void DeleteTheCsv()
    {
        if (File.Exists(_csv))
            File.Delete(_csv);
    }

    [TestMethod]
    public void Reads_a_csv_and_names_the_file_it_came_from()
    {
        Assert.IsTrue(
            DiscoveryStep.TryRead(new DataSourceSpec(DataSourceKind.Csv, _csv),
                out var result, out var error),
            error);

        Assert.HasCount(1, result.Tables);
        Assert.AreEqual(Path.GetFileName(_csv), result.FileLabel);
    }

    // The reader's own message already says what was wrong in the user's terms, so the step
    // passes it through rather than inventing one.
    [TestMethod]
    public void Reports_a_missing_file_rather_than_throwing()
    {
        var missing = Path.Combine(Path.GetTempPath(), "definitely-not-here.csv");

        Assert.IsFalse(
            DiscoveryStep.TryRead(new DataSourceSpec(DataSourceKind.Csv, missing),
                out _, out var error));

        Assert.IsFalse(string.IsNullOrWhiteSpace(error));
    }

    [TestMethod]
    public void Builds_a_model_and_imported_rows_from_the_tables()
    {
        DiscoveryStep.TryRead(new DataSourceSpec(DataSourceKind.Csv, _csv), out var read, out _);

        var (model, imported) = DiscoveryStep.BuildFromData(read, []);

        Assert.IsNotEmpty(model.Entities);
        Assert.IsNotNull(imported);
    }

    // The second argument to DataModelBuilder.Build is what lets `why <class>` answer for a class
    // the user never named, so a chosen finding has to reach it.
    [TestMethod]
    public void An_applied_finding_leaves_a_decision_trail()
    {
        DiscoveryStep.TryRead(new DataSourceSpec(DataSourceKind.Csv, _csv), out var read, out _);

        var (model, _) = DiscoveryStep.BuildFromData(read, read.Report.Findings);

        if (read.Report.Findings.Count > 0)
            Assert.IsNotEmpty(model.Decisions);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
dotnet test CodeGenerator.Tests -c Release --filter DiscoveryStepTests
```

Expected: FAIL to compile — `DiscoveryStep` does not exist.

- [ ] **Step 3: Write the step**

`CodeGenerator/Web/Workflow/DiscoveryStep.cs`:

```csharp
using CodeGenerator.App;
using DataIngestion;
using DataIngestion.Normalization;
using DataIngestion.Schema;
using DataIngestion.Schema.Readers;
using DomainDiscovery;
using DomainDiscovery.Model;

namespace CodeGenerator.Web.Workflow;

/// <summary>What reading the user's file produced, before any normalization is applied.</summary>
internal sealed record ReadResult(
    IReadOnlyList<TabularTable> Tables,
    NormalizationReport Report,
    string FileLabel,
    IReadOnlyList<string> Lines);

/// <summary>
/// Node 5's work: discover a domain from a description, or read a file and analyse it.
/// </summary>
/// <remarks>
/// Split out of the component for the reason <c>ReviewActions</c> is split out of the review loop:
/// everything decidable is lifted out of the thing that cannot be driven from a test. The
/// component owns only the progress panel and the checkboxes.
/// <para>
/// Both paths call exactly the services the console path calls. There is no second pipeline here.
/// </para>
/// </remarks>
internal static class DiscoveryStep
{
    /// <summary>
    /// Reads the source. False with a message rather than an exception: a mistyped path is the
    /// likeliest thing to go wrong at this node, and the panel offers a way back to node 3.
    /// </summary>
    public static bool TryRead(DataSourceSpec source, out ReadResult result, out string error)
    {
        result = null!;
        error = string.Empty;

        if (!DataSourceReaders.TryCreate(source.Kind, out var reader))
        {
            error = DataSourceReaders.NotSupportedMessage(source.Kind);
            return false;
        }

        IReadOnlyList<TabularTable> tables;

        try
        {
            tables = reader.Read(source);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            // The reader's message already says what was wrong in the user's own terms.
            error = ex.Message;
            return false;
        }

        // The file, not tables[0].Label: a workbook labels each table with its own sheet, and a
        // check covering every sheet must not claim to be about the first one.
        var fileLabel = Path.GetFileName(source.Location);

        var report = BuildAppCommand.CombineReports(tables);

        var lines = new List<string>
        {
            $"Read {tables.Count} table(s) from {fileLabel}: "
            + string.Join(", ", tables.Select(t => $"{t.Name} ({t.Rows.Count} rows)")),
        };

        lines.AddRange(
            NormalizationPrompt.Lines(report, fileLabel, tables.Sum(t => t.Rows.Count)));

        result = new ReadResult(tables, report, fileLabel, lines);

        return true;
    }

    /// <summary>
    /// Applies the findings the user ticked and builds the model.
    /// </summary>
    /// <remarks>
    /// <paramref name="chosen"/> goes to <see cref="DataModelBuilder.Build"/> as well as to
    /// <see cref="NormalizationPlan.Apply"/>, and that is not redundant: every applied split
    /// gets a <c>DecisionRecord</c>, which is what lets `why OrderItem` answer for a class the
    /// user never named.
    /// </remarks>
    public static (CandidateDomainModel Model, ImportedData Imported) BuildFromData(
        ReadResult read, IReadOnlyList<NormalizationFinding> chosen)
    {
        var tables = chosen.Count > 0
            ? NormalizationPlan.Apply(read.Tables, chosen)
            : read.Tables;

        return (DataModelBuilder.Build(tables, chosen), TabularImport.ToImportedData(tables));
    }

    /// <summary>
    /// The description-driven path: NLE seeds, Wikipedia topic, enrichment. Slow on a first fetch
    /// for an unseen topic, which is why the component runs it on a background task.
    /// </summary>
    public static (CandidateDomainModel Model, bool TopicRecognized) Discover(
        string description, string workspaceRoot)
    {
        var cacheDir = Path.Combine(workspaceRoot, ".dd-cache");

        var result = DomainDiscoveryPipeline.CreateDefault(cacheDir).Discover(description);

        return (result.Model, result.Topic.IsRecognized);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```powershell
dotnet test CodeGenerator.Tests -c Release --filter DiscoveryStepTests
```

Expected: PASS, 4 tests.

- [ ] **Step 5: Write the panel**

`CodeGenerator/Web/Components/Stages/DiscoverStage.razor`:

```razor
@using CodeGenerator.Web.Workflow
@using DataIngestion.Normalization

@if (_busy)
{
    @* The longest silence in the tool. A first fetch for an unseen topic runs many seconds, and
       an unmoving screen reads as hung — this is the spinner's replacement. *@
    <p class="working" aria-live="polite">@_label</p>
}
else if (_read is not null)
{
    @foreach (var line in _read.Lines)
    {
        <p class="read-line">@line</p>
    }

    @if (_read.Report.Findings.Count > 0)
    {
        <p>These changes would put the data into a better shape. Tick the ones to apply.</p>

        <ul class="findings">
            @foreach (var finding in _read.Report.Findings)
            {
                <li>
                    <label class="choice">
                        <input type="checkbox" checked="@_chosen.Contains(finding)"
                               @onchange="e => Toggle(finding, (bool?)e.Value ?? false)" />
                        <strong>@finding.Headline</strong> — @finding.Proposal
                    </label>

                    @* Evidence is not decoration: a finding the user cannot inspect is a finding
                       they cannot trust. *@
                    <details>
                        <summary>Why</summary>
                        @foreach (var line in finding.Evidence)
                        {
                            <p class="evidence">@line</p>
                        }
                    </details>
                </li>
            }
        </ul>
    }

    <div class="actions">
        <button type="button" class="back" @onclick="Back">Back</button>
        <button type="button" class="next" @onclick="Continue">Continue</button>
    </div>
}
else
{
    <div class="actions">
        <button type="button" class="back" @onclick="Back">Back</button>
        <button type="button" class="next" @onclick="Start">Start</button>
    </div>
}

@code {
    [Parameter, EditorRequired] public required BuildSession Session { get; set; }
    [Parameter] public EventCallback OnAdvance { get; set; }

    private bool _busy;
    private string _label = string.Empty;
    private ReadResult? _read;
    private readonly List<NormalizationFinding> _chosen = [];

    protected override async Task OnInitializedAsync() => await Start();

    private async Task Start()
    {
        if (Session.HasData)
        {
            ReadTheFile();
            return;
        }

        _busy = true;
        _label = "Discovering domain (this may fetch from Wikipedia the first time)…";

        // Off the circuit's thread, so the label above is actually painted before the wait.
        var discovered = await Task.Run(
            () => DiscoveryStep.Discover(Session.Description, Session.WorkspaceRoot));

        _busy = false;

        if (!discovered.TopicRecognized)
        {
            _label = "No topic recognized online — showing seed classes only. "
                     + "You can add classes by hand in the review.";
        }

        Session.SetModel(discovered.Model);
        Session.Advance();

        await OnAdvance.InvokeAsync();
    }

    private void ReadTheFile()
    {
        if (!DiscoveryStep.TryRead(Session.Source!, out var read, out var error))
        {
            Session.Fail(error);
            return;
        }

        _read = read;
    }

    private void Toggle(NormalizationFinding finding, bool ticked)
    {
        if (ticked)
            _chosen.Add(finding);
        else
            _chosen.Remove(finding);
    }

    private async Task Continue()
    {
        var (model, imported) = DiscoveryStep.BuildFromData(_read!, _chosen);

        Session.SetModel(model);
        Session.SetImportedData(imported);
        Session.Advance();

        await OnAdvance.InvokeAsync();
    }

    private async Task Back()
    {
        Session.GoTo(BuildStage.Data);
        await OnAdvance.InvokeAsync();
    }
}
```

- [ ] **Step 6: Add `ImportedData` to the session**

The panel calls `Session.SetImportedData`. Add to `CodeGenerator/Web/Workflow/BuildSession.cs`, beside `Model`:

```csharp
    /// <summary>The rows read from the user's file, or <see cref="ImportedData.None"/> on the
    /// discovery path. Read by the generate step, and its notes — the row caps — are said out
    /// loud rather than swallowed.</summary>
    public ImportedData ImportedData { get; private set; } = ImportedData.None;

    public void SetImportedData(ImportedData imported) => ImportedData = imported;
```

and reset it in `Change` where the model is nulled:

```csharp
            if (invalidated == BuildStage.Discover)
            {
                Model = null;
                ImportedData = ImportedData.None;
            }
```

- [ ] **Step 7: Add the case to the page**

In `BuildWorkflow.razor`'s switch:

```razor
            case BuildStage.Discover:
                <DiscoverStage Session="Session" OnAdvance="Refresh" />
                break;
```

- [ ] **Step 8: Verify and commit**

```powershell
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror --no-incremental
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release
```

```bash
git add CodeGenerator/Web CodeGenerator.Tests/Web/DiscoveryStepTests.cs
git commit -m "feat(web): the discovery and normalization panel"
```

---

### Task 8: `ProcessRunner.RunStreaming`

Node 7 wants the build log line by line. `Run` reads both streams to the end, so it cannot report progress. Add an overload; do not touch `Run`.

**Files:**
- Modify: `CodeGenerationEngine/Helper/ProcessRunner.cs`
- Test: `CodeGenerator.Tests/Helper/ProcessRunnerStreamingTests.cs`

**Interfaces:**
- Consumes: `ProcessResult` (existing).
- Produces: `ProcessRunner.RunStreaming(string fileName, string arguments, Action<string> onLine, string? workingDirectory = null, TimeSpan? timeout = null)` → `ProcessResult`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/Helper/ProcessRunnerStreamingTests.cs`:

```csharp
using CodeGenerationEngine.Helper;

namespace CodeGenerator.Tests.Helper;

/// <summary>
/// The streaming overload. It exists so a three-minute `dotnet build` can report progress
/// instead of appearing at the end all at once; <c>Run</c> reads to the end and cannot.
/// </summary>
[TestClass]
public class ProcessRunnerStreamingTests
{
    [TestMethod]
    public void Hands_each_line_over_as_it_arrives()
    {
        var lines = new List<string>();

        var result = ProcessRunner.RunStreaming(
            "cmd.exe", "/c echo one& echo two& echo three", lines.Add);

        Assert.AreEqual(0, result.ExitCode);
        Assert.HasCount(3, lines);
        Assert.AreEqual("one", lines[0].Trim());
        Assert.AreEqual("three", lines[2].Trim());
    }

    // Callers that already have the whole output must keep working, so the streamed lines are
    // also accumulated into the result exactly as Run would have.
    [TestMethod]
    public void Still_returns_the_whole_output_in_the_result()
    {
        var result = ProcessRunner.RunStreaming("cmd.exe", "/c echo hello", _ => { });

        StringAssert.Contains(result.StandardOutput, "hello");
    }

    [TestMethod]
    public void Reports_a_non_zero_exit_code()
    {
        var result = ProcessRunner.RunStreaming("cmd.exe", "/c exit 3", _ => { });

        Assert.AreEqual(3, result.ExitCode);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```powershell
dotnet test CodeGenerator.Tests -c Release --filter ProcessRunnerStreamingTests
```

Expected: FAIL to compile — `'ProcessRunner' does not contain a definition for 'RunStreaming'`.

- [ ] **Step 3: Write the implementation**

Add to `CodeGenerationEngine/Helper/ProcessRunner.cs`, inside the class and after `Run`:

```csharp
    /// <summary>
    /// Same as <see cref="Run"/>, but hands each output line over as it arrives.
    /// </summary>
    /// <remarks>
    /// <see cref="Run"/> reads both streams with <c>ReadToEndAsync</c>, so nothing it runs can
    /// report progress — a three-minute build appears all at once at the end. This overload
    /// exists for the callers that want to show it happening, and accumulates the same complete
    /// output into its result so it is a drop-in replacement.
    /// <para>
    /// <see cref="Run"/> is deliberately untouched: its existing callers and their tests depend
    /// on it exactly as it is.
    /// </para>
    /// </remarks>
    /// <param name="onLine">
    /// Called on a background thread, once per line, for both stdout and stderr. A caller
    /// updating a UI must marshal to its own thread.
    /// </param>
    /// <exception cref="Win32Exception">The executable could not be started.</exception>
    public static ProcessResult RunStreaming(
        string fileName,
        string arguments,
        Action<string> onLine,
        string? workingDirectory = null,
        TimeSpan? timeout = null)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? string.Empty,
            },
        };

        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();

        // A null Data is the stream closing, not an empty line — forwarding it would hand the
        // caller a blank line for every process that ever exits.
        process.OutputDataReceived += (_, e) => Collect(e.Data, stdout, onLine);
        process.ErrorDataReceived += (_, e) => Collect(e.Data, stderr, onLine);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var limit = timeout ?? DefaultTimeout;

        if (process.WaitForExit((int)limit.TotalMilliseconds))
        {
            // The parameterless overload after a timed one is what flushes the async readers;
            // without it the last lines can be missing from the result.
            process.WaitForExit();

            return new ProcessResult(
                process.ExitCode, stdout.ToString(), stderr.ToString(), TimedOut: false);
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            // Already gone. Nothing to kill.
        }

        return new ProcessResult(-1, stdout.ToString(), stderr.ToString(), TimedOut: true);
    }

    /// <summary>Accumulates a line and forwards it. Null means the stream closed.</summary>
    private static void Collect(
        string? line, System.Text.StringBuilder sink, Action<string> onLine)
    {
        if (line is null)
            return;

        sink.AppendLine(line);
        onLine(line);
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

```powershell
dotnet test CodeGenerator.Tests -c Release --filter ProcessRunnerStreamingTests
```

Expected: PASS, 3 tests. Also run the existing suite for the file you touched:

```powershell
dotnet test CodeGenerator.Tests -c Release --filter ProcessRunnerTests
```

Expected: PASS, unchanged. If it is red, re-read the Global Constraints note about CPU starvation before assuming this change caused it.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Helper/ProcessRunner.cs CodeGenerator.Tests/Helper/ProcessRunnerStreamingTests.cs
git commit -m "feat(engine): stream a child process's output line by line"
```

---

### Task 9: Node 6 — the review screen

The stage that gains the most: in the console the model is currently not printed at all.

**Files:**
- Create: `CodeGenerator/Web/Components/Stages/ReviewStage.razor`
- Modify: `CodeGenerator/Web/Components/BuildWorkflow.razor` (one more case)
- Modify: `CodeGenerator/Web/wwwroot/workflow.css` (entity cards)
- Test: `CodeGenerator.Tests/Web/ReviewStageTests.cs`

**Interfaces:**
- Consumes: `BuildSession` (Task 3), `ModelEditor`, `DecisionReport`, `CustomizationSummary`, `ApplicationTypeMenu`.
- Produces: `ReviewStage` taking `Session` and `OnAdvance`.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/Web/ReviewStageTests.cs`:

```csharp
using AppScaffolding;
using AppScaffolding.Targets;
using Bunit;
using CodeGenerator.Web.Components.Stages;
using CodeGenerator.Web.Workflow;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// The review screen. The rules belong to ModelEditor and are tested there; what is asserted
/// here is that the model is actually on screen — which in the console it is not — and that each
/// control reaches the right editor method and reports its answer.
/// </summary>
[TestClass]
public class ReviewStageTests
{
    [TestMethod]
    public void Shows_every_class_and_its_fields()
    {
        using var context = new BunitContext();

        var markup = Render(context, out _).Markup;

        StringAssert.Contains(markup, "Book");
        StringAssert.Contains(markup, "Title");
    }

    [TestMethod]
    public void Shows_a_suggestion_as_promotable()
    {
        using var context = new BunitContext();

        var rendered = Render(context, out _);

        StringAssert.Contains(rendered.Markup, "Shelf");
        Assert.IsNotEmpty(rendered.FindAll("button.promote"));
    }

    [TestMethod]
    public void Removing_a_class_takes_it_off_the_screen()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out var session);

        rendered.Find("[data-entity=Book] button.remove").Click();

        Assert.IsEmpty(session.Model!.Entities.Where(e => e.Name == "Book"));
    }

    // ModelEditor refuses this (CS0542 — a property may not share its class's name) and returns
    // false. The panel must say so rather than silently doing nothing.
    [TestMethod]
    public void Reports_a_refused_field_rather_than_failing_silently()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out _);

        rendered.Find("[data-entity=Book] input.field-name").Change("Book");
        rendered.Find("[data-entity=Book] button.add-field").Click();

        StringAssert.Contains(rendered.Markup, "Could not add");
    }

    [TestMethod]
    public void Offers_the_five_supported_field_types_and_no_others()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out _);

        var options = rendered.FindAll("[data-entity=Book] select.field-type option");

        Assert.HasCount(5, options);
    }

    [TestMethod]
    public void Shows_the_target_and_the_customization_that_generate_will_use()
    {
        using var context = new BunitContext();

        var markup = Render(context, out _).Markup;

        StringAssert.Contains(markup, CodeGenerator.App.ApplicationTypeMenu.LabelFor(new WpfTarget()));
    }

    [TestMethod]
    public void Generate_advances_to_the_last_node()
    {
        using var context = new BunitContext();
        var rendered = Render(context, out var session);

        rendered.Find("button.generate").Click();

        Assert.AreEqual(BuildStage.Generate, session.Current);
    }

    [TestMethod]
    public void Generate_is_refused_on_an_empty_model()
    {
        using var context = new BunitContext();
        var session = SessionWith(new CandidateDomainModel());

        var rendered = context.Render<ReviewStage>(p => p
            .Add(c => c.Session, session)
            .Add(c => c.OnAdvance, () => { }));

        Assert.IsTrue(rendered.Find("button.generate").HasAttribute("disabled"));
    }

    private static IRenderedComponent<ReviewStage> Render(
        BunitContext context, out BuildSession session)
    {
        session = SessionWith(AModel());

        var captured = session;

        return context.Render<ReviewStage>(p => p
            .Add(c => c.Session, captured)
            .Add(c => c.OnAdvance, () => { }));
    }

    private static BuildSession SessionWith(CandidateDomainModel model)
    {
        var session = new BuildSession("a library", Path.GetTempPath(), AppCustomization.Default);

        session.SetTarget(new WpfTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();
        session.SetModel(model);
        session.Advance();

        return session;
    }

    private static CandidateDomainModel AModel()
    {
        var model = new CandidateDomainModel();

        var book = new CandidateEntity { Name = "Book", Provenance = Provenance.Wiki };
        book.Fields.Add(new CandidateField
        {
            Name = "Title", Type = "string", Provenance = Provenance.Wiki
        });

        model.Entities.Add(book);
        model.Suggestions.Add(new CandidateEntity { Name = "Shelf", Provenance = Provenance.Wiki });

        return model;
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
dotnet test CodeGenerator.Tests -c Release --filter ReviewStageTests
```

Expected: FAIL to compile — `ReviewStage` does not exist.

- [ ] **Step 3: Write the panel**

`CodeGenerator/Web/Components/Stages/ReviewStage.razor`:

```razor
@using CodeGenerator.App
@using CodeGenerator.Web.Workflow
@using DomainDiscovery.Model
@using DomainDiscovery.Review

@* The model, on screen. In the console PrintModel is almost entirely commented out because a
   thirty-class domain reprinted after every edit was unusable — so the review loop currently
   edits a model the user cannot see. This is the fix. *@

<div class="review">
    <div class="entities">
        @foreach (var entity in Model.Entities.ToList())
        {
            <article class="entity" data-entity="@entity.Name">
                <header>
                    <input class="entity-name" value="@entity.Name"
                           @onchange="e => Rename(entity, e.Value?.ToString())" />
                    <span class="provenance">@entity.Provenance</span>
                    <button type="button" class="why" @onclick="() => Why(entity.Name)">why</button>
                    <button type="button" class="remove"
                            @onclick="() => Report(ModelEditor.RemoveEntity(Model, entity.Name), $"Removed {entity.Name}.", "Could not remove.")">
                        remove
                    </button>
                </header>

                <ul class="fields">
                    @foreach (var field in entity.Fields.ToList())
                    {
                        <li>
                            <span class="field-name">@field.Name</span>
                            <span class="field-type">@field.Type</span>
                            <button type="button" class="unfield"
                                    @onclick="() => Report(ModelEditor.RemoveField(Model, entity.Name, field.Name), $"Removed {field.Name}.", "Could not remove that field — Name and a class's last field both stay.")">
                                ×
                            </button>
                        </li>
                    }
                </ul>

                <div class="add-field-row">
                    <input class="field-name" placeholder="new field"
                           @onchange="e => _newField = e.Value?.ToString() ?? string.Empty" />

                    @* The five ModelEditor accepts. A sixth here would compile and then fail. *@
                    <select class="field-type"
                            @onchange="e => _newType = e.Value?.ToString() ?? "string"">
                        <option value="string">string</option>
                        <option value="int">int</option>
                        <option value="decimal">decimal</option>
                        <option value="bool">bool</option>
                        <option value="DateTime">DateTime</option>
                    </select>

                    <button type="button" class="add-field" @onclick="() => AddField(entity.Name)">
                        add field
                    </button>
                </div>
            </article>
        }
    </div>

    @if (Model.Suggestions.Count > 0)
    {
        @* Offered rather than deleted: not knowing an entity's fields is not the same as knowing
           it is not an entity. Invisible in the console, which is the single largest thing the
           terminal hides. *@
        <aside class="suggestions">
            <h3>Also found</h3>
            @foreach (var suggestion in Model.Suggestions.ToList())
            {
                <button type="button" class="promote" @onclick="() => Promote(suggestion.Name)">
                    + @suggestion.Name
                </button>
            }
        </aside>
    }

    <aside class="settled">
        <h3>Will generate</h3>
        <p>@(Session.Target is null ? "not chosen" : ApplicationTypeMenu.LabelFor(Session.Target))</p>
        @foreach (var line in CustomizationSummary.Lines(Session.Customization))
        {
            <p>@line</p>
        }
    </aside>
</div>

<div class="add-class-row">
    <input class="new-class" placeholder="new class"
           @onchange="e => _newClass = e.Value?.ToString() ?? string.Empty" />
    <button type="button" class="add-class" @onclick="AddClass">add class</button>
</div>

@if (_message is not null)
{
    <p class="edit-report" aria-live="polite">@_message</p>
}

@if (_why.Count > 0)
{
    <section class="why-panel">
        <h3>Why this model</h3>
        @foreach (var line in _why)
        {
            <p>@line</p>
        }
        <button type="button" @onclick="() => _why.Clear()">close</button>
    </section>
}

<div class="actions">
    <button type="button" class="generate" disabled="@(Model.Entities.Count == 0)"
            @onclick="Generate">
        Generate
    </button>
    <button type="button" class="cancel" @onclick="() => Session.Cancel()">Cancel</button>
</div>

@code {
    [Parameter, EditorRequired] public required BuildSession Session { get; set; }
    [Parameter] public EventCallback OnAdvance { get; set; }

    private CandidateDomainModel Model => Session.Model!;

    private string _newClass = string.Empty;
    private string _newField = string.Empty;
    private string _newType = "string";
    private string? _message;
    private List<string> _why = [];

    /// <summary>
    /// Reports what <see cref="ModelEditor"/> answered.
    /// </summary>
    /// <remarks>
    /// The rules are not restated here — no control is pre-disabled by second-guessing
    /// <c>RemoveField</c> or <c>AddField</c>. A duplicated rule is a rule that will drift; asking
    /// and reporting keeps one copy.
    /// </remarks>
    private void Report(bool changed, string ok, string fail) => _message = changed ? ok : fail;

    private void Rename(CandidateEntity entity, string? typed)
    {
        var name = (typed ?? string.Empty).Trim();

        if (name.Length == 0 || name == entity.Name)
            return;

        Report(
            ModelEditor.RenameEntity(Model, entity.Name, name),
            $"Renamed to {name}.",
            "Could not rename — the name may already be in use.");
    }

    private void AddClass()
    {
        // BuildAppCommand.AddLine reports the name the model actually received, which is not
        // always the name typed: promoting a suggestion keeps the suggestion's own spelling.
        _message = BuildAppCommand.AddLine(Model, ["add", _newClass]);
        _newClass = string.Empty;
    }

    private void Promote(string name) => _message = BuildAppCommand.AddLine(Model, ["add", name]);

    private void AddField(string entityName)
    {
        // The type is explicit, so TypeInference is deliberately bypassed: the drop-down is the
        // user overriding a guess before it is made.
        Report(
            ModelEditor.AddField(Model, entityName, _newField, _newType),
            $"Added {_newField} to {entityName}.",
            "Could not add that field. The name must be new, a valid identifier, and neither "
            + "'Id' (generated) nor the class's own name.");

        _newField = string.Empty;
    }

    private void Why(string name) => _why = [.. DecisionReport.ForName(Model, name)];

    private async Task Generate()
    {
        Session.Advance();

        await OnAdvance.InvokeAsync();
    }
}
```

- [ ] **Step 4: Add the entity-card styles**

Append to `CodeGenerator/Web/wwwroot/workflow.css`:

```css
.review { display: grid; grid-template-columns: 1fr 220px; gap: 1.5rem; }
.entities { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 1rem; }

.entity { border: 1px solid var(--line); border-radius: 6px; padding: 0.75rem; }
.entity header { display: flex; align-items: center; gap: 0.4rem; flex-wrap: wrap; }
.entity-name { font-weight: 600; border: 1px solid transparent; background: transparent; }
.entity-name:focus { border-color: var(--active); background: #fff; }

.provenance { font-size: 0.72em; text-transform: uppercase; color: var(--pending); }
.fields { list-style: none; padding: 0; margin: 0.5rem 0; }
.fields li { display: flex; gap: 0.5rem; align-items: baseline; }
.field-type { font-size: 0.8em; color: var(--pending); }

.suggestions button.promote { margin: 0 0.3rem 0.3rem 0; }
.settled { grid-column: 2; font-size: 0.88em; }
.edit-report { margin-top: 1rem; font-style: italic; }
.evidence { font-family: ui-monospace, Consolas, monospace; font-size: 0.82em; }
```

- [ ] **Step 5: Add the case to the page**

In `BuildWorkflow.razor`'s switch:

```razor
            case BuildStage.Review:
                <ReviewStage Session="Session" OnAdvance="Refresh" />
                break;
```

- [ ] **Step 6: Run the tests to verify they pass**

```powershell
dotnet test CodeGenerator.Tests -c Release --filter ReviewStageTests
```

Expected: PASS, 8 tests.

- [ ] **Step 7: Commit**

```bash
git add CodeGenerator/Web CodeGenerator.Tests/Web/ReviewStageTests.cs
git commit -m "feat(web): a review screen that shows the model it is editing"
```

---

### Task 10: Node 7 — generate, build, run

**Files:**
- Create: `CodeGenerator/Web/Workflow/GenerateStep.cs`
- Create: `CodeGenerator/Web/Components/Stages/GenerateStage.razor`
- Modify: `CodeGenerator/Web/Components/BuildWorkflow.razor` (last case)
- Test: `CodeGenerator.Tests/Web/GenerateStepTests.cs`

**Interfaces:**
- Consumes: `BuildSession` (Tasks 3, 7), `AppScaffolder`, `ScaffoldRequest`, `GeneratedProjectPath`, `BuildReport`, `NextSteps`, `AppLauncher`, `LaunchSettingsReader`, `WebAppStart`, `ProcessRunner.RunStreaming` (Task 8), `BuildAppCommand.AppNameFor`, `.IsPersistenceAllowed`, `.LayoutNotice`.
- Produces:
  - `GenerateStep.NameFor(BuildSession)` → `string`
  - `GenerateStep.Collides(BuildSession, string name, ITargetScaffolder)` → `bool`
  - `GenerateStep.Scaffold(BuildSession, string name, ITargetScaffolder, PersistenceKind)` → `ScaffoldResult`
  - `GenerateStep.Build(string projectPath, Action<string> onLine)` → `(ProcessResult Result, TimeSpan Elapsed)`
  - `GenerateStep.OutcomeFor(ScaffoldResult, ITargetScaffolder)` → `BuildOutcome`

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/Web/GenerateStepTests.cs`:

```csharp
using AppScaffolding;
using AppScaffolding.Targets;
using CodeGenerator.Web.Workflow;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// Node 7's decisions. The scaffold itself is covered by the existing AppScaffolding suites;
/// what is asserted here is the naming, the collision check and the lines the console prints.
/// </summary>
[TestClass]
public class GenerateStepTests
{
    // One naming rule in the product: the description names the app, through the same
    // DomainAppName.FromDescription the console path uses.
    [TestMethod]
    public void Names_the_project_from_the_description()
        => Assert.AreEqual("LibrarySystem", GenerateStep.NameFor(SessionFor("a library system")));

    [TestMethod]
    public void Falls_back_to_the_source_file_name_when_there_is_no_description()
    {
        var session = new BuildSession("", Path.GetTempPath(), AppCustomization.Default,
            new DataIngestion.DataSourceSpec(DataIngestion.DataSourceKind.Csv, @"C:\data\orders.csv"));

        Assert.AreEqual("Orders", GenerateStep.NameFor(session));
    }

    [TestMethod]
    public void Reports_no_collision_for_a_name_that_is_not_on_disk()
    {
        var session = SessionFor("a library system");

        Assert.IsFalse(GenerateStep.Collides(session, "DefinitelyNotThere", new WpfTarget()));
    }

    [TestMethod]
    public void The_outcome_names_the_project_that_was_written()
    {
        var scaffold = new ScaffoldResult
        {
            Success = true,
            ProjectPath = @"D:\ws\LibrarySystem",
            Message = string.Empty,
        };

        var outcome = GenerateStep.OutcomeFor(scaffold, new WpfTarget());

        Assert.IsTrue(outcome.Lines.Any(l => l.Contains("LibrarySystem")));
    }

    [TestMethod]
    public void A_failed_scaffold_reports_its_own_reason()
    {
        var scaffold = new ScaffoldResult
        {
            Success = false,
            ProjectPath = string.Empty,
            Message = "dotnet new timed out",
        };

        var outcome = GenerateStep.OutcomeFor(scaffold, new WpfTarget());

        Assert.IsTrue(outcome.Lines.Any(l => l.Contains("timed out")));
    }

    private static BuildSession SessionFor(string description)
    {
        var session = new BuildSession(description, Path.GetTempPath(), AppCustomization.Default);

        session.SetTarget(new WpfTarget());
        session.Advance();
        session.SetNoData();
        session.Advance();
        session.SetCustomization(AppCustomization.Default);
        session.Advance();
        session.SetModel(new CandidateDomainModel());
        session.Advance();

        return session;
    }
}
```

**Note for the implementer:** `ScaffoldResult`'s exact member set and whether it is a record with
`init` accessors is not restated here — open `AppScaffolding/ScaffoldResult.cs` and construct it
the way that file allows. If it has no public constructor usable from a test, assert
`OutcomeFor` through a real `Scaffold` call under a **short** output path
(`C:\t\<guid>`) instead: the scratchpad path blows MAX_PATH, and the tell is MSB3030
"file not found" on a file that exists.

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
dotnet test CodeGenerator.Tests -c Release --filter GenerateStepTests
```

Expected: FAIL to compile — `GenerateStep` does not exist.

- [ ] **Step 3: Write the step**

`CodeGenerator/Web/Workflow/GenerateStep.cs`:

```csharp
using System.Diagnostics;
using AppScaffolding;
using CodeGenerationEngine.Helper;
using CodeGenerationEngine.Models;
using CodeGenerator.App;

namespace CodeGenerator.Web.Workflow;

/// <summary>
/// Node 7's work: name it, write it, compile it, and say what happened.
/// </summary>
/// <remarks>
/// Every rule here is the console path's rule, called rather than copied — the naming through
/// <c>BuildAppCommand.AppNameFor</c>, the collision test through
/// <see cref="GeneratedProjectPath.Exists"/>, the report through <c>BuildReport</c> and
/// <c>NextSteps</c>. Two front ends that disagree about what `approve` produces would be worse
/// than one front end.
/// </remarks>
internal static class GenerateStep
{
    /// <summary>How long to wait for the generated app to compile. Matches the console path's
    /// allowance: a first WPF or EF Core build does real work.</summary>
    public static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(3);

    public static string NameFor(BuildSession session)
        => BuildAppCommand.AppNameFor(session.Description, session.Source);

    /// <summary>
    /// Whether generating under <paramref name="name"/> would overwrite something. The same test
    /// <c>AppScaffolder</c> uses to decide it is reusing a project, so the question is put exactly
    /// when a regeneration would happen and never on a fresh name.
    /// </summary>
    public static bool Collides(BuildSession session, string name, ITargetScaffolder target)
        => GeneratedProjectPath.Exists(session.WorkspaceRoot, name, target);

    public static string FolderFor(BuildSession session, string name, ITargetScaffolder target)
        => GeneratedProjectPath.Folder(session.WorkspaceRoot, name, target);

    public static ScaffoldResult Scaffold(
        BuildSession session, string name, ITargetScaffolder target, PersistenceKind persistence)
        => new AppScaffolder().Scaffold(new ScaffoldRequest(
            session.Model!, name, session.WorkspaceRoot, target, persistence,
            session.ImportedData, session.Customization,

            // Read here rather than inside a template: a template reading the clock would turn
            // every golden-output test red on 1 January.
            DateTime.Now.Year));

    /// <summary>Compiles what was just written, handing each line over as it arrives.</summary>
    public static (ProcessResult Result, TimeSpan Elapsed) Build(
        string projectPath, Action<string> onLine)
    {
        var clock = Stopwatch.StartNew();

        var result = ProcessRunner.RunStreaming(
            "dotnet", "build", onLine, projectPath, BuildTimeout);

        clock.Stop();

        return (result, clock.Elapsed);
    }

    /// <summary>What the console prints once the browser is done.</summary>
    public static BuildOutcome OutcomeFor(ScaffoldResult scaffold, ITargetScaffolder target)
    {
        if (!scaffold.Success)
            return new BuildOutcome(
                SessionPhase.Generated, [$"Generation failed: {scaffold.Message}"]);

        var lines = new List<string> { $"Generated project at {scaffold.ProjectPath}" };

        if (scaffold.TestProjectPath is not null)
            lines.Add($"Generated test project at {scaffold.TestProjectPath}");

        // Printed whether or not the build was green: a user whose app does not compile still
        // needs to know where the seed rows are and how to run it once it does.
        lines.AddRange(NextSteps.Lines(
            target, scaffold.ProjectPath, scaffold.TestProjectPath, scaffold.FilesWritten));

        return new BuildOutcome(SessionPhase.Generated, lines);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```powershell
dotnet test CodeGenerator.Tests -c Release --filter GenerateStepTests
```

Expected: PASS, 5 tests.

- [ ] **Step 5: Write the panel**

`CodeGenerator/Web/Components/Stages/GenerateStage.razor`:

```razor
@using AppScaffolding
@using CodeGenerationEngine.Models
@using CodeGenerator.App
@using CodeGenerator.Web.Workflow

@if (_phase == Phase.Confirm)
{
    <p>Ready to write <strong>@_name</strong>.</p>

    <label>Name
        <input class="app-name" value="@_name"
               @onchange="e => _name = DomainAppName.FromDescription(e.Value?.ToString() ?? _name)" />
    </label>

    @* Hidden rather than offered-and-refused: WPF and WinForms read JsonStore directly off the
       shared store seam, so SQLite is not a choice they have. *@
    @if (BuildAppCommand.IsPersistenceAllowed(Target, PersistenceKind.Sqlite))
    {
        <label>Persistence
            <select class="persistence"
                    @onchange="e => _persistence = e.Value?.ToString() == "sqlite"
                        ? PersistenceKind.Sqlite : PersistenceKind.Json">
                <option value="json">JSON</option>
                <option value="sqlite">SQLite</option>
            </select>
        </label>
    }

    @if (LayoutNotice is not null)
    {
        <p class="notice">@LayoutNotice</p>
    }

    <div class="actions">
        <button type="button" class="back" @onclick="Back">Back</button>
        <button type="button" class="write" @onclick="Write">Write it</button>
        <button type="button" class="cancel" @onclick="() => Session.Cancel()">Cancel</button>
    </div>
}
else if (_phase == Phase.Overwrite)
{
    @* A question about losing work, so it is a decision rather than a default. *@
    <div class="overwrite" role="alertdialog">
        <p>@GenerateStep.FolderFor(Session, _name, Target) already exists.</p>
        <button type="button" class="overwrite-yes" @onclick="Overwrite">Overwrite it</button>
        <button type="button" class="overwrite-rename" @onclick="() => _phase = Phase.Confirm">
            Use a different name
        </button>
        <button type="button" class="cancel" @onclick="() => Session.Cancel()">Cancel</button>
    </div>
}
else
{
    @foreach (var line in _report)
    {
        <p class="report-line">@line</p>
    }

    @if (_log.Count > 0)
    {
        <pre class="build-log">@string.Join(Environment.NewLine, _log)</pre>
    }

    @if (_phase == Phase.Done && _canRun)
    {
        <button type="button" class="run" @onclick="Run">Run it now</button>
    }

    @if (_phase == Phase.Done)
    {
        <button type="button" class="finish" @onclick="Finish">Done</button>
    }
}

@code {
    private enum Phase { Confirm, Overwrite, Working, Done }

    [Parameter, EditorRequired] public required BuildSession Session { get; set; }
    [Parameter] public EventCallback OnAdvance { get; set; }

    private Phase _phase = Phase.Confirm;
    private string _name = string.Empty;
    private PersistenceKind _persistence = PersistenceKind.Json;
    private readonly List<string> _report = [];
    private readonly List<string> _log = [];
    private ScaffoldResult? _scaffold;
    private bool _canRun;

    private ITargetScaffolder Target => Session.Target ?? TargetScaffolders.Default;

    /// <summary>Re-checked here rather than only at node 4, because the target can still be
    /// changed after the layout was named.</summary>
    private string? LayoutNotice => BuildAppCommand.LayoutNotice(Target, Session.Customization);

    protected override void OnInitialized() => _name = GenerateStep.NameFor(Session);

    private void Write()
    {
        // A rename can collide too, so the question is asked about whatever name is current.
        if (GenerateStep.Collides(Session, _name, Target))
        {
            _phase = Phase.Overwrite;
            return;
        }

        _ = Go();
    }

    private void Overwrite() => _ = Go();

    private async Task Go()
    {
        _phase = Phase.Working;
        _report.Clear();
        _log.Clear();

        var scaffold = await Task.Run(
            () => GenerateStep.Scaffold(Session, _name, Target, _persistence));

        _scaffold = scaffold;

        // Said out loud: a row cap the user is not told about reads as "all your data is in there".
        foreach (var note in Session.ImportedData.Notes)
            _report.Add(note);

        if (!scaffold.Success)
        {
            _report.Add($"Generation failed: {scaffold.Message}");
            _phase = Phase.Done;
            return;
        }

        _report.Add($"Generated project at {scaffold.ProjectPath}");

        if (scaffold.TestProjectPath is not null)
            _report.Add($"Generated test project at {scaffold.TestProjectPath}");

        StateHasChanged();

        var built = await Task.Run(() => GenerateStep.Build(
            scaffold.ProjectPath,
            line => InvokeAsync(() => { _log.Add(line); StateHasChanged(); })));

        _report.AddRange(BuildReport.Lines(built.Result, built.Elapsed, scaffold.ProjectPath));

        // Nothing to offer for a class library, and nothing worth starting from a red build.
        _canRun = BuildReport.Succeeded(built.Result)
                  && Target.ProjectType != DotNetProjectType.ClassLibrary;

        _phase = Phase.Done;
    }

    private void Run()
    {
        var isWebApp = Target.ProjectType == DotNetProjectType.BlazorServer;

        var launched = AppLauncher.Launch(
            _scaffold!.ProjectPath, string.Join(Environment.NewLine, _log),
            fromProjectFolder: isWebApp);

        _report.Add(launched is null
            ? "Could not start the app — use the command below."
            : $"Started {launched}");

        // A web app shows nothing of its own and waits to be visited, so opening it once
        // something answers is what makes the offer honest.
        if (launched is not null && isWebApp)
        {
            var url = LaunchSettingsReader.HttpUrl(_scaffold.ProjectPath);

            if (url is not null && WebAppStart.Wait(url))
                WebAppStart.OpenBrowser(url);
        }
    }

    private async Task Finish()
    {
        Session.Complete(GenerateStep.OutcomeFor(_scaffold!, Target));

        await OnAdvance.InvokeAsync();
    }

    private async Task Back()
    {
        Session.GoTo(BuildStage.Review);
        await OnAdvance.InvokeAsync();
    }
}
```

- [ ] **Step 6: Add the case to the page**

In `BuildWorkflow.razor`'s switch:

```razor
            case BuildStage.Generate:
                <GenerateStage Session="Session" OnAdvance="Refresh" />
                break;
```

- [ ] **Step 7: Verify and commit**

```powershell
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror --no-incremental
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release
```

```bash
git add CodeGenerator/Web CodeGenerator.Tests/Web/GenerateStepTests.cs
git commit -m "feat(web): generate, build and run from the workflow page"
```

---

### Task 11: Wire `build app` to the browser

**Files:**
- Modify: `CodeGenerator/App/BuildAppCommand.cs:67-128`
- Test: `CodeGenerator.Tests/App/BuildAppWebRoutingTests.cs`

**Interfaces:**
- Consumes: `BuildAppWebHost.Run`, `BuildSession`, `BuildOutcome` (Tasks 3–4).
- Produces: `BuildAppCommand.UseWebWorkflow` → `bool` (the routing rule, internal and pure so it can be asserted).

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/BuildAppWebRoutingTests.cs`:

```csharp
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Which front end an interactive `build app` gets. The scope boundary of the whole feature:
/// redirected stdin must never reach the browser, because that is what every piped script and
/// E2E test drives.
/// </summary>
[TestClass]
public class BuildAppWebRoutingTests
{
    [TestMethod]
    public void Redirected_input_never_opens_a_browser()
        => Assert.IsFalse(BuildAppCommand.UseWebWorkflow(inputRedirected: true, noWeb: false));

    [TestMethod]
    public void An_interactive_run_opens_the_browser()
        => Assert.IsTrue(BuildAppCommand.UseWebWorkflow(inputRedirected: false, noWeb: false));

    // The escape hatch for a headless or SSH session, where there is no browser to open.
    [TestMethod]
    public void CG_NO_WEB_keeps_the_console_wizard()
        => Assert.IsFalse(BuildAppCommand.UseWebWorkflow(inputRedirected: false, noWeb: true));
}
```

- [ ] **Step 2: Run the test to verify it fails**

```powershell
dotnet test CodeGenerator.Tests -c Release --filter BuildAppWebRoutingTests
```

Expected: FAIL to compile — `'BuildAppCommand' does not contain a definition for 'UseWebWorkflow'`.

- [ ] **Step 3: Add the routing rule**

In `CodeGenerator/App/BuildAppCommand.cs`, add beside `UseTypedInput`:

```csharp
    /// <summary>The environment variable that keeps the console wizard.</summary>
    /// <remarks>
    /// For a headless or SSH session, where there is no browser to open and no display to open
    /// it on. Not a second product path — a way out of one.
    /// </remarks>
    internal const string NoWebVariable = "CG_NO_WEB";

    /// <summary>
    /// Whether this run should open the workflow page.
    /// </summary>
    /// <remarks>
    /// Pure, and taking its two inputs as arguments, for the reason
    /// <see cref="ResolveTarget"/> does: the branch it governs reads the console and cannot be
    /// driven from a test, so the decision is lifted out of it.
    /// <para>
    /// Redirected stdin is the hard boundary. A piped run keeps <see cref="InlineDataSource"/>
    /// parsing and the typed review loop, byte for byte — that is what every scripted E2E test
    /// drives, and none of them should have to change.
    /// </para>
    /// </remarks>
    internal static bool UseWebWorkflow(bool inputRedirected, bool noWeb)
        => !inputRedirected && !noWeb;
```

- [ ] **Step 4: Route the interactive branch**

In `Handle`, replace the `if (!Console.IsInputRedirected)` block (lines 93-118) with:

```csharp
        if (UseWebWorkflow(
                Console.IsInputRedirected,
                Environment.GetEnvironmentVariable(NoWebVariable) == "1"))
        {
            var start = Merge(CustomizationDefaults.Load(workspaceRoot), inline);

            var session = new Web.Workflow.BuildSession(description, workspaceRoot, start, source);

            if (Web.BuildAppWebHost.TryStart(session, out var host))
            {
                using (host)
                {
                    Console.WriteLine($"  Opening the build workflow at {host.Url}");

                    if (!WebAppStart.OpenBrowser(host.Url))
                        Console.WriteLine("  No default browser — open that address yourself.");

                    Console.WriteLine("  Ctrl-C here cancels.");
                    Console.WriteLine("");

                    var outcome = Web.BuildAppWebHost.Run(session, Interrupt.Token);

                    foreach (var line in outcome.Lines)
                        Console.WriteLine(line);

                    Console.WriteLine("");
                }

                // The workflow generated, cancelled or was abandoned in the browser. Either way
                // this command is over — nothing below runs.
                return;
            }

            // Could not bind loopback. Say so and fall through to the console wizard rather
            // than leaving the user with no way to run the command at all.
            Output.Warning(
                "Could not start the build workflow page — using the console wizard instead.");
        }

        if (!Console.IsInputRedirected)
        {
            // The remembered profile is read only here: a redirected-stdin run must not have its
            // output changed by a file it never mentioned.
            var start = Merge(CustomizationDefaults.Load(workspaceRoot), inline);

            var outcome = BuildAppWizard.Run(
                request, Console.In, Console.Out, ApplicationTypeMenu.Choose, start, Menu.Choose);

            if (outcome.Decision == WizardDecision.Cancelled)
            {
                Console.WriteLine("Cancelled. Nothing was generated.");
                return;
            }

            description = outcome.Description;
            target = outcome.Target;
            source = outcome.Source;
            normalize = outcome.Normalize;
            customization = outcome.Customization;

            CustomizationDefaults.Save(workspaceRoot, customization);
        }
```

- [ ] **Step 5: Run the tests to verify they pass**

```powershell
dotnet test CodeGenerator.Tests -c Release --filter BuildAppWebRoutingTests
```

Expected: PASS, 3 tests.

- [ ] **Step 6: Prove the piped path is untouched**

This is the scope boundary's only real proof:

```powershell
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release
```

Expected: every existing test green, **with no edits to any of them**. If a piped E2E test fails, the routing rule has leaked — do not change the test.

- [ ] **Step 7: Drive it by hand once**

```powershell
$env:CG_NO_WEB=$null
dotnet run --project CodeGenerator -c Release
```

Type `build app`, complete the workflow in the browser, confirm the console prints the generated path and returns to the prompt. Then `Ctrl-C` mid-workflow on a second run and confirm the prompt comes back.

- [ ] **Step 8: Commit**

```bash
git add CodeGenerator/App/BuildAppCommand.cs CodeGenerator.Tests/App/BuildAppWebRoutingTests.cs
git commit -m "feat(cli): build app opens the workflow page"
```

---

### Task 12: Documentation

The user manual currently describes a console interview that is no longer what an interactive user sees. Leaving it is a documentation bug, not a nicety.

**Files:**
- Modify: `CodeGenerationEngine/Core/CommandDictionary.cs` (the `build app` entry)
- Modify: `docs/commands.md`
- Modify: `docs/architecture.md`
- Modify: `CodeGenerator/README.md`
- Modify: `docs/user-manual-source.html`
- Test: `CodeGenerator.Tests/App/HelpCatalogTests.cs` (existing — extend)

- [ ] **Step 1: Find what claims the old behaviour**

```powershell
Select-String -Path docs\commands.md,docs\architecture.md,CodeGenerator\README.md,docs\user-manual-source.html -Pattern "build app" -Context 3
Select-String -Path CodeGenerationEngine\Core\CommandDictionary.cs -Pattern "build app" -Context 6
```

- [ ] **Step 2: Write the failing help test**

Add to the existing `CodeGenerator.Tests/App/HelpCatalogTests.cs` (match its existing class and idiom):

```csharp
    [TestMethod]
    public void The_build_app_entry_mentions_the_workflow_page()
    {
        var entry = CommandDictionary.Entries.First(e => e.Command.StartsWith("build app"));

        StringAssert.Contains(entry.Description.ToLowerInvariant(), "browser");
    }
```

**Note:** `CommandDictionary`'s member names are not restated here — open the file and match what
it actually exposes.

- [ ] **Step 3: Run it to verify it fails**

```powershell
dotnet test CodeGenerator.Tests -c Release --filter HelpCatalogTests
```

Expected: FAIL — the description says nothing about a browser.

- [ ] **Step 4: Update the help entry and the four documents**

Each needs the same three facts and nothing more:

1. Interactive `build app` opens a workflow page in the browser; the console prints the address and waits.
2. The workflow is a diagram of seven stages; a completed stage can be clicked to go back, and changing a description or a data source discards the model discovered from the old one.
3. Piped and redirected runs are unchanged, and `CG_NO_WEB=1` keeps the console wizard.

Do not describe the panels field by field — a screen that changes shape would make that wrong within a release.

- [ ] **Step 5: Run the tests to verify they pass**

```powershell
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release
```

Expected: PASS, everything.

- [ ] **Step 6: Regenerate the .docx manual**

`docs/SmartCodeGenerator-User-Manual.docx` is built from `docs/user-manual-source.html` through
Word COM. Regenerate it, then verify by unzipping the result rather than by trusting the
conversion.

- [ ] **Step 7: Commit**

```bash
git add CodeGenerationEngine/Core/CommandDictionary.cs docs CodeGenerator/README.md CodeGenerator.Tests/App/HelpCatalogTests.cs
git commit -m "docs: build app opens a workflow page in the browser"
```

---

## Plan Self-Review

**Spec coverage.** Every spec section maps to a task: approach and SDK → Task 1; stage graph and invalidation table → Tasks 2–3; host, token, lifetime, error handling → Task 4; the diagram and revisiting → Task 5; stages 1–4 including the data-file question → Task 6; stage 5 with discovery, normalization and evidence → Task 7; the streaming overload → Task 8; stage 6 including suggestions and `why` → Task 9; stage 7 including overwrite, build log and run → Task 10; the console wiring and the `CG_NO_WEB` fallback → Task 11; docs → Task 12.

**Two places the plan defers to the file rather than restating it**, deliberately, because guessing a signature would be worse than reading one: `ScaffoldResult`'s constructor shape (Task 10, Step 1) and `CommandDictionary`'s member names (Task 12, Step 2). Both carry an explicit instruction to open the file. `AppLayoutCatalog.Keys` is checked in Task 6 Step 7 and added if absent.

**Type consistency.** `BuildSession` grows two members after Task 3 — `Outcome` in Task 4 Step 6 and `ImportedData` in Task 7 Step 6 — and both are written out where they are added rather than assumed. `Complete()` becomes `Complete(BuildOutcome)` in Task 4 Step 6, and Task 10 calls the one-argument form. `StageState` and `SessionPhase` are used with the names Task 2 defines throughout.
