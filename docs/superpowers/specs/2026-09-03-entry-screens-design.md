# Master and transaction entry screens

Date: 2026-09-03
Branch: `entry-screens`

## The problem

Two mockups were supplied — `Sample Master Screen.png` (Customer Entry) and
`Sample Transaction Entry Screen.png` (Purchase Order Entry). Both show a screen shape the
business layout does not emit today.

What the business shell emits now, for both desktop targets:

- A **master** is a list screen — a title, a search box, New/Open/Delete, and an
  auto-columned read-only `DataGrid`. Opening a row replaces the whole content area with a
  separate **editor** screen: a flat reflected form plus Save/Cancel.
- A **transaction** is the same list with a date range and column totals, opening the same flat
  editor with its line grids underneath.

The mockups differ in structure, not only in styling:

1. The form and the list are on **one** surface for a master. The list is a tab beneath the
   form, not a screen in front of it.
2. Fields are **grouped into titled panels** — Basic / Address / Contact / Other Information,
   Supplier Information / Order Details / Delivery Information — laid out in three columns.
   Today's form is one flat two-column reflection of every property in declaration order.
3. There is a **header band**: an icon tile, a title and subtitle, and a right-hand strip of key
   fields (code, date, status) held out of the panels.
4. Required fields carry a **red asterisk**. Nothing on screen indicates requiredness today.
5. A transaction's lines sit in a **tab strip** with a totals panel beside them, not in a bare
   `ItemsControl` under the form.
6. There is a bottom **action bar** and a **status bar**.

## Decisions taken before this spec was written

Recorded because each one closed off work that would otherwise look in scope.

| Question | Decision |
|---|---|
| Which targets | **WPF and WinForms.** Blazor is out of scope on this branch even though it also builds screens from the role split. |
| How fields are grouped | **A fixed name-based rule set.** Not an LLM call (non-deterministic, needs Ollama, costs wall-clock on every build), not equal-sized columns (meaningless headings), not per-build overrides (deferred, not rejected). |
| Buttons with no behaviour — View, Print, Export, Save & Print, Import Items | **Omitted.** A button that does nothing is output that misinforms. Export-to-CSV and Print were offered and declined for this branch. |
| How it ships | **Replaces what the `business` layout emits.** No new layout key. `business` is already the default, so every new WPF/WinForms build gets these screens with nothing extra on the command line. The old list-then-editor shape does not survive under another key. |
| How an existing transaction is reached | **Through today's transaction list screen**, unchanged — its date range and column totals are real and are kept. Masters become one combined screen; transactions stay two. |

## What is NOT changing

- The four pre-business layouts: `tabs`, `sidebar`, `master-detail`, `dashboard`.
  `WpfLayoutNeutralityTests` and `WinFormsLayoutNeutralityTests` pin them byte for byte and
  must stay green untouched.
- The Blazor target's screens.
- `EntityRole`, `EntityRoles.Classify`, `ScreenPlan`, `ScreenOverrides` — the master /
  transaction / line split itself is not in question.
- The transaction **list** screen: `TransactionScreenViewModel<T>`, its `From`/`To` range, its
  `DateField` derivation and its `Totals`.
- The store, repository, validation and relationship seams. This is a view-and-view-model
  change; nothing about persistence moves.
- `PropertyEditor.cs`, which the `master-detail` layout also emits. The business layout stops
  naming it; master-detail keeps it exactly as it is.

## Design

### 1. `Screens/FieldGroups.cs` — new emitted file

The grouping rules live **once**, in the generated application, reflecting over a `Type` at
runtime. Both desktop targets consume the same file. This deliberately avoids a second copy in
the generator: `PropertyEditorTemplate` already documents an `IsDisplayable` convention that
exists in three emitted files and has to be hand-kept in sync, and that is a trap worth not
walking into again. Nothing in the generator needs to know the groups at build time, so one
copy is achievable here.

```csharp
public sealed record FieldGroup(string Title, IReadOnlyList<PropertyInfo> Fields);

/// <summary>The code, date and status fields the header band shows, any of which may be null.</summary>
public sealed record KeyFields(PropertyInfo? Code, PropertyInfo? Date, PropertyInfo? Status);

public static class FieldGroups
{
    /// <summary>The titled panels, in display order. Never contains an empty group.</summary>
    public static IReadOnlyList<FieldGroup> For(Type type);

    /// <summary>The long-text fields, which the view shows as a tab rather than a panel.</summary>
    public static IReadOnlyList<PropertyInfo> Additional(Type type);

    /// <summary>The panels packed into the three columns the mockups use.</summary>
    public static IReadOnlyList<IReadOnlyList<FieldGroup>> Columns(Type type);

    /// <summary>What the header band shows. Excluded from every panel above.</summary>
    public static KeyFields Key(Type type);
}
```

