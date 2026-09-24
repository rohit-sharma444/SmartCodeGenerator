# Entry screen templates: multiple layouts for master and transaction screens

Date: 2026-09-22

## Problem

Every generated Business-layout screen — master or transaction, WPF, Windows
Forms or Blazor — renders its entry form exactly one way: fields grouped into
titled panels, packed three columns wide. That is one credible shape for a
business form among several; a generated application cannot currently look
any other way, and the person running `build app` has no lever for it.

## Scope

In scope:

| Area | Delivered |
| --- | --- |
| Choice axis | `EntryTemplate` enum + catalog, mirroring `AppLayout`/`AppLayoutCatalog` exactly |
| Storage | Nullable field on `AppCustomization`, defaulting to today's rendering |
| Wiring | Console wizard, inline token, review menu, web wizard, help catalogue/manual |
| Templates | Six shapes — Classic, Grouped, TwoColumn, Tabbed, Split, Ribbon — one shared choice applied to both master and transaction screens |
| Targets | WPF, Windows Forms, Blazor Server — delivered in that order, one phase each |

Explicitly out of scope:

- **The four pre-Business layouts** (`tabs`, `sidebar`, `master-detail`,
  `dashboard`). They keep the flat generic form they emit today, untouched.
- **Console, Web API, Worker, Class Library targets.** No screens to shape.
- **Per-role template choice.** One template governs both master and
  transaction screens; see Approach for why.
- **Runtime template switching inside a generated app.** The template is a
  `build app` decision; the generated app ships with the one shape chosen.
- **Any change to `EntityRole` classification, `ILineSet`, or the
  authentication/shell mechanics.** All reused as they stand.

## Approach

**A. Generation-time specialization (chosen).** The chosen template selects
which of six source-emission branches each target's Business layout uses
when building the entry form — the same way `AppLayout` already selects an
entire `IAppLayout` implementation at generation time, not a runtime switch
inside the generated app. A generated application ships with exactly one
baked-in field arrangement: the emitted `EntryForm`/panel-builder/
`RecordEditor` source is written differently depending on which template was
chosen, not parameterized by a value the emitted app reads at run time.

**B. Runtime-switchable templates inside the generated app.** Rejected: no
such requirement was named, it emits all six templates into every generated
app whether or not the six are ever compared, and it multiplies the testing
surface for a control nobody asked the *end user* of a generated app to have.

**C. One parameterized renderer, still emitted once per app, called with an
enum argument everywhere.** Closer to A, but `FieldGroups`/`WpfEntryControls`
and their siblings are string templates the generator interpolates — baking
the chosen constant directly into the emitted call site (e.g.
`FieldGroups.Columns(Record.GetType(), 2)` versus the implicit `3`) is less
code than emitting a runtime switch that can only ever take one branch in any
given generated app.

**Why one shared choice, not one per role.** The six template names
(Classic, Grouped, Tabbed, Two Column, Split, Ribbon) describe a form's field
arrangement and chrome, not a property of masters versus transactions — a
"Tabbed" master and a "Grouped" transaction in the same application would be
an inconsistency the application's own user would notice, for no benefit
this design's requester asked for. One choice, applied uniformly, is also
the smaller data model and the smaller wizard/CLI/testing surface.

## The axis

Mirrors `AppLayout`'s mechanism exactly (`AppScaffolding/Layouts/AppLayout.cs`,
`AppLayoutCatalog.cs`, `AppCustomization.cs:35-48,79-87`):

- **`AppScaffolding/Screens/EntryTemplate.cs`** (new) —
  `enum EntryTemplate { Classic, Grouped, TwoColumn, Tabbed, Split, Ribbon }`,
  doc-commented the way `AppLayout.cs` is, each value naming what it draws.
- **`AppScaffolding/Screens/EntryTemplateCatalog.cs`** (new) — the same shape
  as `AppLayoutCatalog.cs`: `Keys` (`"classic"`, `"grouped"`, `"two-column"`,
  `"tabbed"`, `"split"`, `"ribbon"`), `Descriptions`, `PromptOptions`,
  `HelpOptions`, `KeyFor`, `TryResolve`. `two-column` is hyphenated for the
  same reason `master-detail` is (`AppLayoutCatalog.cs:17-18`): the inline
  parser tokenizes on whitespace.
- **`AppCustomization`** (`AppCustomization.cs:35-48`) gains
  `Screens.EntryTemplate? EntryTemplate`, and
  `EffectiveEntryTemplate => EntryTemplate ?? Screens.EntryTemplate.Grouped`
  beside `EffectiveLayout` (`AppCustomization.cs:87`) — nullable for the
  identical reason `Layout` is (`AppCustomization.cs:79-86`): the merge logic
  needs to tell "not mentioned" from "explicitly chose the default". The
  default is `Grouped`, not `Classic`, because `Grouped` is what
  `WpfEntryControls`/`WinFormsBusinessLayout`'s panel code already emit —
  a build that names no entry template must golden-match today's output.
