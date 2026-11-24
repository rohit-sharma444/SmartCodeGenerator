# Constructors, property modifiers and default values — design

**Status:** approved 2026-11-24 (builder-checked).

## Intent

A user cannot add a constructor, give a property a starting value, or mark a property readonly,
private, static or required. A CLI probe on 2026-11-24 (fresh classlib) showed:

- `add constructor to Order` and `add constructor with Total and Name to Order` → "Command not
  recognized".
- `create class Line with properties Qty int default 1 and Price decimal` → written as
  `public int Qty { get; set; }`: **the default is dropped with no message.**
- `create class Customer with properties Name string required and Email string` → `required`
  **dropped with no message.**
- `add property Count int = 5 to Order` and `add property Status string default "New" to Order`
  → "A property name is required", after "Using the current class 'Line'" (a class the command did
  not name).
- `add readonly|private|required|static property ... to Order`, `make Total readonly in Order`,
  `make Name required in Order` → "Command not recognized".

Group 2, feature 3 of the core command-path review (features 1 and 2 are merged: PRs #177, #178).

## What exists

- `create class` reads its property list in `CommandEntityExtractor.ExtractProperties` →
  `TypedEntries`: each entry is matched by `TypedEntry` (`<name> <built-in type>`) **anywhere in the
  entry**, so any words after the type are ignored without a word. `ParseModifiers` (validation
  words: required, min/max length, unique ...) runs only in the fallback path.
- `PropertyDefinition.Initializer` (a C# expression) exists and `RoslynClassGenerator` writes it
  (used by `build app` from SQL defaults). The CLI's writer, `CodeGenerationEngine.GenerateClass`
  in `CodeGenerationEngine/Core/CodeGenerator.cs`, ignores it and writes only
  `PropertyInitializers.SafeFor` (`= string.Empty` and the like). It writes `[Required]` from a
  rule's `IsRequired`, which only the fallback path sets.
- `add property` → `CommandFront` (`AddPropertyStartRe` + `CommandEntityExtractor.AddPropertyRe`:
  `add|create [a|an|new] property|field <name> [<type>] to|in|on|into [class] <Class>`) →
  `ClassPropertyService.AddProperty(workspaceRoot, className, propertyName, propertyType, out message)`,
  which binds the type in the project and writes `public [static ]T Name { get; set|init; }[ = safe];`
  — `static` and `init` follow the TYPE today (a static class, a record or readonly type), not a
  word in the command.
- No constructor command or service exists. `ParsedCommand.GenerateConstructor` and
  `IntentType.GenerateConstructor` exist and are unused; the new command uses a new
  `IntentType.AddConstructor` and the unused pair is left alone.

## Goals

1. **Constructors.**
   - `add constructor to Order` — one parameter for each settable instance property the class
     declares (in declaration order), each assigned: `public Order(decimal total, string name) { Total = total; Name = name; }`.
   - `add constructor with Total and Name to Order` (also `with Total, Name`, `taking`) —
     only those properties, in the order typed. A name that is not a property of the class is
     refused by name.
   - A class with no such properties gets `public Order() { }`.
   - Works on class, record, struct (and their abstract/sealed forms). Refused on a static class,
     an enum and an interface, in the kind's terms.
2. **Default values** in `create class` and `add property`:
   - `Qty int default 1`, `Qty int = 1`, `Status string default "New"`, `Active bool default true`.
   - The value must fit the type: it is compiled as `T x = <value>;` in the class's project (or
     alone when there is no project); a value that does not fit is refused by name ("'abc' is not
     a valid int value for Qty"), nothing written.
   - A text value without quotes for a `string` property is quoted (`default New` → `"New"`); with
     quotes it is kept.
3. **Modifiers** on a property, before `property` in `add property` and after the type in a
   `create class` list: `readonly`, `private`, `static`, `required`, in any order.
   - `readonly` → `{ get; init; }` (on a static property: `{ get; }` with the default as its
     initializer — `init` is not allowed on a static property).
   - `private` → `private T Name { get; set; }`.
   - `static` → `public static T Name { get; set; }`.
   - `required` → the C# keyword: `public required T Name { get; set; }` (decided by Rohit,
     2026-11-24; the validation rule stays `add validation required on ...`).
   - C#'s rules are refused, not written: `required` with `static` or `private`; `readonly` and
     `required` together are allowed (`required ... { get; init; }`).
   - A constructor added to a class that has a `required` property gets
     `[System.Diagnostics.CodeAnalysis.SetsRequiredMembers]` only when it sets every required
     property; otherwise the required properties it does not set are named in a note.
4. **No silent loss.** A word after a property's type in a `create class` list that is not a
   default, a modifier or a validation word the list already reads is reported as
   `Ignored '<word>' after <Name> <type>` — the class is still written. Today such words vanish.
5. `help`, `docs/commands.md` and the manual show `add constructor`, the modifiers and `default`.

## Rules settled after the builder check (my calls, 2026-11-24)

- **`required` in a `create class` list** is the C# keyword on the typed path (the path almost every
  list takes). The fallback path (`ParseModifiers`, reached only when no entry has a built-in type)
  keeps what it does today. Validation words after a type on the typed path (`min length 3`,
  `unique`, `email` ...) are not applied there: they are reported by goal 4 with a pointer to
  `add validation ... on <Name> in <Class>`. `required` is never both the keyword and `[Required]`.
