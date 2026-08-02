# `build app` review loop: pick the action from a list

Date: 2026-08-02

## Problem

`RunReviewLoop` (`CodeGenerator/App/BuildAppCommand.cs:242`) advertises its nine commands as two
lines of dense help text and then reads a typed line:

```
  [a]pprove [target] [sqlite] add <name>  remove <name>  rename <old> <new>  field <entity> <name>[:<type>]  unfield <entity> <name>  why [name]
    [c]ancel
> 
```

Everything a user can do is crammed into a syntax summary they have to read and retype. The
codebase already has an arrow-key picker — `SelectionPrompt` (`CodeGenerator/App/SelectionPrompt.cs:16`),
used by `ApplicationTypeMenu.Choose` — so the review loop should offer the same affordance.

## Decisions

| Question | Decision |
| --- | --- |
| How far does the list go? | It picks the **action only**. Arguments are typed at a follow-up prompt. |
| Where do `approve`'s target/persistence go? | `approve` opens pickers for them, skipping either when already settled or not applicable. |
| How does the picker reach the existing handlers? | It composes the same `string[] parts` the typed line produces and falls into the unchanged `switch`. |
| Does the typed path change? | **No.** See "Considered and rejected". |

## Behaviour: interactive

`PrintModel` and the `Target:` line (`:248`-`:253`) are unchanged. The two help lines, the `> `
write and the `Console.ReadLine()` (`:255`-`:259`) are replaced by a call to
`SelectionPrompt.Choose("What next?", ReviewActions.Labels, 0)`.

The list, in this order:

```
approve   — generate the app
add       — add an entity
remove    — remove an entity
rename    — rename an entity
field     — add a field to an entity
unfield   — remove a field from an entity
why       — explain how the model was decided
cancel    — nothing is generated
```

Esc on the list is the same as picking `cancel`.

### Arguments

Each action then prompts for its arguments, one typed line each. A blank answer abandons the
action and returns to the list; `why` is the exception, where blank is meaningful.

| Action | Prompts |
| --- | --- |
| `approve` | none (see below) |
| `add` | `New entity name> ` |
| `remove` | `Entity to remove> ` |
| `rename` | `Entity to rename> ` then `New name> ` |
| `field` | `Entity> ` then `Field, name or name:type> ` |
| `unfield` | `Entity> ` then `Field to remove> ` |
| `why` | `Entity (blank for the whole report)> ` — blank means the whole report, not abandon |
| `cancel` | none |

### `approve`

`approve` asks for what it needs through pickers rather than through inline tokens:

1. **Application type** — `ApplicationTypeMenu.Choose()` for the index, then
   `ApplicationTypeMenu.Resolve(index)` for the target, but only when the review loop has no
   `target` already (the wizard settles it for most paths, which is why `:252` can print it).
   Mirrors `ResolveTarget`'s existing precedence: an already-settled target wins.
2. **Persistence** — a JSON/SQLite picker, but only when
   `IsPersistenceAllowed(target, Sqlite)` (`:443`) is true. WPF and WinForms are JSON-only, so
   they skip the question entirely instead of offering a choice that is then rejected at `:300`.

Esc at either picker returns to the action list without generating anything.

The empty-model guard at `:278` is unchanged: `approve` on a model with no entities still reports
"Nothing to generate — the model is empty." and returns, before any picker opens.

## Behaviour: redirected input

Unchanged, and guarded exactly as `ApplicationTypeMenu.Choose` guards
(`CodeGenerator/App/ApplicationTypeMenu.cs:105`):

```csharp
Console.IsInputRedirected || Console.IsOutputRedirected
```

`SelectionPrompt` needs `Console.ReadKey` and `Console.SetCursorPosition`, both of which throw
with no console attached. When redirected, the loop prints today's help lines and `> `, reads a
line, and splits it into `parts` exactly as it does now — so piped E2E scripts, and
`ResolveTarget(..., inputRedirected: true, ...)`, behave identically.

The current single-line help text is kept verbatim for this path.

## Components

### `ReviewActions` — new, pure, tested

```csharp
internal sealed record ReviewAction(
    string Verb,                            // "approve", "add", …
    string Label,                           // "add       — add an entity"
    IReadOnlyList<string> ArgumentPrompts); // empty for approve/cancel

internal static class ReviewActions
{
    public static IReadOnlyList<ReviewAction> All { get; }
    public static IReadOnlyList<string> Labels { get; }

    /// The parts array the switch already expects: [verb, ...answers].
    public static string[] Compose(ReviewAction action, IReadOnlyList<string> answers);
}
```

`Compose` trims each answer and drops a trailing blank one, so `why` with no name yields
`["why"]` while `why Book` yields `["why", "Book"]`. `why` is the only action whose answer can be
blank by the time `Compose` runs — for every other action the prompting layer abandons on a blank
answer and never calls `Compose` — so the remaining actions always supply exactly as many answers
as they have prompts, and the arrays satisfy the `when parts.Length >= N` guards at `:331`,
`:335`, `:339`, `:343` and `:353` by construction.

