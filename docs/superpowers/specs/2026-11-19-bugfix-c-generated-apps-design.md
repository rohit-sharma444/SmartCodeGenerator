# Bug fixes C — generated application output — design

Branch: `bug-hunt-specs`. Date: 2026-11-19. Status: draft for review.
Base: `main` = 10224d0. Project: `AppScaffolding`. Evidence key as in spec A.

## C1. Generated `JsonStore.cs` has warning CS8602 (PROVEN)

**Where:** `NewRecordTemplate.cs:22` emits `item.GetType().GetProperties(...)`, spliced into
`JsonStoreTemplate.cs:122` (`StartDatesAtToday(item)`). `JsonStore<T>` is
`where T : new()` (`JsonStoreTemplate.cs:36`), so `T` may be null. `EfStore` has
`class, new()` and does not warn.

**Failure:** `dotnet build --no-incremental` of a webapi and a blazor app from a CSV:
`Data\JsonStore.cs(163,34): warning CS8602`. A user with `-warnaserror` cannot build.

**Fix:** add the `class` constraint: `where T : class, new()` (matches `EfStore` and the
entity classes, which are all classes). Check every emitted user of `JsonStore<T>` compiles.

**Tests:** compile `JsonStoreTemplate.Source(...)` with nullable enabled through
`CompileSources`; assert no CS8602 (and no warnings at all from that file).

## C2. Seed and test records break value-list and unique rules (PROVEN)

**Where:**
- `SeedDataTemplate.cs:256-278` `SampleValue` ignores rules and gives "Status 1/2/3"; rows are
  saved with `SaveAllUnchecked`.
- `TestProjectTemplates.cs:534-541` `AcceptedString` always uses the first listed value; its
  comment calls the clash "unavoidable".

**Failure:** webapi with `Status` allowedvalues=[open, shut, hold, done, new] + unique. GET shows
`"status":"Status 1"`; PUT of that same row unchanged → 400 "Status must be one of: …". Every
seeded row breaks the value list. In tests, a second record with "open" fails the unique rule
(an imported seed row, or a second parent that `ForeignKeyValue` creates); that parent then falls
back to FK 0.

**Fix:**
1. Seeder: for a property with an `allowedvalues` rule, sample value n is
   `allowed[(n-1) % allowed.Count]`. With `unique` too, stop seeding that class after
   `allowed.Count` rows and add a comment line in `SeedData.cs` saying why.
2. Test template: emit a runtime pick — the first listed value that no stored row uses — instead
   of a constant. If all are used, the test marks itself inconclusive with the reason.

**Tests:** (a) generate an app with list + unique, run `EnsureSeeded`, validate every seeded row:
all valid. (b) generated tests for a parent with list + unique and a child that references it: all
pass. Run both through the app-check harness.

## C3. `add` writes .new files when the model renamed a class (PROVEN)

**Where:** `Incremental/IncrementalScaffolder.cs:106-110`. The baseline is the current model minus
the new classes (`Without`, `:167`), not the model the app was built from. A class on disk that
is not in the baseline makes `IncrementalFiles.Decide` return Merge (`:80`); the `LineMerge`
context fails; `SetAsideNewVersion` writes `.new`.

**Failure:** run 1 Book + Member; run 2 Book + LibraryMember + Loan (a typical LLM rename). For
wpf, blazor and webapi: SeedData.cs, Permissions.cs, ValidatorFactory.cs and the screen file are
set aside as `.new`. **Loan is not wired in** (no seed, no validator registration, no screen or
endpoint) and the app still compiles. `AddReport.cs:50` explains it but does not prevent it.

**Fix (recommended):** build the baseline from what is on disk: add a `ClassSnapshot` → class
definition for each class in the app's `Models` folder that the new model lacks, into **both** the
baseline and the staged model. The staged output then keeps `Member` wired and adds `Loan`. The
renamed `LibraryMember` is offered as a new class (the user decides; no automatic rename).
Alternative: store the generated model in `BuildSettingsRecord` and use it as the baseline — more
exact, but old apps have no record.

**Tests:** scaffold `Model("Customer","Member")`, then
`Add(Model("Customer","LibraryMember","Supplier"), ["LibraryMember","Supplier"])`: `NotMerged` is
empty, `ValidatorFactory.cs` contains `SupplierValidator` and still contains `MemberValidator`.

## C4. Seed dates carry a local offset (TRACED, side finding, low — confirm first)

**Where:** seed `DateTime` values use `DateTime.Today` (Kind = Local), so `CreatedOn` is written as
`"2026-11-18T00:00:00+05:30"`; other stored dates have no offset. `NewRecordTemplate` was already
changed for this reason.

**Fix:** use `DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified)` in the seed
template, as `NewRecordTemplate` does.

**Tests:** serialize a seeded record; the date text has no offset.

## Order of work

C3 (silent missing wiring) → C2 → C1 → C4. After all: AppScaffolding/CodeGenerator suites with
`-m:1`, then the app-check harness 26/26, and look at the screenshots.
