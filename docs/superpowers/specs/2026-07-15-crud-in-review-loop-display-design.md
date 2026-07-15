# Show CRUD Functions in the `build app:` Review-Loop Display

**Date:** 2026-07-15
**Status:** Approved — ready for implementation planning
**Branch:** `FixesAndEnhancements`

## Problem

The `build app:` review loop (`BuildAppCommand.PrintModel`) shows each candidate
entity as `- Name [provenance] { fields } -> relationships`, but nothing tells the
user that every entity will also get a generated `<Entity>Repository` exposing CRUD
functions. Now that the scaffolder emits those repositories (see
[per-entity CRUD repositories](2026-07-15-per-entity-crud-repositories-design.md)),
the review display should surface them so the user sees the full shape of what
approval will generate.

## Decision (settled)

A **single summary line**, not per-entity output. The five functions are identical
for every entity, and a model can run to ~20 entities, so repeating them per line is
noise. One line after the entity list conveys it without clutter.

## Design

Modify `PrintModel` in `CodeGenerator/App/BuildAppCommand.cs`:

- After the entity `foreach` loop, and only when `model.Entities.Count > 0`, print a
  blank line followed by:

  ```
  Each entity also gets a <Entity>Repository with CRUD: GetAll, GetById, Create, Update, Delete
  ```

- Placed **before** the existing *Suggestions* ("Also found …") and *Rejected*
  ("Not classes: …") blocks, so it reads as a footnote to the entity list.
- Omitted entirely when the model has no entities — there is nothing to describe, and
  the loop already handles the empty-model case elsewhere.
- Function names appear in the repository's own order: `GetAll`, `GetById`, `Create`,
  `Update`, `Delete`.
- The label is the literal `<Entity>Repository`, not a per-entity name. The review
  loop prints **candidate** names (pre-mapping), whereas the real repository name is
  the post-rename class name (e.g. an entity `Type` becomes `TypeItemRepository`).
  A single placeholder avoids showing a name the scaffolder would not actually emit.

## Testability

`PrintModel` lives in the `CodeGenerator` **CLI** project, which is outside the current
unit-test boundary: `CodeGenerator.Tests` references the libraries (`AppScaffolding`,
`CodeGenerationEngine`, `DomainDiscovery`, `NaturalLanguageBridge`) but **not** the
`CodeGenerator` project, and there is no `InternalsVisibleTo` for it. The entire CLI —
`BuildAppCommand`, the REPL, the review loop — is currently unit-tested by nothing.

Making this one display line unit-testable would require either adding a
`CodeGenerator.Tests → CodeGenerator` project reference plus `InternalsVisibleTo`, or
relocating a CRUD-name list into a library purely so the test project can reach it. Both
are disproportionate machinery for a static, approved sentence, and the first crosses a
deliberate boundary (the test project tests libraries, not the executable). So this
change is **not** unit-tested; it is verified end-to-end, consistent with how the rest
of the review loop is verified.

The summary text stays a private detail of `PrintModel` (a small private helper is fine
for readability but is not a testability seam).

## Testing

- **Manual CLI smoke** (the verification of record): run `build app:` with at least one
  entity and confirm the summary line renders in the review loop after the entity list
  and before the Suggestions/Rejected blocks, listing all five functions; and confirm
  it is absent for an empty model. The entity set can be supplied by hand in the review
  loop (`add`/`field`), so the smoke does not depend on discovery output.

## Out of scope

- No change to what is generated, to `RepositoryTemplate`, or to the review-loop
  commands (`add`/`field`/`approve`/…).
- No per-entity repository naming in the display.
- No shared enumeration linking the display's function list to `RepositoryTemplate`'s
  hardcoded source — the two lists stay independent (YAGNI). The display's list is
  maintained by hand; the manual smoke is the guard against it misstating the surface.
- No new `CodeGenerator.Tests → CodeGenerator` project reference or `InternalsVisibleTo`
  — the CLI stays outside the unit-test boundary as it is today.
