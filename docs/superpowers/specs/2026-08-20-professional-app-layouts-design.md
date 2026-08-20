# Professional application layouts: login, masters and transactions

Date: 2026-08-20

## Problem

A generated WPF, Windows Forms or Blazor application does not look like a
business application. Every discovered entity is presented identically — a grid
with Add, Delete and Save — in one of four layouts (`tabs`, `sidebar`,
`master-detail`, `dashboard`) that differ only in how those grids are arranged.
There is no sign-in, no notion of who is using the application, and no
distinction between reference data and the records that reference it.

Three requirements follow, agreed with the requester:

1. Every generated application starts at a **login screen** backed by real
   users, real password hashing and a role.
2. Screens are grouped and shaped as **masters** and **transactions**, the
   division every line-of-business application makes.
3. The result is what a generated application looks like **by default**, not an
   option someone has to know to ask for.

## Scope

In scope:

| Area | Delivered |
| --- | --- |
| Authentication | `User` record, PBKDF2 hashing, two roles, seeded admin, sign-in/sign-out, change password, Users admin screen |
| Classification | `EntityRole` of Master / Transaction / DocumentLine, inferred from the model, overridable from the command line and the wizard |
| Shell | Grouped navigation rail, dashboard home grouped by role, header naming the signed-in user, role-filtered navigation |
| Transaction screens | Filtered list with column totals; a dedicated document editor with header fields and a lines grid |
| Targets | WPF, Windows Forms, Blazor Server |

Explicitly **out** of scope:

- **Console, Web API, Worker and Class Library targets.** They have no screens
  to gate. `build app ... console` is unchanged by this work.
- **Registration, password reset, lockout, audit of sign-ins.** The Users
  screen is how an account is created.
- **Per-entity permissions.** The role has exactly two effects, listed under
  "Roles" below. Anything finer needs a permission model, which is a separate
  feature.
- **Reports and printing.** A real business application has them; nothing in
  this design depends on them, and they would double its size.

## Approach

Three were considered for how entity-aware screens reach the desktop targets.

**A. Template-driven screens (chosen).** `IEntityViewModel` gains a `Role`; the
single main view keeps one view file and selects a `DataTemplate` — or, in
Windows Forms, a `UserControl` — by role. Master, transaction and document
panes are three templates, not N views. The emitted file count stays flat as the
discovered model grows, and `EntityViewModel<T>`, `RelationshipResolver` and the
existing repositories are untouched.

**B. A view per entity.** Emit `OrderScreen.xaml`, `CustomerScreen.xaml` and so
on. Its one genuine advantage is that the output is easier to hand-edit
afterwards, which is arguably the point of a code generator. Rejected because it
multiplies emitted code by the entity count, makes every golden-output test
model-shaped, and would need the same three templates anyway — just copied per
entity.

**C. Minimal composition.** Reuse the `dashboard` and `master-detail` layouts
nearly as they stand, adding only a login and navigation grouping. Cheapest, and
rejected: it delivers none of the transaction screen, which is the half of the
brief that makes an application look professional.

Blazor is the exception to A and B alike: its pages are already one per entity,
so the per-entity page stays and renders one of two shared components.

## Authentication

Emitted into the shared core beside `Models/` and `Data/`, so all three targets
receive identical files and only the login *view* differs.

**The user record rides the existing store.** `Models/User.cs` — `Id`,
`UserName`, `DisplayName`, `PasswordHash`, `PasswordSalt`, `Role`, `IsActive` —
is persisted through `IStore<User>` like any other entity. `JsonStore` gives it
a `User.json`; `AppDbContext` gains a `DbSet<User>` exactly as discovered
classes do. No new persistence code, and both backends work unchanged.

`User` is *emitted*, not *discovered*. It must not reach the "Generated N
class(es)" summary, the navigation rail, the dashboard tiles or the seeded
sample data. This is the single easiest thing in this design to get wrong, and
it is wrong in a way that shows up only by reading the generated app's rail.

**Hashing.** `Auth/PasswordHasher.cs` uses PBKDF2 — `Rfc2898DeriveBytes`,
SHA-256, a 16-byte cryptographically random salt, 100,000 iterations — with
verification through `CryptographicOperations.FixedTimeEquals`. No third-party
package, and nothing reversible is stored.

