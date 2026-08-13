# Window Layout Customization — End-to-End Verification

Evidence for Task 15 of `2026-08-13-app-layout-customization.md`. Branch `app-layout`, verified at
`b5ac3b4` against `main` at `896b97e`. Every command below was run; nothing here is inferred.

## 1. CLI rebuilt

`dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release` — `dotnet test` leaves the
CLI's own `bin` stale, so the Release binary was rebuilt before any run.

## 2. Eight apps generated through the real CLI

Piped stdin, `D:\lt\products.csv` as the source (2 rows: Widget 9.99, Gadget 14.50) so entity
discovery is deterministic and does not depend on the Wikipedia store. For each of
`tabs sidebar master-detail dashboard` × `wpf winforms`:

```
build app: a shop from csv D:\lt\products.csv layout <layout>
approve <target>
exit
```

**Options must go after the data source.** A first attempt used
`build app: a shop layout sidebar from csv ...` and the app was generated as `ShopLayoutSidebar`
with the tabbed window — the option words stayed in the description, exactly as `help` says they
would ("Options go LAST on the line, after any data source"). Not a defect; worth knowing because
the failure is silent: you get a working app of the wrong shape and a wrong name.

The review loop printed `Layout: <key>` on all eight runs, including `layout tabs`, which is the
CustomizationSummary line from Task 13 confirming the merge kept what was typed.

Generated projects land in `D:\SampleProjects\Shop` / `Shop.WinForms` (a fixed workspace root, not
the working directory); each was moved to `D:\lt\<target>-<layout>` after generation.

## 3. All eight compile

`dotnet build <dir> -c Debug` → **Build succeeded, 8/8.** This is the first check that can see a
broken XAML tree: WPF compiles its markup at build time, so the master-detail app's
`local:PropertyEditor` element and the emitted `PropertyEditor.cs` behind it are both proven to
resolve here and nowhere earlier.

## 4. All eight launched; the two risky WPF apps driven

Each app was started and left for 5 seconds: **8/8 still alive**, each reporting a real
`MainWindowTitle` (`Shop` / `Shop.WinForms`). A window title is only non-empty once the window
handle exists, so this rules out the failure that survives compilation — a `XamlParseException` at
load, which is silent until runtime.

The two apps carrying real interaction risk were then driven with synthetic mouse input and
screenshotted:

**WPF dashboard.** One card per entity, reading `Product` over `2` — the count matching the two
imported rows, so `{Binding Items.Count}` resolves through the runtime `ObservableCollection` as the
design predicted with no view-model change. Clicking the card hid the cards, showed that entity's
grid with both rows and the Add/Delete/Save row, **and revealed the Home button**. Clicking Home
returned to the cards and hid the button again. That full round trip is what the plan's own version
of this layout could not do: its card handler never set the button visible and never hid the cards.

**WPF master-detail.** Tab chrome, grid with both rows, and an empty editor area on startup —
correct, because nothing is selected and `Record` is null. Clicking the Widget row filled the editor
with five label/textbox pairs bound to that record: `Id 1`, `Name Widget`, `Price 9.99`, `CreatedOn`,
`ModifiedOn`. This is the runtime confirmation of both Task 8 fixes. As the plan's `ContentControl`
the editor's single Content slot would have held the record instead of the grid and nothing would
have rendered; with the plan's unsourced bindings all five boxes would have been empty, because the
DataContext inherited from the pane template is the entity's view model, not the record.

**Left to a human.** Editing a field and seeing it write through to the grid, that `Id` refuses
edits, and that `Save` persists. Synthetic clicks can select a row; verifying a two-way binding
round trip and a file write needs someone at the keyboard. The apps are still under `D:\lt` for that.

## 5. Default output is byte-identical to `main`

`git worktree add D:/wt-head main`, then a file-based .NET 10 app per tree
(`#:project <tree>/AppScaffolding.csproj`) dumping every `FrontEndFiles` result for all six targets
with `AppCustomization.Default` — 21 files per tree.

```
diff -r /d/lt-neutral-head /d/lt-neutral-branch    → BYTE-IDENTICAL
diff -r /d/lt-neutral-branch /d/lt-neutral-tabs    → BYTE-IDENTICAL
```

The second diff is the branch against itself with an explicit `Layout = AppLayout.Tabs`: naming the
default layout must also change nothing, and no cross-tree diff can prove that because `main` has
no `Layout` member. Substring tests cannot see a CRLF regression; these can.

Worktree removed afterwards.

## 6. CI's own commands

```
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror
    → Build succeeded, 0 Warning(s), 0 Error(s)
dotnet test  SmartCodeGenerator/SmartCodeGenerator.slnx -c Release
    → 2,175 passed, 0 failed, 0 skipped
      CodeGenerator.Tests 1,051 · DomainDiscovery.Tests 518 · DataIngestion.Tests 285
      NaturalLanguageEngine.Tests 228 · CodeIntelligence.Tests 93
```

`--no-incremental` matters: without it `dotnet build` prints "Build succeeded" without rerunning the
analyzers.

## Cosmetic parity nit, not fixed

The WPF dashboard's back button reads **Home**; the WinForms one reads **Back**. Both come from the
plan, which specified them separately. Harmless, but the two targets are meant to be parity
implementations of the same layout, so one word should win.
