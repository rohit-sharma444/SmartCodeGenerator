# Show CRUD Functions in the Review-Loop Display Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The `build app:` review loop prints one summary line telling the user that every candidate entity will also get a generated `<Entity>Repository` with the five CRUD functions.

**Architecture:** A single additive edit to `BuildAppCommand.PrintModel` in the `CodeGenerator` CLI project — after the entity list, when the model has entities, print one summary line before the existing Suggestions/Rejected blocks. Display-only; nothing generated changes.

**Tech Stack:** C# / .NET 10. The change is in the `CodeGenerator` CLI project, which is outside the unit-test boundary (`CodeGenerator.Tests` references the libraries, not the CLI), so it is verified by build + full-suite-no-regressions + a manual CLI smoke.

## Global Constraints

- The summary line text is exactly: `Each entity also gets a <Entity>Repository with CRUD: GetAll, GetById, Create, Update, Delete` — the label is the literal `<Entity>Repository` (not a per-entity name), and the five function names appear in that order.
- The line is printed only when `model.Entities.Count > 0`, and is placed after the entity `foreach` loop but **before** the `Suggestions` ("Also found …") and `Rejected` ("Not classes: …") blocks.
- Do NOT change what is generated, `RepositoryTemplate`, the review-loop commands (`add`/`field`/`approve`/…), or any other display line.
- Do NOT add a `CodeGenerator.Tests → CodeGenerator` project reference or `InternalsVisibleTo` — the CLI stays outside the unit-test boundary as it is today.
- Match the surrounding style in `PrintModel` (`Console.WriteLine`, string concatenation as used by the existing Suggestions block).
- Solution: `SmartCodeGenerator/SmartCodeGenerator.slnx`. Test project: `CodeGenerator.Tests/CodeGenerator.Tests.csproj`. CLI: `CodeGenerator/CodeGenerator.csproj`. Run all `dotnet` commands from repo root `D:\SmartCodeGenerator-Repository`.
- Commit after the task. Work stays on branch `FixesAndEnhancements`.

## File Structure

- Modify: `CodeGenerator/App/BuildAppCommand.cs` — add the summary line in `PrintModel` (only file touched).

## Reference: existing code

`PrintModel` (in `CodeGenerator/App/BuildAppCommand.cs`) currently ends the entity loop and then prints the Suggestions/Rejected blocks:

```csharp
            Console.WriteLine($"  - {entity.Name} [{entity.Provenance}]{fields}{relationships}");
        }

        if (model.Suggestions.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine(
                "Also found - 'add <name>' to include: "
                + string.Join(", ", model.Suggestions.Select(e => e.Name)));
        }

        if (model.Rejected.Count > 0)
        {
            Console.WriteLine("Not classes: " + string.Join(", ", model.Rejected));
        }
    }
```

---

### Task 1: Print the CRUD summary line in `PrintModel`

**Files:**
- Modify: `CodeGenerator/App/BuildAppCommand.cs`

**Interfaces:**
- Consumes: `CandidateDomainModel.Entities` (already in scope in `PrintModel`).
- Produces: `PrintModel` now prints one CRUD summary line after the entity list (when entities exist), before the Suggestions/Rejected blocks. No new public/internal surface.

This task's deliverable is a Console display line in an untested CLI method, so it is verified by build + full-suite-no-regressions + a manual CLI smoke rather than a unit test (see Global Constraints).

- [ ] **Step 1: Add the summary line**

In `CodeGenerator/App/BuildAppCommand.cs`, in `PrintModel`, locate the end of the entity `foreach` loop (the line printing `$"  - {entity.Name} ..."` followed by its closing `}`) and the `if (model.Suggestions.Count > 0)` block that follows it. Insert the new block **between** them, so the code reads:

```csharp
            Console.WriteLine($"  - {entity.Name} [{entity.Provenance}]{fields}{relationships}");
        }

        // Every entity is scaffolded with a typed repository exposing these CRUD functions,
        // so the review loop shows the full shape of what approval will generate.
        if (model.Entities.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine(
                "Each entity also gets a <Entity>Repository with CRUD: "
                + "GetAll, GetById, Create, Update, Delete");
        }

        if (model.Suggestions.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine(
                "Also found - 'add <name>' to include: "
                + string.Join(", ", model.Suggestions.Select(e => e.Name)));
        }
```

Leave the `Rejected` block and everything else unchanged.

- [ ] **Step 2: Build the solution**

Run: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx`
Expected: build succeeds, 0 errors.

- [ ] **Step 3: Full test suite (no regressions)**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj`
Expected: all PASS (208/208 — this change adds no tests and must not break any). Treat "all green" as the gate.

- [ ] **Step 4: Manual CLI smoke — confirm the line renders in the review loop**

The entity set is supplied by hand in the review loop, so this does not depend on discovery output. Run (bash), from the repo root `D:\SmartCodeGenerator-Repository`:

```bash
WS="$(mktemp -d)"; echo "workspace: $WS"

# Discovery runs first (may print progress / fetch); the piped lines drive the review
# loop. Add one entity, print the model once more via an unrecognized no-op, then cancel
# so nothing is generated — we only want to see the printed model.
printf 'add Customer\nfield Customer FullName:string\nc\n' \
  | PROJECTPATH="$WS" dotnet run --project CodeGenerator -- "build app: shop" \
  | tee "$WS/out.txt"

echo "--- summary line present? ---"
grep -n "Each entity also gets a <Entity>Repository with CRUD: GetAll, GetById, Create, Update, Delete" "$WS/out.txt"
```

Expected:
- The transcript shows the candidate model with `- Customer ...`, then a blank line, then the line:
  `Each entity also gets a <Entity>Repository with CRUD: GetAll, GetById, Create, Update, Delete`
- The `grep` finds the line (non-empty match).
- The line appears after the entity list and before any "Also found …" / "Not classes: …" output in the same model print.

Note on the empty-model case: the line is guarded by `if (model.Entities.Count > 0)`, so a model with no entities does not print it. This is self-evident from the guard and does not need a forced empty-model run (the CLI seeds entities from discovery, making an empty model hard to reproduce deterministically).

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/App/BuildAppCommand.cs
git commit -m "Show each entity's CRUD functions in the build app: review loop"
```

---

## Self-Review

**Spec coverage:**
- Single summary line, not per-entity → Task 1 prints one line. ✓
- Exact text, literal `<Entity>Repository`, five functions in repository order → Step 1 code + Global Constraints. ✓
- Only when entities exist; placed after entity list, before Suggestions/Rejected → `if (model.Entities.Count > 0)` inserted between the loop and the Suggestions block. ✓
- Display-only; no change to generation, `RepositoryTemplate`, or commands → only `BuildAppCommand.cs` `PrintModel` touched. ✓
- No unit test / no new project reference or `InternalsVisibleTo`; verified by build + no-regression suite + manual smoke → Task 1 verification steps + Global Constraints. ✓

**Placeholder scan:** No TBD/TODO/"similar to". The code step shows the full inserted block in context; every command states expected output. ✓

**Type consistency:** Uses only `model.Entities.Count` and `Console.WriteLine`, both already used in `PrintModel`. No new types, methods, or signatures introduced. ✓