**The header's fields are held out of the panels.** `Key` is resolved first and its three
properties are removed from the pool the buckets see:

- `Code` — the first `string` property whose name ends `Code`, `No` or `Number`; none if the
  model has no such property, in which case the header shows the record's `Id` as `(Auto)`.
- `Date` — for a transaction, the `DateField` the view model already derives (the first
  `DateTime` that is not `CreatedOn`/`ModifiedOn`); none for a master.
- `Status` — a property named exactly `Status` or `State`.

Without this the same field is drawn twice on one screen: `Status` matches the Other
Information bucket and `Date` matches it by type, so both would appear in the header strip *and*
in a panel. The mockups do show Customer Code and Status in both places; a generated form
repeating an input is a defect rather than a style, so this departs from them deliberately.

**Bucket rules.** Each displayable property is tested against the buckets in this order and
lands in the first that matches, case-insensitively, on a substring of the property name:

| Bucket | Name contains |
|---|---|
| Additional Information (tab) | `remark`, `note`, `terms`, `condition`, `description` |
| Address Information | `address`, `street`, `city`, `state`, `province`, `district`, `postal`, `pincode`, `pin`, `zip`, `country`, `region` |
| Contact Information | `contact`, `phone`, `mobile`, `telephone`, `fax`, `email`, `website`, `url`, `designation` |
| Other Information | `status`, `active`, `since`, or the property type is `DateTime`/`DateTime?` |
| Basic Information | everything else |

Ordering matters and is the reason the table is ordered: `ContactAddress` is an address, and
`ContactPerson` is a contact, so `address` must be tested before `contact`. `pincode` before
`pin` for the same reason.

**Displayable** follows the existing convention: public, readable, writable, not `Id`, not a
navigation property, not `[Browsable(false)]`.

**Foreign keys are kept, and rendered as lookups on the entry FORM.** `EntryForm` turns an
`int <Nav>Id` whose sibling `<Nav>` is an entity type into a combo box over that entity's rows, so
`SupplierId` becomes a **Supplier** picker — which is what the transaction mockup's Supplier Code
lookup is. Dropping foreign keys from the panels would leave no way to set a purchase order's
supplier at all. The display property is chosen by `FieldGroups.LookupLabel`: the code field, then
Name, then Title, then any string, and only then `Id`.

> **Correction, made while checking a running app.** This section previously said an `int <Nav>Id`
> was "already turned into a picker by the shared `OnAutoGeneratingColumn` rule that
> `MainWindowTemplate` emits". That is not true of this layout and never was. The shared rule ends
> in `if (DataContext is not MainViewModel main) return false;`, and the business shell binds a
> `ShellViewModel` — so the rule bails out for **every grid in the business layout**, and the
> WinForms business layout has no foreign-key column rule at all. In a generated app the master
> list, the transaction list and the lines grid therefore show `SupplierId` and `ProductId` as
> plain integer columns. Left as it stands by decision: the number shown is the real one, so this
> is a picker that is absent rather than output that misinforms. See "Known gaps" below.

The bucket a foreign key falls into is decided on the **nav name**, not the `*Id` name, so
`ShipToLocationId` is labelled `ShipToLocation` and lands in Address Information.

Only `string` properties are eligible for the Additional Information bucket, so a
`TermsAccepted` bool stays a checkbox in a panel rather than becoming a multi-line box in a tab.

**Emptiness.** A group with no fields is not emitted at all, following the rule
`DashboardViewModel.Group` already states: a heading with nothing under it is noise. A model
with no long-text fields therefore gets no Additional Information tab.

**Overflow.** If Basic Information holds more than 12 fields it splits, the overflow taking the
title "More Information". Twelve is the point at which a panel stops fitting the mock's panel
height at the mock's row spacing; it is a layout constant, not a semantic claim.

**Columns.** `Columns` packs the ordered groups into three lists, assigning each group to
whichever column currently holds the fewest fields, ties going left. For the four-group case in
the master mockup this reproduces the mockup: Basic alone, Address alone, Contact and Other
stacked. A model producing one group gets one populated column and two empty ones, and the view
collapses empty columns so a small model is not two-thirds whitespace.

### 2. `Screens/RequiredFields.cs` — new emitted file

Requiredness lives in `PropertyDefinition.Rules` at generation time and is compiled into the
generated validator's body. It is not on the model, so a running application cannot see it by
reflection — which is why the asterisk needs its own emitted map.

