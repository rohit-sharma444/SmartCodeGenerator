# Type kinds: enum, record, struct, abstract/static/sealed class — design

**Status:** approved 2026-11-24 (builder-checked).

## Intent

The CLI can make only a class and an interface. A probe on 2026-11-24 tried `create enum`,
`create record`, `create struct`, `create abstract class`, `create static class` and
`create sealed class`: all six answered "Command not recognized". A user who wants any of these
must write it by hand, then the CLI's edit commands are the only help they get.

This is group 2, feature 2 of the core command-path review (feature 1, method parameters, is
PR #177; this branch, `core-type-kinds`, is based on it).

## What exists

- `create class` → `IntentType.CreateClass` → `CodeGenerationEngine.GenerateClass`
  (`CodeGenerationEngine/Core/CodeGenerator.cs`), which writes `public class X : Base, IFace { ... }`
  as text. `NewClassCompletion.Complete` then compiles it with the project and adds stubs for
  missing **interface** members (not abstract base members).
- The member edits (`add property`, `add method`, rename, retype, remove) find the type through
  `TypeDeclarationSyntax`, so they already reach a record or a struct. An enum is a
  `BaseTypeDeclarationSyntax`, not a `TypeDeclarationSyntax`: no edit reaches it.
- Delete and rename already name all these kinds (`ClassDeletionService`, `ClassRenameService`).
  `show class` (`ClassValidator.ShowClass`) prints the file found by its name, so it works for any
  kind.
- `remove property` removes first and then warns about uses (`MemberReferences.Warning`); no
  remove refuses because of uses.
- `add property` writes `{ get; set; }`, and `init` only on a `readonly` type
  (`ClassPropertyService`).
- Recognition has three sources (`LiteralCommandTable`, the regex guards in `CommandFront`,
  `NlpCommandMapper`); a new guard's intent goes in `CommandFront.GuardedIntents`, and
  `ReachableIntentGateTests` checks each reachable intent has a handler. `help` and
  `docs/commands.md` (and the manual appendix) are generated from `CommandDictionary`.

## Goals

1. **Record, struct, abstract/static/sealed class** — every `create class` phrasing works with the
   kind word in front of `class`, or in place of it:
   - `create record Money with properties Amount decimal and Currency string`
   - `create struct Point with properties X int and Y int`
   - `create abstract class Shape with properties Name string and methods Area returns decimal`
   - `create static class Helpers with methods Format returns string`
   - `create sealed class Token with properties Value string`
   - `create sealed record ...` is accepted too (a sealed record is common).
   Members, method parameters, `implements` and `inherits` work as for a class, within C#'s
   rules for the kind (below).
2. **Enum:** `create enum Status with values Active, Closed, Pending` writes
   `public enum Status { Active, Closed, Pending }`. `with`, `with values`, `with members` and
   `having` all lead the list.
3. **Enum edits:**
   - `add value Archived to Status` → appends `Archived`.
   - `remove value Pending from Status` → removes it.
4. **Abstract base members:** `create class Circle inherits Shape` adds an `override` stub for each
   abstract member of `Shape` it lacks (a method that throws `NotImplementedException`, a property
   as an auto-property), as `NewClassCompletion` already does for interface members, and names
   them in its output.
5. **Edits that follow the kind:** `add method` / `add property` on a static class write a
   `static` member; `add property` on a record writes `init`; `add abstract method` adds an
   abstract method to an abstract class (see "Edits after create").
6. `help`, `docs/commands.md` and the manual appendix show the new commands.

## Rules for each kind

| Kind | Written as | Members | `inherits` | `implements` |
|---|---|---|---|---|
| record | `public record Money` | properties `{ get; init; }`; methods as in a class | a record only | yes |
| struct | `public struct Point` | as in a class | refused | yes |
| abstract class | `public abstract class Shape` | properties as in a class; methods `public abstract`, no body | yes | yes |
| static class | `public static class Helpers` | every member `static` | refused | refused |
| sealed class / record | `public sealed class` / `public sealed record` | as in a class / record | yes | yes |
| enum | `public enum Status` | values only | refused | refused |

A refusal names the reason in the kind's terms (for example "A struct cannot inherit from a class;
use implements for an interface."). Nothing is written on a refusal.

Enum values: each must be an identifier, not a C# keyword, and not repeated (case-insensitive);
values are kept as typed. A value that fails is refused by name, as a parameter is.

`remove value` removes the value and then warns, naming the files that still use it
(`Status.Pending` elsewhere), as `remove property` does today (`HandleDeleteProperty` →
`MemberReferences.Warning`: "was removed, but code may still use it"). Same behaviour as the
existing remove, so the two do not differ for no reason.

`add value` on an enum written by hand:
- values with explicit numbers → the new value gets the next number explicitly (`= max + 1`), so
  it cannot repeat an existing number;
- a `[Flags]` enum → refused ("Status is a [Flags] enum; give the new value its bit by hand"), as
  the next bit is the user's choice.

**Kind words that combine.** Accepted: `abstract class`, `sealed class`, `static class`,
`abstract record`, `sealed record`. Any other pair (`static struct`, `sealed abstract class`,
`static record`, `abstract struct`) is recognised and refused with the C# rule
("A struct cannot be static."), not answered "Command not recognized".

**Edits after create:**
- `add property` on a record writes `{ get; init; }`, as `create record` does.
- `add property` / `add method` on a static class write a `static` member (goal 5).
- `add method` on an abstract class writes a normal method with a body (a shared helper);
  `add abstract method Area returns decimal to Shape` writes `public abstract decimal Area();`.
  `add abstract method` on a type that is not abstract is refused.

**An abstract class that implements an interface** (`create abstract class Shape implements
IShape`): each interface member it lacks is added as a `public abstract` declaration, not a
throw-stub, so the classes that inherit Shape implement it. `NewClassCompletion` skips abstract
types today, so this is new.

`add property` / `add method` / `add validation` on an enum are refused with "Status is an enum;
use add value Archived to Status." instead of a generic not-found message.

## Non-goals

- Explicit enum numbers (`Active = 1`), `[Flags]`, an underlying type (`: byte`).
- `rename value` on an enum (rename for members is a later group-2 feature).
- Positional records (`record Money(decimal Amount)`): a record gets normal properties so the
  existing member edits work on it unchanged.
- `record struct`, `readonly struct`, `partial`, generic types.
- Changing the kind of an existing type (`make Shape abstract`).
- `build app` and the web pages: CLI command path only, as for feature 1.

## Anti-goals

- A command that answers "created" and writes a file that does not compile. The existing compile
  check (`NewClassCompletion`) must run for every kind, enum included.
- A kind word read as the class name: `create record Money` must not make a class called `Record`,
  and a class actually called `Record` (`create class Record`) must still work.
- Breaking `create class` phrasings that work now, including "create class X and add method Y"
  as ONE command (`CommandFront.TwoCommands`).

## Approaches

1. **One create path with a kind (chosen by Rohit, 2026-11-24).** `create record/struct/abstract/
   static/sealed` stay `IntentType.CreateClass`; `ParsedCommand` gets the kind and the modifier,
   and `GenerateClass` writes them. The enum gets its own `IntentType.CreateEnum`, and the two
   edits get `AddEnumValue` / `RemoveEnumValue`. About three new commands; the type kinds share
   one generator and one compile check, so they cannot drift apart.
2. One new command for each kind (`CreateRecord`, `CreateStruct`, ...). About seven new commands,
   each touching about seven files, mostly copies of `create class` — rejected for that.

## Decided

- Approach 1 (Rohit).
- Enum edit commands are in scope (Rohit).
- A record gets `{ get; init; }` properties, not the positional form (my call, accepted with
  approach 1).
- Methods listed in an abstract class are written `abstract` (my call: a user who says
  "abstract class Shape with methods Area" wants each shape to give its own Area).
- After the builder check (my calls, 2026-11-24): `remove value` removes and warns like
  `remove property`; `add value` numbers explicitly when the enum does and refuses a `[Flags]`
  enum; the five kind pairs above are accepted and other pairs refused with the C# rule;
  `add property` on a record writes `init`; plain `add method` on an abstract class writes a body
  and a new `add abstract method` writes an abstract one; an abstract class gets abstract
  declarations for missing interface members.

## Left to the builder

The regexes and where they live; the exact text of each refusal (following existing messages);
whether the enum handler and the enum edit service share code with `ClassMethodService`; how
`remove value` finds uses (`MemberReferences` is the likely helper).
