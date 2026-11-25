# Implements on an existing class — design

Group 2, feature 4 of the core-improvements review. Approved by Rohit 2026-11-25.

## Intent

Let the user add one or more interfaces to a class that already exists, and leave the project
compiling afterwards — the same result `create class ... implements` already gives a new class.

## Today

- `Order inherits IAuditable` reaches `ClassInheritanceService.AddInheritance`, which adds the
  interface correctly but only *warns* about the members the class now lacks, so the project no
  longer builds (CS0535).
- `Order implements IAuditable` is not recognised: `CommandFront.ClassInheritRe` knows only
  `inherits|extends|derives from`, and takes one bare `\w+` name (no list, no generics).
- The edit finds only `ClassDeclarationSyntax` / `InterfaceDeclarationSyntax`, so a `struct` or a
  `record` is "Class not found".
- `NewClassCompletion` already stubs missing interface members and abstract-base overrides for
  `create class`.

## Goals

1. Recognise `<type> implements <list>` (also `class <type> implements ...`), where the list is
   separated by commas and/or "and", and a name may be generic (`IComparable<Order>`).
2. After the edit, add a stub for each member the type now lacks: a property as an auto-property,
   a method that throws `NotImplementedException` — the same stub shapes `create class` emits.
   This also applies when `inherits` names an abstract base class (overrides of its abstract
   members), unless the edited class is itself abstract.
3. Accept `struct`, `record` and `record struct` as the edited type (a struct/record struct may only
   implement interfaces; a record may inherit only a record).
4. `implements <class>` is refused with a hint to use `inherits`; the file is left unchanged.
5. A list is all-or-nothing: if any name is refused, nothing is written and the message names it.
6. Help text in `CommandDictionary` and the generated `docs/commands.md` / manual appendix updated.

## Non-goals

- Removing an interface from a type (Rohit: no remove command).
- Lists after `inherits`/`extends` beyond what `implements` accepts — the builder may let
  `inherits` share the list parser, but it is not required.

## Anti-goals

- Reformatting the file: edits stay text insertions at one place each, as today.
- A stub that duplicates a member the type already has (by name and signature) — that is CS0111.

## Approaches

- **A (chosen): extend `ClassInheritanceService`** — accept a list, then reuse the stub logic of
  `NewClassCompletion` (extracted to a shared helper) to insert stubs into the existing type.
  One code path for inherits and implements; stub shape identical to `create class`.
- B: a new `InterfaceImplementService` — duplicates the binding, cycle and refusal checks.
- C: keep the warning only — leaves the project broken, which is the gap itself.

## Left to the builder

- Where the stubs go in the type body (end of the body is fine), and their indentation (match the
  type's existing members).
- How the shared stub helper is factored out of `NewClassCompletion`.
- Exact message wording, in the style of the existing messages.

Constraints: Roslyn compile-tested emitted code; check new phrases against `CommandFront.TwoCommands`;
any agent that runs the CLI sets `PROJECTPATH` to a scratch folder.
