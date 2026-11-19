# Bug fixes B — edits to the user's workspace — design

Branch: `bug-hunt-specs`. Date: 2026-11-19. Status: draft for review.
Base: `main` = 10224d0. Project: `CodeGenerationEngine/Core`.

These defects touch the user's own files. B1, B2 and B4 can damage or delete user data; fix them
first. Evidence key as in spec A.

## B1. A refused relationship rewrites every class file and damages non-UTF-8 text (PROVEN, critical)

**Where:**
- `Relationships/RelationshipWriter.cs:70` reads `originals` for **all** workspace classes with
  `File.ReadAllText`; `Restore` (`:206-219`) writes **all** of them back through
  `Validation/SourceFileText.Rewrite`, even files the attempt never touched.
- `Relationships/JoinClassWriter.cs:39` has the same `originals` pattern.
- `Validation/SourceFileText.cs:22` always decodes as UTF-8; `ClassMethodEditService.TryLoad`
  (`:242`) uses `File.ReadAllText`. So member edits share the decoding defect.

**Failure (run):** workspace with Customer.cs, Invoice.cs (`// caf\xE9`) and an unrelated Util.cs
(`// r\xE9sum\xE9`) in Windows-1252, plus a top-level Program.cs that makes Verify fail.
`Apply(Invoice→Customer)` returns `Ok=False … Nothing was changed`, but Invoice.cs and Util.cs
now differ: the é bytes became `EF BF BD` (U+FFFD). `remove property Old from Note` on a 1252
file changed the literal `"Café"` to `"Caf�"` — a silent behaviour change.
Traced: a UTF-16 CRLF file comes back as UTF-8 with LF.

**Fix:**
1. `RelationshipWriter`/`JoinClassWriter`: snapshot as raw bytes (`File.ReadAllBytes`), and only
   for the files `Parts(...)` will write (owner file, target file, any created join file).
   `Restore` writes back with `File.WriteAllBytes`, and only for those files.
2. `SourceFileText.Read`: detect the encoding — BOM first (UTF-8, UTF-16 LE/BE, UTF-32), else
   strict UTF-8 (`new UTF8Encoding(false, throwOnInvalidBytes: true)`). On invalid bytes, refuse
   the edit: "<file> is not UTF-8; save it as UTF-8 and try again. Nothing was changed."
   `Rewrite` writes with the encoding (and BOM) it read, and keeps the line endings it found.
3. `ClassMethodEditService.TryLoad` and other member-edit loaders go through `SourceFileText.Read`.
   Grep `File.ReadAllText` under `Core/` for other edit paths and route them the same way.

**Tests (red, then green):**
- Relationship that fails Verify, with a 1252 bystander file: bystander bytes unchanged
  (byte-compare); owner/target bytes equal the originals.
- `remove property` on a 1252 file: refused with the message, bytes unchanged.
- Member edit on a UTF-16 LE BOM CRLF file: result still UTF-16 LE BOM, CRLF.

## B2. The relationship offer crashes on a file that declares two classes (PROVEN, high)

**Where:** `RelationshipWriter.cs:70`, `JoinClassWriter.cs:39` —
`classes.ToDictionary(c => c.File, …)` throws on a duplicate key.

**Failure:** Order.cs with `class Order {CustomerId}` and `class OrderLine` →
`ArgumentException: An item with the same key has already been added`. Every generated app has
such files (`ValidatorFactory.cs` 2 classes, `ValidationContext.cs` 3). Console: the loop prints
"Invalid argument: …". Chat: `ChatSession.Send` has only try/finally and `ChatPage.razor:287` does
not catch, so the exception kills the Blazor circuit.

**Fix:** B1's change (snapshot only the files to be written, as a distinct set keyed with
`StringComparer.OrdinalIgnoreCase`) removes this. Also wrap the chat call at `ChatPage.razor:287`
so an unexpected exception shows an error line, not a dead circuit.

**Tests:** a file with two classes plus a target: `Apply` succeeds. A chat-page bUnit test where
the command throws: the page shows the error and stays interactive.

## B3. Relationship Verify fails on errors that were there before the edit (PROVEN, medium)

**Where:** `RelationshipWriter.cs:174-195` `Verify` compiles every `ClassFiles.Under` file as one
assembly and refuses on any error.

**Failure:** Customer.cs, Invoice.cs (`int CustomerId`) and the default `dotnet new console`
Program.cs → `error CS8805: Program using top-level statements must be an executable. Nothing was
changed.` Without Program.cs it works. Also fails always for test projects (xunit refs), WinForms/EF
types not in the TPA, and duplicate entity names across projects.