Composing `parts` directly — rather than formatting a line and re-splitting it — is what keeps
`ResolveTarget`, `ResolvePersistence`, `AddLine`, `WhyLines` and `FieldSpec.Parse` untouched,
along with their existing tests.

### `Generate` — extracted from the `approve` case

`approve` currently derives its target and persistence *from* `parts` (`:284`-`:298`). The picker
path already holds both as values, so the body from `:308` to `:324` moves into:

```csharp
private static void Generate(
    CandidateDomainModel model, string appName, string workspaceRoot,
    ImportedData importedData, ITargetScaffolder target, PersistenceKind persistence)
```

- Typed path: `ResolveTarget` / `ResolvePersistence` / `IsPersistenceAllowed` as today, then `Generate`.
- Picker path: pickers, then `Generate`.

Nothing else in the `switch` moves.

### Console prompting — untested by convention

The action prompt and the argument prompts talk to a real console, so they follow the rule
`SelectionPrompt` states in its own remarks and `CommandLineEditor` follows: the console-facing
shell stays untested, and every decision it makes lives in a pure type that is tested.

## Data flow

```
interactive:  SelectionPrompt.Choose -> ReviewAction -> typed answers -> ReviewActions.Compose ┐
                                                                                              ├─> parts[] -> switch
redirected:   Console.ReadLine -> line.Split(' ', 3, RemoveEmptyEntries) ──────────────────────┘
```

One execution path below `parts[]`; the two input modes differ only above it.

## Error handling

| Situation | Result |
| --- | --- |
| Esc on the action list | Same as `cancel`: "Cancelled. Nothing was generated.", loop returns |
| Blank argument answer | Abandon the action, redraw the model, back to the list |
| Esc on the app-type or persistence picker | Back to the action list, nothing generated |
| Blank `why` answer | The whole report (`WhyLines` already treats an absent name this way, `:475`) |
| `approve` on an empty model | Unchanged message at `:280`, loop returns |
| Failed edit (no such entity, duplicate, bad field) | Unchanged `Report(...)` messages |
| EOF on an argument prompt (`ReadLine` returns null) | Treated as blank: abandon the action |

## Testing

New `ReviewActionsTests`:

- `All` covers exactly the verbs the switch handles — `approve`, `cancel`, `add`, `remove`,
  `rename`, `field`, `unfield`, `why` — and no others.
- Every action's `ArgumentPrompts.Count` matches the arity its switch case requires.
- `Compose` output satisfies each case's length guard for every action.
- `Compose` with a blank trailing answer yields `["why"]`; with a name, `["why", "Book"]`.
- Multi-word answers survive into a single element (`["rename", "Book", "cash register"]`), which
  is what `ToUserTypedEntityName` then turns into `CashRegister`.

No existing test changes. `ResolveTarget`, `ResolvePersistence`, `AddLine`, `WhyLines` and
`FieldSpec.Parse` keep their signatures and their coverage.

Manual check, since neither picker can be driven from a test: run `build app:` interactively and
walk the list, and run the existing piped E2E script to confirm the redirected path is untouched.

## Considered and rejected

**Entity pickers for `remove`/`rename`/`field`/`unfield`/`why`.** A second list of the model's
entities instead of typing the name. Rejected as more new surface than the problem warrants; the
names are visible in `PrintModel` immediately above the prompt.

**Changing how the typed path splits arguments.** Investigated and dropped: it would fix nothing.
Entity names cannot contain a space — `AddEntity` runs the typed name through
`ToUserTypedEntityName` (`"cash register"` → `"CashRegister"`) and `IsValidIdentifier`
(`DomainDiscovery/SeedExtraction/NameNormalizer.cs:33`) permits only letters, digits and `_`. The
existing `Split(' ', 3)` already puts the whole remainder in `parts[2]`, so `rename Book cash
register` and `field Book due date:DateTime` work today. The only position that cannot hold
multiple words is `parts[1]`, the entity lookup, which never needs to.

**Making the entity lookup accept prose** so `remove cash register` finds `CashRegister` (today it
looks up `Cash` and fails). A real improvement, but it lives in `ModelEditor`'s lookups, which are
shared by four commands and other callers. Out of scope here; worth its own brainstorm.

## Risks

- `SelectionPrompt` paints from `Console.CursorTop` and grows the buffer when the list would run
  past it (`SelectionPrompt.cs:30`-`:33`). The action list is 8 rows and is drawn straight after
  `PrintModel`, which can be long — the existing guard covers this, but it is the first caller to
  paint a list that far down the screen.
- The loop redraws the model on every iteration, so an abandoned action reprints the whole model.
  That is today's behaviour for a mistyped command too, so it is not a regression.
