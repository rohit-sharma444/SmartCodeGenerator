# Task 15 run notes — validation-phase-3

What running the generated apps actually showed, per the task 15 brief. Every defect
below was invisible to the ~4,200-test suite that existed before this task started; each
was found only by generating a real app and trying to break it.

## Step 1 — generate from SQL

Created `ValidationPhase3Test` on `localhost` (SQL Server): a `Customer` master (Id, Code,
Status, Name — 5 rows, `Status` has 3 distinct values Active/Inactive/Pending, `Code`
unique C001–C005) and an `[Order]` transaction (Id, CustomerId FK, OrderDate, Total — 4
rows: Customer 1 has 2 orders, Customer 2 has 1, Customer 3 has 1, Customers 4 and 5 have
none).

```
build app "ordersblazorfinal" from sql "Server=localhost;Database=ValidationPhase3Test;Trusted_Connection=True;TrustServerCertificate=True;"
```

`rules` listed 5 proposals from the real data. Accepted #3 (`Customer.Status` allowed
values) and #2 (`Customer.Code` uniqueness), left the other three (two `Name` proposals,
one more `Code` proposal) unticked. `approve wpf` and `approve blazor` (two separate runs,
one review session per `approve`) both generated cleanly.

## Step 2 — read the emitted validator before running anything

`Validation/CustomerValidator.Generated.cs`, checked against all four points:
- `Stages = [Security, Field, Entity]` ✅
- Allowed-values check names exactly `Active, Inactive, Pending` ✅
- Uniqueness check names `Code` ✅
- Nothing names the three unticked proposals (`Code` second proposal, `Name`
  allowed-values, `Name` uniqueness) ✅
- Bonus: task 8's delete-refusal (`CountReferencing<Order>("CustomerId", ...)` inside
  `Op == Delete`) present and wired ✅

## Step 3 — run it and try to break it (WPF)

Drove the real `Orders.exe` with a from-scratch UIA harness (`net10.0-windows`,
`System.Windows.Automation`, short-pathed at `D:\wpfharness` to avoid `MAX_PATH`). All six
checks pass on the live app:

1. Bad status → "Cannot save: Status must be one of: Active, Inactive, Pending."
2. Duplicate code → "Cannot save: Another Customer already has this Code."
3. Same record saved twice unchanged → "Saved." both times (`exceptId` confirmed working)
4. Delete Customer with 2 referencing Orders → "This Customer cannot be deleted — 2 Order
   record(s) refer to it.", record stays (Total Records: 5)
5. Delete Customer with no references → "Deleted.", Total Records: 4
6. New Order with no Customer selected → "Cannot save: Customer is required."

**Two severe defects found and fixed while getting here** (see "Defects found and fixed"
below): a fresh login app could never be signed into (`39e392c`), and a signed-in
Administrator could not save any master edit (`58c1c80`).

## Step 4 — run the same six against a second target (Blazor)

Regenerated fresh from the current build (after every fix below had landed, including the
Blazor-specific Save/Delete permission fix at `7d0f22a`) into
`D:\SampleProjects\Ordersblazorfinal.Web`, using the same accepted rules and the same
seed data. Built clean (`dotnet build -c Debug`, 0/0). Drove the running app headless via
patchright (Chromium), signed in as `admin`/`admin`, and ran the identical six checks:

1. Bad status → "Cannot save: Status must be one of: Active, Inactive, Pending."
2. Duplicate code → "Cannot save: Another Customer already has this Code."
3. Same record (Customer 1, 2 referencing Orders) saved twice unchanged → both returned to
   the grid with no error (`exceptId` confirmed working on the Blazor path too)
4. Delete Customer 1 (2 referencing Orders) → "This Customer cannot be deleted — 2 Order
   record(s) refer to it.", record stays in the grid
5. Delete Customer 4 (no references) → "Deleted.", row removed from the grid
6. New Order with `CustomerId` left at its default 0 (no master selected) → "Cannot save:
   Customer is required."

All six pass, word-for-word identical refusal text to the WPF run — confirming the
Blazor-specific fix at `7d0f22a` (see below) actually works for a real signed-in user
clicking through a real page, not just under unit/integration coverage.

## Step 5 — from a file source and from a discovered domain