```csharp
public static class RequiredFields
{
    public static bool IsRequired(Type type, string property);
}
```

Backed by a `Dictionary<string, string[]>` written at generation time from the same
`PropertyDefinition.Rules` scan `ValidatorTemplate` performs, so the asterisk and the validator
cannot disagree.

**Always emitted**, with an empty map when the build has no validation. The alternative —
emitting the file conditionally — would force every consumer into the conditional-fragment
bargain `RecordEditorTemplate` and `ViewModelTemplates.EntityViewModel` already have to strike,
in two more places, for no gain.

Rejected alternative: putting `[Required]` on the generated model properties. Cleaner to
consume, but it changes every generated model file, and the model output is depended on
byte-for-byte in more places than this branch should disturb.

### 3. `MasterScreenViewModel<T>` — an inline editor

The master screen holds its own editor rather than asking the shell for one.

```csharp
public RecordEditorViewModel<T> Editor { get; private set; }   // never null
public ICommand NewCommand    { get; }   // blank draft
public ICommand EditCommand   { get; }   // load Selected into a fresh editor
public ICommand ClearCommand  { get; }   // blank draft, keeps the selection
public ICommand CancelCommand { get; }   // blank draft, clears the selection
public ICommand SaveCommand   { get; }   // Editor.Save, then Reload
public ICommand DeleteCommand { get; }   // unchanged
```

- `Editor` starts on a blank draft, so the form is usable the moment the screen opens.
- Setting `Selected` to a `T` loads it into a **fresh** `RecordEditorViewModel<T>` and raises
  `PropertyChanged` for `Editor`. A fresh instance rather than a mutated one, for the reason
  `RecordEditorViewModel` already documents: the draft is a clone, and reusing the instance
  would leave the previous record's `_openedAs` snapshot in place and make `IsDirty` lie.
- `SaveCommand` no longer means "reload from the store". It saves the editor and then reloads,
  which is what a user pressing Save on this screen means.
- The screen **stops raising `EditorRequested`**. `ShellViewModel.Route` looks the event up by
  reflection and returns early when a screen does not have it — "a pane with no such event … is
  simply left alone" — so the shell needs no change, and the transaction path that still raises
  it is untouched.

`OpenCommand` is removed. Nothing will call it: opening a record is now selecting a row.

### 4. `IDirtyScreen` — a new emitted interface

The shell's discard prompt tests `_current is IRecordEditor { IsDirty: true }` in three places
(`TryShow`, `SignOut`, and the WPF/WinForms confirm hooks). A master screen with a dirty inline
editor is not an `IRecordEditor` and would slip past all three.

```csharp
public interface IDirtyScreen { bool IsDirty { get; } }
public interface IRecordEditor : IDirtyScreen { /* Title, Lines as today */ }
```

`MasterScreenViewModel<T>` implements `IDirtyScreen`, delegating to `Editor.IsDirty`. The shell
tests `IDirtyScreen`.

Making `MasterScreenViewModel<T>` implement `IRecordEditor` instead would compile and would
pass the dirty check — and would silently break rendering, because `ScreenTemplateSelector`
tests `IRecordEditor` **before** `IEntityViewModel`, so every master screen in the application
would draw as the editor template. The separate interface is what avoids that.

### 5. `RecordEditorViewModel<T>` — totals and Clear All

For the transaction entry screen:

```csharp
public sealed record LineTotal(string Column, decimal Value, bool IsGrand);
public IReadOnlyList<LineTotal> Totals { get; }   // over Lines, recomputed on change
```

One row per money-shaped column of the **line** type — every `decimal`/`double` that is neither
`Id` nor `*Id` — summed across that set's lines. This is the same rule
`TransactionScreenViewModel.MoneyColumns` already applies, and the same reasoning is written
into it: the generator cannot know which column is Sub Total and which is Freight, and one
confidently wrong number is worse than several honest ones.

The mockup's Sub Total / Discount / Tax Amount / Freight / Grand Total rows are therefore
**not** emitted. `IsGrand` is true only when a line set has exactly one money column, in which
case that single row takes the emphasised treatment.

`ILineSet` gains `void Clear()`, removing every line without touching the store — the mockup's
Clear All. `Add` and `Remove` already exist.

### 6. WPF — `WpfBusinessLayout`

`MasterTemplate` and `EditorTemplate` are rewritten; `DashboardTemplate` and
`TransactionTemplate` (the transaction *list*) keep their current shape and gain only the
palette treatment.

