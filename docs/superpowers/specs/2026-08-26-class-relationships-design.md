# Relationships between generated classes — design

**Date:** 2026-08-26
**Status:** approved, not yet implemented
**Branch:** `class-relationships`

## Problem

A relationship between two classes in this tool is a convention, not a declaration.
`AppScaffolding/ForeignKeys.cs:23` defines it: an `int` property named `<Target>Id`, **plus** a
sibling navigation property typed as `<Target>`. Both halves are required, and everything downstream
reads that pair — seeding order and runtime foreign-key assignment, the generated per-entity
validators, the console picker (`ConsoleMenuTemplate.TryPickForeignKey`), the master/transaction
screen split, and the lines grid in the WPF and WinForms layouts.

`build app` writes that pair for you: `DomainModelMapper.AddNavigationProperties`
(`AppScaffolding/DomainModelMapper.cs:187`) turns each mined relationship into a forward reference on
the foreign-key owner and an inverse `List<Owner>` on the target, both attributed `[JsonIgnore]` and
`[Browsable(false)]`.

Nothing writes that pair for classes you build by hand. The class vocabulary has
`<Class> inherits <Base>` (`CommandDictionary.cs:193`) for inheritance and nothing at all for
associations. `IntentType` has no relationship intent. The only route today is three separate
commands, and it requires the user to already know the convention:

```
add property CustomerId int to Order
add property Customer Customer to Order
add property Orders List<Order> to Customer
```

And it cannot be done while creating the class. `create class Order with properties CustomerId int,
Customer Customer` **silently drops** the navigation property: the extractor's primary regex matches
only `string|int|long|bool|decimal|double|float|datetime|guid` and returns as soon as one of them
matches, so non-primitive entries never reach the fallback parser
(`NaturalLanguageBridge/CommandEntityExtractor.cs:744`).

So relationships are generated but not declarable, and a hand-built project silently produces a model
that the seeder, the validators and the screen split cannot see.

## Goal

The tool sets the relationships itself. It reads the classes, works out which relationships are
missing, proposes them with the evidence that produced each one, and asks the user to confirm,
correct or reject. The user never has to know the convention, and never has to remember a command.

## Scope

**In:** a detector that reads the project's classes and proposes missing relationships; a one-line
offer, once per session, on the surfaces that can ask; a proposal flow answerable per relationship; an
emitter that shares `build app`'s definition of a relationship; a way for the user to add a
relationship the tool did not propose, including many-to-many with a generated join class;
per-relationship compile and rollback; a choice question on the chat page.

**Deliberately not in scope:**

- **Changing `create class`.** It creates the class and returns, exactly as today. No question, no
  loop. Every existing script, test and habit around it is untouched. This reverses an earlier draft
  of this design and it is deliberate.
- **Touching `build app` or the screen classifier.** The join class this flow can generate carries two
  foreign keys and no date of its own, so the existing classifier reads it as a master and would give
  it a screen of its own. That is a known consequence, recorded below, not a thing this branch fixes.
- **Weak name guesses.** `CustomerRef`, `CustomerCode` and similar suffixes are not proposed. They
  invent schema on a hunch.
- **Emitting the dropped inline property.** `create class ... Customer Customer` will stop being
  silent, but will not start emitting the property. See "The adjacent defect".

## What gets written

One definition, reused rather than restated, so the hand-built path and the `build app` path cannot
drift. For `Order` referencing `Customer`:

| Property | Type | Attributes | Usings needed |
|---|---|---|---|
| `Order.CustomerId` | `int` | none | none |
| `Order.Customer` | `Customer` | `[JsonIgnore]`, `[Browsable(false)]` | `System.Text.Json.Serialization`, `System.ComponentModel` |
| `Customer.Orders` | `List<Order>` | `[JsonIgnore]`, `[Browsable(false)]` | same |

The foreign key must be exactly `int`. `ForeignKeys.TargetsOf` skips any property where
`p.Type != "int"`, so an `int?` foreign key would be invisible to seeding order, the generated
validators, the console picker and the screen split — related on paper, unrelated in every behaviour.
Unset stays `0`, which is what the generated validators already treat as "not set".

`AddNavigation` (`DomainModelMapper.cs:216`) already refuses a name that collides with the class's own
name (CS0542) or with a property already there. The shared emitter keeps both rules.

## Trigger: the tool offers, once it notices

