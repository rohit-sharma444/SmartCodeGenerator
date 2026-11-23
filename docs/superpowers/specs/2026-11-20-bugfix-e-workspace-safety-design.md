# Bug fixes E: workspace safety (bug hunt #2)

Found 2026-11-20 by a read-only audit of undo/redo, `run`/`save session`, `batch`,
`WorkspaceSnapshot` and the destructive edit commands, on main b5cae20. Every item was
reproduced with a piped CLI run in a probe workspace (PROVEN). The B fixes of PR #174 were
re-checked and hold.

## E1. Edit commands ignore `use project`; `delete class` deletes the class in every project (PROVEN, high)

**Repro:** `create console project Alpha`, `create console project Beta`, `use project Alpha`,
`create a Customer class`, `use project Beta`, `create a Customer class`, then
`add property Age int to Customer`, `add method Save to Customer`,
`remove property Age from Customer`.

**Failure:** all three edits went to `Alpha/Customer.cs`; Beta's class was untouched and no
message named the file. `rename class Customer to Client` with Beta selected renamed Alpha's
class and then warned, wrongly, that `Beta\Customer.cs` may still use `Customer`. With Beta
selected, `delete class Customer` said "Class 'Customer' deleted (Customer.cs, Customer.cs)"
and moved BOTH projects' files to DeletedClasses; `restore class` brings back only the newest.

**Cause:** the commands resolve the class file three different ways.
`ClassMethodEditService.TryLoad` (`CodeGenerationEngine/Core/ClassMethodEditService.cs` ~238)
takes `FirstOrDefault` of `Directory.GetFiles(workspaceRoot, "*.cs", AllDirectories)` - which
also does not skip bin/obj/DeletedClasses as `ClassFiles.Under` does. `ClassRenameService.cs:59`
uses `ClassFiles.FileOf(workspaceRoot)`; `ClassDeletionService.cs:28-30` collects every match in
the workspace. `create class` and `generate validator` (`EntityExtractor.FindClassFile`) do use
`ProjectManager.CurrentProject`.

**Fix:** one resolver for "the file of class X": the selected project first; the whole workspace
(through `ClassFiles.Under`, so bin/obj/DeletedClasses are skipped) only when no project is
selected. When more than one file matches, refuse and name them. `delete class` never deletes
more than one class.

**Tests:** a workspace with `Alpha/Customer.cs` and `Beta/Customer.cs`, Beta selected:
`add property` changes only Beta's bytes; `rename class` renames only Beta's; `delete class`
moves only Beta's file. With no project selected and two matches: each command refuses and
names both files.

## E2. A refused `rename class` leaves a half-renamed workspace but says "Nothing was changed." (PROVEN, high)

**Repro:** App/App.csproj; `Customer.cs` (UTF-8); `Invoice.cs` (Windows-1252, holds `// café`,
uses `Customer?`); `Order.cs` (uses Customer). `rename class Customer to Client`.

**Failure:** "Invoice.cs is not UTF-8; save it as UTF-8 and try again. Nothing was changed." -
but `Customer.cs` now declares `class Client` (file not renamed, Order.cs not updated), so the
project no longer compiles. Only an explicit `undo` repairs it.

**Cause:** `ClassRenameService.cs:127-128` writes `plan.Edits` one at a time with
`SourceFileText.Rewrite`; the second file throws `NotUtf8Exception` and the catch at ~147
reports nothing changed. The `File.Move` loop (~130) has the same partial-write risk.

**Fix:** before the first write, `SourceFileText.Load` every file in `plan.Edits` (as
`RelationshipWriter.Unreadable` does) and refuse on the first that is not UTF-8. Also keep each
file's original bytes and put them all back (and undo any moves) on any failure in the write or
move loops, so the message is true.

**Tests:** the fixture above: the bytes of all three files are unchanged and there is no
`Client.cs`. A second test fails a `File.Move` (target locked) and asserts the same.

