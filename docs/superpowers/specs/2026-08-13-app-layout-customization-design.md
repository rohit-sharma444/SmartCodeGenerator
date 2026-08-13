# Window layout customization for `build app` — design

**Date:** 2026-08-13
**Branch:** `app-layout`, off `main` at `896b97e`
**Follows:** `2026-08-12-build-app-customization-design.md` (PR #78) and
`2026-08-12-namespace-prefix-design.md` (PR #79). This is the next customization
item: the one thing `AppCustomization` deliberately did not reach.

## The requirement

`build app` should let the user choose the **shape** of the generated desktop
front end, not just its colours and captions. Today every WPF app gets one
layout — a `TabControl` with a `DataGrid` per entity — and every WinForms app
gets its equivalent. That single shape is currently unchangeable.

Four layouts ship, on **both** desktop targets:

| Key | Shape |
|---|---|
| `tabs` | Today's shape: a tab per entity, grid filling the tab. **The default.** |
| `sidebar` | A navigation list of entities on the left, the selected entity's grid on the right. |
| `master-detail` | Tab chrome retained; each pane splits into a grid above and an edit form for the selected row below. |
| `dashboard` | A home screen of cards, one per entity showing its row count, clicking through to that entity's grid. |

## Scope decisions

**Built-in layouts only.** The user picks from a fixed menu the tool ships and
tests. There is no user-supplied XAML override and no model-driven automatic
layout selection. Both were considered: an override file cannot be guaranteed to
compile, and model-driven selection makes the output unpredictable for a
generator whose value is that you can tell what you will get.

**Full parity across both desktop targets.** All four layouts exist for WPF and
for Windows Forms, under the same four names, so the help text needs no
per-target caveats.

This was nearly scoped to a WinForms subset, on the assumption that
`master-detail` and `dashboard` would be expensive there. That assumption was
wrong and the correction is worth recording: WinForms ships `PropertyGrid`
(bind `SelectedObject` and the detail pane is done, with no reflection code of
our own) and a dashboard is a `FlowLayoutPanel` of buttons. Both are **cheaper**
in WinForms than in WPF, where the detail pane needs an emitted reflection-driven
editor.

**Non-desktop targets ignore the setting**, as `theme` and `accent` already do on
a console build — but see "A layout named against a target that has no window"
below, because silence is not the whole answer.

## Neutrality requirement

No `layout` option, or `layout tabs`, must reproduce today's output **byte for
byte**. This is the rule `AppCustomization.Default` already lives by. If an
existing test has to change, the change is not neutral.

The mechanism that delivers it: `WpfTabsLayout` and `WinFormsTabsLayout`
reimplement nothing. They delegate to today's `MainWindowTemplate` and
`WinFormsMainFormTemplate`, whose current public methods stay exactly as they
are. The default path therefore emits the identical string it emits now, and the
existing tests that call those templates directly keep passing untouched.

## What makes this cheap: the view-model layer does not change

`DesktopViewModelFiles`, `MainViewModelTemplate`, `ViewModelTemplates` and the
emitted `IEntityViewModel` are **untouched** by this feature. Everything new
lives in the layout classes.

That is not obvious, and the first reading of it was wrong. It was assumed that
dashboard needs a `Count` member on `IEntityViewModel` and a navigation notion on
`MainViewModel` — a change to the *shared* files, which would have put WinForms
parity at risk. Neither is needed:

- **Counts need no interface change.** `Items` is declared as `IEnumerable` but
  returns `Records`, an `ObservableCollection<T>`. WPF bindings reflect over the
  *runtime* type, and `ObservableCollection<T>` raises
  `PropertyChanged("Count")` on every mutation, so a card binds
  `{Binding Items.Count}` and updates live as rows are added or deleted.
- **WinForms reaches the same fact view-locally**, by casting `entity.Items` to
  `INotifyCollectionChanged`. `WinFormsMainFormTemplate` already has
  `using System.Collections.Specialized;`.
- **Navigation is a view concern.** Sidebar and dashboard are both a selector
  plus a `ContentControl` bound to `{Binding SelectedItem, ElementName=…}`;
  "back to the dashboard" is `SelectedIndex = -1` in the code-behind that already
  exists. `MainViewModel` goes on exposing only `Entities`.

## Architecture

### The setting

`AppCustomization` gains one member:

```csharp
AppLayout? Layout   // null by default — "not specified"
```

and one derived accessor for the emit path:

```csharp
public AppLayout EffectiveLayout => Layout ?? AppLayout.Tabs;
```

```csharp
public enum AppLayout { Tabs, Sidebar, MasterDetail, Dashboard }
```

**Why nullable.** Every other inline option can only ever set a *non-default*
value, and `BuildAppCommand.Merge` relies on exactly that: it decides a setting
was named on the command line by comparing it against
`AppCustomization.Default`. `layout tabs` is typeable and equals the default, so
under the existing rule it would be indistinguishable from "not mentioned" —
typing `layout tabs` against a profile remembering `dashboard` would silently
produce a dashboard. Nullability restores the rule's exactness
(`inline.Layout is not null`), keeps the default record value neutral, and makes
`layout tabs` mean what it says.

### The abstraction

`ITargetScaffolder` **does not change**.
`FrontEndFiles(projectName, entityNames, customization)` already exists as a
default interface method, so the setting reaches the targets through plumbing
that is already in place.

```csharp
public interface IAppLayout
{
    /// <summary>Stable lower-case key used inline, in the wizard and in the help catalog.</summary>
    string Key { get; }

    /// <summary>Human phrase for the help text and the wizard prompt.</summary>
    string Description { get; }

    IReadOnlyList<GeneratedFile> ViewFiles(LayoutContext context);
}

public sealed record LayoutContext(
    string RootNamespace,
    string ProjectName,
    AppCustomization Customization,
    ThemePalette? Palette);
```

`LayoutContext` exists because a layout needs four things all derived from the
same two inputs — `RootNamespaceFor`, `TitleAttributeFor`, the resolved palette
and `IconFileName`. Passing them loose would mean re-resolving the palette in
eight places.

### Layout, files

```
AppScaffolding/Layouts/
    AppLayout.cs             enum
    IAppLayout.cs
    LayoutContext.cs
    AppLayoutCatalog.cs      the shared key/description vocabulary (parity is full,
                             so one catalogue serves both targets and the CLI)
    AppLayouts.cs            Supports(targetKey) — the CLI's capability question
    Wpf/
        WpfLayouts.cs        registry
        EntityPaneXaml.cs    the shared per-entity pane DataTemplate fragment
        WpfTabsLayout.cs
        WpfSidebarLayout.cs
        WpfMasterDetailLayout.cs
        WpfDashboardLayout.cs
    WinForms/
        WinFormsLayouts.cs   registry
        WinFormsTabsLayout.cs
        WinFormsSidebarLayout.cs
        WinFormsMasterDetailLayout.cs
        WinFormsDashboardLayout.cs
```

`WpfLayouts` and `WinFormsLayouts` are deliberately the same shape as the
existing `TargetScaffolders`: a private array, keys in display order, `Default`
as the first element, `TryResolve` for the CLI.

`WpfTarget.FrontEndFiles` then reduces to: build the shared view-model files,
resolve the palette, ask `WpfLayouts.Resolve(customization.EffectiveLayout)` for
the view files, append `Theme.xaml` when themed. **Theming stays outside the
layouts** — it is orthogonal, and keeping it out means four layouts do not each
reimplement it.

### What each layout emits

| Layout | WPF | WinForms |
|---|---|---|
| `tabs` | delegates to `MainWindowTemplate.Xaml` + `.CodeBehind`, unchanged | delegates to `WinFormsMainFormTemplate.Source`, unchanged |
| `sidebar` | new `MainWindow.xaml`: two-column `Grid`, `ListBox` over `Entities` with `DisplayMemberPath="Title"` and `SelectedIndex="0"`, plus `ContentControl Content="{Binding SelectedItem, ElementName=EntityList}"` over the shared pane template. Code-behind **reused verbatim** | `MainForm.cs` with a docked `ListBox` and a fill `Panel`, swapping the pane on `SelectedIndexChanged` |
| `master-detail` | `MainWindow.xaml` keeping the tab chrome, each pane split grid-over-editor; plus one new emitted file, `Views/PropertyEditor.cs` | `MainForm.cs` with a `PropertyGrid` docked under each grid, `SelectedObject` following the grid selection |
| `dashboard` | `MainWindow.xaml` with a `WrapPanel`-panelled `ListBox` of cards (`Title` + `{Binding Items.Count}`), `SelectedIndex="-1"` so it opens on the cards, plus a Home button. Code-behind is today's plus one handler | `MainForm.cs` with a `FlowLayoutPanel` of count buttons and a Back button |

`SelectedIndex` is explicit on every selector for the reason the existing
`TabControl` comment already gives: a `Selector` only follows its collection's
current item when the `ItemsSource` is a `CollectionView`, so bound to an
`ObservableCollection` it would otherwise open with nothing selected.

Both sides already have the seam that makes the new containers cheap. WPF's
per-entity pane is a single `DataTemplate`, shared across the three new layouts
as one raw-string-literal fragment (`EntityPaneXaml`); WinForms already factors
its pane into a `BuildTab` method the new containers can call. **The layouts
differ only in the container around the pane.**

On fragment composition and the CRLF hazard: PR #78 established that a template
line rebuilt from `"\n"` concatenation silently drops a CR, and that this is
invisible to `diff`. Sharing a fragment here is safe *because* the only output
owing byte-identity is `tabs`, which delegates to the untouched template and
composes nothing. No new layout has a byte-baseline to preserve. Fragments are
raw string literals regardless.

**Accepted duplication:** the pane markup consequently exists twice — inline in
`MainWindowTemplate`'s `TabControl.ContentTemplate`, and again in
`EntityPaneXaml` for the three new layouts. Refactoring `tabs` onto the shared
fragment is the obvious tidy-up and is **ruled out**: it would recompose the one
file that owes byte-identity, which is the exact failure mode PR #78 recorded.
Anyone changing the pane must change both sites, and a test asserting `tabs`
neutrality will catch them if they don't.

### `PropertyEditor.cs` — the one genuinely new emitted type

WPF has no `PropertyGrid`, so master-detail's form pane needs an emitted
reflection-driven editor: a `ContentControl` subclass that rebuilds a
label/editor `Grid` when `Content` changes, used declaratively from XAML as
`<local:PropertyEditor Content="{Binding Selected}" />`.

The alternative — code-behind reaching for named elements — was rejected because
named elements inside a `DataTemplate` are per-instance and can only be found by
walking the visual tree.

**Accepted cost:** this adds a third copy of the `IsDisplayable` helper, which is
already deliberately duplicated between `MainWindowTemplate.CodeBehind` and
`WinFormsMainFormTemplate.Source` under a "keep the two in sync" comment.
Deduplicating is not an option — it would change the `tabs` output and break
neutrality. The existing comment is extended to name the third site.

## CLI surfaces

Each follows the pattern its neighbours already use.

**Inline.** `"layout"` joins `InlineCustomization.ValueKeys`, resolved through
`AppLayoutCatalog`. An unknown name returns null, leaving the words on the line
as prose — the `theme mauve` rule. `master-detail` is hyphenated, so the
right-to-left tokenizer sees one token.

**Wizard.** `CustomizationWizard.Run` takes the chosen `ITargetScaffolder?`,
available in `BuildAppWizard` at line 81, before the call at line 98. It asks
`Layout [tabs/sidebar/master-detail/dashboard]` **first**, ahead of Company: it
is structural, where everything else in that step is decoration. The prompt is
skipped entirely for a target with no window. The capability check follows the
`JsonOnlyTargets` precedent (`BuildAppCommand.cs:715`) — a key list rather than a
new interface member — but lives on the layout side as
`AppLayouts.Supports(targetKey)` so the CLI keeps no second copy of which
targets have a window.

**Profile.** `Layout` as a `string?` in `CustomizationDefaults.Profile`, parsed
like `ParseTheme`, with an unrecognized stored value falling back rather than
throwing, as `ValidatedPrefix` does for a hand-edited namespace.

**Summary.** One more line in `CustomizationSummary.Lines`, emitted only when a
layout was named.

**Help.** The customization entry in `CommandDictionary` gains the `layout`
vocabulary, noting it applies to the WPF and Windows Forms targets. The
help-catalog audit test has to stay green.

## Error handling

| Case | Behaviour | Precedent |
|---|---|---|
| Unknown inline value (`layout kanban`) | words stay on the line as prose; no error | `theme mauve` |
| Unrecognized value in a hand-edited `app-profile.json` | falls back to the default | `ValidatedPrefix` |
| A layout named against a target that has no window | a one-line note at `approve` | the SQLite refusal at `BuildAppCommand.cs:425` |

That last case needs the check **at `approve`**, not only in the wizard: the
target can still be changed there, which is exactly why `IsPersistenceAllowed`
is re-checked at that point. Ignoring it silently would leave a user who typed
`layout dashboard` and then chose `console` with no idea the option evaporated.

## Testing

In this order:

1. **Neutrality first, before any new layout exists.** Tests asserting
   `WpfTabsLayout.ViewFiles(…)` returns strings *equal to*
   `MainWindowTemplate.Xaml(…)` / `.CodeBehind(…)`, and the WinForms equivalent,
   in `CodeGenerator.Tests/App/` beside `MainWindowTemplateTests`. The existing
   template tests stay untouched; if one needs editing, the refactor is not
   neutral and that is the signal to stop.
2. **Roslyn compile-testing for all new emitted C#** — `PropertyEditor.cs` and
   the two new code-behind variants go through `Compiler.Compile`, never
   substring or `ParseText` assertions, which pass green on CS0023 and on
   literal-injection bugs.
3. **Unit tests per CLI surface:** inline parse (each valid name; an invalid one
   leaving the line alone; `layout tabs` beating a remembered `dashboard` —
   written red first, since that is the `Merge` defect the nullable field
   exists to prevent), profile round-trip, wizard skip-for-console, summary
   line, help-catalog audit.
4. **Real builds, because XAML is not Roslyn-checkable.** Eight generated apps,
   four layouts × two targets, each actually built. Generated under a **short
   path**, not the scratchpad: `MAX_PATH` there produces an MSB3030 "file not
   found" on a file that plainly exists.
5. **Hand-run two of them:** a themed WPF dashboard (cards render, counts
   correct, clicking navigates, Home returns) and a WPF master-detail (editing
   in the form pane writes through to the grid and saves). Automated UIA is not
   worth building here — it reports `Header.ToString()` rather than rendered
   text and needs a real dispatcher loop.
6. **The `diff -r` neutrality proof from PR #78**, as a one-off rather than a
   test: a worktree at `HEAD`, a file-based app dumping every emitted file from
   each tree, `diff -r`. Substring tests cannot see a CRLF regression.

Every new layout is checked **themed and unthemed**. Only `tabs` owes
byte-identity; the other three have to look right with the `DynamicResource`
brushes in play.