**The service.** `Auth/AuthService.cs` exposes `SignIn(userName, password)` and
`ChangePassword`. `SignIn` distinguishes *no such user*, *wrong password* and
*account disabled* internally, and reports one message — "User name or password
not recognised" — to the screen.

**Roles.** `Auth/AppRole.cs` has exactly two values, `Admin` and `User`, with
exactly two effects:

- The Admin group (the Users screen) is reachable only by an Admin.
- Delete on a master screen is enabled only for an Admin.

Both roles may create, edit and save transactions. A clerk who cannot raise an
order is not a useful account, so the role deliberately does not make
transaction screens read-only.

**Seeding moves earlier.** Entity seeding happens today in `MainViewModel`'s
constructor. The admin account must exist *before* the login screen, so user
seeding becomes its own step at application start — `App.xaml.cs` in WPF,
`Program.cs` in Windows Forms, and the sign-in path in Blazor — while entity
seeding stays where it is. First run creates `admin` with password `admin`,
which the login screen names in a first-run hint. There is no forced password
change on first sign-in; the hint plus the change-password action on the Users
screen covers it without an extra screen and an extra state flag.

**Turning it off.** Login is orthogonal to layout and applies to all four
existing layouts as well as the new one. A `nologin` flag omits the whole auth
core for a throwaway application — a bare word joining `novalidation`,
`logging` and `noseed` in `InlineCustomization.Flags`, not the two-word form,
because the parser's flags are single tokens.

**Reaching the Users screen without the shell.** The Admin group belongs to the
new shell, but login applies to the four existing layouts too, and an
application whose only account is the seeded admin needs a way to add a second
one. So the Users screen is emitted as an ordinary pane — an extra tab, an extra
navigation entry, an extra card — in whichever layout is active, visible only to
an Admin. In the `business` shell that pane is what the Admin group opens. This
is what makes the auth branch shippable before the shell branch exists, and it
is a permanent requirement rather than a temporary scaffold.

## Classification

`AppScaffolding/Screens/EntityRoles.cs` is a pure function from
`IReadOnlyList<ClassDefinition>` plus the user's overrides to a role per entity
name. No I/O, no UI, testable on its own.

**The audit-field trap.** `DomainModelMapper.AddAuditFields` gives *every*
discovered class a `CreatedOn` and a `ModifiedOn`. "Has a `DateTime` field" is
therefore true of everything and carries no information. The heuristic must
exclude audit fields by name, and that exclusion needs its own test: without it
every entity classifies as a transaction, and the mistake is invisible in the
emitted diff.

**Transaction** — at least one outgoing foreign key (`ForeignKeys.TargetsOf`,
the existing shared definition) **and** either a non-audit `DateTime` field or a
name in a transaction gazetteer (Order, Invoice, Payment, Booking, Sale,
Purchase, Shipment, Receipt, Transfer, Reservation and similar). Both halves are
required: a foreign key alone makes every child entity a transaction, and the
gazetteer alone cannot see a domain whose vocabulary the lexicon never learned.

**DocumentLine** — a foreign key to an entity already classified as a
Transaction, *and* a name that is that transaction's name followed by `Line`,
`Item`, `Detail` or `Row`, or that simply ends in one of those. Lines are not
listed in the rail; they are edited inside their parent document. A false
positive therefore *hides a screen*, which is why this test is a strict name
test rather than a shape test.

**Master** — everything else. This is the safe default: a misclassified master
renders as an ordinary grid, which is what the generator produces today.

**Override.** Two keywords, parsed inline and prompted for in the wizard:

```
build app: ... transactions Order,Invoice masters Region,Category
```

Both are value keys in `InlineCustomization.ValueKeys`, so each is two tokens
and the value must be comma-joined **without spaces** — the parser splits on
whitespace, the same constraint that made `master-detail` hyphenated. A quoted
value (`transactions "Order, Invoice"`) also works, because values pass through
`Unquote`. Options are stripped right to left from the end of the line, so both
keywords must follow any data-source clause.

