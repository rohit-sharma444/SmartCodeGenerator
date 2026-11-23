# Method parameters — design

**Status:** approved 2026-11-23 (short design: a small change).

## Intent

A user cannot give a method parameters. Every phrasing tried in a CLI probe —
`add method ApplyDiscount(decimal percent) returns decimal to Order`,
`... with parameters string address ...`, `... taking string name ...` — failed with
"A method name is required." It was the largest capability gap the core-product review found.

## What exists

- `MethodDefinition.Parameters` (`CodeGenerationEngine/Models/MethodDefinition.cs`) exists, and
  `CodeGenerationEngine.Generate` already writes it for `create class` / `create interface`.
- `CommandEntityExtractor.ParseMethod` matches `Name(...)` but throws the parameter text away.
- `CommandFront.AddMethodRe` has no parameter part; `ClassMethodService.AddMethod` always writes `()`.

## Goals

1. Two forms, in `add method`, `create class ... methods ...` and `create interface ... methods ...`:
   - C# form: `ApplyDiscount(decimal percent, string code)` — type, then name.
   - English form: `ApplyDiscount with parameters percent decimal, code string` — name, then type,
     as property lists are written. `with parameter`, `taking` and `that takes` are accepted too.
2. Each parameter name and type is checked as `add property` checks its own: a name that is not an
   identifier, a duplicate name, or a type that does not bind in the project is refused by name.
3. `add method` on an interface names the implementers that lack the method by its full signature.
4. `help` and `docs/commands.md` (generated from `CommandDictionary`) show the parameter forms.

## Non-goals

- Method bodies stay as now (`// TODO: implement` / `return default`). Better bodies are group 3.
- Overloads: a method name already taken is still refused, parameters or not.
- Default values, `ref`/`out`/`params`, generic methods.

## Anti-goals

- A parameter silently dropped or a type silently changed — the failure this round exists to fix.
- Breaking the commas of a `create class` member list: a method's parameter list is inside
  parentheses in the C# form, so the list splitter must not split inside them.

## Approaches

1. **Extend the existing parsers (chosen).** Read the parameter text in `ParseMethod` and in
   `AddMethodRe`, carry it on `MethodDefinition.Parameters` / `ParsedCommand.Parameters`, and have
   `ClassMethodService` write it. Smallest change; the model and generator already carry parameters.
2. A new parameter grammar shared with the future constructor command. Better reuse later, but it is
   a parser rewrite ahead of the one-registry refactor (group 4) and adds risk now.
3. Accept only the C# form. Simple, but users wrote the English form first in the probe.

## Left to the builder

The exact regexes, where the shared parameter-list parser lives (one helper used by both paths),
and the wording of refusals, following the existing messages.