`create class` stays silent. The detector runs after any **other** command completes, while a
per-session "already offered" flag is unset. When it finds candidates it says one line and asks one
yes/no:

```
> create class Customer with properties Name string, City string
Class 'Customer' created.

> create class Order with properties Reference string, CustomerId int
Class 'Order' created.

> list classes
  Customer
  Order

Order.CustomerId points at Customer but nothing links them.
Set relationships now? (y/N):
```

The flag is not a single boolean, and this matters. Suppressing all further offers for the session
would mean that a user who declines once, or who accepts and skips a proposal, is never asked about
classes they create afterwards — which undercuts the whole feature in exactly the session where it is
most useful. So what is remembered is **the candidates already put to the user**, declined or skipped.
A candidate that has appeared since re-arms the offer; one that was already refused never comes back.

That state is session-only — the REPL process, or the `ChatSession` instance. Nothing is written to
`.codegen`, so a restart offers everything outstanding again.

A piped run has no question port (see below), so it never offers and never asks. That is the mechanism
by which every existing scripted test stays green without being edited.

## Detection

The detector reads each class's properties with `EntityExtractor.ExtractProperties` — the same reader
`ProjectTree` uses (`CodeGenerator/Web/Chat/ProjectTree.cs:250`) — and matches three signals. It
writes nothing; it returns proposals.

| Signal | Certainty | Proposal |
|---|---|---|
| `Order.CustomerId` is `int`, class `Customer` exists, no navigation property typed `Customer` | certain: direction and cardinality are already stated by the foreign key | add `Order.Customer` and `Customer.Orders` |
| `Order.Customer` is typed as an existing class, no `Order.CustomerId` beside it | certain: the mirror image | add `Order.CustomerId` and `Customer.Orders` |
| Class `OrderLine` begins with the name of an existing class `Order` | confident, but invents the foreign key | add the whole triple, one-to-many |

Each proposal carries the evidence sentence it came from, because the user is being asked to approve a
schema change and "because `Order.CustomerId` is an int and `Customer` exists" is the difference
between a decision and a guess.

The detector is pure: classes in, proposals out. No console, no file writes, fully unit-testable.

## The proposal flow

Proposals are answered one at a time — accept, change the cardinality, or skip. After the last one,
the flow asks once whether the user wants to relate anything it did not spot; that branch picks the
two classes and the cardinality itself.

The tool can never propose a many-to-many. A join class it could detect is already covered by row one
of the detection table, which finishes its navigation properties without knowing it is a join. So
many-to-many exists only on the "add one I did not propose" branch.

## Cardinalities

**One-to-many.** The foreign key goes on the many side, so the cardinality answer settles the
direction and there is no separate question about which class carries it.

**One-to-one.** Foreign key on one side; the inverse is a single reference rather than a collection.
Needs nothing new downstream — consumers read the foreign-key side — and correctly grows no lines
grid.

**Many-to-many.** A join class, named `<A><B>` by default with a chance to rename it, carrying two
foreign keys and two forward references; both parents get `List<Join>`:

```
New class OrderProduct
  OrderProduct.OrderId     int
  OrderProduct.Order       Order        [JsonIgnore] [Browsable(false)]
  OrderProduct.ProductId   int
  OrderProduct.Product     Product      [JsonIgnore] [Browsable(false)]

Order.OrderProducts    List<OrderProduct>   [JsonIgnore] [Browsable(false)]
Product.OrderProducts  List<OrderProduct>   [JsonIgnore] [Browsable(false)]
```

The consequence, stated so it is not a surprise later: that join class has two foreign keys and no
date of its own, so if the project is later built as an app the existing classifier reads it as a
master and gives it a screen of its own. `docs/commands.md:35` documents the rule. This branch does
not change it.

## Asking the question on three surfaces

A question port passed into the flow, on the same seam `confirmAi` already uses
(`CodeGenerator/App/CodeGeneratorStarter.cs:298`).

**Console REPL** — `Menu.Choose` (`CodeGenerator/App/Menu.cs:27`), which already picks an arrow-key
list on a real console and a numbered list read a line at a time when input or output is redirected.

**Chat page** — the new plumbing. `AiQuestion` is `record AiQuestion(string Text)`
(`Web/Chat/ChatTurn.cs:43`) and `Ask` returns `bool` (`Web/Chat/ChatSession.cs:126`). Both need to
grow: a question with options, and an answer that is a chosen index.