Both reach the help catalogue and the manual. Line detection stays derived;
naming a line entity in `masters` promotes it to a screen of its own, which is
the only correction anyone would want to make.

**Reporting.** The build summary states what was decided — "3 masters, 2
transactions (Order has 4 lines)" — because a silent misclassification is
otherwise discoverable only by launching the application.

**Reaching the targets.** `ITargetScaffolder.FrontEndFiles` currently takes an
`IReadOnlyList<string> entityNames`, which cannot carry a role. A
`FrontEndFiles(FrontEndContext)` overload is added as a default interface
method — the pattern the customization overload already uses — so the four
screenless targets are untouched. `LayoutContext` gains the classified list.

## The shell

A fifth layout key, `business`, which becomes the default.

**The default flip.** `AppCustomization.EffectiveLayout` changes from
`Layout ?? AppLayout.Tabs` to `Layout ?? AppLayout.Business`. Mechanically one
line; in practice the line that turns a large number of golden-output tests red.
That is expected and is not a regression.

`AppLayout.Tabs` keeps its byte-for-byte guarantee only when asked for by name
**and** with `nologin`. That pairing is the precise statement of the old
promise, and it is narrower than the one the layout work made in August:
because login is universal, a named `tabs` build now also emits a login window
and a Users pane. Those layouts therefore change exactly once, in the auth
branch, and are frozen again afterwards.

**`MainViewModel` does not change.** The four existing layouts bind to it and
must go on working. The shell introduces `ShellViewModel`, which *owns* a
`MainViewModel` and adds the chrome's state: `SignedInUser`, the grouped
navigation, the current screen, `SignOutCommand`. `IEntityViewModel` gains only
`Role` and `Count`, which the old layouts ignore.

**WPF.** `LoginWindow` is shown by `App.xaml.cs` before the main window, and
sign-out is a loop there rather than a process restart: close the shell, show
the login again. The content area is a `ContentControl` bound to the current
screen; the three panes are implicit `DataTemplate`s keyed by `DataType` —
`DashboardViewModel`, `MasterScreenViewModel`, `TransactionScreenViewModel`. No
selector class and no per-entity XAML.

**Windows Forms.** The same view models with different plumbing, because there
are no data templates: three `UserControl`s swapped into a content panel, a
`TreeView` for the grouped rail (the least code that still reads as grouping),
and `LoginForm.ShowDialog` from `Program.cs`.

**Blazor.** `MainLayout` gains the header and the grouped navigation; `Home`
becomes the grouped dashboard; `Login.razor` is deliberately **not** interactive
— it is static-rendered and posts to a `/auth/login` minimal-API endpoint,
because an interactive circuit cannot set a cookie. Pages carry
`@attribute [Authorize]`, the Admin group sits behind `AuthorizeView`, and the
Users page behind `[Authorize(Roles = "Admin")]`. That last check is the one
that matters, because a URL can be typed.

**The named risk.** `BlazorTarget` has never written `Program.cs`, on purpose.
Cookie authentication forces it: `AddAuthentication`, `AddAuthorization`,
`AddCascadingAuthenticationState`, `UseAuthentication`, `UseAuthorization` and
the two endpoints, inserted by text surgery in `BlazorProjectFixup`. Two things
in that area have broken this repository before — `UseAntiforgery` ordering,
which the login form's token depends on, and the static web asset manifest. This
piece cannot be signed off by assertions over emitted strings. It requires a
generated application built and signed into, under a short output path.

**Theming** stays orthogonal, as today: the rail, header and dashboard bind to
the `ThemePalette` brushes when the application is themed, and use the same
hardcoded neutrals `WpfSidebarLayout` already uses when it is not.

## Master screens

Unchanged in substance from today's `master-detail` pane: a grid with an edit
form beneath it, foreign keys editing as drop-downs, existing generated
validators running on save. What is new is that it is now one of three panes
selected by role, that Delete is Admin-only, and that it sits inside the shell.

## Transaction screens

