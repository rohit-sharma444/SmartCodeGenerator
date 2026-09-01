# Adding to an app you already generated — design

**Date:** 2026-09-01
**Status:** approved, not yet implemented
**Branch:** `add-to-an-existing-app`

## Problem

There is no way to add to an application the tool has already generated. The only path is to run
the whole thing again.

You built a shop app last week. Now you want `Supplier` and `PurchaseOrder`. Today that means
re-describing the system, letting discovery run over all of it, re-reviewing the eight classes you
already had alongside the two you want, and regenerating every file.

Four things make that worse than it sounds.

**Discovery is not deterministic.** The second run can produce a different model — Wikipedia
content changes, the AI layer may be on or off, the lexicon may hit differently. Classes you
already shipped can come back renamed or reshaped, and you have to notice that in Review and undo
it by hand.

**The diff is the whole application.** Every generated file is rewritten whether or not it changed,
so a two-class addition arrives as a hundred-file diff. Reviewing what actually changed is
impractical.

**Hand edits inside generated files are lost.** Narrower than it first appears — `OverwritePrompt`
already promises that `.Custom.cs` rules, the project file and anything you added yourself are
kept — but a tweak to a generated view goes.

**`create class` looks like the answer and is not.** It writes a class file, and nothing in
`CodeGenerationEngine/Core/` touches the shell, `EntityRoles`, the view models or the DbContext.
The class exists and no screen, seed or registration knows about it.

The guard that fires today is `GeneratedProjectPath.Exists`, which tests only whether the `.csproj`
is there. Nothing records what was generated.

## Goal

Run `build app` again with two more classes in the description, and write only those two classes
plus whatever has to change for them to appear — leaving every other generated file exactly as it
is on disk.

## Scope

**In:**

- Reading the classes already in a generated app.
- `ModelDelta` — which proposed classes are new, unchanged, or changed.
- A fourth answer at the overwrite guard: **add**, offered only when there is something to add.
- Writing the new classes' own files plus the entity-enumerating file, by the three rules in *What gets written*.
- A report naming what was written AND what was left alone.

**Out:**

- `build app: add suppliers to the shop`, and any other new command form. The overwrite guard
  already fires at the right moment; a second entry point can come later if the first proves
  useful.
- Removing a class from an existing app. Adding is the asked-for case, and deleting generated
  files carries a different risk.
- Reconciling a **changed** class. See *A changed class is not an add*.
- Recovering the answers — theme, layout, screen split — from the generated code. They are not in
  the C#.

## Decisions taken before this was written

Each settled with Rohit over a real alternative:

- **Read the generated code, do not write a manifest.** A manifest at generation time would be
  richer — it could keep the answers as well as the classes — but it is a new file that can be
  deleted, edited or drift from the code, and every app generated before this feature would have
  none, so the read-the-code path would be needed anyway as a fallback. The code cannot drift from
  itself.
- **Write the new files plus the composition files.** Purely additive was the alternative: write
  the new class and print the line to paste into `MainViewModel` yourself. Rejected because a
  generator that leaves the last step to the user is a generator that half-worked.
- **A fourth answer at the overwrite prompt.** Describing only the addition
  (`build app: add suppliers to the shop`) reads far better and would spare re-reviewing the
  classes you already have, but it needs a new command form and a way to resolve an app name to a
  folder, and "add X to Y" has to parse reliably enough never to build the wrong thing quietly.
  The guard already fires at exactly the right moment.

## Reading what is already there

`ClassSnapshots.Read(root)` — the same call `WorkspaceRelationships.Of` uses to draw the classes on
disk for `show relationships`. It walks `ClassFiles.Under(root)`, parses each file with Roslyn and
returns a name, a file and a property list.

**Scoped by passing the app's `Models` folder as the root**, not the workspace. `Read` recurses
whatever it is given, and the workspace may hold several generated apps; reading all of them would
diff this app's model against everybody's classes. `GeneratedProjectPath.Folder` already computes
the app folder, so the root is `Path.Combine(that, "Models")`.

Two limitations of that reader, both documented on it, and both consequential here:

- **A file declaring more than one distinct type name contributes no snapshot at all.** For a
  generated `Models/*.cs` that is one class per file, so it does not arise today — but a class
  someone has since given a nested type becomes invisible to the read, is therefore classified
  `New`, and is rewritten. The report naming every file written is what makes that visible rather
  than silent; it is not prevented.
- **Properties come from `EntityExtractor.ExtractProperties`, which works per file.** That is the
  same reader the chat tree uses, so the two cannot disagree about what a class holds.

The read recovers the **classes** and nothing else. Theme, layout and the master/transaction split
are not in the C#; they come from `.codegen/app-profile.json` or from what was chosen on this run,
exactly as they do today.

## The delta

A pure type, so it is testable without a filesystem:

```csharp
public enum ClassFate { New, Unchanged, Changed }

public sealed record ClassDelta(string Name, ClassFate Fate);

public static class ModelDelta
{
    public static IReadOnlyList<ClassDelta> Between(
        IReadOnlyList<ClassSnapshot> existing,
        IReadOnlyList<ClassDefinition> proposed);
}
```

- `New` — no class of that name on disk.
- `Unchanged` — same name, same property names.
- `Changed` — same name, different property names.