The layout stops emitting `PropertyEditor.cs` and emits instead:

- `Views/EntryForm.cs` — a `Grid`-derived control taking a `Record` dependency property, the
  same pattern and for the same documented reason as `PropertyEditor` (a `ContentControl`
  cannot work: its single Content slot would hold the record instead of the generated form). It
  builds three columns of panels from `FieldGroups.Columns`, each panel a bordered box with a
  title in the accent colour, each row a label — asterisked via `RequiredFields.IsRequired` —
  and an editor chosen by property type: `CheckBox` for `bool`, `DatePicker` for `DateTime`,
  `ComboBox` for an enum, a **lookup `ComboBox` over the referenced entity** for a `<Nav>Id`
  foreign key, `TextBox` otherwise.
- `Views/EntryHeader.cs` — the accent band: icon tile, title, subtitle, and the key strip.

Both templates then compose: header band, `EntryForm`, tab strip, action bar, status bar. The
master's tab strip holds the record list (with its search box and a record count) and, when the
model has long-text fields, Additional Information. The transaction's holds one tab per line set
plus Additional Information, with the totals panel beside the grid.

No per-entity XAML: the file stays the same length for a model of two entities and of twenty,
which is the property the current file's remarks call out and which this must not give up.

### 7. WinForms — `WinFormsBusinessLayout`

The same shape through the same seam. `BuildList` and `BuildEditor` are rewritten;
`BuildDashboard` keeps its shape. The screen is still built by reflection over the view model
rather than typed against it, for the reason already documented there: every screen is
`MasterScreenViewModel<T>` or similar and a form cannot name an open generic.

`BuildHeader` (the shell header) is untouched; the entry-screen band is a separate control built
by a new `BuildEntryHeader`. Panels are `GroupBox`-free — a bordered `Panel` with a coloured
title `Label`, so the heading can take the accent colour, which a `GroupBox` caption cannot.

`WinFormsPaneCode` is used by the pre-business layouts and is not touched.

### 8. Palette

The header band and the panel titles use `Palette.Accent`, whose default is already `#2563EB` —
the blue in both mockups. Panel bodies use `Palette.Surface`, borders `Palette.GridLine`, text
`Palette.Foreground`.

An unthemed build falls back to the neutrals the sidebar layout uses, plus `#2563EB` for the
accent, which is what `An_unthemed_shell_uses_the_same_neutrals_the_sidebar_uses` already pins
for the shell.

## Files touched

| File | Change |
|---|---|
| `Screens/FieldGroupsTemplate.cs` | new — emits `Screens/FieldGroups.cs` |
| `Screens/RequiredFieldsTemplate.cs` | new — emits `Screens/RequiredFields.cs` |
| `Screens/MasterScreenTemplate.cs` | inline `Editor`, new commands, `IDirtyScreen`, no `EditorRequested` |
| `Screens/RecordEditorTemplate.cs` | `IDirtyScreen`, `LineTotal`, `Totals` |
| `Screens/LineSetTemplate.cs` | `ILineSet.Clear()` |
| `Screens/ShellTemplate.cs` | the discard check tests `IDirtyScreen` |
| `Layouts/Wpf/WpfBusinessLayout.cs` | master + editor templates, `EntryForm.cs`, `EntryHeader.cs` |
| `Layouts/WinForms/WinFormsBusinessLayout.cs` | `BuildList`, `BuildEditor`, `BuildEntryHeader` |
| `Targets/DesktopViewModelFiles.cs` | registers the two new emitted files |

`Screens/ScreenTemplates.cs` is expected to need **no** change: `ScreenTemplateSelector` keeps
testing `IRecordEditor` before `IEntityViewModel`, and a master screen is deliberately not an
`IRecordEditor` (section 4). If it turns out to need one, that is a signal the interface split
went wrong.

## Testing

**Emitted code is compiled, not substring-matched.** `FieldGroups`, `RequiredFields`,
`IDirtyScreen`, the two view models and both layouts' new controls are compiled with
`Compiler.Compile` and exercised by reflection. Substring assertions pass on code that does not
work, and the rule that generated C# must be Roslyn-compile-tested exists here because CS0023
and literal-injection defects have shipped green past `Contains` before.

Every new assertion is seen red before it is seen green.

New coverage:

- `FieldGroups`: each bucket; ordering (`ContactAddress` → Address, `ContactPerson` → Contact,
  `PinCode` → Address); empty groups dropped; the 12-field split; three-column packing for
  one, two, four and six groups; a model with no long-text fields yielding no Additional tab;
  only strings reaching the Additional bucket.