CSV (`build app "shopcsv" from csv D:\SampleProjects\data\shop.csv`, `rules` then
`approve wpf`, nothing accepted): `ShopValidator.Generated.cs` contains only `maxlength`
checks — zero `allowedvalues`/uniqueness/pattern anywhere. Confirms an unaccepted
proposal enforces nothing.

Discovered domain (`build app: a library with books and members`, no data source): Ollama
discovery produced 8 entities. Builds 0/0, launches, runs with no crash. Confirms the
no-tables-no-crash expectation.

**One pre-existing, out-of-scope defect found on the original CSV path**: an entity named
after the app itself ("Shop") collided with the app's own root namespace (8× CS0118).
`DomainModelMapper.ReservedNames` already exists to prevent exactly this class of
collision but never included the app's own root namespace. Confirmed present at this
branch's merge-base, untouched by any of the 15 tasks. Filed as **issue #132**, not fixed,
same precedent as **issue #131** (unbounded `SELECT *` in `SqlClientSchemaSource`, also
pre-existing and out of scope).

## Defects found and fixed (all four, real production code, all regression-tested)

1. **`39e392c`** — A freshly generated login app could never be signed into.
   `UserSeed.EnsureAdminSeeded` wrote the first admin account through the same `SaveAll`
   that correctly refuses a null-role write in a login app — but nobody can be signed in
   yet during seeding, so the very first write silently refused itself. Fixed with a new
   `IStore<T>.SaveAllUnchecked`, the one exemption grep-able by name.

2. **`58c1c80`** — Even after #1, a signed-in Administrator could not save any edit to a
   master record on either desktop target. Three production save paths built their
   `ValidationContext` via a test-only `.For(...)` factory instead of threading the real
   signed-in user through. Fixed by mirroring each file's own pre-existing `Delete`
   pattern.

3. **`735a34e`** — A mechanical consequence of #2: the Blazor save fix's shared template
   left two adjacent blank lines in `RecordEditor.razor` whenever `login=false`. Caught by
   the existing `No_emitted_file_has_two_blank_lines_together` guard on the full suite.

4. **`9592770`** — Same root cause as #1, one call site further out: business-data seeding
   (`SeedData.EnsureSeeded`, both branches) hit the identical bug, so a login app started
   with empty data. Fixed the same way as #1.

5. **`7d0f22a`** — A Blazor-specific, one-layer-deeper version of #2: `JsonStore<T>
   .SaveAll`'s own internal permission check reads `ActingUser.Role()`, which is hardcoded
   to `null` whenever `sessionAuth=false` — always true for Blazor, since it has no
   desktop `Session`. So even after #2, Blazor's Delete and Save both failed for
   *everyone*, because the store's own redundant, structurally-broken gate refused every
   write regardless of the real, correctly-authorized signed-in user.
   - Delete: `_store.SaveAll` → `SaveAllUnchecked` on the `login=true` branch only —
     `DeleteHandler` already authorizes with the real role before ever reaching the store.
   - Save: no existing check ran at all under `novalidation+login=true`. Added an explicit
     guard gated on `login` alone (mirroring `MayDelete`), widened `RecordEditor`'s
     `@using {root}.Validation` from `validation`-only to `validation || login` to match
     `MasterScreen`/`TransactionScreen`'s own unconditional import, then `SaveAllUnchecked`
     for the actual write.
   - This fix was implemented only after the user's explicit sign-off (a subagent
     correctly refused to accept the parent session's own authorization for a
     permission-classifier-flagged change and asked for the human directly).
   - Verified live in this task's Step 4, above — the first time this specific fix was
     clicked through in a browser rather than only unit/integration tested.

Two issues found and filed, not fixed (pre-existing, out of this branch's scope):
**#131** (`SqlClientSchemaSource.cs` reads a table with no cap), **#132** (an entity named
for the app itself collides with the app's own root namespace).

## Final verification

- Release `dotnet build SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror`:
  0 warnings, 0 errors.
- Full suite `dotnet test SmartCodeGenerator.slnx -c Release --no-build`: **4259/4259**,
  0 failed, 0 skipped (228 NaturalLanguageEngine.Tests + 397 DataIngestion.Tests + 93
  CodeIntelligence.Tests + 638 DomainDiscovery.Tests + 2903 CodeGenerator.Tests), run at
  head `7d0f22a`.
