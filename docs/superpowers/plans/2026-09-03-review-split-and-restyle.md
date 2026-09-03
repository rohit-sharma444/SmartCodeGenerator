# Review Split and Workflow Restyle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split the Review stage into two stages — *Review classes* (structure) and *Relationships* (links, screens, and what will be generated) — and give every page a deliberate visual identity.

**Architecture:** The split is additive: one new `BuildStage` member, one new node in `StageGraph.Nodes`, one new `.razor` panel, and a move of three markup regions out of `ReviewStage`. Nothing about the walk, the session or the resume path changes shape. The restyle is CSS-led: existing class names are preserved because 27 of them are asserted by bUnit tests, so markup changes are additive (new wrapper elements and modifier classes) and the visual work happens in the stylesheets.

**Tech Stack:** .NET 10, Blazor Server (`InteractiveServer`, prerender off), MSTest + bUnit (`CodeGenerator.Tests`), plain CSS with no build step.

**Spec:** The approved mockup — https://claude.ai/code/artifact/c71749e5-b8a9-4507-8c8a-d77aec37593c — plus this plan's Global Constraints. There is no separate design doc; the mockup is the design, approved 2026-09-03.

## Global Constraints

- **No web fonts, no CDN.** `workflow.css` line 1: *"Self-contained: no CDN, no web font. The tool must work with no network."* Fonts are `"Segoe UI Variable Text", "Segoe UI", system-ui, -apple-system, sans-serif` for prose and `"Cascadia Code", "Cascadia Mono", Consolas, ui-monospace, monospace` for code.
- **Never scope colour tokens to `:root`.** `App.razor` links `workflow.css` AND `chat.css` on every page, so a `:root` block in one restyles the other. Workflow tokens go on `.workflow`; chat tokens go on `.chat`. This has already caused a defect once.
- **Do not rename an existing CSS class.** These are asserted by bUnit tests and renaming one turns a passing test into a false green or a red for the wrong reason: `.stage-continue`, `.panel`, `.panel-broken`, `.edit-report`, `.finished`, `.failure`, `.discard-question`, `.discard-confirm`, `.discard-keep`, `.cancel`, `.cancel-question`, `.cancel-confirm`, `.cancel-keep`, `.step-done`, `.step-current`, `.stage-back`, `.stage-mark`, `.stage-diagram`, `.screen-summary`, `.peek`, `.node-open`, `.entity`, `.rows`, `.progress`, `.drop`, `.command-echo`, `.add-field-row`. Add classes; never rename.
- **Palette (light):** `--paper #eceff4`, `--surface #ffffff`, `--machine #f2f5f9`, `--ink #161d26`, `--muted #5c6875`, `--faint #8a94a1`, `--line #d5dce4`, `--line-soft #e4e9ef`, `--accent #1e5fa8`, `--accent-ink #14446f`, `--accent-wash #e6eef8`, `--ok #1a7f4b`, `--ok-wash #e4f2ea`, `--warn #a8620a`, `--stop #b3261e`.
- **The design idea:** anything the machine owns (class names, field names, C# types, PK/FK markers, the command echo) is set in the mono stack on a `--machine` surface; anything the user wrote stays proportional. Preserve this distinction in every page.
- **The rail must fit 1280×720.** Eight boxes were cut from 814px to 504px to achieve this; a ninth must not push the last box off-screen. Verify by measurement, not by eye.
- Build and test with `dotnet build SmartCodeGenerator.slnx -c Release -warnaserror --no-incremental` and `dotnet test SmartCodeGenerator.slnx -c Release --no-build`, run from `D:\SmartCodeGenerator-Repository\SmartCodeGenerator`. Baseline before starting: **3,891 passing**.

---

## Task 1: Add the Relationships stage to the workflow graph

**Files:**
- Modify: `CodeGenerator/Web/Workflow/BuildStage.cs`
- Modify: `CodeGenerator/Web/Workflow/StageGraph.cs` (`Nodes`, `Invalidates`)
- Modify: `CodeGenerator/Web/Workflow/BuildSession.cs` (`HoldsAnAnswer`, ~line 399)
- Modify: `CodeGenerator/Web/Workflow/StageSummary.cs` (~line 37)
- Test: `CodeGenerator.Tests/Web/StageGraphTests.cs`
- Test: `CodeGenerator.Tests/Web/BuildSessionTests.cs`
- Test: `CodeGenerator.Tests/Web/StageSummaryTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `BuildStage.Relationships`, positioned between `BuildStage.Review` and `BuildStage.Generate`. `StageGraph.Nodes` becomes 9 entries. `StageSummary.For(session, BuildStage.Relationships)` returns a `string?`.

**Why the enum insert is safe:** `BuildSnapshot` persists the stage as `session.Current.ToString()` and `ResumedSession` reads it with `Enum.TryParse<BuildStage>` — by name, never by ordinal. An existing snapshot holding `"Review"` still resolves after the insert. No migration.

- [ ] **Step 1: Write the failing tests**

In `CodeGenerator.Tests/Web/StageGraphTests.cs`:

```csharp
[TestMethod]
public void RelationshipsFollowsReviewAndPrecedesGenerate()
{
    Assert.AreEqual(BuildStage.Relationships, StageGraph.Next(BuildStage.Review));
    Assert.AreEqual(BuildStage.Generate, StageGraph.Next(BuildStage.Relationships));
    Assert.AreEqual(BuildStage.Review, StageGraph.Previous(BuildStage.Relationships));
}

[TestMethod]
public void EveryStageHasANode()
{
    // The walk throws on a stage with no node, so a member added to the enum and forgotten here
    // is a wiring bug that only shows when someone reaches that stage.
    foreach (var stage in Enum.GetValues<BuildStage>())
        Assert.IsTrue(StageGraph.Nodes.Any(n => n.Stage == stage), $"no node for {stage}");
}

[TestMethod]
public void ChangingTheDescriptionInvalidatesRelationshipsToo()
{
    // Relationships is derived from the model, so anything that throws the model away throws
    // this away with it. Missing it would leave a green tick on a page describing a model that
    // no longer exists.
    foreach (var changed in new[] { BuildStage.Describe, BuildStage.Data, BuildStage.Discover })
        CollectionAssert.Contains(
            StageGraph.Invalidates(changed).ToList(),
            BuildStage.Relationships,
            $"{changed} should invalidate Relationships");
}

[TestMethod]
public void AnsweringRelationshipsInvalidatesNothing()
{
    Assert.AreEqual(0, StageGraph.Invalidates(BuildStage.Relationships).Count);
}
```

In `CodeGenerator.Tests/Web/BuildSessionTests.cs`:

```csharp
[TestMethod]
public void RelationshipsHoldsAnAnswerForAsLongAsThereIsAModel()
{
    var session = new BuildSession("a shop", TestRoot(), AppCustomization.Default);

    Assert.IsFalse(session.HoldsAnAnswer(BuildStage.Relationships));

    session.SetModel(OneClassModel());

    Assert.IsTrue(session.HoldsAnAnswer(BuildStage.Relationships));
}
```

In `CodeGenerator.Tests/Web/StageSummaryTests.cs`:

```csharp
[TestMethod]
public void RelationshipsSummaryCountsTheLinks()
{
    var session = new BuildSession("a shop", TestRoot(), AppCustomization.Default);
    Assert.IsNull(StageSummary.For(session, BuildStage.Relationships));

    session.SetModel(ModelWithOneRelationship());

    Assert.AreEqual("1 relationship", StageSummary.For(session, BuildStage.Relationships));
}
```

Reuse whatever `TestRoot()` / model-building helpers already exist in each file rather than adding new ones; if a file has none, build the model inline the way its neighbouring tests do.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~StageGraphTests|FullyQualifiedName~StageSummaryTests"`
Expected: FAIL to compile — `'BuildStage' does not contain a definition for 'Relationships'`.

- [ ] **Step 3: Add the enum member**

In `CodeGenerator/Web/Workflow/BuildStage.cs`, between `Review` and `Generate`:

```csharp
    Review,

    /// <summary>
    /// How the classes connect, which of them get a screen, and what will be written.
    /// </summary>
    /// <remarks>
    /// Split out of <see cref="Review"/>: that one node carried four separate jobs — editing the
    /// classes, reading the relationship diagram, choosing each class's screen, and confirming
    /// what would be generated — on a page the user had to scroll past the whole model to reach
    /// the last of them. Structure is settled here; presentation and output are settled next.
    /// <para>
    /// Ordinal position is not persisted: <c>BuildSnapshot</c> writes the stage by name, so
    /// inserting this member does not invalidate a snapshot written before it existed.
    /// </para>
    /// </remarks>
    Relationships,

    Generate,
```

- [ ] **Step 4: Add the node and the invalidation rules**

In `StageGraph.Nodes`, after the Review entry:

```csharp
        new(BuildStage.Review,        "Review classes", "Change the model before it is written"),
        new(BuildStage.Relationships, "Relationships",  "How they connect, and what gets built"),
```

In `Invalidates`, add `BuildStage.Relationships` to the three lists that already name `BuildStage.Review`, and add its own arm:

```csharp
            BuildStage.Describe =>
                [BuildStage.Discover, BuildStage.Review, BuildStage.Relationships,
                 BuildStage.Generate, BuildStage.Run],
            BuildStage.Data     =>
                [BuildStage.Discover, BuildStage.Review, BuildStage.Relationships,
                 BuildStage.Generate, BuildStage.Run],
            BuildStage.Discover =>
                [BuildStage.Discover, BuildStage.Review, BuildStage.Relationships,
                 BuildStage.Generate, BuildStage.Run],
            BuildStage.AppType       => [],
            BuildStage.Look          => [],
            BuildStage.Review        => [],
            BuildStage.Relationships => [],
            BuildStage.Generate      => [],
            BuildStage.Run           => [],
```

- [ ] **Step 5: Teach the session and the summary about it**

In `BuildSession.HoldsAnAnswer`, beside the Review arm:

```csharp
            BuildStage.Review => Model is not null,

            // Same rule as Review, and for the same reason: this node holds the model it
            // describes, so it stays answered for as long as there is one.
            BuildStage.Relationships => Model is not null,
```

In `StageSummary.For`, beside the Review arm:

```csharp
            BuildStage.Relationships => session.Model is null
                ? null
                : Links(session.Model.Entities.Sum(e => e.Relationships.Count)),
```

and add the helper next to the existing `Classes` helper, matching its style:

```csharp
    private static string Links(int count) => count == 1 ? "1 relationship" : $"{count} relationships";
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~StageGraphTests|FullyQualifiedName~StageSummaryTests|FullyQualifiedName~BuildSessionTests"`
Expected: PASS. If `TabTitle` tests now read "of 9", that is correct — the test uses `StageGraph.Nodes.Count`, so it should not need editing.

- [ ] **Step 7: Run the whole suite**

Run: `dotnet build SmartCodeGenerator.slnx -c Release -warnaserror --no-incremental` then `dotnet test SmartCodeGenerator.slnx -c Release --no-build`
Expected: the only failures are in `WorkflowPageTests` / `ResumedSessionTests`, which Tasks 2 and 3 fix. Record which ones fail — do not fix them here.

- [ ] **Step 8: Commit**

```bash
git add CodeGenerator/Web/Workflow CodeGenerator.Tests/Web
git commit -m "Add a Relationships stage between Review and Generate"
```

---

## Task 2: Move relationships, screens and the output summary onto their own panel

**Files:**
- Create: `CodeGenerator/Web/Components/Stages/RelationshipsStage.razor`
- Modify: `CodeGenerator/Web/Components/Stages/ReviewStage.razor` (remove the moved regions; change the offered step)
- Modify: `CodeGenerator/Web/Components/BuildWorkflow.razor` (~line 133, add a `case`)
- Test: `CodeGenerator.Tests/Web/WorkflowPageTests.cs`
- Test: `CodeGenerator.Tests/Web/RelationshipsStageTests.cs` (create)

**Interfaces:**
- Consumes: `BuildStage.Relationships` from Task 1.
- Produces: `RelationshipsStage` with parameters `Session` (`BuildSession`, required), `Navigation` (`StageNavigation`, required), `OnAdvance` (`EventCallback`) — the same trio every other stage panel takes.

**What moves out of `ReviewStage` (and nothing else):**
1. `<RelationshipDiagram Sources="Sources()" />` and its `Sources()` method.
2. The per-card `<div class="screen">` block and its `plan` variable, `ChooseRole` and `OwnScreen` handlers, becoming one table of every class rather than a control on each card.
3. The `<aside class="settled">` block whole — "Will generate", the customization lines, `.screen-summary`, and the `.command-echo` block.

**What stays in `ReviewStage`:** `.model-source`, the `.entities` cards (name, provenance, why, remove, fields, add-field), `.suggestions`, `.add-class-row`, `.edit-report`, `.why-panel`, `.actions`.

- [ ] **Step 1: Write the failing tests**

Create `CodeGenerator.Tests/Web/RelationshipsStageTests.cs`. Follow the arrangement used by the existing `WorkflowPageTests` for standing a session up and rendering a panel.

```csharp
[TestMethod]
public void ShowsEveryClassScreenChoiceInOneTable()
{
    var page = RenderRelationshipsFor(ModelWithMasterAndTransaction());

    // One row per class that can hold a screen, not a dropdown buried in each card.
    Assert.IsTrue(page.FindAll(".screens tbody tr").Count >= 2);
}

[TestMethod]
public void CarriesTheCommandEchoAndTheScreenSummary()
{
    var page = RenderRelationshipsFor(ModelWithMasterAndTransaction());

    Assert.IsNotNull(page.Find(".command-echo"));
    Assert.IsNotNull(page.Find(".screen-summary"));
}

[TestMethod]
public void OffersGenerateAsTheStepForward()
{
    var page = RenderRelationshipsFor(ModelWithMasterAndTransaction());

    StringAssert.Contains(page.Find(".stage-continue").TextContent, "Generate");
}
```

Then, in `CodeGenerator.Tests/Web/WorkflowPageTests.cs`, add:

```csharp
[TestMethod]
public void ReviewNoLongerCarriesTheOutputSummary()
{
    // The split's whole point: the class editor stops carrying the confirm-and-generate block.
    var page = RenderReviewFor(ModelWithMasterAndTransaction());

    Assert.AreEqual(0, page.FindAll(".command-echo").Count);
    Assert.AreEqual(0, page.FindAll(".screen-summary").Count);
    Assert.IsTrue(page.FindAll(".entity").Count > 0);
}

[TestMethod]
public void ReviewStepsForwardToRelationships()
{
    var page = RenderReviewFor(ModelWithMasterAndTransaction());

    StringAssert.Contains(page.Find(".stage-continue").TextContent, "Relationships");
}
```

If `WorkflowPageTests` has no `RenderReviewFor` helper, extract one from whatever the existing Review-rendering test does and reuse it in both new tests rather than duplicating the setup.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~RelationshipsStageTests|FullyQualifiedName~WorkflowPageTests"`
Expected: FAIL — `RelationshipsStage` does not exist, and Review still renders `.command-echo`.

- [ ] **Step 3: Create the panel**

`CodeGenerator/Web/Components/Stages/RelationshipsStage.razor`. Move the three regions listed above out of `ReviewStage` verbatim where possible — this is a move, not a rewrite; the restyle happens in Task 6. The screen choice becomes one table:

```razor
@using AppScaffolding.Screens
@using CodeGenerator.App
@using CodeGenerator.Web.Workflow
@using DomainDiscovery.Model

@* Node 7: how the classes connect, which get a screen, and what will be written. Split out of
   ReviewStage, which carried this under a full screen of entity cards — a confirmation the user
   had to scroll past the thing being confirmed to reach. *@
@{
    var plan = Session.Target?.HasScreens == true
        ? ScreenPlan.For(Model, Session.Customization, Session.Target)
        : null;
}

<div class="relationships">
    <RelationshipDiagram Sources="Sources()" />

    @if (plan is not null)
    {
        <section class="screens-block">
            <h3>Screens</h3>
            <table class="screens">
                <thead>
                    <tr><th>Class</th><th>Screen</th></tr>
                </thead>
                <tbody>
                    @foreach (var entity in Model.Entities.ToList())
                    {
                        var role = plan.RoleOfEntity(entity.Name);

                        <tr>
                            <td class="screen-class">@entity.Name</td>
                            <td>
                                @* A line is not offered the two-way choice: asking for
                                   "transaction" on one is an answer EntityRoles drops in its
                                   second pass, so the single correction that does work is the
                                   only one offered. *@
                                @if (role == EntityRole.DocumentLine)
                                {
                                    <span class="role-line">lines of @plan.TransactionHolding(entity.Name)</span>
                                    <button type="button" class="own-screen"
                                            @onclick="() => OwnScreen(entity.Name)">own screen</button>
                                }
                                else
                                {
                                    <select class="role" value="@role"
                                            @onchange="e => ChooseRole(entity.Name, e.Value?.ToString())">
                                        <option value="@nameof(EntityRole.Master)"
                                                selected="@(role == EntityRole.Master)">master</option>
                                        <option value="@nameof(EntityRole.Transaction)"
                                                selected="@(role == EntityRole.Transaction)">transaction</option>
                                    </select>
                                }
                            </td>
                        </tr>
                    }
                </tbody>
            </table>

            <p class="screen-summary">@ScreenSummary.Describe(plan.Roles)</p>
        </section>
    }

    <aside class="settled">
        <h3>Will generate</h3>
        <p>@(Session.Target is null ? "not chosen" : ApplicationTypeMenu.LabelFor(Session.Target))</p>
        @foreach (var line in CustomizationSummary.Lines(Session.Customization))
        {
            <p>@line</p>
        }

        <h3>The same run as a command</h3>
        <pre class="command-echo">@string.Join(Environment.NewLine, BuildCommandEcho.For(Session))</pre>
    </aside>
</div>

<div class="actions">
    <button type="button" class="cancel" @onclick="() => Session.RequestCancel()">Cancel</button>
</div>

@code {
    [Parameter, EditorRequired] public required BuildSession Session { get; set; }
    [Parameter, EditorRequired] public required StageNavigation Navigation { get; set; }
    [Parameter] public EventCallback OnAdvance { get; set; }

    private CandidateDomainModel Model => Session.Model!;

    /// <summary>The model, in the shape the diagram takes.</summary>
    /// <remarks>
    /// The same resolution DomainModelMapper applies is left to the layout, so the picture and the
    /// generator cannot disagree about which targets count.
    /// </remarks>
    private List<RelationshipSource> Sources() =>
        [.. Model.Entities.Select(e => new RelationshipSource(
            e.Name,
            [.. e.Relationships.Select(r => r.Target)],
            [.. e.Fields.Select(f => f.Name)]))];

    protected override void OnInitialized()
        => Navigation.Offer(Generate, ready: () => Model.Entities.Count > 0, label: "Generate");
}
```

Copy `ChooseRole`, `OwnScreen` and `Generate` out of `ReviewStage`'s `@code` block unchanged, and delete them from `ReviewStage`.

- [ ] **Step 4: Trim ReviewStage and re-point its step**

Delete from `ReviewStage.razor`: the `<RelationshipDiagram …>` line, the `plan` computation at the top, the whole `<div class="screen">` block inside each card, the `<aside class="settled">` block, and the `Sources()`, `ChooseRole` and `OwnScreen` members. Change the offered step:

```csharp
    /// <summary>
    /// The step forward is now Relationships, not Generate: this node settles what the classes
    /// ARE, and the next one settles how they connect and what gets written.
    /// </summary>
    protected override void OnInitialized()
        => Navigation.Offer(
            () => Task.FromResult(true),
            ready: () => Model.Entities.Count > 0,
            label: "Relationships");
```

- [ ] **Step 5: Render the new stage from the page**

In `BuildWorkflow.razor`, after the `case BuildStage.Review:` arm:

```razor
                case BuildStage.Relationships:
                    <RelationshipsStage @key="BuildStage.Relationships" Session="Session"
                                        Navigation="_navigation" OnAdvance="Refresh" />
                    break;
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~RelationshipsStageTests|FullyQualifiedName~WorkflowPageTests"`
Expected: PASS.

- [ ] **Step 7: Run the whole suite and commit**

Run: `dotnet build SmartCodeGenerator.slnx -c Release -warnaserror --no-incremental` then `dotnet test SmartCodeGenerator.slnx -c Release --no-build`
Expected: 3,891 + the new tests, all green.

```bash
git add CodeGenerator/Web CodeGenerator.Tests/Web
git commit -m "Split Review: classes on one page, relationships and output on the next"
```

---

## Task 3: Keep the resume path landing somewhere that holds an answer

**Files:**
- Modify: `CodeGenerator/App/Resume/ResumedSession.cs:127`
- Test: `CodeGenerator.Tests/App/Resume/ResumedSessionTests.cs`

**Interfaces:**
- Consumes: `BuildStage.Relationships` from Task 1.
- Produces: no new API. The resumable position for a snapshot taken at Generate or Run stays `BuildStage.Review`.

**Decision, stated so the next reader does not have to re-derive it:** a snapshot taken at Generate or Run resumes at `Review`, not `Relationships`, even though both hold an answer. Review is where the edits are, and `ResumedSession.MoveTo` walks forward one node at a time (`GoTo` throws on a Pending stage), so landing earlier costs one extra click and landing later would skip a page the user may want. What must be verified is that walking *through* the new node still works.

- [ ] **Step 1: Write the failing test**

```csharp
[TestMethod]
public void ResumingAtRelationshipsWalksThroughEveryNodeBeforeIt()
{
    // GoTo throws on a Pending stage, so restoring has to Advance() node by node. A node added
    // to the middle of the walk is exactly what breaks that, and it breaks it silently: the
    // guard in MoveTo just stops.
    var snapshot = SnapshotAt(BuildStage.Relationships, withModel: true);

    var session = ResumedSession.From(snapshot, TestRoot());

    Assert.AreEqual(BuildStage.Relationships, session.Current);
    Assert.IsTrue(session.HoldsAnAnswer(BuildStage.Review));
    Assert.IsTrue(session.HoldsAnAnswer(BuildStage.Relationships));
}

[TestMethod]
public void ASnapshotWrittenBeforeRelationshipsExistedStillResumes()
{
    // The stage is persisted by NAME, so an old snapshot holding "Review" must still parse.
    var snapshot = SnapshotAt(BuildStage.Review, withModel: true);
    snapshot.Stage = "Review";

    var session = ResumedSession.From(snapshot, TestRoot());

    Assert.AreEqual(BuildStage.Review, session.Current);
}
```

Match the existing helpers in `ResumedSessionTests` for building a snapshot; if `SnapshotAt` does not exist, extract it from the file's existing arrangement.

- [ ] **Step 2: Run the tests to verify they fail or pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~ResumedSessionTests"`
Expected: these may already PASS, because `MoveTo` walks `StageGraph` generically and its guard is `Nodes.Count + 1`. If they pass, that is the answer — keep the tests as the pin and skip Step 3.

- [ ] **Step 3: Fix only if red**

If `ResumingAtRelationshipsWalksThroughEveryNodeBeforeIt` fails, the cause will be the loop guard or an `Advance()` that stops at a node with no answer. Fix at the cause; do not special-case `Relationships`.

- [ ] **Step 4: Run the whole suite and commit**

```bash
git add CodeGenerator/App/Resume CodeGenerator.Tests/App/Resume
git commit -m "Pin the resume walk across the new Relationships node"
```

---

## Task 4: Lay the token layer into workflow.css

**Files:**
- Modify: `CodeGenerator/wwwroot/workflow.css:1-30` (the `:root` block and `body`)

**Interfaces:**
- Consumes: nothing.
- Produces: the custom properties named in Global Constraints, plus `--mono`, `--sans`, `--radius`, `--shadow-sm`, `--shadow-md`, and a `--space-1` … `--space-6` scale of `4px 8px 12px 16px 24px 32px`. Every later CSS task uses these names and defines no new colour literals.

**Critical:** the current file declares its tokens on `:root`. Move them to `.workflow` (the page's root element, set in `BuildWorkflow.razor`). `App.razor` links this stylesheet on the chat page too, and a `:root` block here restyles that page as a side effect — a defect this codebase has already shipped once.

- [ ] **Step 1: Replace the token block**

```css
/* Self-contained: no CDN, no web font. The tool must work with no network — so the page's
   character comes from colour, spacing and weight rather than from a typeface nobody has.

   Scoped to .workflow, NOT :root. App.razor links this stylesheet on every page including the
   chat page, so a :root block here silently restyles a page this file knows nothing about. */
.workflow {
    --paper:      #eceff4;
    --surface:    #ffffff;
    --machine:    #f2f5f9;
    --ink:        #161d26;
    --muted:      #5c6875;
    --faint:      #8a94a1;
    --line:       #d5dce4;
    --line-soft:  #e4e9ef;
    --accent:     #1e5fa8;
    --accent-ink: #14446f;
    --accent-wash:#e6eef8;
    --ok:         #1a7f4b;
    --ok-wash:    #e4f2ea;
    --warn:       #a8620a;
    --stop:       #b3261e;

    --space-1: 4px;  --space-2: 8px;  --space-3: 12px;
    --space-4: 16px; --space-5: 24px; --space-6: 32px;

    --radius:    10px;
    --radius-sm: 6px;
    --shadow-sm: 0 1px 2px rgba(22, 29, 38, .06), 0 1px 3px rgba(22, 29, 38, .05);
    --shadow-md: 0 2px 4px rgba(22, 29, 38, .05), 0 8px 20px rgba(22, 29, 38, .07);

    /* Prose and code are different materials in this tool, and the page says which is which:
       anything the machine owns — a class name, a field, a C# type, the command echo — is set
       in the mono stack; anything the user wrote stays proportional. */
    --sans: "Segoe UI Variable Text", "Segoe UI", system-ui, -apple-system, sans-serif;
    --mono: "Cascadia Code", "Cascadia Mono", Consolas, ui-monospace, monospace;
}
```

Keep the existing `--pending`, `--active`, `--done`, `--failed` and `--lift-*` names as aliases inside the same block so nothing downstream breaks before Task 5 rewrites it:

```css
.workflow {
    --pending: var(--muted);
    --active:  var(--accent);
    --done:    var(--ok);
    --failed:  var(--stop);
    --lift-flat:   var(--shadow-sm);
    --lift-raised: var(--shadow-md);
    --lift-active: 0 0 0 3px rgb(30 95 168 / 15%), var(--shadow-md);
    --lift-sunk:   inset 0 1px 3px rgba(22, 29, 38, .06);
}
```

Because `body` cannot see `.workflow`'s variables, set the page ground with a literal on `body` and let `.workflow` paint its own:

```css
body {
    margin: 0;
    padding: var(--space-5) var(--space-6);
    background: #eceff4;
    color: #161d26;
    font: 15px/1.55 "Segoe UI Variable Text", "Segoe UI", system-ui, -apple-system, sans-serif;
    -webkit-font-smoothing: antialiased;
}
```

- [ ] **Step 2: Build and run the suite**

Run: `dotnet build SmartCodeGenerator.slnx -c Release -warnaserror --no-incremental` then `dotnet test SmartCodeGenerator.slnx -c Release --no-build`
Expected: 3,891+ green. No test reads CSS, so this proves only that nothing else broke — the visual check happens in Task 9.

- [ ] **Step 3: Commit**

```bash
git add CodeGenerator/wwwroot/workflow.css
git commit -m "Scope the workflow's design tokens to .workflow and give them a real scale"
```

---

## Task 5: Restyle the stage rail for nine boxes

**Files:**
- Modify: `CodeGenerator/wwwroot/workflow.css` (the `.stage-diagram`, `.stage-node`, `.stage-mark`, `.stage-title`, `.node-open` blocks)
- Modify: `CodeGenerator/Web/Components/StageDiagram.razor` (add a `.stage-answer` wrapper only if the answer text is not already in its own element)

**Interfaces:**
- Consumes: tokens from Task 4.
- Produces: no new API. Class names `.stage-diagram`, `.stage-node`, `.stage-mark`, `.stage-title`, `.node-open`, `.stage-continue`, `.stage-back` all survive unchanged.

**The measurement that governs this task:** eight boxes previously needed 814px and were cut to 504px so they fit 595px of room below the heading at 1280×720. Nine boxes at the same 63px per box would be 567px — inside the budget but with almost nothing spare. Target **≤ 52px per box**, giving ~468px for nine, and verify by measuring the rendered rail in Task 9, not by eye.

- [ ] **Step 1: Restyle the rail**

```css
.stage-diagram {
    display: grid;
    grid-template-columns: minmax(0, 1fr);
    gap: var(--space-1);
    margin: 0;
    padding: 0;
    list-style: none;
    position: sticky;
    top: var(--space-4);
}

.stage-node {
    display: grid;
    grid-template-columns: 18px minmax(0, 1fr);
    gap: var(--space-2);
    align-items: start;
    padding: var(--space-2) var(--space-2);
    border: 1px solid transparent;
    border-radius: var(--radius-sm);
    font-size: .8rem;
    line-height: 1.35;
}

.stage-mark {
    width: 16px; height: 16px; margin-top: 2px;
    border: 1.5px solid var(--line);
    border-radius: 50%;
    display: grid; place-items: center;
    font-size: .62rem; font-weight: 700;
    color: var(--faint);
}

.stage-title { font-weight: 600; color: var(--ink); display: block; }

/* One line, clipped: a rail whose boxes grow with their answers is a rail that stops fitting. */
.stage-answer {
    display: block;
    font-size: .74rem;
    color: var(--muted);
    white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
}

.stage-node.done .stage-mark { background: var(--ok-wash); border-color: var(--ok); color: var(--ok); }
.stage-node.active {
    background: var(--surface);
    border-color: var(--accent);
    box-shadow: var(--shadow-md);
}
.stage-node.active .stage-mark { background: var(--accent); border-color: var(--accent); color: #fff; }
.stage-node.active .stage-title { color: var(--accent-ink); }
.stage-node.pending { opacity: .62; }
.stage-node.pending .stage-title { font-weight: 500; color: var(--muted); }
```

Read the existing state class names off `StageDiagram.razor`'s `CssFor(state)` before writing this and use whatever it actually emits — `done` / `active` / `pending` above are the expected names, not confirmed ones.

- [ ] **Step 2: Build, run the suite, commit**

```bash
git add CodeGenerator/wwwroot/workflow.css CodeGenerator/Web/Components/StageDiagram.razor
git commit -m "Restyle the stage rail, sized so nine boxes still fit a laptop screen"
```

---

## Task 6: Restyle the panel, the class cards and the relationships page

**Files:**
- Modify: `CodeGenerator/wwwroot/workflow.css` (`.panel`, `.entity`, `.fields`, `.add-field-row`, `.suggestions`, `.settled`, `.command-echo`, and new `.screens` rules)
- Modify: `CodeGenerator/Web/Components/Stages/ReviewStage.razor` (add `.role-chip` and `.field-key` spans only)

**Interfaces:**
- Consumes: tokens from Task 4.
- Produces: new classes `.role-chip`, `.field-key`, `.screens`, `.screens-block`, `.screen-class`. Every existing class name is kept.

- [ ] **Step 1: Add the role chip and key marker to the card markup**

In `ReviewStage.razor`'s card header, replace the bare `<span class="provenance">` line with the provenance plus a role chip. The role is `entity.Role`:

```razor
                    <span class="role-chip role-@entity.Role.ToString().ToLowerInvariant()">@entity.Role</span>
                    <span class="provenance">@entity.Provenance</span>
```

In the field row, mark the key. A field is the key when its name matches `Id` or `<Entity>Id`, which is the same rule `OllamaIntegrationTests` asserts:

```razor
                            @if (ModelKeys.IsKey(entity, field))
                            {
                                <span class="field-key">PK</span>
                            }
                            else if (ModelKeys.IsForeignKey(entity, field))
                            {
                                <span class="field-key fk">FK</span>
                            }
```

If no such helper exists, write `ModelKeys` as a small static class beside `ModelEditor` with those two predicates and unit-test it — do not inline the rule into the markup.

- [ ] **Step 2: Restyle the cards**

```css
.entity {
    border: 1px solid var(--line);
    border-radius: var(--radius-sm);
    background: var(--surface);
    display: flex; flex-direction: column;
    overflow: hidden;
}
.entity:hover { border-color: var(--accent); }

.entity > header {
    display: flex; align-items: center; gap: var(--space-2);
    padding: var(--space-2) var(--space-3);
    border-bottom: 1px solid var(--line-soft);
}

/* A class name is code, so it is set in code type — and the box it sits in only looks like a
   box once you reach for it, so a card reads as information rather than as a form. */
.entity-name {
    font: 650 .95rem/1.2 var(--mono);
    color: var(--ink);
    background: none;
    border: 1px solid transparent;
    border-radius: var(--radius-sm);
    padding: 2px 4px; margin-left: -4px;
    flex: 1; min-width: 0;
}
.entity-name:hover { border-color: var(--line); background: var(--machine); }
.entity-name:focus-visible { outline: 2px solid var(--accent); outline-offset: 1px; }

.role-chip {
    font: 700 .63rem/1 var(--sans);
    letter-spacing: .07em; text-transform: uppercase;
    padding: 4px 6px; border-radius: 4px; white-space: nowrap;
    background: var(--accent-wash); color: var(--accent-ink);
}
.role-transaction { background: #f4ecdf; color: #7d4b0c; }
.role-document    { background: #ece9f6; color: #4b3f8c; }
.role-place       { background: var(--ok-wash); color: #12603a; }

.fields { margin: 0; padding: var(--space-1) 0; list-style: none; background: var(--machine); flex: 1; }
.fields li {
    display: grid; grid-template-columns: minmax(0, 1fr) auto 20px;
    gap: var(--space-2); align-items: center;
    padding: 2px var(--space-3);
    font: .82rem/1.6 var(--mono);
}
.fields li:hover { background: var(--surface); }
.field-key { color: var(--accent); font-weight: 700; font-size: .62rem; letter-spacing: .05em; }
.field-type { color: var(--muted); font-size: .76rem; font-variant-numeric: tabular-nums; }

.screens { width: 100%; border-collapse: collapse; font-size: .87rem; }
.screens th {
    text-align: left;
    font: 700 .68rem/1 var(--sans); letter-spacing: .07em; text-transform: uppercase;
    color: var(--muted);
    padding: 0 var(--space-3) var(--space-2) 0;
    border-bottom: 1px solid var(--line);
}
.screens td { padding: var(--space-2) var(--space-3) var(--space-2) 0; border-bottom: 1px solid var(--line-soft); }
.screen-class { font-family: var(--mono); font-weight: 600; }

.command-echo {
    margin: 0;
    background: var(--machine);
    border: 1px solid var(--line-soft);
    border-radius: var(--radius-sm);
    padding: var(--space-3);
    font: .78rem/1.6 var(--mono);
    overflow-x: auto;
}
```

- [ ] **Step 3: Build, run the suite, commit**

Expected: green. `.entity`, `.add-field-row`, `.command-echo` and `.screen-summary` are all still present, which is what the existing tests assert.

```bash
git add CodeGenerator/wwwroot/workflow.css CodeGenerator/Web/Components/Stages
git commit -m "Restyle the class cards: code in code type, roles as chips, fields in columns"
```

---

## Task 7: Bring chat.css onto the same tokens

**Files:**
- Modify: `CodeGenerator/wwwroot/chat.css` (its token block and the tree / transcript / composer rules)

**Interfaces:**
- Consumes: the palette from Global Constraints.
- Produces: no new API.

**Critical:** `chat.css` already has a light/dark treatment scoped to `.chat`. Keep that scoping. Change the token *values* to the shared palette and leave the light/dark structure exactly as it is — do not move any token to `:root`.

- [ ] **Step 1: Re-point the chat tokens**

Read the existing `.chat` token block first and map each existing name onto the shared palette, keeping every existing custom-property name so no rule below needs editing. Where chat has a token with no counterpart, leave it.

- [ ] **Step 2: Align the three regions**

Bring the tree, transcript and composer to the same radii, spacing scale and mono/sans split as the workflow pages, matching the mockup's Chat tab.

- [ ] **Step 3: Build, run the suite, commit**

```bash
git add CodeGenerator/wwwroot/chat.css
git commit -m "Bring the chat page onto the shared palette and spacing scale"
```

---

## Task 8: The standalone pages

**Files:**
- Modify: `CodeGenerator/Web/Components/ValidationPage.razor`, `RelationshipsPage.razor`, `SolutionPicker.razor` and their rules in `chat.css`

**Interfaces:**
- Consumes: tokens from Tasks 4 and 7.
- Produces: no new API.

**Watch for:** `RelationshipsPage` borrows `.chat`'s class for its tokens, which once imported that page's viewport-fixed flex layout with it. If it still does, give it its own token block rather than borrowing.

- [ ] **Step 1: Restyle each page to the shared tokens, build, run the suite, commit**

```bash
git add CodeGenerator/Web/Components CodeGenerator/wwwroot/chat.css
git commit -m "Restyle the validation, relationships and solution-picker pages to match"
```

---

## Task 9: Verify in a real browser

**Files:**
- Create (throwaway, deleted before commit): `CodeGenerator.Tests/Web/ZzRestyleProbe.cs`

No unit test can see any of this. The defects this codebase has shipped in CSS — an id in a hiding rule beating two classes in a showing rule, a `:root` block repainting another page, a rail whose last two boxes fell off the bottom — were every one of them found by looking.

- [ ] **Step 1: Stand the workflow up on each stage**

Write an MSTest probe modelled on the one used for the Discover verification: build a `BuildSession`, walk it to the stage under test, `BuildAppWebHost.TryStart`, write the URL to a file, sleep. Gate it on an environment variable so a normal run skips it.

- [ ] **Step 2: Measure the rail, do not eyeball it**

```bash
node "C:/Users/admin/.claude/skills/browser-automation/browser.mjs" "$URL" \
  --eval "JSON.stringify({rail: document.querySelector('.stage-diagram').getBoundingClientRect().height, boxes: document.querySelectorAll('.stage-node').length, viewport: innerHeight})"
```

Expected: 9 boxes, rail height ≤ 470px. If taller, reduce `.stage-node` padding and the answer line's size until it fits — do not declare it fine because it looks fine in a tall window.

- [ ] **Step 3: Screenshot every page and read them**

Capture Review classes, Relationships, Generate, Run, and the chat page, then Read each PNG. Check specifically: no horizontal body scroll, the class cards align on a shared grid, the role chips are legible, the command echo does not overflow its box, and the focus ring is visible on a tabbed control.

- [ ] **Step 4: Delete the probe, run the full suite, commit**

```bash
rm CodeGenerator.Tests/Web/ZzRestyleProbe.cs
```

---

## Task 10: Documentation

**Files:**
- Modify: `docs/user-manual-source.html` (the build-workflow chapter — the walk is nine steps now, and the Review chapter splits)
- Regenerate: `docs/SmartCodeGenerator-User-Manual.docx`

- [ ] **Step 1: Update the manual's description of the workflow**

Find every place that says the workflow has eight steps or that Review carries the relationship diagram / screen choice / command echo, and correct it. The screens table and the generate confirmation are now step 7.

- [ ] **Step 2: Regenerate and verify**

```powershell
cd D:\SmartCodeGenerator-Repository; .\tools\build-user-manual.ps1; .\tools\verify-user-manual.ps1
```

Expected: "All checks passed."

- [ ] **Step 3: Commit**

```bash
git add docs
git commit -m "docs: the workflow is nine steps, and Review is two of them"
```

---

## Self-review notes

- **Spec coverage:** the mockup's four tabs map to Task 2 (both new panels), Task 6 (cards, chips, field columns, screens table), Task 7 (chat), and the Before/After tab's six numbered changes — 1, 2, 3 and 4 land in Task 6; 5 in Task 2; 6 in Task 5.
- **Two things the mockup shows that this plan deliberately does not build:** a dark theme for the workflow pages (the artifact needs one; the app's workflow pages have never had one, and adding it is a separate decision), and the `Step N of 9` chip in the header (`StageGraph.TabTitle` already says this in the browser tab; putting it on the page too is duplication). Raise both with the user rather than silently including or excluding them.
- **Known unverified assumption:** Task 5 names the rail's state classes `done` / `active` / `pending` from `CssFor(state)` without having read that method. The task says to read it first; if the names differ, use the real ones.