**The list.** `TransactionScreenViewModel` wraps the existing
`EntityViewModel<T>` rather than replacing it, adding a date range, a
foreign-key drop-down, a search box and a recomputed `FilteredItems`
collection — recomputed rather than an `ICollectionView`, because
`DesktopViewModelFiles` is deliberately stack-neutral and Windows Forms has no
collection view. The grid itself becomes read-only; opening a row is what edits
it.

**Date and totals.** The filter binds to the non-audit `DateTime` the classifier
already had to find, and that field's name is emitted into the generated code;
a model with no such field renders no date range. The footer totals every
`decimal` or `double` property that is neither an Id nor a foreign key, one
labelled total per column — more honest than guessing which single column is
*the* total.

**The editor.** One dedicated surface per transaction:
`DocumentWindow.ShowDialog` on WPF, `DocumentForm.ShowDialog` on Windows Forms,
and the routes `/order/{id:int}` and `/order/new` in Blazor. That divergence is
accepted; a modal is the wrong shape on the web.

It edits a **copy**, which is the reason for having a dedicated editor at all:
Cancel must genuinely cancel, including line edits. The clone is a
`System.Text.Json` round-trip — already a dependency — and navigation properties
are `[JsonIgnore]`, so they drop out of the copy and are re-resolved by
`RelationshipResolver` on save.

**The document total** follows a fallback ladder rather than guessing: if the
line entity has both a quantity-shaped and a price-shaped field, the total is
the sum of their products; failing that, the sum of a single amount column;
failing that, no total is shown. A generated application displaying a
confidently wrong number is worse than one displaying none.

**Saving** assigns the header Id first, then stamps it onto new lines — a line
cannot take its foreign key before its parent exists. Generated validators run
over header and lines alike when validation is enabled.

**Deleting a header deletes its lines.** Orphaned lines are unreachable, since
no screen lists them, so leaving them would silently grow the store.

**A transaction with no lines still gets the editor**, without the lines grid.
The alternative — falling back to an inline form — would mean the same entity
type edits two different ways depending on the discovered model. Uniform
interaction wins.

## Delivery

Four sequenced branches, each its own plan and pull request. The pieces are not
independent, which is why they share one design document, but each branch leaves
the repository green and shippable.

1. **Auth core and login.** `User`, hashing, roles, seeded admin, the
   split-panel login in all three targets, the Users pane inside the existing
   layouts, the `nologin` flag. Universal, so everything else builds on it.
2. **Entity classification.** `EntityRole`, the heuristic, the `transactions` and
   `masters` overrides, the wizard prompt, the help catalogue, the manual, the
   build summary line.
3. **The shell.** Grouped rail, grouped dashboard home, header, role filtering,
   the `business` layout key and the default flip.
4. **Transaction screens.** Filter bar and totals, the document editor in all
   three targets, the no-lines fallback.

## Testing

- **Classification** is a pure function and gets ordinary unit tests, including
  the audit-field exclusion, a model with no dates, a model whose only signal is
  the gazetteer, and an override that contradicts the heuristic.
- **Emitted code is compile-tested with Roslyn**, as everything in this
  repository is. Substring assertions over generated source do not catch the
  failures this feature can produce.
- **Password hashing** is tested for round-trip verification, salt uniqueness
  across two users with the same password, and rejection of a wrong password.
- **The Blazor `Program.cs` surgery** is verified by generating an application,
  building it, launching it and signing in — under a short output path, because
  the scratchpad exceeds `MAX_PATH`. No unit test substitutes for this.
- **The desktop shells** are verified the way the existing layout work was: a
  generated application launched with a real dispatcher loop, driven by
  synthetic input, screenshotted.
- **Golden-output tests** for the four existing layouts must still pass
  unchanged when the layout is named explicitly **and** `nologin` is given. That
  combination is the regression proof for the whole feature: if it has to
  change, the change is a bug in this work, not in the test. Without `nologin`
  those same layouts change once, in the auth branch, by exactly two additions —
  a login window and a Users pane — and any third difference is a bug.

## Open questions

None outstanding. Decisions taken during design, recorded so they are not
relitigated: the role has two effects and does not make transactions read-only;
there is no forced password change on first sign-in; the transaction editor is
uniform whether or not the model has line entities; and the four existing
layouts remain selectable and gain only the login.