## E3. A failed `run`/`batch` rollback stops on any file another program holds open (PROVEN, high)

**Repro:** workspace with `data.csv` and `Generated/Customer.cs`; script `s.cgs`:
`add property Age int to Customer`, `add property Name string to Nobody`. Hold `data.csv` open
with `FileShare.Read` (as a viewer or Excel does), then `run s.cgs`.

**Failure:** "Line 2 failed. Putting it back failed too: The process cannot access the file
'...\data.csv'... The workspace is in an unknown state". `Customer.cs` keeps `public int Age`.
The snapshot folder is kept in %TEMP% for good.

**Cause:** `WorkspaceSnapshot.cs:156-161` `File.Copy(..., overwrite: true)` for EVERY captured
file, changed or not, in capture order, and stops at the first exception - so one untouched,
locked bystander blocks the restore of the files the script really changed.

**Fix:** copy back only files whose bytes differ from the snapshot (length first, then content).
Carry on past a failure; at the end report only the files that really could not be restored,
and keep the snapshot only then.

**Tests:** snapshot; change file B; open untouched file A with `FileShare.Read`; `Restore`:
B is restored and no exception is thrown. A changed and locked file is named in the failure.

## E4. One locked file switches undo off for the whole session without a word (PROVEN, medium)

**Repro:** a workspace with `app.log` held with `FileShare.None` (a running app's log or DB).
`create a Customer class`, `add property Age int to Customer`, `undo` -> "Nothing to undo."

**Cause:** `UndoJournal.cs:147-167`: `Start()` (~408-438) throws while mirroring the locked
file; `Begin` catches the IOException and returns `Mode.Unrecorded` silently, and every later
command retries the full mirror (up to the size cap) again.

**Fix:** leave a file that cannot be read out of the shadow and say once, through `_notify`,
which file undo cannot cover; do not retry the full mirror on every command. If the journal
must be off, say so once with the file name.

**Tests:** a journal over a workspace with a `FileShare.None` file: the next edit is recorded and
undoable, and `_notify` named the locked file once.

## E5. `generate validator` overwrites a hand-written `<Class>.Validation.cs` (PROVEN, medium, data loss)

**Repro:** a Person class plus a hand-written `Person.Validation.cs` (a comment and an
`IsAdult()` method). `add validation must be between 18 and 65 on Age in Person`,
`generate validator for Person` -> "Validator generated"; the hand-written file is gone.

**Cause:** `EntityExtractor.cs:124` `File.WriteAllText(validatorFile, ...)` with no check.
`StaleValidators.Regenerate` already guards with `EmittedSignature` ("so a hand-written
Item.Validation.cs is never replaced"); the explicit command misses that guard.

**Fix:** in `GenerateValidator`, when the file exists and does not carry the emitter signature,
refuse and name the file.

**Tests:** hand-written partial exists -> bytes unchanged and a refusal naming it; an emitted
file -> regenerated as before.

## E6. Undo of a case-only rename keeps the new file name (PROVEN, low)

**Repro:** `App/customer.cs` declares `class customer`; `rename class customer to Customer`;
`undo` -> content says `class customer` again, but the file is still `Customer.cs`.

**Cause:** the journal's scan dictionary ignores case (`UndoJournal.cs:471-477`) and `Apply`
(~587-610) copies onto the existing differently-cased name. `WorkspaceSnapshot.Restore` has the
same effect after a failed script.

**Fix:** when the recorded path differs from the file on disk only in case, `File.Move` it to the
recorded casing (via a temporary name, as Windows requires) before the copy. Same in
`WorkspaceSnapshot.Restore`.

**Tests:** the repro: after undo `Directory.GetFiles` returns `customer.cs`.

## Order of work

E1 → E2 → E3 → E5 → E4 → E6. After all: CodeGenerator suite, then a live piped CLI run that
repeats each repro.