The constraint that shapes this: `ChatSession.Declined` (`Web/Chat/ChatSession.cs:329`) turns away
`build app`, `batch` and `demo` for one stated reason — *"a buffered question is a question nobody can
see."* The chat captures a command's console output and renders it only once the command finishes. So
every question in this flow must travel out-of-band through `Ask`, and the question object must carry
its own proposal text and evidence. Anything written with `Console.WriteLine` mid-flight arrives after
the answer was needed.

That is also why this flow can live in the chat when those three commands cannot: they print their
prompts, this one does not. `ChatSession` already runs one command at a time with the composer
disabled in every tab, and `Ask` already blocks the worker thread and is already released by `End` if
a tab closes mid-question — so a sequence of questions is mechanically the same as one, repeated.

**Piped** — a null port. Offers nothing, asks nothing, writes nothing.

**Not** `Console.ReadLine` from inside the engine. The engine is shared with the web host, where there
is no console to read and a blocking read hangs the page.

## Writing into the files

`ClassPropertyService.AddProperty` (`CodeGenerationEngine/Core/ClassPropertyService.cs:12`) inserts a
bare `public {type} {name} { get; set; }` with no attributes and no usings, so it cannot write a
navigation property. It needs a sibling that adds an attributed property and ensures the file's
usings. `AddProperty` itself is left alone so nothing existing shifts.

## Compile and rollback

This is the hazard worth naming. `GenerateAndSave`
(`CodeGenerationEngine/Service/CommandHandlingService.cs:529`) sends a new file to
`SemanticCompilerRunner.RunSemanticAndCompile`, which calls `Compiler.Compile(code)` — one file,
alone. A property typed as another class is a guaranteed CS0246 there. The tool has form on exactly
this: a per-class check that reports success while the project it belongs to does not build.

So each accepted relationship is verified with `Compiler.CompileSources`
(`CodeGenerationEngine/Core/Compiler.cs:64`) over the files it touched plus the classes they
reference, and reverted on failure. Per relationship, not per pass: one bad proposal must not cost the
user the good ones.

A half-written relationship — a foreign key with no navigation property, or the reverse — is invisible
to every consumer of the convention and worse than none at all. Rollback is therefore part of the
feature, not error handling bolted on afterwards.

## The adjacent defect

`create class Order with properties CustomerId int, Customer Customer` drops `Customer Customer` with
no message (`CommandEntityExtractor.cs:744`). With this feature the drop is self-healing — the
detector sees the orphan `CustomerId` afterwards and offers to finish it — so the fix is to stop being
silent and to say where the entry will be handled instead. The property is still not emitted: emitting
it would trade a silent drop for a guaranteed CS0246 from the isolated compile above.

## Where the code lives

Detection and emission go in the engine: pure, no console, unit-testable, and the emitter shares the
convention with `DomainModelMapper` so the two cannot drift. The offer and the questions go in the app
layer, one component driven by both the REPL and `ChatSession` — the same split the repo already makes
between `SelectionState` (every decision) and `SelectionPrompt` (talking to a real console).

## Testing

- The detector is pure: classes in, proposals out. Table-driven over the three signals, including the
  cases that must **not** propose — a foreign key that is already complete, an `int?`, a `<Target>Id`
  whose target class does not exist, a name collision that `AddNavigation` refuses.
- The emitter's output is compiled with `Compiler.CompileSources`, per the standing rule in this repo
  that emitted C# is proved by compiling it rather than by substring assertions.
- Rollback is tested by making a relationship that cannot compile and asserting both files come back
  byte for byte.
- The console flow is drivable: `Menu.ChooseNumbered` takes a `TextReader` and a `TextWriter`.
- The chat choice question is drivable with bUnit.
- Existing piped tests must stay green **without being edited**. That is the proof the null port
  works, and a stronger check than any new test of it.
- Two things no test can see: the real console prompt and the real chat page. Both get verified by
  running them, not by asserting about them.

## Risks

- **The chat question is new plumbing on a page that renders in a browser.** The record in this repo
  is that defects on this surface compile clean, pass every unit test, and show up only in a running
  page. It gets run before it is called done.
- **The detector runs after most commands.** It reads every `.cs` file under the workspace each time,
  until it has offered once. Fine for a handful of classes; worth measuring rather than assuming on a
  large workspace.
- **A confident proposal is still a proposal.** The "`OrderLine` begins with `Order`" rule invents a
  foreign key. It is shown with its reason and needs an explicit accept.