- **Quoted text in a list** (`default "Black and White"`, `default "a, b"`) is kept whole: quoted
  text is shielded before the list is split on `,` / `;` / `and`, as `MemberListText` already
  shields parentheses.
- **An unquoted default is one token** (`default New`, `default 1`, `= true`). Further words in the
  entry are reported by goal 4 (so `default New York` writes `"New"` and reports `York`; quoting
  is the way to give more than one word).
- **Structs:** a default value on a struct property is refused ("A struct property gets its value
  in a constructor: add constructor to Point") — C# 11 refuses the initializer (CS8983). `required`
  on a struct property is allowed (C# 11 allows it).
- **Which properties `add constructor` takes** (no list): instance auto-properties the class
  declares — `{ get; set; }`, `{ get; init; }`, `{ get; }`, any setter access — excluding static,
  private, and computed (non-auto) properties, in declaration order across partial parts. A
  property with an initializer is included (the constructor value wins). With a list, any instance
  auto-property named, private included. A **positional record** (`record Money(decimal Amount)`)
  is refused: its own primary constructor would need `: this(...)`.
- **`add property` phrasing:** the default comes before ` to <Class>`, and the class is the last
  word (the regex is already anchored at the end); a default containing ` to ` must be quoted.
  `add constructor` takes its class after `to`, `for` or `in`; its property list after `with` or
  `taking` (not `for`).
- **The default-value check** compiles `T x = <value>;` beside the class's project files, with its
  nullable setting and usings, the way `TypeResolution` binds a type — so a project type and
  `default null` work in `add property` wherever they compile.

## Non-goals

- `make Total readonly in Order` / changing modifiers or defaults of an existing property (a later
  "edit property" feature).
- Constructor bodies beyond assignments; constructor chaining; a base-class constructor call
  (`: base(...)`) — a class whose base has no parameterless constructor is left to the compile
  check, which refuses with the compiler's text.
- Fields (as opposed to properties), `protected`/`internal`, `const`, `virtual`/`override`
  properties.
- Project types in a `create class` property list (`Buyer Customer`) — a known pre-existing gap.
- `build app` and the web pages.

## Anti-goals

- A command that says it succeeded and writes code that does not compile.
- A word the user typed dropped without a message (the failure this feature exists to fix).
- Breaking a property list or `add property` phrasing that works now, including validation words
  (`Name string required min length 3`) where they are read today, and "create class X and add
  method Y" as ONE command (`CommandFront.TwoCommands`).

## Approaches

1. **Extend the existing parsers; one new constructor command (chosen by Rohit, 2026-11-24).**
   `PropertyDefinition` gets the modifiers (it already has `Initializer`); the `create class` list
   and `AddPropertyRe` read them and `default`/`=`; `GenerateClass` and `ClassPropertyService` write
   them. `add constructor` gets its own intent and a Roslyn `ConstructorService`.
2. One shared Roslyn member writer for create and add — rejected for now: a rewrite of
   `GenerateClass`, which many tests pin word for word, ahead of the group 4 restructure.
3. A constructor only inside `create class` — rejected: adding one to an existing class is the
   common case.

## Decided

- Approach 1 (Rohit).
- `required` is the C# keyword (Rohit).
- Constructor parameters are the property names in camelCase (`Total` → `total`; a keyword gets
  `@`); refused on a static class or an enum; a constructor whose parameter types match an existing
  one is refused (C# would reject it) (my calls, accepted with the approach).
- Words after the type that are not understood are reported, not dropped (my call; goal 4).

## Left to the builder

The regexes; where the default-value check lives (one helper used by both create and add); the
exact refusal texts, following existing messages; whether `ConstructorService` shares its file
lookup with `ClassMethodEditService.TryLoadParts` (expected).