Compared on property **names**, not types or order. A type change is a change worth knowing about,
but comparing types would make every run of a differently-versioned generator report churn it
cannot act on, and this delta exists to decide what to write rather than to audit.

Names are matched case-insensitively, because a class name is also a file name on Windows.

Classes on disk that the proposed model does not contain are **not** reported. Removing is out of
scope, and a class the user added by hand is not a discrepancy — it is their code.

## The fourth answer

`OverwriteAction` gains `Add`. `OverwritePrompt.Warning` gains a line naming the counts:

```
D:\SampleProjects\Shop.Wpf already holds a generated project.
It has 8 classes; this model has 10, of which 2 are new.

Regenerating overwrites its generated sources — Models, Data, Views and the
*.Generated.cs validator halves. Your .Custom.cs rules, the project file and
anything you added yourself are kept.

  [a]dd the 2 new   [o]verwrite all   [r]ename   [c]ancel   (Enter cancels)
```

**`Add` is offered only when the delta holds something new.** An answer that would do nothing must
not be on the menu; offering it would be a lie about what the tool can do.

Enter still cancels, for the reason already written on `OverwritePrompt`: the effortless answer has
to be the safe one, and a closed pipe is the absence of an answer rather than consent.

## What gets written

Three rules, in this order, and between them they decide every file:

1. **Always**: the new classes' own files — model, repository or store registration, screen,
   validator, seed.
2. **When `target.HasScreens`**: the one file that enumerates entities for that target.
   `MainViewModel` on the desktop, which is the single list `ShellViewModel` and
   `DashboardViewModel` are both built from and filter into panes and admin panes. Blazor has
   its own; the rule is one lookup on the target, not a list maintained here.
3. **When persistence is SQLite**: `AppDbContext`, for the `DbSet`. Independent of rule 2 —
   a Web API has no screens and still has a context.

Nothing else. Every other generated file is left exactly as it is.

A target with no screens and JSON persistence therefore has an **empty** set for rules 2 and 3,
which makes an add on a console app or class library purely additive. That is the honest
consequence of the rules rather than a special case: those targets have no composition file
because they have nothing to compose.

That set is already inside the promise `OverwritePrompt` makes — "regenerating overwrites its
generated sources… anything you added yourself is kept" — so this breaks no commitment the tool
has made.

The run reports both halves:

```
  Written:
    + Models/Supplier.cs
    + Models/PurchaseOrder.cs
    + Views/SupplierScreen.xaml (+ .cs)
    ~ ViewModels/MainViewModel.cs   (rewritten)

  6 existing classes untouched. 41 files untouched.
```

The untouched count is the half a user actually wants verified, and it is the half a generator
never usually says.

## A changed class is not an add

If `Customer` is on disk with different fields from the model just reviewed, `Add` will not fix it
and `Overwrite` would. The prompt names them rather than pretending:

```
It has 8 classes; this model has 10, of which 2 are new and 1 differs (Customer).
Adding writes the 2 new classes and leaves Customer as it is.
```

Reconciling a changed class — merging fields, or rewriting one class in place — is a separate
problem with its own risks, and bundling it here would make `Add` mean two different things.

## Testing

- **`ModelDelta`, pure**: new, unchanged, changed; case-insensitive name matching; a class on disk
  that the model does not have is not reported; an empty existing list makes everything new.
- **`OverwritePrompt`**: `a` and `add` parse; `Add` absent from the lines when nothing is new;
  present when something is; Enter still cancels.
- **The claim the whole feature rests on, asserted on disk.** Generate an app. Run again with two
  more classes and answer `Add`. Then assert: the two new model files exist, `MainViewModel.cs`
  changed, and **a named existing file is byte-identical to what it was before** — captured as
  bytes before the second run, compared after. Everything else in this spec is machinery; that
  assertion is the feature.
- **The generated project still compiles after an add**, via the existing Roslyn
  compile-the-output path. A new class the composition file does not know about, or a DbContext
  missing a DbSet, compiles to a broken app — and this codebase's rule is that emitted code is
  proved by compiling it, not by substring.
- Then the app is **run**, not just compiled, and the new screen looked for in the rail. Five
  consecutive branches have shipped a defect that compiled, passed its tests and was visible only
  in a running application or page — and two of those branches are still carrying an unperformed
  browser check.

## Risks

- **The blast radius is the write path every target shares.** `AppScaffolder` writes for WPF,
  WinForms, Blazor, Web API, console, class library and worker. A mistake in "which files does this
  write" is silent: a file not written produces a compile error at best and a missing screen at
  worst.
- **Composition differs per target, and the rules above are only as good as the lookup behind
  rule 2.** `WpfBusinessLayout.Shell()` and `WinFormsBusinessLayout` compose the same
  `MainViewModel` differently, and Blazor does something else again. Getting the file right for
  one target while wrong for another is exactly the shape of defect this repo keeps producing —
  so the compile-the-output check has to run for **every** target with screens, not just the
  one being demonstrated.
- **A class invisible to `ClassSnapshots`** — a `Models/*.cs` that has gained a second type name —
  is classified `New` and rewritten. Visible in the report, not prevented.
- **`Add` on an app generated by an older version** may find composition files it would write
  differently in ways that go beyond the new classes. The compile check is the backstop; there is
  no version marker on a generated app to consult.
- **This is the largest of the five improvements.** The other four each had one seam;
  this one has a seam per target.