- `FieldGroups.Key`: each of the three resolved; each absent when the model has no such
  property; and — the assertion that matters — **a `Status` property and the transaction date
  appear in the header and in no panel**, which is the duplication this exists to prevent.
- Foreign keys: `SupplierId` is kept, labelled `Supplier`, and rendered as a lookup, not a
  text box and not dropped; `ShipToLocationId` buckets into Address Information on the nav name.
- `RequiredFields`: the map matches what `ValidatorTemplate` emits for the same class; an empty
  map compiles and answers false when the build has no validation.
- `MasterScreenViewModel`: selecting a row loads a fresh editor; saving writes and reloads;
  Clear blanks the draft; `IsDirty` follows the inline editor; **`EditorRequested` is no longer
  raised**, and `ShellViewModel.Route` tolerates its absence.
- `RecordEditorViewModel.Totals`: one row per money column; `Id`/`*Id` excluded; `IsGrand` only
  on a single-money-column set.
- `ILineSet.Clear`: empties the set, marks it dirty, writes nothing until save.
- Both layouts: the header band, three columns, tab strip, action bar and status bar are
  present; no entity name appears in either file; the emitted controls compile.

Rewritten: the ~64 existing tests across `WpfBusinessLayoutTests`, `WinFormsBusinessLayoutTests`,
`BusinessViewModelTests`, `MasterScreenTests` and `TransactionScreenTests`.

Untouched and expected green: `WpfLayoutNeutralityTests`, `WinFormsLayoutNeutralityTests`, and
every `master-detail` / `sidebar` / `tabs` / `dashboard` test.

**Verification by running.** A WPF app and a WinForms app are generated, launched and
screenshotted, against a model with enough properties to fill the panels. Every previous round
in this repository has produced defects that compiled clean with a green suite and appeared only
on a running screen — a missing render mode, a blank overlay, a caption escaping bug, an
alphabetical field order. Passing tests are not the completion criterion here.

## Deliberate departures from the mockups

Stated so they are choices on the record rather than things that were missed.

1. **No View / Print / Export / Save & Print / Import Items buttons.** Decided above.
2. **No Sub Total / Discount / Tax Amount / Freight / Grand Total rows.** The generator cannot
   know which column is which; it emits one honest total per money column instead.
3. **Remarks moves out of the Other Information panel** into the Additional Information tab,
   with Terms and Conditions. This is what makes that tab — present in both mockups — carry real
   content instead of being decorative, and it is what gives the transaction's Terms box a home
   without printing the same field twice.
4. **A transaction still has a list screen in front of its entry screen.** The mockup shows only
   the entry screen; the existing list's date range and column totals are real and are kept.
5. **The freight input, the tax percentage column and the UOM column are not emitted.** They are
   properties of the mockup's specific model, not of every model.
6. **A header field is not repeated in a panel.** Both mockups show Customer Code and Status in
   the header strip *and* again in a panel below. Two live inputs bound to one property is a
   defect in generated output, so the header wins and the panel drops the field.

7. **On the WPF transaction entry screen, Additional Information is a titled section under the
   lines, not a tab beside them.** The lines tab strip is driven by `ItemsSource="{Binding Lines}"`,
   and mixing a static `TabItem` into it needs a `CompositeCollection`, whose `CollectionContainer`
   is not in the visual tree — inside a `DataTemplate` it has no `DataContext`, binds nothing, and
   renders an EMPTY tab strip. That parses cleanly and no test would catch it. The master screen
   keeps a real tab, because both of its tabs are static, and Windows Forms keeps a real `TabPage`
   on both screens, because it adds pages in code either way.

## Known gaps

Found by running the generated apps, and left in place deliberately rather than missed.

- **Foreign keys in GRIDS are integer columns, not pickers** — the master list, the transaction
  list and the lines grid all show `SupplierId` / `ProductId` as numbers. The cause is above: the
  shared WPF rule cannot fire under a `ShellViewModel`, and WinForms has no such rule. The entry
  FORM's pickers are unaffected and work on both targets. Not fixed here because the value shown is
  correct — the picker is absent, not wrong.
- **The Windows Forms entry screen wants more than the form's own default 1000x640.** On a model
  with several line sets the content is wider and taller than the default window, so the screen
  opens with scrollbars and the header's Status can sit off the right edge until the window is
  maximised. Nothing is unreachable.

## Deferred, not rejected

- Per-build group overrides ("put Currency in Other Information"), the way `ScreenOverrides`
  already lets masters and transactions be reassigned.
- Export to CSV and Print.
- The Blazor target.