**Fix:** compile once **before** the write (baseline) and once after. Refuse only when the after
set has an error not in the baseline. Compare by (code, file, message) and ignore line numbers,
because the edit moves lines. Report only the new errors.

**Tests:** a workspace with a top-level Program.cs → `Ok=True`. A relationship whose new property
truly does not compile (e.g. a name clash) → still refused, message names only the new error.

## B4. A failed `run <script>` or `batch` deletes the user's empty folders (PROVEN, high)

**Where:** `WorkspaceSnapshot.cs:170` — `RemoveEmptyDirectories(_workspaceRoot, isRoot: true)`
deletes **every** empty directory. `_directories` (captured at `:119`) is used for build output at
`:162` but not here.

**Failure (CLI):** make `Assets/Images` and `EmptyTop`, run a script whose line 2 fails. The CLI
says "Everything the script did was put back"; both folders are gone. Real cases: VS
`<Folder Include="Migrations\" />`, empty `wwwroot/...` folders.

**Fix:** delete only directories in `IncludedDirectories(root).Except(_directories)` that are
empty, deepest first (sort by path depth descending). Never touch a directory in `_directories`.

**Tests:** snapshot a workspace with an empty folder, create a new folder, Restore: the old folder
exists, the new one is gone. Nested case: new `A/B` under an existing empty `A` → `A` stays.

## B5. `.vs` is not excluded from the undo journal and the snapshot (TRACED, medium)

**Where:** `WorkspaceSnapshot.cs:22-23` excludes `bin`, `obj`, `.git`, `TestResults`,
`node_modules`; `UndoJournal.cs:46` adds `.codegen`, `.dd-cache`.

**Failure (traced; needs Visual Studio open):** `.vs` holds live SQLite (`*.db`, `-wal`, `-shm`),
`.vsidx`, `.dtbcache` (30 MB on this machine). VS writes them during a command, so the journal
credits the changes to that command; a later undo refuses ("has changed since") or copies stale
bytes over open files; a rollback deletes `-wal`/`-shm`; a locked `.vsidx` makes the copy throw so
every command is Unrecorded; `.vs` counts toward the 200 MB limit that turns undo off.

**Fix:** add `.vs` and `.idea` to `ExcludedDirectories`. (`.vscode` holds user settings; keep it
included.)

**Tests:** a file under `.vs` that changes during a command is not in the journal entry, and
Restore leaves it alone.

## B6. `add property` initializers can produce code that does not compile in older projects (TRACED, medium)

**Where:** `Core/PropertyInitializers.SafeFor`, used by `add property` and retype.

**Failure:**
- Emits `= [];` (C# 12 collection expression) for arrays and `IEnumerable`/`IList`/… In a net6/net7
  project (C# 10/11) with `<Nullable>enable`, `add property Tags string[] to X` writes a file that
  does not compile. `TypeResolution` compiles with `LanguageVersion.Latest`, so nothing catches it.
- Emits unqualified `new Dictionary<K,V>()` for `IDictionary<K,V>` → CS0246 when
  `System.Collections.Generic` is not in scope (ImplicitUsings off, type written fully qualified).

**Fix:** emit forms valid since C# 6 and independent of usings:
`global::System.Array.Empty<T>()` for arrays and read-only interfaces,
`new global::System.Collections.Generic.List<T>()` for `IList`/`ICollection`/`List`,
`new global::System.Collections.Generic.Dictionary<K,V>()` for dictionaries.

**Tests:** a net6.0 project with ImplicitUsings off; add a `string[]` property and a qualified
`IDictionary<string,int>` property; Roslyn-compile at `LanguageVersion.CSharp10`: no errors.

## B7. `PropertyInitializers.NullableOn` reads the last `#nullable` in the file (TRACED, low)

**Failure:** it does not use the directive in effect at the class; `#nullable restore` counts as
"off" even when the project enables nullable; a `#nullable disable` at the end of the file
overrides an earlier enable. Effect: warnings only (CS8618 not prevented).

**Fix:** use Roslyn: parse the file with the project's nullable option and ask the semantic model
(`GetNullableContext(position)`) at the class declaration.

**Tests:** three files — enable then disable at end; `#nullable restore` with a project that
enables; disable before class then enable after — each gives the right answer.

## Order of work

B1+B2 (one change) → B4 → B3 → B5 → B6 → B7. After all: CodeGenerator suite with `-m:1`, then a
live CLI run in a workspace that has a 1252 file, an empty folder and a top-level Program.cs.