- **`CustomizationWizard.cs:67-99`** gains an Entry Template prompt, asked
  only when the layout question was asked *and* the chosen/offered layout is
  `business` — narrower than `layout:`'s own gating (`target is not null &&
  AppLayouts.Supports(target.Key)`), because asking about field arrangement
  on a `tabs` build has no effect and would falsely imply one.
- **`InlineCustomization.cs:37,174-178`** gains an `entry-template` value
  key, parsed the way `layout` is.
- **`CustomizationMenus.cs:58-73,221-242`** and the web wizard
  **`LookStage.razor:17-23,91,104-105,127,156-158`** gain the equivalent
  prompt/dropdown, gated on the layout answer being (or defaulting to)
  `business`.
- **`CustomizationSummary.cs:50-51`** gains an `Entry template: <key>` line
  when the customization sets one.
- **Help catalogue / manual.** `CommandDictionaryDataTests.cs:101-106`
  asserts every `AppLayoutCatalog.Keys` string appears in the `Build App`
  help entry; the `build app` `CommandDictionary` entry gains
  `EntryTemplateCatalog.HelpOptions` the same way. `docs/commands.md` and the
  manual appendix are both generated from `CommandDictionary`, so no separate
  doc edit is needed once that entry changes.

## The six templates

1. **Grouped** (default, unchanged) — today's 3-column `FieldGroups.Columns`
   panels. No new code; every other template is written against this
   baseline.
2. **TwoColumn** — `FieldGroups.Columns` (`FieldGroupsTemplate.cs:229-249`)
   gains a `columnCount` parameter, defaulting to 3 so every existing caller
   reproduces Grouped byte for byte. The TwoColumn call sites in
   `EntryForm.Build()` (`WpfEntryControls.cs:242`) and the WinForms panel
   builder (`WinFormsBusinessLayout.cs:557`) are generated with the literal
   `2` baked in instead.
3. **Classic** — the entry form drops grouping and panels entirely: one flat
   stack of label+editor rows (`EntryForm.Row`, `WpfEntryControls.cs:294-323`)
   over `FieldGroups.Displayable(type)` minus the key fields, with no
   `Panel()`, border or heading. Reuses the existing `Row`/`Editor`
   machinery — only which method builds the form's children changes.
4. **Tabbed** — `FieldGroups.For(type)` (`FieldGroupsTemplate.cs:175-214`),
   today stacked into columns, instead becomes one tab per named group
   (Basic/More/Address/Contact/Other), sitting alongside the existing
   Additional Information tab (`WpfEntryControls.Notes`) rather than a
   separate panel column.
5. **Split** — a composition change, not a `FieldGroups` change. Today only
   the master screen combines the editor form and the record list in one
   view (`MasterTemplate`, `WpfBusinessLayout.cs:140-192`); the transaction
   screen instead swaps the whole content area for a separate editor
   (`WpfBusinessLayout.cs:194-239` list, `:244-358` editor). Split applies
   the master screen's combined form-above/list-below composition to
   transaction screens too, so both kinds of record are edited in place
   beside their list.
6. **Ribbon** — a chrome change confined to the action bar. The plain
   `Button` row (`WpfBusinessLayout.cs:148-155` master, `:205-208`
   transaction list, `:252-255` editor) becomes an icon-and-label command
   strip, and equivalently for the WinForms button panel and Blazor's
   `<div class="d-flex gap-2 mt-4">` (`BlazorScreenTemplates.cs:598-601`).
   Field arrangement underneath stays Grouped.

## Per-target notes

**WPF** is the reference implementation and lands first. `WpfBusinessLayout
.ViewFiles` (`WpfBusinessLayout.cs:30-48`) reads
`context.Customization.EffectiveEntryTemplate` and selects which
`WpfEntryControls` emission branch to call — no change to `IAppLayout` or
`LayoutContext` is needed, since `LayoutContext.Customization`
(`Layouts/LayoutContext.cs:16`) already carries the full `AppCustomization`.

**Windows Forms** mirrors WPF, reflecting the same `FieldGroups` API
(`WinFormsBusinessLayout.cs:541-,557,587`) to build the equivalent controls
in WinForms widgets — the existing pattern of one shared `FieldGroups`
emitted file behind two widget-construction front ends, unchanged by this
feature beyond the new branches.

**Blazor** currently groups nothing — `RecordEditor.razor`'s
`@foreach (var field in Fields)` (`BlazorScreenTemplates.cs:531`) is already
what Classic looks like. Grouped/TwoColumn/Tabbed need `ScreenFields` to gain
the equivalent of `FieldGroups.For`/`Columns` — a fourth copy of that
convention, deliberately: `FieldGroupsTemplate.cs`'s own remarks
(lines 12-16) already note `Displayable` is a fourth copy of it, because
`ScreenFields.Editable`'s `IsScalar` admits neither an enum nor a Guid, and
reusing it would silently drop both from every desktop form — the same trap
applies here. Split, for Blazor, means the editor renders beside the list on
one page instead of `/order/{id}` replacing `/order` — the largest of the
three targets' Split changes, since page-per-route is what currently gives
Blazor its master/detail split.

## Delivery

Three phases, in dependency order — WPF proves the mechanism before it is
repeated:

1. **Axis + WPF.** The enum, catalog, `AppCustomization` field, and all
   wizard/inline/menu/web/help wiring, defaulting to Grouped — a build that
   sets nothing must still golden-match. All six WPF template branches, with
   compile-tested golden-output coverage for each.
2. **Windows Forms.** The same six branches against the same `FieldGroups`.
3. **Blazor.** `ScreenFields` gains grouping/columns/tabs, and the six
   branches land there, including the Split routing change.

## Testing

- `EntryTemplateCatalog`/`AppCustomization.EffectiveEntryTemplate` get the
  same unit-test shape `AppLayoutCatalog`/`EffectiveLayout` already have.
- Emitted code is Roslyn-compile-tested, not substring-asserted — six
  templates across three targets across two roles is exactly the surface
  substring assertions miss.
- A build naming no entry template, on `layout: business`, must still
  golden-match today's output, mirroring the `nologin`-plus-named-layout
  regression proof in the professional-app-layouts design.
- Each template is verified per target by generating, building and launching
  a real app under a short output path — this codebase's layout and screen
  work has repeatedly shipped defects invisible to a fully green test suite
  and visible only by running the generated application.

## Open questions

None outstanding. Scope, the shared-versus-per-role choice, target breadth,
and the Business-layout-only gating were settled with the requester before
this spec was written.
