# build app Customization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let `build app` produce a customized application — company branding, theme/accent/title/icon, and three feature toggles — supplied inline or through an opt-in wizard step.

**Architecture:** An immutable `AppCustomization` record is parsed from the command line (or collected by a wizard), carried inside a new `ScaffoldRequest` parameter object, and threaded into `AppScaffolder` and the four UI targets via a default-interface-method overload on `ITargetScaffolder`. Templates stay pure static functions of their arguments so the existing Roslyn compile-tests keep working.

**Tech Stack:** C# / .NET 10, MSTest, Roslyn (`Compiler.Compile`), raw string literal templates.

**Spec:** `docs/superpowers/specs/2026-08-12-build-app-customization-design.md`

**Branch:** `app-customization` (already created, spec committed at `c10336e`)

## Global Constraints

- **Repository root is `D:\SmartCodeGenerator-Repository`**, which is the PARENT of the tool's default working directory. Every path below is relative to that root. Use `git -C D:\SmartCodeGenerator-Repository ...`.
- **`AppCustomization.Default` must be byte-for-byte neutral.** Every existing test in `CodeGenerator.Tests` must stay green with no edits to its expectations. If an existing test needs changing, the default is not neutral and the change is wrong.
- **Emitted code is verified by compiling it**, via `EmittedSource.AssertCompiles` / `EmittedSource.Flatten` in `CodeGenerator.Tests/App/EmittedSource.cs`. Substring assertions on emitted C# are not acceptable as the primary check.
- **Test framework is MSTest**: `[TestClass]`, `[TestMethod]`. Analyzer MSTEST0037 rejects `Assert.AreEqual(n, x.Count)` — use `Assert.HasCount(n, x)`. `Assert.IsEmpty(x)` exists and is used.
- **Never read `DateTime.Now` inside a template.** The copyright year is a parameter. Otherwise golden-output tests redden on 1 January.
- **Local test command:** `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj`
- **CI parity command** (run before the final commit): `dotnet build D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx -c Release -warnaserror --no-incremental`. The `--no-incremental` is required: an incremental build prints "Build succeeded" without rerunning analyzers.
- **Close any running CodeGenerator CLI before building** — a launched CLI locks `CodeGenerator/bin` and MSB3061 reports "access denied" on files that exist.
- **Emitted files use `(c)` rather than `©`** in the copyright banner, so emitted source carries no encoding dependency.
- **Default title pattern uses a plain hyphen**, `{company} - {app}`, for the same reason.

## Spec correction found during planning

The spec says the company/title lands in "the Web API OpenAPI title". **The generated Web API has no OpenAPI.** `WebApiProgramTemplate.Build` emits `WebApplication.CreateBuilder(args)` then `builder.Build()` with no `AddOpenApi()`/`AddSwaggerGen()` call anywhere in the file (verified end to end, lines 14–59). Adding OpenAPI would be scope creep and a new package.

**Resolution used by this plan:** the Web API target gets a root endpoint returning the title — `app.MapGet("/", () => "Codis Ltd - Shop");` — emitted only when the title is non-default. Task 6 implements this. Update the spec's "Look and feel" section to match when Task 6 lands.

## File Structure

**Created**

| File | Responsibility |
| --- | --- |
| `AppScaffolding/AppCustomization.cs` | the settings record, `Default`, and title rendering |
| `AppScaffolding/ThemePalette.cs` | theme + accent → named colours; `null` when nothing is themed |
| `AppScaffolding/FileHeader.cs` | the copyright banner string |
| `AppScaffolding/ScaffoldRequest.cs` | parameter object for `AppScaffolder.Scaffold` |
| `AppScaffolding/ThemeXamlTemplate.cs` | emits `Views/Theme.xaml` |
| `AppScaffolding/LoggingTemplate.cs` | emits `Logging/AppLog.cs` |
| `CodeGenerator/App/InlineCustomization.cs` | parses inline options off a `build app` line |
| `CodeGenerator/App/CustomizationWizard.cs` | the opt-in gate and eight prompts |
| `CodeGenerator/App/CustomizationDefaults.cs` | `.codegen/app-profile.json` load/save |

**Modified**

| File | Change |
| --- | --- |
| `AppScaffolding/AppScaffolder.cs` | `ScaffoldRequest` entry point; toggle branching; header injection |
| `AppScaffolding/ITargetScaffolder.cs` | customization-aware `FrontEndFiles` default method |
| `AppScaffolding/CsprojEditing.cs` | `InjectProperties` |
| `AppScaffolding/MainWindowTemplate.cs` | title, theme resources, icon |
| `AppScaffolding/WinFormsMainFormTemplate.cs` | title, colours, icon |
| `AppScaffolding/ConsoleProgramTemplate.cs` | banner; drop `SeedData.EnsureSeeded()` when off |
| `AppScaffolding/WebApiProgramTemplate.cs` | root title endpoint; drop `EnsureSeeded()` when off |
| `AppScaffolding/WorkerProgramTemplate.cs` | drop `EnsureSeeded()` when off |
| `AppScaffolding/MainViewModelTemplate.cs` | drop `EnsureSeeded()` when off |
| `AppScaffolding/RepositoryTemplate.cs` | validation branch; logging |
| `AppScaffolding/ViewModelTemplates.cs` | validation branch |
| `AppScaffolding/StoreFactoryTemplate.cs` | logging |
| `AppScaffolding/TestProjectTemplates.cs` | suppress seed/validator tests when off |
| `AppScaffolding/WpfProjectFixup.cs` | merge `Views/Theme.xaml` into `App.xaml` |
| `AppScaffolding/Targets/WpfTarget.cs` `WinFormsTarget.cs` `ConsoleTarget.cs` `WebApiTarget.cs` | override the 3-arg `FrontEndFiles` |
| `CodeGenerator/App/BuildAppCommand.cs` | parse, wizard, carry customization, usage text |
| `CodeGenerator/App/BuildAppWizard.cs` | call the customization step |
| `CodeGenerationEngine/Core/CommandDictionary.cs` | help entries |
| `AppScaffolding/README.md` | document the new files and toggles |

---

### Task 1: AppCustomization and ThemePalette

The pure value types everything else consumes. No wiring yet.

**Files:**
- Create: `AppScaffolding/AppCustomization.cs`
- Create: `AppScaffolding/ThemePalette.cs`
- Create: `AppScaffolding/FileHeader.cs`
- Test: `CodeGenerator.Tests/App/AppCustomizationTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `enum AppTheme { Default, Light, Dark }`
  - `sealed record AppCustomization(string Company, AppTheme Theme, string? Accent, string TitlePattern, string? IconPath, bool Validation, bool Logging, bool SampleSeedData)` with `static AppCustomization Default`, `string TitleFor(string appName)`, `bool HasBranding`.
  - `sealed record ThemePalette(string Background, string Surface, string Foreground, string GridLine, string Accent, string AccentForeground)` with `static ThemePalette? Resolve(AppTheme theme, string? accent)` and `static bool IsValidHexColour(string value)`.
  - `static class FileHeader` with `static string For(string company, int year)`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/AppCustomizationTests.cs`:

```csharp
using AppScaffolding;

namespace CodeGenerator.Tests.App;

/// <summary>The customization value types. Default must be neutral: it is what every existing
/// caller passes, so anything it changes changes today's generated output.</summary>
[TestClass]
public class AppCustomizationTests
{
    [TestMethod]
    public void The_default_renders_the_app_name_alone()
        => Assert.AreEqual("Shop", AppCustomization.Default.TitleFor("Shop"));

    [TestMethod]
    public void The_default_themes_nothing()
        => Assert.IsNull(ThemePalette.Resolve(AppTheme.Default, null));

    [TestMethod]
    public void The_default_has_no_branding()
        => Assert.IsFalse(AppCustomization.Default.HasBranding);

    [TestMethod]
    public void A_company_and_pattern_are_substituted()
    {
        var customization = AppCustomization.Default with
        {
            Company = "Codis Ltd",
            TitlePattern = "{company} - {app}",
        };

        Assert.AreEqual("Codis Ltd - Shop", customization.TitleFor("Shop"));
    }

    /// <summary>A pattern naming a company that was never set must not leave a dangling
    /// separator: "{company} - {app}" with no company reads " - Shop" without this.</summary>
    [TestMethod]
    public void A_pattern_with_no_company_collapses_its_separator()
    {
        var customization = AppCustomization.Default with { TitlePattern = "{company} - {app}" };

        Assert.AreEqual("Shop", customization.TitleFor("Shop"));
    }

    [TestMethod]
    public void An_accent_alone_themes_over_the_light_base()
    {
        var palette = ThemePalette.Resolve(AppTheme.Default, "#B91C1C");

        Assert.IsNotNull(palette);
        Assert.AreEqual("#B91C1C", palette.Accent);
        Assert.AreEqual("#FFFFFF", palette.Background);
    }

    [TestMethod]
    public void The_dark_palette_differs_from_the_light_one()
    {
        var dark = ThemePalette.Resolve(AppTheme.Dark, null);
        var light = ThemePalette.Resolve(AppTheme.Light, null);

        Assert.IsNotNull(dark);
        Assert.IsNotNull(light);
        Assert.AreNotEqual(light.Background, dark.Background);
    }

    [TestMethod]
    public void A_malformed_colour_is_rejected()
    {
        Assert.IsTrue(ThemePalette.IsValidHexColour("#0B6E4F"));
        Assert.IsFalse(ThemePalette.IsValidHexColour("0B6E4F"));
        Assert.IsFalse(ThemePalette.IsValidHexColour("#0B6E4"));
        Assert.IsFalse(ThemePalette.IsValidHexColour("#GGGGGG"));
    }

    [TestMethod]
    public void No_company_means_no_file_header()
        => Assert.AreEqual(string.Empty, FileHeader.For(string.Empty, 2026));

    [TestMethod]
    public void A_company_produces_a_one_line_banner()
        => Assert.AreEqual("// Copyright (c) 2026 Codis Ltd.\n", FileHeader.For("Codis Ltd", 2026));
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter AppCustomizationTests`

Expected: FAIL — the build fails, `AppCustomization` / `ThemePalette` / `FileHeader` do not exist.

- [ ] **Step 3: Write the implementation**

Create `AppScaffolding/AppCustomization.cs`:

```csharp
namespace AppScaffolding;

/// <summary>Which theme a generated desktop front end uses. <see cref="Default"/> means the
/// generator emits no theme at all, which is what every pre-customization app looked like.</summary>
public enum AppTheme
{
    Default,
    Light,
    Dark
}

/// <summary>
/// What a `build app` run asked the generated application to look like and contain.
/// </summary>
/// <remarks>
/// <see cref="Default"/> is deliberately neutral: it must reproduce the output the generator
/// produced before customization existed, because it is what every pre-existing caller passes.
/// The regression proof is the existing suite — if an old test has to change, this record is
/// not neutral.
/// </remarks>
public sealed record AppCustomization(
    string Company,
    AppTheme Theme,
    string? Accent,
    string TitlePattern,
    string? IconPath,
    bool Validation,
    bool Logging,
    bool SampleSeedData)
{
    /// <summary>The only two placeholders a title pattern may use.</summary>
    public const string CompanyPlaceholder = "{company}";
    public const string AppPlaceholder = "{app}";

    /// <summary>The title a customized app usually wants once a company is named. Plain hyphen,
    /// not an en dash: emitted source and XAML then carry no encoding dependency.</summary>
    public const string CompanyTitlePattern = "{company} - {app}";

    public static AppCustomization Default { get; } = new(
        Company: string.Empty,
        Theme: AppTheme.Default,
        Accent: null,
        TitlePattern: AppPlaceholder,
        IconPath: null,
        Validation: true,
        Logging: false,
        SampleSeedData: true);

    /// <summary>Whether anything company-derived should be emitted at all.</summary>
    public bool HasBranding => Company.Length > 0;

    /// <summary>
    /// The window/page title for <paramref name="appName"/>.
    /// </summary>
    /// <remarks>
    /// Substituting an empty company would leave the pattern's separator behind — "{company} -
    /// {app}" reading " - Shop" — so leading and trailing separator characters are trimmed off
    /// the result rather than the pattern being rejected.
    /// </remarks>
    public string TitleFor(string appName)
    {
        var title = TitlePattern
            .Replace(CompanyPlaceholder, Company)
            .Replace(AppPlaceholder, appName);

        return title.Trim().Trim('-', ':', '|').Trim();
    }
}
```

Create `AppScaffolding/ThemePalette.cs`:

```csharp
namespace AppScaffolding;

/// <summary>The colours a themed front end paints with, as `#rrggbb` literals.</summary>
public sealed record ThemePalette(
    string Background,
    string Surface,
    string Foreground,
    string GridLine,
    string Accent,
    string AccentForeground)
{
    /// <summary>The accent used when a theme was chosen but no accent was.</summary>
    public const string DefaultAccent = "#2563EB";

    private static readonly ThemePalette Light = new(
        Background: "#FFFFFF",
        Surface: "#F3F4F6",
        Foreground: "#111827",
        GridLine: "#E5E7EB",
        Accent: DefaultAccent,
        AccentForeground: "#FFFFFF");

    private static readonly ThemePalette Dark = new(
        Background: "#1F2937",
        Surface: "#111827",
        Foreground: "#F9FAFB",
        GridLine: "#374151",
        Accent: DefaultAccent,
        AccentForeground: "#FFFFFF");

    /// <summary>
    /// The palette for <paramref name="theme"/> and <paramref name="accent"/>, or null when
    /// nothing was customized and the generator should emit no theme.
    /// </summary>
    /// <remarks>
    /// An accent with no theme is meaningful — "keep the normal look, but in our colour" — and
    /// resolves over the light base. An invalid accent is ignored rather than rejected, so a
    /// typo cannot fail a build the user has already answered ten questions for.
    /// </remarks>
    public static ThemePalette? Resolve(AppTheme theme, string? accent)
    {
        var usable = accent is not null && IsValidHexColour(accent) ? accent : null;

        if (theme == AppTheme.Default && usable is null)
            return null;

        var basePalette = theme == AppTheme.Dark ? Dark : Light;

        return usable is null ? basePalette : basePalette with { Accent = usable };
    }

    /// <summary>Whether <paramref name="value"/> is exactly `#rrggbb`.</summary>
    public static bool IsValidHexColour(string value)
        => value.Length == 7
           && value[0] == '#'
           && value[1..].All(Uri.IsHexDigit);
}
```

Create `AppScaffolding/FileHeader.cs`:

```csharp
namespace AppScaffolding;

/// <summary>The copyright banner prepended to generated C#.</summary>
public static class FileHeader
{
    /// <summary>
    /// The banner for <paramref name="company"/>, or empty when there is none.
    /// </summary>
    /// <remarks>
    /// The year is a parameter, never <c>DateTime.Now</c>: a template reading the clock would
    /// turn every golden-output test red on 1 January. "(c)" rather than the symbol keeps
    /// emitted source free of any encoding dependency.
    /// </remarks>
    public static string For(string company, int year)
        => company.Length == 0 ? string.Empty : $"// Copyright (c) {year} {company}.\n";
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter AppCustomizationTests`

Expected: PASS, 10 tests.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj`

Expected: PASS. Nothing is wired up yet, so no existing test may move.

- [ ] **Step 6: Commit**

```bash
git -C D:\SmartCodeGenerator-Repository add AppScaffolding/AppCustomization.cs AppScaffolding/ThemePalette.cs AppScaffolding/FileHeader.cs CodeGenerator.Tests/App/AppCustomizationTests.cs
git -C D:\SmartCodeGenerator-Repository commit -m "feat(scaffold): add the AppCustomization, ThemePalette and FileHeader value types"
```

---

### Task 2: Inline option parsing

**Files:**
- Create: `CodeGenerator/App/InlineCustomization.cs`
- Test: `CodeGenerator.Tests/App/InlineCustomizationTests.cs`

**Interfaces:**
- Consumes: `AppCustomization`, `AppTheme` from Task 1.
- Produces: `static class InlineCustomization` with
  `static (string Remaining, AppCustomization Customization) Split(string tail, AppCustomization start)`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/InlineCustomizationTests.cs`:

```csharp
using AppScaffolding;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>Inline options are lifted off the line BEFORE InlineDataSource.Parse sees it,
/// because that treats everything after `from &lt;format&gt;` as the location to end of line.</summary>
[TestClass]
public class InlineCustomizationTests
{
    private static (string Remaining, AppCustomization Customization) Split(string tail)
        => InlineCustomization.Split(tail, AppCustomization.Default);

    [TestMethod]
    public void A_line_with_no_options_is_returned_unchanged()
    {
        var (remaining, customization) = Split("a shop that sells books");

        Assert.AreEqual("a shop that sells books", remaining);
        Assert.AreEqual(AppCustomization.Default, customization);
    }

    [TestMethod]
    public void A_quoted_company_is_taken_whole()
    {
        var (remaining, customization) = Split("a shop company \"Codis Ltd\"");

        Assert.AreEqual("a shop", remaining);
        Assert.AreEqual("Codis Ltd", customization.Company);
    }

    /// <summary>Naming a company adopts the company title pattern: a user who says who they are
    /// means the app to say so, and asking again would be a question with one sensible answer.</summary>
    [TestMethod]
    public void A_company_adopts_the_company_title_pattern()
    {
        var (_, customization) = Split("a shop company Codis");

        Assert.AreEqual(AppCustomization.CompanyTitlePattern, customization.TitlePattern);
        Assert.AreEqual("Codis - Shop", customization.TitleFor("Shop"));
    }

    [TestMethod]
    public void An_explicit_title_wins_over_the_company_pattern()
    {
        var (_, customization) = Split("a shop company Codis title \"{app} at {company}\"");

        Assert.AreEqual("Shop at Codis", customization.TitleFor("Shop"));
    }

    [TestMethod]
    public void Theme_and_accent_are_read()
    {
        var (remaining, customization) = Split("a shop theme dark accent #0B6E4F");

        Assert.AreEqual("a shop", remaining);
        Assert.AreEqual(AppTheme.Dark, customization.Theme);
        Assert.AreEqual("#0B6E4F", customization.Accent);
    }

    [TestMethod]
    public void The_three_flags_are_read()
    {
        var (remaining, customization) = Split("a shop novalidation logging noseed");

        Assert.AreEqual("a shop", remaining);
        Assert.IsFalse(customization.Validation);
        Assert.IsTrue(customization.Logging);
        Assert.IsFalse(customization.SampleSeedData);
    }

    /// <summary>Options sit after the data source, and the location must survive intact —
    /// a Windows path contains spaces and is taken as the rest of the line downstream.</summary>
    [TestMethod]
    public void Options_are_stripped_from_after_a_data_source()
    {
        var (remaining, customization) = Split(
            "orders from csv D:\\my data\\orders.csv company \"Codis Ltd\" noseed");

        Assert.AreEqual("orders from csv D:\\my data\\orders.csv", remaining);
        Assert.AreEqual("Codis Ltd", customization.Company);
        Assert.IsFalse(customization.SampleSeedData);
    }

    /// <summary>`normalize` belongs to InlineDataSource and must be left where it is.</summary>
    [TestMethod]
    public void Normalize_is_left_on_the_line()
    {
        var (remaining, _) = Split("orders from csv orders.csv normalize");

        Assert.AreEqual("orders from csv orders.csv normalize", remaining);
    }

    [TestMethod]
    public void Scanning_stops_at_the_first_unrecognized_token()
    {
        var (remaining, customization) = Split("a shop theme dark and a warehouse");

        Assert.AreEqual("a shop theme dark and a warehouse", remaining);
        Assert.AreEqual(AppTheme.Default, customization.Theme);
    }

    /// <summary>Documented limitation, asserted rather than pretended away: a description whose
    /// final words spell a valid option is read as one. Quoting or reordering avoids it.</summary>
    [TestMethod]
    public void A_description_ending_in_an_option_is_misread()
    {
        var (remaining, customization) = Split("track theme dark");

        Assert.AreEqual("track", remaining);
        Assert.AreEqual(AppTheme.Dark, customization.Theme);
    }

    [TestMethod]
    public void An_unknown_theme_value_leaves_the_theme_alone_and_stops_the_scan()
    {
        var (remaining, customization) = Split("a shop theme mauve");

        Assert.AreEqual("a shop theme mauve", remaining);
        Assert.AreEqual(AppTheme.Default, customization.Theme);
    }

    [TestMethod]
    public void An_icon_path_is_read()
    {
        var (remaining, customization) = Split("a shop icon \"C:\\brand\\logo.ico\"");

        Assert.AreEqual("a shop", remaining);
        Assert.AreEqual("C:\\brand\\logo.ico", customization.IconPath);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter InlineCustomizationTests`

Expected: FAIL — `InlineCustomization` does not exist.

- [ ] **Step 3: Write the implementation**

Create `CodeGenerator/App/InlineCustomization.cs`:

```csharp
using AppScaffolding;

namespace CodeGenerator.App;

/// <summary>
/// Lifts the customization options off a `build app` line, leaving the rest for
/// <c>InlineDataSource.Parse</c>.
/// </summary>
/// <remarks>
/// Runs FIRST, and that ordering is not incidental: <c>InlineDataSource.Parse</c> treats
/// everything after `from &lt;format&gt;` as the location, right to the end of the line, because
/// Windows paths contain spaces. Options left on the line would be swallowed by the path.
/// <para>
/// Scanning is right to left and stops at the first token that is not part of a recognized
/// option — the rule <c>InlineDataSource</c> already uses to strip a trailing `normalize`. The
/// known cost is that a description whose final words spell a valid option is misread
/// ("track theme dark"); quoting or reordering avoids it, and
/// <c>InlineCustomizationTests.A_description_ending_in_an_option_is_misread</c> pins the
/// behaviour so it stays a decision rather than a surprise.
/// </para>
/// </remarks>
internal static class InlineCustomization
{
    /// <summary>Options taking one value.</summary>
    private static readonly string[] ValueKeys =
        ["company", "theme", "accent", "icon", "title"];

    /// <summary>Options that are a bare word.</summary>
    private static readonly string[] Flags =
        ["novalidation", "logging", "noseed"];

    /// <summary>
    /// Splits <paramref name="tail"/> into what remains for the data-source parser and the
    /// customization it asked for, applied over <paramref name="start"/>.
    /// </summary>
    public static (string Remaining, AppCustomization Customization) Split(
        string tail, AppCustomization start)
    {
        var text = (tail ?? string.Empty).Trim();
        var customization = start;

        while (true)
        {
            var stripped = TryStripLast(text, ref customization);

            if (stripped is null)
                return (text, customization);

            text = stripped;
        }
    }

    /// <summary>
    /// Removes one trailing option from <paramref name="text"/>, applying it to
    /// <paramref name="customization"/>. Null when the line no longer ends in one.
    /// </summary>
    private static string? TryStripLast(string text, ref AppCustomization customization)
    {
        if (text.Length == 0)
            return null;

        var (lastWord, beforeLast) = SplitLastToken(text);

        if (Flags.Contains(lastWord, StringComparer.OrdinalIgnoreCase))
        {
            customization = ApplyFlag(customization, lastWord);
            return beforeLast;
        }

        // A value option is two tokens, and the value may be a quoted phrase, so the value is
        // taken first and the key looked for behind it.
        var (value, beforeValue) = SplitLastToken(text);
        var (key, beforeKey) = SplitLastToken(beforeValue);

        if (!ValueKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            return null;

        var applied = ApplyValue(customization, key, Unquote(value));

        // An unusable value (theme mauve) leaves the line alone rather than silently eating two
        // tokens: the user's words are more likely prose than a typo we should guess at.
        if (applied is null)
            return null;

        customization = applied;
        return beforeKey;
    }

    private static AppCustomization ApplyFlag(AppCustomization customization, string flag)
        => flag.ToLowerInvariant() switch
        {
            "novalidation" => customization with { Validation = false },
            "logging" => customization with { Logging = true },
            "noseed" => customization with { SampleSeedData = false },
            _ => customization,
        };

    /// <summary>The customization with <paramref name="key"/> set, or null when
    /// <paramref name="value"/> is not usable for it.</summary>
    private static AppCustomization? ApplyValue(
        AppCustomization customization, string key, string value)
    {
        if (value.Length == 0)
            return null;

        switch (key.ToLowerInvariant())
        {
            case "company":
                // Naming a company adopts the company title pattern unless a title was already
                // given. Options are read right to left, so a `title` later on the line has
                // already been applied by the time this runs — hence the check.
                return customization with
                {
                    Company = value,
                    TitlePattern = customization.TitlePattern == AppCustomization.AppPlaceholder
                        ? AppCustomization.CompanyTitlePattern
                        : customization.TitlePattern,
                };

            case "theme":
                if (string.Equals(value, "light", StringComparison.OrdinalIgnoreCase))
                    return customization with { Theme = AppTheme.Light };
                if (string.Equals(value, "dark", StringComparison.OrdinalIgnoreCase))
                    return customization with { Theme = AppTheme.Dark };
                return null;

            case "accent":
                return ThemePalette.IsValidHexColour(value)
                    ? customization with { Accent = value }
                    : null;

            case "icon":
                return customization with { IconPath = value };

            case "title":
                return customization with { TitlePattern = value };

            default:
                return null;
        }
    }

    /// <summary>
    /// The last token of <paramref name="text"/> and what precedes it. A token is a quoted
    /// phrase when the text ends in a quote, so `company "Codis Ltd"` is two tokens, not three.
    /// </summary>
    private static (string Token, string Before) SplitLastToken(string text)
    {
        var trimmed = text.TrimEnd();

        if (trimmed.Length == 0)
            return (string.Empty, string.Empty);

        if (trimmed[^1] == '"')
        {
            var open = trimmed.LastIndexOf('"', trimmed.Length - 2);

            if (open >= 0)
                return (trimmed[open..], trimmed[..open].TrimEnd());
        }

        var space = trimmed.LastIndexOf(' ');

        return space < 0
            ? (trimmed, string.Empty)
            : (trimmed[(space + 1)..], trimmed[..space].TrimEnd());
    }

    private static string Unquote(string text)
        => text.Length >= 2 && text[0] == '"' && text[^1] == '"'
            ? text[1..^1]
            : text;
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter InlineCustomizationTests`

Expected: PASS, 12 tests.

If `InlineCustomization` is not visible from the test project, check that `CodeGenerator.Tests` already sees `CodeGenerator` internals — `BuildAppDataSourceTests` tests the internal `BuildAppCommand`, so the access already exists; do not add a second `InternalsVisibleTo`.

- [ ] **Step 5: Commit**

```bash
git -C D:\SmartCodeGenerator-Repository add CodeGenerator/App/InlineCustomization.cs CodeGenerator.Tests/App/InlineCustomizationTests.cs
git -C D:\SmartCodeGenerator-Repository commit -m "feat(build-app): parse inline customization options off the command line"
```

---

### Task 3: The remembered profile

**Files:**
- Create: `CodeGenerator/App/CustomizationDefaults.cs`
- Test: `CodeGenerator.Tests/App/CustomizationDefaultsTests.cs`

**Interfaces:**
- Consumes: `AppCustomization` from Task 1.
- Produces: `static class CustomizationDefaults` with `const string FileName = "app-profile.json"`,
  `static string PathFor(string workspaceRoot)`, `static AppCustomization Load(string workspaceRoot)`,
  `static void Save(string workspaceRoot, AppCustomization customization)`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/CustomizationDefaultsTests.cs`:

```csharp
using AppScaffolding;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>The remembered profile. A broken one must leave the CLI working exactly as it does
/// without one — the precedent AiConfig.Load sets.</summary>
[TestClass]
public class CustomizationDefaultsTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void CreateWorkspace()
    {
        _root = Path.Combine(Path.GetTempPath(), "cust-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void RemoveWorkspace()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [TestMethod]
    public void No_profile_yields_the_defaults()
        => Assert.AreEqual(AppCustomization.Default, CustomizationDefaults.Load(_root));

    [TestMethod]
    public void A_saved_profile_round_trips()
    {
        var saved = AppCustomization.Default with
        {
            Company = "Codis Ltd",
            Theme = AppTheme.Dark,
            Accent = "#0B6E4F",
            TitlePattern = AppCustomization.CompanyTitlePattern,
            Logging = true,
            SampleSeedData = false,
        };

        CustomizationDefaults.Save(_root, saved);

        Assert.AreEqual(saved, CustomizationDefaults.Load(_root));
    }

    [TestMethod]
    public void The_profile_is_written_under_dot_codegen()
    {
        CustomizationDefaults.Save(_root, AppCustomization.Default with { Company = "Codis" });

        Assert.IsTrue(File.Exists(Path.Combine(_root, ".codegen", CustomizationDefaults.FileName)));
    }

    [TestMethod]
    public void A_corrupt_profile_yields_the_defaults()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".codegen"));
        File.WriteAllText(
            Path.Combine(_root, ".codegen", CustomizationDefaults.FileName), "{ not json");

        Assert.AreEqual(AppCustomization.Default, CustomizationDefaults.Load(_root));
    }

    /// <summary>A profile written by an older build, missing the fields a newer one added,
    /// must fill them from the defaults rather than with nulls.</summary>
    [TestMethod]
    public void A_partial_profile_fills_the_rest_from_the_defaults()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".codegen"));
        File.WriteAllText(
            Path.Combine(_root, ".codegen", CustomizationDefaults.FileName),
            "{ \"company\": \"Codis Ltd\" }");

        var loaded = CustomizationDefaults.Load(_root);

        Assert.AreEqual("Codis Ltd", loaded.Company);
        Assert.IsTrue(loaded.Validation);
        Assert.AreEqual(AppTheme.Default, loaded.Theme);
    }

    [TestMethod]
    public void Saving_into_an_unwritable_root_does_not_throw()
        => CustomizationDefaults.Save(
            Path.Combine(_root, "no", "such", "\0bad"), AppCustomization.Default);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter CustomizationDefaultsTests`

Expected: FAIL — `CustomizationDefaults` does not exist.

- [ ] **Step 3: Write the implementation**

Create `CodeGenerator/App/CustomizationDefaults.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using AppScaffolding;

namespace CodeGenerator.App;

/// <summary>
/// The customization a previous run settled on, remembered under
/// <c>.codegen/app-profile.json</c> beside the existing <c>learned.json</c>.
/// </summary>
/// <remarks>
/// Read only to pre-fill the wizard's prompts, and never on a redirected-stdin run: a profile
/// silently changing a scripted build's output would break the determinism `normalize` is
/// already careful about. Never throws — a missing or unreadable profile yields the defaults,
/// as <c>AiConfig.Load</c> does, because a broken config must leave the CLI working exactly as
/// it does without one.
/// </remarks>
internal static class CustomizationDefaults
{
    public const string FileName = "app-profile.json";
    private const string FolderName = ".codegen";

    /// <summary>The shape on disk. Nullable throughout so a profile written by an older build,
    /// missing a field a newer one added, fills that field from the defaults rather than with a
    /// null or a zero.</summary>
    private sealed class Profile
    {
        public string? Company { get; set; }
        public string? Theme { get; set; }
        public string? Accent { get; set; }
        public string? TitlePattern { get; set; }
        public string? IconPath { get; set; }
        public bool? Validation { get; set; }
        public bool? Logging { get; set; }
        public bool? SampleSeedData { get; set; }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string PathFor(string workspaceRoot)
        => Path.Combine(workspaceRoot, FolderName, FileName);

    public static AppCustomization Load(string workspaceRoot)
    {
        try
        {
            var path = PathFor(workspaceRoot);

            if (!File.Exists(path))
                return AppCustomization.Default;

            var profile = JsonSerializer.Deserialize<Profile>(File.ReadAllText(path), Options);

            if (profile is null)
                return AppCustomization.Default;

            var fallback = AppCustomization.Default;

            return new AppCustomization(
                Company: profile.Company ?? fallback.Company,
                Theme: ParseTheme(profile.Theme, fallback.Theme),
                Accent: profile.Accent ?? fallback.Accent,
                TitlePattern: profile.TitlePattern ?? fallback.TitlePattern,
                IconPath: profile.IconPath ?? fallback.IconPath,
                Validation: profile.Validation ?? fallback.Validation,
                Logging: profile.Logging ?? fallback.Logging,
                SampleSeedData: profile.SampleSeedData ?? fallback.SampleSeedData);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return AppCustomization.Default;
        }
    }

    public static void Save(string workspaceRoot, AppCustomization customization)
    {
        try
        {
            var path = PathFor(workspaceRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var profile = new Profile
            {
                Company = customization.Company,
                Theme = customization.Theme.ToString(),
                Accent = customization.Accent,
                TitlePattern = customization.TitlePattern,
                IconPath = customization.IconPath,
                Validation = customization.Validation,
                Logging = customization.Logging,
                SampleSeedData = customization.SampleSeedData,
            };

            File.WriteAllText(path, JsonSerializer.Serialize(profile, Options));
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or ArgumentException
                     or NotSupportedException)
        {
            // A profile that cannot be written is not worth failing a build over.
        }
    }

    private static AppTheme ParseTheme(string? value, AppTheme fallback)
        => Enum.TryParse<AppTheme>(value, ignoreCase: true, out var theme) ? theme : fallback;
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter CustomizationDefaultsTests`

Expected: PASS, 6 tests.

- [ ] **Step 5: Commit**

```bash
git -C D:\SmartCodeGenerator-Repository add CodeGenerator/App/CustomizationDefaults.cs CodeGenerator.Tests/App/CustomizationDefaultsTests.cs
git -C D:\SmartCodeGenerator-Repository commit -m "feat(build-app): remember customization defaults in .codegen/app-profile.json"
```

---

### Task 4: Plumbing — ScaffoldRequest and the target interface

Threads customization to where it is needed **without changing any output yet**. The deliverable is that every existing test still passes.

**Files:**
- Create: `AppScaffolding/ScaffoldRequest.cs`
- Modify: `AppScaffolding/AppScaffolder.cs`, `AppScaffolding/ITargetScaffolder.cs`
- Test: `CodeGenerator.Tests/App/ScaffoldRequestTests.cs`

**Interfaces:**
- Consumes: `AppCustomization` from Task 1.
- Produces:
  - `sealed record ScaffoldRequest(CandidateDomainModel Model, string AppName, string WorkspaceRoot, ITargetScaffolder Target, PersistenceKind Persistence, ImportedData? ImportedData, AppCustomization Customization, int Year)`
  - `ScaffoldResult AppScaffolder.Scaffold(ScaffoldRequest request)`
  - `ITargetScaffolder.FrontEndFiles(string, IReadOnlyList<string>, AppCustomization)` default method.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/ScaffoldRequestTests.cs`:

```csharp
using AppScaffolding;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>The request object and the customization-aware target overload. A target that does
/// not care about customization must keep working untouched, which is what the default
/// interface method buys.</summary>
[TestClass]
public class ScaffoldRequestTests
{
    [TestMethod]
    public void A_request_defaults_to_neutral_customization()
    {
        var request = ScaffoldRequest.For(
            new DomainDiscovery.Model.CandidateDomainModel(), "Shop", "C:\\ws", new ConsoleTarget());

        Assert.AreEqual(AppCustomization.Default, request.Customization);
        Assert.AreEqual(PersistenceKind.Json, request.Persistence);
    }

    /// <summary>ClassLibraryTarget does not override the three-argument overload; the default
    /// interface method must forward it to the two-argument one.</summary>
    [TestMethod]
    public void An_unaware_target_forwards_to_its_two_argument_overload()
    {
        ITargetScaffolder target = new ClassLibraryTarget();

        var plain = target.FrontEndFiles("Shop", ["Order"]);
        var customized = target.FrontEndFiles("Shop", ["Order"], AppCustomization.Default);

        Assert.HasCount(plain.Count, customized);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter ScaffoldRequestTests`

Expected: FAIL — `ScaffoldRequest` does not exist.

- [ ] **Step 3: Create the request record**

Create `AppScaffolding/ScaffoldRequest.cs`:

```csharp
using DomainDiscovery.Model;

namespace AppScaffolding;

/// <summary>
/// Everything one <see cref="AppScaffolder.Scaffold(ScaffoldRequest)"/> run needs.
/// </summary>
/// <remarks>
/// A parameter object rather than a seventh and eighth argument: the overload chain already
/// stood at four overloads and six arguments before customization, and the older overloads are
/// kept only as thin wrappers over this.
/// <para>
/// <paramref name="Year"/> is supplied rather than read from the clock inside a template,
/// so the copyright banner cannot turn golden-output tests red on 1 January.
/// </para>
/// </remarks>
public sealed record ScaffoldRequest(
    CandidateDomainModel Model,
    string AppName,
    string WorkspaceRoot,
    ITargetScaffolder Target,
    PersistenceKind Persistence,
    ImportedData? ImportedData,
    AppCustomization Customization,
    int Year)
{
    /// <summary>A request with today's defaults: JSON persistence, no imported data, neutral
    /// customization, and the current year for the copyright banner.</summary>
    public static ScaffoldRequest For(
        CandidateDomainModel model, string appName, string workspaceRoot, ITargetScaffolder target)
        => new(model, appName, workspaceRoot, target, PersistenceKind.Json,
               ImportedData: null, AppCustomization.Default, DateTime.Now.Year);
}
```

- [ ] **Step 4: Add the target overload**

In `AppScaffolding/ITargetScaffolder.cs`, immediately after the existing `FrontEndFiles` declaration (line 28), add:

```csharp
    /// <summary>
    /// As <see cref="FrontEndFiles(string,IReadOnlyList{string})"/>, for a target that varies its
    /// front end with <paramref name="customization"/>.
    /// </summary>
    /// <remarks>
    /// A default interface method, like <see cref="FixupProject"/> above it: only the targets
    /// with a window, a page or a banner override this, and the rest are untouched.
    /// </remarks>
    IReadOnlyList<GeneratedFile> FrontEndFiles(
        string projectName, IReadOnlyList<string> entityNames, AppCustomization customization)
        => FrontEndFiles(projectName, entityNames);
```

- [ ] **Step 5: Route AppScaffolder through the request**

In `AppScaffolding/AppScaffolder.cs`, replace the signature of the six-argument `Scaffold` (line 121-123) so the body becomes the request-taking method, and keep the old signature as a wrapper. Change:

```csharp
    public ScaffoldResult Scaffold(
        CandidateDomainModel model, string appName, string workspaceRoot, ITargetScaffolder target,
        PersistenceKind persistence, ImportedData? importedData)
    {
        var classes = DomainModelMapper.ToClassDefinitions(model, out var classNames);
```

to:

```csharp
    /// <summary>Backward-compatible overload: neutral customization, current year.</summary>
    public ScaffoldResult Scaffold(
        CandidateDomainModel model, string appName, string workspaceRoot, ITargetScaffolder target,
        PersistenceKind persistence, ImportedData? importedData)
        => Scaffold(new ScaffoldRequest(
            model, appName, workspaceRoot, target, persistence, importedData,
            AppCustomization.Default, DateTime.Now.Year));

    /// <summary>
    /// Creates a project named <c>&lt;AppName&gt;&lt;Target.FolderSuffix&gt;</c> under
    /// <see cref="ScaffoldRequest.WorkspaceRoot"/> (reusing it if present) and writes the shared
    /// model + data core plus the front end supplied by the target, shaped by
    /// <see cref="ScaffoldRequest.Customization"/>.
    /// </summary>
    public ScaffoldResult Scaffold(ScaffoldRequest request)
    {
        var model = request.Model;
        var appName = request.AppName;
        var workspaceRoot = request.WorkspaceRoot;
        var target = request.Target;
        var persistence = request.Persistence;
        var importedData = request.ImportedData;

        var classes = DomainModelMapper.ToClassDefinitions(model, out var classNames);
```

The rest of the method body is unchanged in this task, except line 218, where the target call becomes customization-aware:

```csharp
        foreach (var file in target.FrontEndFiles(projectName, entityNames, request.Customization))
            files.Add((Path.Combine(outputFolder, file.RelativeFolder), file.Name, file.Source));
```

- [ ] **Step 6: Run the new test and the whole suite**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj`

Expected: PASS, including the two new tests. **No existing test may be edited to make this pass** — this task changes no output.

- [ ] **Step 7: Commit**

```bash
git -C D:\SmartCodeGenerator-Repository add AppScaffolding/ScaffoldRequest.cs AppScaffolding/ITargetScaffolder.cs AppScaffolding/AppScaffolder.cs CodeGenerator.Tests/App/ScaffoldRequestTests.cs
git -C D:\SmartCodeGenerator-Repository commit -m "refactor(scaffold): take a ScaffoldRequest and pass customization to targets"
```

---

### Task 5: Branding — csproj metadata and file headers

**Files:**
- Modify: `AppScaffolding/CsprojEditing.cs`, `AppScaffolding/AppScaffolder.cs`
- Create: `CodeGenerator.Tests/App/TestModels.cs` (shared fixture, used by Tasks 5, 8, 9, 10, 11)
- Test: `CodeGenerator.Tests/App/CsprojEditingTests.cs` (create if absent), `CodeGenerator.Tests/App/FileHeaderInjectionTests.cs`

**Interfaces:**
- Consumes: `FileHeader.For`, `AppCustomization.HasBranding`, `ScaffoldRequest.Year`.
- Produces: `CsprojEditing.InjectProperties(string csproj, string company, int year)`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/CsprojEditingTests.cs`:

```csharp
using AppScaffolding;

namespace CodeGenerator.Tests.App;

/// <summary>Branding properties are inserted with the same idempotent
/// insert-before-&lt;/Project&gt; edit InjectPackageReference and InjectSeedContent use.</summary>
[TestClass]
public class CsprojEditingTests
{
    private const string Csproj = """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
""";

    [TestMethod]
    public void Branding_properties_are_inserted()
    {
        var updated = CsprojEditing.InjectProperties(Csproj, "Codis Ltd", 2026);

        StringAssert.Contains(updated, "<Company>Codis Ltd</Company>");
        StringAssert.Contains(updated, "<Authors>Codis Ltd</Authors>");
        StringAssert.Contains(updated, "<Copyright>Copyright (c) 2026 Codis Ltd.</Copyright>");
    }

    [TestMethod]
    public void Injection_is_idempotent()
    {
        var once = CsprojEditing.InjectProperties(Csproj, "Codis Ltd", 2026);
        var twice = CsprojEditing.InjectProperties(once, "Codis Ltd", 2026);

        Assert.AreEqual(once, twice);
    }

    [TestMethod]
    public void No_company_leaves_the_project_file_alone()
        => Assert.AreEqual(Csproj, CsprojEditing.InjectProperties(Csproj, string.Empty, 2026));

    /// <summary>An ampersand or angle bracket in a company name must not produce invalid XML.</summary>
    [TestMethod]
    public void A_company_name_is_xml_escaped()
    {
        var updated = CsprojEditing.InjectProperties(Csproj, "Smith & Sons <Ltd>", 2026);

        StringAssert.Contains(updated, "<Company>Smith &amp; Sons &lt;Ltd&gt;</Company>");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter CsprojEditingTests`

Expected: FAIL — `InjectProperties` does not exist.

- [ ] **Step 3: Implement InjectProperties**

Add to `AppScaffolding/CsprojEditing.cs`, after `InjectSeedContent`:

```csharp
    /// <summary>
    /// Adds the branding properties for <paramref name="company"/> before the closing
    /// <c>&lt;/Project&gt;</c> tag. Idempotent, a no-op when there is no company, and a no-op on
    /// a project file with no closing tag.
    /// </summary>
    /// <remarks>
    /// <paramref name="year"/> is a parameter rather than read from the clock, so a golden
    /// project file does not change on 1 January. The company is XML-escaped: "Smith &amp; Sons"
    /// would otherwise produce a project file MSBuild cannot parse.
    /// </remarks>
    public static string InjectProperties(string csproj, string company, int year)
    {
        if (company.Length == 0 || csproj.Contains("<Company>", StringComparison.Ordinal))
            return csproj;

        var escaped = Escape(company);

        var group =
            "  <PropertyGroup>\n" +
            $"    <Company>{escaped}</Company>\n" +
            $"    <Authors>{escaped}</Authors>\n" +
            $"    <Copyright>Copyright (c) {year} {escaped}.</Copyright>\n" +
            "  </PropertyGroup>\n";

        var close = csproj.LastIndexOf("</Project>", StringComparison.Ordinal);
        return close < 0 ? csproj : csproj.Insert(close, group);
    }

    private static string Escape(string value)
        => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter CsprojEditingTests`

Expected: PASS, 4 tests.

- [ ] **Step 5: Write the failing header test**

Create `CodeGenerator.Tests/App/FileHeaderInjectionTests.cs`:

```csharp
using AppScaffolding;
using AppScaffolding.Targets;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

/// <summary>The copyright banner is prepended to generated C# only. MainWindow.xaml and the
/// Data/Seed/*.json files go through the same WriteSource, and a // banner corrupts both.</summary>
[TestClass]
public class FileHeaderInjectionTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void CreateWorkspace()
    {
        _root = Path.Combine(Path.GetTempPath(), "hdr-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void RemoveWorkspace()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [TestMethod]
    public void Generated_cs_files_carry_the_banner()
    {
        var request = ScaffoldRequest.For(
                TestModels.OneEntity(), "Shop", _root, new ClassLibraryTarget())
            with { Customization = AppCustomization.Default with { Company = "Codis Ltd" }, Year = 2026 };

        var result = new AppScaffolder().Scaffold(request);

        Assert.IsTrue(result.Success, result.Message);

        var order = File.ReadAllText(Path.Combine(result.ProjectPath, "Models", "Order.cs"));
        StringAssert.StartsWith(order, "// Copyright (c) 2026 Codis Ltd.");
    }

    [TestMethod]
    public void Non_cs_files_do_not_carry_the_banner()
    {
        var request = ScaffoldRequest.For(TestModels.OneEntity(), "Shop", _root, new WpfTarget())
            with { Customization = AppCustomization.Default with { Company = "Codis Ltd" }, Year = 2026 };

        var result = new AppScaffolder().Scaffold(request);

        var xaml = File.ReadAllText(Path.Combine(result.ProjectPath, "Views", "MainWindow.xaml"));
        Assert.IsFalse(xaml.StartsWith("//", StringComparison.Ordinal));
    }

    [TestMethod]
    public void No_company_means_no_banner()
    {
        var request = ScaffoldRequest.For(
            TestModels.OneEntity(), "Shop", _root, new ClassLibraryTarget());

        var result = new AppScaffolder().Scaffold(request);

        var order = File.ReadAllText(Path.Combine(result.ProjectPath, "Models", "Order.cs"));
        Assert.IsFalse(order.StartsWith("//", StringComparison.Ordinal));
    }
}
```

**Create the shared fixture first.** Every later task's scaffolder test needs a model, so create `CodeGenerator.Tests/App/TestModels.cs` now rather than five near-copies:

```csharp
using AppScaffolding;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

/// <summary>Minimal fixtures for the scaffolder tests. One place, so a change to how a
/// CandidateDomainModel is built does not have to be made in five test files.</summary>
internal static class TestModels
{
    /// <summary>A model with a single entity carrying a Name field.</summary>
    public static CandidateDomainModel OneEntity(string name = "Order")
    {
        // Build this the way CodeGenerator.Tests/App/AppScaffolderWriteTests.cs already does —
        // read that file and copy its construction verbatim rather than inventing a second one.
        throw new NotImplementedException("Copy from AppScaffolderWriteTests.");
    }

    /// <summary>ImportedData carrying one row for <paramref name="entityName"/>. Copy the
    /// construction from CodeGenerator.Tests/App/SeedFileWriterTests.cs.</summary>
    public static ImportedData OneImportedRow(string entityName)
    {
        throw new NotImplementedException("Copy from SeedFileWriterTests.");
    }
}
```

Replace both bodies with the real construction from those two files before running any test in this task. They are the only two fixtures in this plan whose exact code depends on a type this plan does not define; both have an existing in-repo example to copy, named above.

- [ ] **Step 6: Run it to verify it fails**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter FileHeaderInjectionTests`

Expected: FAIL — no banner is written.

- [ ] **Step 7: Implement header injection and csproj branding**

In `AppScaffolding/AppScaffolder.cs`:

1. Add a field for the current run's banner, set at the top of `Scaffold(ScaffoldRequest)`:

```csharp
        // Prepended to generated C# only. XAML and the Data/Seed/*.json files go through the
        // same WriteSource, and a `//` banner would corrupt both.
        var header = FileHeader.For(request.Customization.Company, request.Year);
```

2. Change `WriteSource` to a static method taking the banner:

```csharp
    private static string? WriteSource(
        string outputFolder, string fileName, string source, string header = "")
    {
        try
        {
            Directory.CreateDirectory(outputFolder);
            var path = Path.Combine(outputFolder, fileName);

            var content = header.Length > 0
                          && fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                ? header + source
                : source;

            File.WriteAllText(path, content);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
```

3. Pass `header` at each generated-C# call site inside `Scaffold(ScaffoldRequest)`: the `WriteClasses` call, the `files` loop, and the custom-validator-stub loop. Leave the seed-JSON loop passing nothing.

4. `WriteClasses` gains an optional header parameter and forwards it:

```csharp
    public IReadOnlyList<string> WriteClasses(
        string outputFolder, IReadOnlyList<ClassDefinition> classes, string namespaceName,
        string header = "")
```

5. After the `InjectEfCorePackage` call, brand the project file:

```csharp
        if (request.Customization.HasBranding)
            InjectBranding(csprojPath, request.Customization.Company, request.Year);
```

with:

```csharp
    /// <summary>Best-effort branding of the app project file. Silent on failure: a project that
    /// could not be branded still builds and runs.</summary>
    private static void InjectBranding(string csprojPath, string company, int year)
    {
        try
        {
            if (!File.Exists(csprojPath))
                return;

            File.WriteAllText(
                csprojPath,
                CsprojEditing.InjectProperties(File.ReadAllText(csprojPath), company, year));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
```

- [ ] **Step 8: Run the tests**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj`

Expected: PASS. Existing tests must not need edits — with no company, `header` is empty and `InjectBranding` never runs.

- [ ] **Step 9: Commit**

```bash
git -C D:\SmartCodeGenerator-Repository add AppScaffolding/CsprojEditing.cs AppScaffolding/AppScaffolder.cs CodeGenerator.Tests/App/CsprojEditingTests.cs CodeGenerator.Tests/App/FileHeaderInjectionTests.cs
git -C D:\SmartCodeGenerator-Repository commit -m "feat(scaffold): brand the project file and generated C# with the company name"
```

---

### Task 6: Titles across the four UI targets

**Files:**
- Modify: `AppScaffolding/MainWindowTemplate.cs`, `WinFormsMainFormTemplate.cs`, `ConsoleProgramTemplate.cs`, `WebApiProgramTemplate.cs`, and the four targets in `AppScaffolding/Targets/`
- Test: `CodeGenerator.Tests/App/CustomizedTitleTests.cs`

**Interfaces:**
- Consumes: `AppCustomization.TitleFor` (Task 1), the 3-arg `FrontEndFiles` (Task 4).
- Produces: `MainWindowTemplate.Xaml(string appName, string title)`, `WinFormsMainFormTemplate.Source(string appName, string title)`, `ConsoleProgramTemplate.Build(string projectName, IReadOnlyList<string> entityNames, string title)`, `WebApiProgramTemplate.Build(string projectName, IReadOnlyList<string> entityNames, string title)`. In every case the original overload remains and forwards with `title: appName`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/CustomizedTitleTests.cs`:

```csharp
using AppScaffolding;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>The company-derived title reaches every front end that has somewhere to show it.</summary>
[TestClass]
public class CustomizedTitleTests
{
    private static readonly AppCustomization Branded = AppCustomization.Default with
    {
        Company = "Codis Ltd",
        TitlePattern = AppCustomization.CompanyTitlePattern,
    };

    private static string SourceOf(ITargetScaffolder target, string name)
        => target.FrontEndFiles("Shop", ["Order"], Branded).Single(f => f.Name == name).Source;

    [TestMethod]
    public void The_wpf_window_title_carries_the_company()
        => StringAssert.Contains(
            SourceOf(new WpfTarget(), "MainWindow.xaml"), "Title=\"Codis Ltd - Shop\"");

    [TestMethod]
    public void The_winforms_form_title_carries_the_company()
        => StringAssert.Contains(
            SourceOf(new WinFormsTarget(), "MainForm.cs"), "Text = \"Codis Ltd - Shop\";");

    [TestMethod]
    public void The_console_banner_carries_the_company()
        => StringAssert.Contains(
            SourceOf(new ConsoleTarget(), "Program.cs"), "Codis Ltd - Shop");

    /// <summary>The generated Web API has no OpenAPI document to title, so the title is served
    /// from the root instead. Emitted only when it differs from the bare project name.</summary>
    [TestMethod]
    public void The_web_api_serves_its_title_from_the_root()
        => StringAssert.Contains(
            SourceOf(new WebApiTarget(), "Program.cs"),
            "app.MapGet(\"/\", () => \"Codis Ltd - Shop\");");

    [TestMethod]
    public void An_unbranded_web_api_has_no_root_endpoint()
        => Assert.IsFalse(
            new WebApiTarget().FrontEndFiles("Shop", ["Order"], AppCustomization.Default)
                .Single(f => f.Name == "Program.cs").Source
                .Contains("app.MapGet(\"/\"", StringComparison.Ordinal));

    /// <summary>A quote in a title must not break out of the emitted string literal.</summary>
    [TestMethod]
    public void A_quote_in_a_title_does_not_break_the_emitted_code()
    {
        var quoted = AppCustomization.Default with { Company = "Bob\"s", TitlePattern = "{company}" };

        var source = new WebApiTarget()
            .FrontEndFiles("Shop", ["Order"], quoted)
            .Single(f => f.Name == "Program.cs").Source;

        Assert.IsFalse(source.Contains("\"Bob\"s\"", StringComparison.Ordinal));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter CustomizedTitleTests`

Expected: FAIL — the titles are still the bare project name.

- [ ] **Step 3: Add a literal escaper**

Add to `AppScaffolding/AppCustomization.cs`:

```csharp
    /// <summary>The title as a C# string-literal body — a quote or backslash in a company name
    /// would otherwise break out of the literal it is emitted into.</summary>
    public string TitleLiteralFor(string appName)
        => TitleFor(appName).Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>The title as an XML attribute value.</summary>
    public string TitleAttributeFor(string appName)
        => TitleFor(appName)
            .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
```

- [ ] **Step 4: Thread the title through the four templates**

`MainWindowTemplate.Xaml` — add an overload and change the `Title=` line:

```csharp
    public static string Xaml(string appName) => Xaml(appName, appName);

    /// <summary>Source of <c>Views/MainWindow.xaml</c>, titled <paramref name="title"/>.</summary>
    public static string Xaml(string appName, string title) => $$"""
<Window x:Class="{{appName}}.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="{{title}}" Height="600" Width="900">
```

(the rest of the XAML is unchanged.)

`WinFormsMainFormTemplate.Source` — same shape; line 33 becomes `Text = "{{title}}";`.

`ConsoleProgramTemplate.Build` — add the three-argument overload; inside the emitted top-level statements, print the banner just before the `while (true)` loop:

```csharp
Console.WriteLine("{{title}}");
```

`WebApiProgramTemplate.Build` — add the three-argument overload and emit, immediately after `SeedData.EnsureSeeded();`:

```csharp
{{rootEndpoint}}
```

built in C# as:

```csharp
        // The generated Web API has no OpenAPI document to carry a title, so a branded run
        // serves the title from the root. An unbranded run adds no endpoint at all, leaving
        // today's output untouched.
        var rootEndpoint = title == projectName
            ? string.Empty
            : $"app.MapGet(\"/\", () => \"{title}\");\n";
```

- [ ] **Step 5: Override FrontEndFiles in the four targets**

`WpfTarget`:

```csharp
    public IReadOnlyList<GeneratedFile> FrontEndFiles(
        string projectName, IReadOnlyList<string> entityNames, AppCustomization customization)
    {
        var files = DesktopViewModelFiles.Build(
            projectName, entityNames, ViewModelTemplates.RelayCommand(projectName));

        files.Add(new("Views", "MainWindow.xaml",
            MainWindowTemplate.Xaml(projectName, customization.TitleAttributeFor(projectName))));
        files.Add(new("Views", "MainWindow.xaml.cs", MainWindowTemplate.CodeBehind(projectName)));

        return files;
    }
```

Keep the existing two-argument method delegating: `=> FrontEndFiles(projectName, entityNames, AppCustomization.Default);`

Do the equivalent in `WinFormsTarget` (using `TitleLiteralFor`), `ConsoleTarget` (`TitleLiteralFor`) and `WebApiTarget` (`TitleLiteralFor`).

- [ ] **Step 6: Run the tests**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj`

Expected: PASS, including the existing `WpfTargetTests`, `WinFormsTargetTests`, `ConsoleTargetTests`, `WebApiTargetTests` unchanged.

- [ ] **Step 7: Compile-test the branded front ends**

Add to `CustomizedTitleTests`:

```csharp
    /// <summary>A branded Web API Program.cs must still compile — the root endpoint is emitted
    /// into top-level statements, where order and syntax both matter.</summary>
    [TestMethod]
    public void A_branded_web_api_program_compiles()
    {
        var source = new WebApiTarget()
            .FrontEndFiles("Acme", ["Order"], Branded)
            .Single(f => f.Name == "Program.cs").Source;

        EmittedSource.AssertCompiles(EmittedSource.Flatten(
            WebApiCompileStubs, source));
    }
```

Build `WebApiCompileStubs` the way the existing `WebApiTargetTests` does — read that file and reuse its stub set rather than writing a second one. If `WebApiTargetTests` has no compile test, assert on the emitted text only and note that in the test's summary.

- [ ] **Step 8: Commit**

```bash
git -C D:\SmartCodeGenerator-Repository add AppScaffolding CodeGenerator.Tests/App/CustomizedTitleTests.cs
git -C D:\SmartCodeGenerator-Repository commit -m "feat(scaffold): title the generated front ends with the company name"
```

---

### Task 7: Theme and accent

**Files:**
- Create: `AppScaffolding/ThemeXamlTemplate.cs`
- Modify: `AppScaffolding/Targets/WpfTarget.cs`, `WinFormsTarget.cs`, `AppScaffolding/WinFormsMainFormTemplate.cs`, `AppScaffolding/WpfProjectFixup.cs`
- Test: `CodeGenerator.Tests/App/ThemeTests.cs`

**Interfaces:**
- Consumes: `ThemePalette.Resolve` (Task 1).
- Produces: `ThemeXamlTemplate.Source(ThemePalette palette)`; `WpfProjectFixup.Apply(string projectFolder, bool mergeTheme)`; `WinFormsMainFormTemplate.Source(string appName, string title, ThemePalette? palette)`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/ThemeTests.cs`:

```csharp
using AppScaffolding;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>Theme resources are emitted only when something was themed, so an unthemed run is
/// byte-identical to the output before customization existed.</summary>
[TestClass]
public class ThemeTests
{
    private static readonly AppCustomization Dark =
        AppCustomization.Default with { Theme = AppTheme.Dark, Accent = "#0B6E4F" };

    [TestMethod]
    public void An_unthemed_wpf_app_gets_no_theme_file()
        => Assert.IsFalse(
            new WpfTarget().FrontEndFiles("Shop", ["Order"], AppCustomization.Default)
                .Any(f => f.Name == "Theme.xaml"));

    [TestMethod]
    public void A_themed_wpf_app_gets_a_theme_file()
    {
        var theme = new WpfTarget().FrontEndFiles("Shop", ["Order"], Dark)
            .Single(f => f.Name == "Theme.xaml");

        Assert.AreEqual("Views", theme.RelativeFolder);
        StringAssert.Contains(theme.Source, "#0B6E4F");
        StringAssert.Contains(theme.Source, "ResourceDictionary");
    }

    /// <summary>The window has to consume the dictionary or the palette is dead markup.</summary>
    [TestMethod]
    public void A_themed_window_binds_to_the_theme_brushes()
    {
        var xaml = new WpfTarget().FrontEndFiles("Shop", ["Order"], Dark)
            .Single(f => f.Name == "MainWindow.xaml").Source;

        StringAssert.Contains(xaml, "{DynamicResource AppBackgroundBrush}");
    }

    [TestMethod]
    public void An_unthemed_window_is_unchanged()
    {
        var xaml = new WpfTarget().FrontEndFiles("Shop", ["Order"], AppCustomization.Default)
            .Single(f => f.Name == "MainWindow.xaml").Source;

        Assert.IsFalse(xaml.Contains("DynamicResource", StringComparison.Ordinal));
    }

    [TestMethod]
    public void A_themed_winforms_app_sets_its_colours()
    {
        var source = new WinFormsTarget().FrontEndFiles("Shop", ["Order"], Dark)
            .Single(f => f.Name == "MainForm.cs").Source;

        StringAssert.Contains(source, "BackColor = ColorTranslator.FromHtml(");
    }

    [TestMethod]
    public void An_unthemed_winforms_app_sets_no_colours()
        => Assert.IsFalse(
            new WinFormsTarget().FrontEndFiles("Shop", ["Order"], AppCustomization.Default)
                .Single(f => f.Name == "MainForm.cs").Source
                .Contains("ColorTranslator", StringComparison.Ordinal));
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter ThemeTests`

Expected: FAIL — no theme is emitted.

- [ ] **Step 3: Create the theme dictionary template**

Create `AppScaffolding/ThemeXamlTemplate.cs`:

```csharp
namespace AppScaffolding;

/// <summary>
/// Emits <c>Views/Theme.xaml</c>: the palette as a ResourceDictionary of brushes.
/// </summary>
/// <remarks>
/// A separate dictionary rather than brushes written into MainWindow.xaml, so that window stays
/// the static, entity-agnostic file it has always been and an unthemed app keeps it byte for
/// byte. <c>WpfProjectFixup</c> merges this into App.xaml, which it already rewrites.
/// </remarks>
public static class ThemeXamlTemplate
{
    public const string FileName = "Theme.xaml";

    public static string Source(ThemePalette palette) => $"""
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <SolidColorBrush x:Key="AppBackgroundBrush" Color="{palette.Background}" />
    <SolidColorBrush x:Key="AppSurfaceBrush" Color="{palette.Surface}" />
    <SolidColorBrush x:Key="AppForegroundBrush" Color="{palette.Foreground}" />
    <SolidColorBrush x:Key="AppGridLineBrush" Color="{palette.GridLine}" />
    <SolidColorBrush x:Key="AppAccentBrush" Color="{palette.Accent}" />
    <SolidColorBrush x:Key="AppAccentForegroundBrush" Color="{palette.AccentForeground}" />
</ResourceDictionary>
""";
}
```

- [ ] **Step 4: Make MainWindow.xaml theme-aware**

Give `MainWindowTemplate.Xaml` a third parameter, `bool themed`, and interpolate two fragments built in C#:

```csharp
    public static string Xaml(string appName, string title, bool themed = false)
    {
        // Emitted only for a themed app: an unthemed window must stay byte-identical to the one
        // this generator has always produced.
        var windowBrushes = themed
            ? "\n        Background=\"{DynamicResource AppBackgroundBrush}\""
              + "\n        Foreground=\"{DynamicResource AppForegroundBrush}\""
            : string.Empty;

        var gridBrushes = themed
            ? "\n                              Background=\"{DynamicResource AppSurfaceBrush}\""
              + "\n                              Foreground=\"{DynamicResource AppForegroundBrush}\""
              + "\n                              HorizontalGridLinesBrush=\"{DynamicResource AppGridLineBrush}\""
              + "\n                              VerticalGridLinesBrush=\"{DynamicResource AppGridLineBrush}\""
            : string.Empty;

        return $$"""
...Title="{{title}}" Height="600" Width="900"{{windowBrushes}}>
...
                    <DataGrid ItemsSource="{Binding Items}"{{gridBrushes}}
...
""";
    }
```

Keep the two-argument overload forwarding with `themed: false`.

- [ ] **Step 5: Emit the dictionary from WpfTarget and merge it**

In `WpfTarget.FrontEndFiles(projectName, entityNames, customization)`:

```csharp
        var palette = ThemePalette.Resolve(customization.Theme, customization.Accent);

        files.Add(new("Views", "MainWindow.xaml",
            MainWindowTemplate.Xaml(
                projectName, customization.TitleAttributeFor(projectName), palette is not null)));

        if (palette is not null)
            files.Add(new("Views", ThemeXamlTemplate.FileName, ThemeXamlTemplate.Source(palette)));
```

`FixupProject` runs before the front-end files are written and does not know the customization, so the merge is driven by the file being present. In `WpfProjectFixup`, add:

```csharp
    private const string MergeMarker = "Views/Theme.xaml";

    /// <summary>
    /// Merges <c>Views/Theme.xaml</c> into <c>App.xaml</c>'s resources when the scaffolder wrote
    /// one. Best-effort and idempotent, like the StartupUri repoint above it.
    /// </summary>
    /// <remarks>
    /// Called after the front-end files are written, not from <see cref="Apply"/>: the theme file
    /// does not exist yet when the project is first created.
    /// </remarks>
    public static void MergeTheme(string projectFolder)
    {
        try
        {
            var appXamlPath = Path.Combine(projectFolder, "App.xaml");

            if (!File.Exists(appXamlPath)
                || !File.Exists(Path.Combine(projectFolder, "Views", ThemeXamlTemplate.FileName)))
                return;

            var xaml = File.ReadAllText(appXamlPath);

            if (xaml.Contains(MergeMarker, StringComparison.Ordinal))
                return;

            const string open = "<Application.Resources>";
            var index = xaml.IndexOf(open, StringComparison.Ordinal);

            if (index < 0)
                return;

            var merged =
                "\n            <ResourceDictionary>\n"
                + "                <ResourceDictionary.MergedDictionaries>\n"
                + $"                    <ResourceDictionary Source=\"{MergeMarker}\" />\n"
                + "                </ResourceDictionary.MergedDictionaries>\n"
                + "            </ResourceDictionary>";

            File.WriteAllText(appXamlPath, xaml.Insert(index + open.Length, merged));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
```

Call it in `AppScaffolder.Scaffold(ScaffoldRequest)` after the `files` write loop:

```csharp
        // After the files are written, not in FixupProject: Views/Theme.xaml does not exist yet
        // when the project is created.
        if (target.ProjectType == DotNetProjectType.Wpf)
            WpfProjectFixup.MergeTheme(outputFolder);
```

- [ ] **Step 6: Theme WinForms**

Give `WinFormsMainFormTemplate.Source` a `ThemePalette? palette` parameter. When non-null, emit inside the `MainForm` constructor after `Height = 600;`:

```csharp
        BackColor = ColorTranslator.FromHtml("{{palette.Background}}");
        ForeColor = ColorTranslator.FromHtml("{{palette.Foreground}}");
```

and in `BuildTab`, after the grid is constructed, emit the grid colours the same way. When null, emit nothing. `System.Drawing` is already in the template's using list (line 19), so `ColorTranslator` needs no new using.

- [ ] **Step 7: Run the tests**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj`

Expected: PASS, existing `MainWindowTemplateTests` and `WinFormsTemplateTests` unchanged.

- [ ] **Step 8: Commit**

```bash
git -C D:\SmartCodeGenerator-Repository add AppScaffolding CodeGenerator.Tests/App/ThemeTests.cs
git -C D:\SmartCodeGenerator-Repository commit -m "feat(scaffold): theme the WPF and WinForms front ends"
```

---

### Task 8: App icon

**Files:**
- Modify: `AppScaffolding/AppScaffolder.cs`, `AppScaffolding/CsprojEditing.cs`, `MainWindowTemplate.cs`, `WinFormsMainFormTemplate.cs`, `WpfTarget.cs`, `WinFormsTarget.cs`
- Test: `CodeGenerator.Tests/App/AppIconTests.cs`

**Interfaces:**
- Consumes: `AppCustomization.IconPath`.
- Produces: `CsprojEditing.InjectApplicationIcon(string csproj, string relativePath)`; `AppScaffolder` copies the icon to `Assets/`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/AppIconTests.cs`:

```csharp
using AppScaffolding;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>The icon is copied into the project, and only a .ico becomes ApplicationIcon —
/// MSBuild rejects a .png there.</summary>
[TestClass]
public class AppIconTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void CreateWorkspace()
    {
        _root = Path.Combine(Path.GetTempPath(), "ico-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void RemoveWorkspace()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string WriteIcon(string extension)
    {
        var path = Path.Combine(_root, "brand" + extension);
        File.WriteAllBytes(path, [0, 0, 1, 0]);
        return path;
    }

    [TestMethod]
    public void An_ico_becomes_the_application_icon()
    {
        var updated = CsprojEditing.InjectApplicationIcon(
            "<Project><PropertyGroup></PropertyGroup></Project>", "Assets\\app.ico");

        StringAssert.Contains(updated, "<ApplicationIcon>Assets\\app.ico</ApplicationIcon>");
    }

    [TestMethod]
    public void A_missing_icon_file_is_ignored_rather_than_failing_the_build()
    {
        var request = ScaffoldRequest.For(
                TestModels.OneEntity(), "Shop", _root, new ClassLibraryTarget())
            with { Customization = AppCustomization.Default with { IconPath = "C:\\nope\\x.ico" } };

        var result = new AppScaffolder().Scaffold(request);

        Assert.IsTrue(result.Success, result.Message);
    }

    [TestMethod]
    public void A_supplied_icon_is_copied_into_assets()
    {
        var request = ScaffoldRequest.For(
                TestModels.OneEntity(), "Shop", _root, new WpfTarget())
            with { Customization = AppCustomization.Default with { IconPath = WriteIcon(".ico") } };

        var result = new AppScaffolder().Scaffold(request);

        Assert.IsTrue(File.Exists(Path.Combine(result.ProjectPath, "Assets", "app.ico")));
    }

    [TestMethod]
    public void A_png_is_copied_but_is_not_the_application_icon()
    {
        var request = ScaffoldRequest.For(
                TestModels.OneEntity(), "Shop", _root, new WpfTarget())
            with { Customization = AppCustomization.Default with { IconPath = WriteIcon(".png") } };

        var result = new AppScaffolder().Scaffold(request);
        var csproj = File.ReadAllText(Path.Combine(result.ProjectPath, "Shop.csproj"));

        Assert.IsTrue(File.Exists(Path.Combine(result.ProjectPath, "Assets", "app.png")));
        Assert.IsFalse(csproj.Contains("<ApplicationIcon>", StringComparison.Ordinal));
    }
}
```

`TestModels.OneEntity()` is the fixture created in Task 5. Do not add a second one.

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter AppIconTests`

Expected: FAIL.

- [ ] **Step 3: Implement**

Add to `CsprojEditing`:

```csharp
    /// <summary>Sets <c>&lt;ApplicationIcon&gt;</c> to <paramref name="relativePath"/>.
    /// Idempotent, and a no-op on a project file with no closing tag.</summary>
    public static string InjectApplicationIcon(string csproj, string relativePath)
    {
        if (csproj.Contains("<ApplicationIcon>", StringComparison.Ordinal))
            return csproj;

        var group =
            "  <PropertyGroup>\n" +
            $"    <ApplicationIcon>{relativePath}</ApplicationIcon>\n" +
            "  </PropertyGroup>\n";

        var close = csproj.LastIndexOf("</Project>", StringComparison.Ordinal);
        return close < 0 ? csproj : csproj.Insert(close, group);
    }
```

In `AppScaffolder.Scaffold(ScaffoldRequest)`, after the branding call:

```csharp
        var iconFileName = CopyIcon(request.Customization.IconPath, outputFolder, csprojPath);
```

```csharp
    /// <summary>
    /// Copies the chosen icon to <c>Assets/app&lt;ext&gt;</c> and, for a <c>.ico</c> only, sets it
    /// as the project's ApplicationIcon. Returns the copied file name, or null if there was no
    /// icon or it could not be copied.
    /// </summary>
    /// <remarks>
    /// MSBuild rejects a <c>.png</c> as ApplicationIcon, so a PNG is copied for the window icon
    /// but never written into the project file. A missing or unreadable source is ignored: it is
    /// not worth failing a build the user has already answered ten questions for.
    /// </remarks>
    private static string? CopyIcon(string? iconPath, string outputFolder, string csprojPath)
    {
        if (iconPath is null || !File.Exists(iconPath))
            return null;

        try
        {
            var extension = Path.GetExtension(iconPath).ToLowerInvariant();
            var fileName = "app" + extension;
            var assets = Path.Combine(outputFolder, "Assets");

            Directory.CreateDirectory(assets);
            File.Copy(iconPath, Path.Combine(assets, fileName), overwrite: true);

            if (extension == ".ico" && File.Exists(csprojPath))
                File.WriteAllText(
                    csprojPath,
                    CsprojEditing.InjectApplicationIcon(
                        File.ReadAllText(csprojPath), $"Assets\\{fileName}"));

            return fileName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
```

The window icon itself is a front-end concern and the front-end files are built before the copy happens, so pass the *intended* file name into the target instead: compute `iconFileName` from `request.Customization.IconPath` (extension only) before `target.FrontEndFiles` is called, and have `WpfTarget` emit `Icon="Assets/app.ico"` on the `<Window>` element and `WinFormsTarget` emit `Icon = new Icon("Assets/app.ico");` when it is non-null.

- [ ] **Step 4: Run the tests**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git -C D:\SmartCodeGenerator-Repository add AppScaffolding CodeGenerator.Tests/App
git -C D:\SmartCodeGenerator-Repository commit -m "feat(scaffold): copy a supplied app icon into the generated project"
```

---

### Task 9: The `novalidation` toggle

The risky one. Generated code hard-depends on the Validation folder in two places.

**Files:**
- Modify: `AppScaffolding/RepositoryTemplate.cs`, `ViewModelTemplates.cs`, `AppScaffolder.cs`, `TestProjectTemplates.cs`
- Test: `CodeGenerator.Tests/App/NoValidationTests.cs`

**Interfaces:**
- Consumes: `AppCustomization.Validation`.
- Produces: `RepositoryTemplate.Base(string appName, bool validation = true)`, `ViewModelTemplates.EntityViewModel(...)` gaining a `bool validation = true` parameter (check the exact method name in the file before editing — the call site is `DesktopViewModelFiles.Build`).

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/NoValidationTests.cs`:

```csharp
using AppScaffolding;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>Skipping the Validation folder is not just skipping files: Repository and the
/// desktop view models both call ValidatorFactory.Create&lt;T&gt;(), so the emitted code must
/// change with them or the generated app compiles nowhere.</summary>
[TestClass]
public class NoValidationTests
{
    [TestMethod]
    public void The_repository_still_references_the_validator_by_default()
        => StringAssert.Contains(RepositoryTemplate.Base("Acme"), "ValidatorFactory.Create<T>()");

    [TestMethod]
    public void A_repository_without_validation_names_no_validator()
    {
        var source = RepositoryTemplate.Base("Acme", validation: false);

        Assert.IsFalse(source.Contains("ValidatorFactory", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IValidator", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("Acme.Validation", StringComparison.Ordinal));
    }

    /// <summary>The point of the whole task: it has to COMPILE without the Validation folder.</summary>
    [TestMethod]
    public void A_repository_without_validation_compiles_without_the_validation_seams()
    {
        var source = EmittedSource.Flatten(
            StoreSeamTemplate.Source("Acme"),
            SaveResultTemplate.Source("Acme"),
            StoreFactoryTemplate.Source("Acme", PersistenceKind.Json),
            JsonStoreTemplate.Source("Acme.Data"),
            RepositoryTemplate.Base("Acme", validation: false));

        EmittedSource.AssertCompiles(source);
    }

    [TestMethod]
    public void Create_and_update_still_return_a_save_result()
    {
        var source = RepositoryTemplate.Base("Acme", validation: false);

        StringAssert.Contains(source, "public SaveResult<T> Create(T item)");
        StringAssert.Contains(source, "public SaveResult<T> Update(T item)");
    }
}
```

If `EmittedSource.Flatten` cannot strip `namespace Acme.Data;` (it currently strips only `Acme.Validation`), extend `Flatten` to strip `namespace Acme.Data;` too and note it in that file's remarks.

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter NoValidationTests`

Expected: FAIL — `Base` takes one argument.

- [ ] **Step 3: Branch RepositoryTemplate**

```csharp
    public static string Base(string appName) => Base(appName, validation: true);

    /// <summary>
    /// Source of <c>Data/Repository.cs</c>. With <paramref name="validation"/> false the
    /// Validation folder is not generated at all, so every mention of it must go: the using, the
    /// field, and both Validate calls.
    /// </summary>
    public static string Base(string appName, bool validation)
    {
        var validationUsing = validation ? $"using {appName}.Validation;\n" : string.Empty;
        var validatorField = validation
            ? "    private readonly IValidator<T> _validator = ValidatorFactory.Create<T>();\n"
            : string.Empty;
        var validateCall = validation
            ? """
                    var errors = _validator.Validate(item);
                    if (errors.Count > 0)
                        return new SaveResult<T> { Success = false, Errors = errors };

            """
            : string.Empty;

        return $$"""
    ... using System.Reflection;
    {{validationUsing}}
    ...
        private readonly IStore<T> _store = StoreFactory.Create<T>();
    {{validatorField}}
    ...
        public SaveResult<T> Create(T item)
        {
    {{validateCall}}        var all = _store.GetAll();
    ...
    """;
    }
```

Reproduce the existing body verbatim around those four interpolation points — do not retype it from memory; copy the current template text and insert the placeholders.

- [ ] **Step 4: Branch ViewModelTemplates**

In the `Save()` method emitted around line 210-228, replace the validator loop with an interpolated fragment that is empty when validation is off, and drop the `using {{appName}}.Validation;` from that file's emitted using list on the same condition.

- [ ] **Step 5: Branch AppScaffolder**

In `Scaffold(ScaffoldRequest)`, wrap the four validation core files and the per-model validator loop:

```csharp
        var validation = request.Customization.Validation;

        if (validation)
        {
            files.Add((validationFolder, "IValidator.cs", ValidatorTemplate.ValidatorInterface(projectName)));
            files.Add((validationFolder, "IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface(projectName)));
            files.Add((validationFolder, "StoreEntityLookup.cs", ValidatorTemplate.StoreEntityLookup(projectName)));
            files.Add((validationFolder, "ValidatorFactory.cs", ValidatorTemplate.Factory(projectName, entityNames)));
        }
```

and guard the `.Generated.cs` loop, the `.Custom.cs` stub loop, and pass `validation` to `RepositoryTemplate.Base` and the desktop view models. Also pass it to `ScaffoldTestProject` so the per-model `ValidatorTests` are not emitted.

- [ ] **Step 6: Add the end-to-end compile check**

Add to `NoValidationTests`:

```csharp
    /// <summary>A whole generated app with validation off must build. This is the check that
    /// catches a call site nobody remembered.</summary>
    [TestMethod]
    public void A_generated_app_without_validation_has_no_validation_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "nov-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        try
        {
            var request = ScaffoldRequest.For(
                    TestModels.OneEntity(), "Shop", root, new ConsoleTarget())
                with { Customization = AppCustomization.Default with { Validation = false } };

            var result = new AppScaffolder().Scaffold(request);

            Assert.IsTrue(result.Success, result.Message);
            Assert.IsFalse(Directory.Exists(Path.Combine(result.ProjectPath, "Validation")));
            Assert.IsFalse(
                File.ReadAllText(Path.Combine(result.ProjectPath, "Data", "Repository.cs"))
                    .Contains("Validator", StringComparison.Ordinal));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }
```

- [ ] **Step 7: Run the whole suite**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj`

Expected: PASS, and no existing validator test changed.

- [ ] **Step 8: Commit**

```bash
git -C D:\SmartCodeGenerator-Repository add AppScaffolding CodeGenerator.Tests/App/NoValidationTests.cs
git -C D:\SmartCodeGenerator-Repository commit -m "feat(scaffold): support generating an app without the validation layer"
```

---

### Task 10: The `noseed` toggle

**Files:**
- Modify: `AppScaffolding/AppScaffolder.cs`, `ConsoleProgramTemplate.cs`, `WebApiProgramTemplate.cs`, `WorkerProgramTemplate.cs`, `MainViewModelTemplate.cs`, `TestProjectTemplates.cs`
- Test: `CodeGenerator.Tests/App/NoSeedTests.cs`

**Interfaces:**
- Consumes: `AppCustomization.SampleSeedData`, `ScaffoldRequest.ImportedData`.
- Produces: a `bool seed` parameter (defaulting true) on `ConsoleProgramTemplate.Build`, `WebApiProgramTemplate.Build`, `WorkerProgramTemplate.Build` and `MainViewModelTemplate.Build`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/NoSeedTests.cs`:

```csharp
using AppScaffolding;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>SeedData.EnsureSeeded() is called from four entry points and the generated tests, so
/// dropping SeedData.cs means dropping all of them.</summary>
[TestClass]
public class NoSeedTests
{
    private static readonly AppCustomization NoSeed =
        AppCustomization.Default with { SampleSeedData = false };

    [TestMethod]
    public void The_console_entry_point_still_seeds_by_default()
        => StringAssert.Contains(
            new ConsoleTarget().FrontEndFiles("Shop", ["Order"], AppCustomization.Default)
                .Single(f => f.Name == "Program.cs").Source,
            "SeedData.EnsureSeeded();");

    [TestMethod]
    public void No_entry_point_seeds_when_seeding_is_off()
    {
        foreach (ITargetScaffolder target in new ITargetScaffolder[]
                 { new ConsoleTarget(), new WebApiTarget(), new WorkerTarget(), new WpfTarget() })
        {
            foreach (var file in target.FrontEndFiles("Shop", ["Order"], NoSeed))
            {
                Assert.IsFalse(
                    file.Source.Contains("SeedData.EnsureSeeded", StringComparison.Ordinal),
                    $"{target.Key}/{file.Name} still seeds.");
            }
        }
    }

    /// <summary>The interaction the spec calls out: imported rows arrive THROUGH SeedData, so a
    /// data-driven build keeps it. `noseed` suppresses sample rows, not the user's own data.</summary>
    [TestMethod]
    public void Imported_data_still_produces_a_seed_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "seed-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        try
        {
            var request = ScaffoldRequest.For(
                    TestModels.OneEntity(), "Shop", root, new ConsoleTarget())
                with
                {
                    Customization = NoSeed,
                    ImportedData = TestModels.OneImportedRow("Order"),
                };

            var result = new AppScaffolder().Scaffold(request);

            Assert.IsTrue(
                File.Exists(Path.Combine(result.ProjectPath, "Data", "SeedData.cs")),
                "Imported rows are loaded by SeedData, so it must survive `noseed`.");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }

    [TestMethod]
    public void A_seedless_app_has_no_seed_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "seed-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        try
        {
            var request = ScaffoldRequest.For(
                    TestModels.OneEntity(), "Shop", root, new ConsoleTarget())
                with { Customization = NoSeed };

            var result = new AppScaffolder().Scaffold(request);

            Assert.IsFalse(File.Exists(Path.Combine(result.ProjectPath, "Data", "SeedData.cs")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }
}
```

`TestModels.OneImportedRow` is the fixture created in Task 5.

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter NoSeedTests`

Expected: FAIL.

- [ ] **Step 3: Implement**

In `AppScaffolder.Scaffold(ScaffoldRequest)`, compute one flag and use it everywhere:

```csharp
        // Imported rows are loaded BY SeedData, so a data-driven build keeps it whatever the
        // toggle says: `noseed` suppresses the generator's own sample rows, not the user's data.
        var seed = request.Customization.SampleSeedData || seedFiles.Length > 0;
```

Move the `seedFiles` computation above this line, guard the `SeedData.cs` entry in `files`, pass `seed` to `target.FrontEndFiles` via the customization already being passed, and pass it to `ScaffoldTestProject` to suppress `SeedDataTests`.

Each of the four entry-point templates gains a `bool seed = true` parameter and emits `SeedData.EnsureSeeded();` only when true. The four targets pass `customization.SampleSeedData`.

**Note:** `WpfTarget`/`WinFormsTarget` reach `MainViewModelTemplate` through `DesktopViewModelFiles.Build`, so that helper needs the flag too.

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git -C D:\SmartCodeGenerator-Repository add AppScaffolding CodeGenerator.Tests/App
git -C D:\SmartCodeGenerator-Repository commit -m "feat(scaffold): support generating an app without sample seed data"
```

---

### Task 11: The `logging` toggle

**Files:**
- Create: `AppScaffolding/LoggingTemplate.cs`
- Modify: `AppScaffolding/AppScaffolder.cs`, `RepositoryTemplate.cs`, `StoreFactoryTemplate.cs`
- Test: `CodeGenerator.Tests/App/LoggingTests.cs`

**Interfaces:**
- Consumes: `AppCustomization.Logging`.
- Produces: `LoggingTemplate.Source(string appName)`, `LoggingTemplate.Packages` (an `IReadOnlyList<(string Id, string Version)>`).

- [ ] **Step 1: Confirm the package versions resolve**

Before writing any template, verify the logging packages exist for `net10.0`:

```bash
cd C:\Users\admin\AppData\Local\Temp\claude\D--SmartCodeGenerator-Repository-SmartCodeGenerator\bf10dc54-d506-4ac0-b0a3-8ac1a3b58d4c\scratchpad
dotnet new classlib -n LogProbe -f net10.0
cd LogProbe
dotnet add package Microsoft.Extensions.Logging
dotnet add package Microsoft.Extensions.Logging.Console
dotnet add package Microsoft.Extensions.Logging.Debug
dotnet build
```

Record the exact versions `dotnet add package` resolved from the resulting `LogProbe.csproj` and use those literals in `LoggingTemplate.Packages`. Do not guess a version — an unresolvable one fails restore in every generated app. Delete the probe afterwards.

- [ ] **Step 2: Write the failing test**

Create `CodeGenerator.Tests/App/LoggingTests.cs`:

```csharp
using AppScaffolding;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>Logging is off by default; with it on the app gains AppLog and its packages.</summary>
[TestClass]
public class LoggingTests
{
    [TestMethod]
    public void The_repository_logs_nothing_by_default()
        => Assert.IsFalse(
            RepositoryTemplate.Base("Acme").Contains("AppLog", StringComparison.Ordinal));

    [TestMethod]
    public void A_logging_repository_records_each_write()
    {
        var source = RepositoryTemplate.Base("Acme", validation: true, logging: true);

        StringAssert.Contains(source, "using Acme.Logging;");
        StringAssert.Contains(source, "AppLog.For<");
    }

    [TestMethod]
    public void The_logging_file_compiles()
        => EmittedSource.AssertCompiles(LoggingTemplate.Source("Acme"));

    [TestMethod]
    public void Every_declared_package_has_a_version()
    {
        Assert.IsNotEmpty(LoggingTemplate.Packages);

        foreach (var (id, version) in LoggingTemplate.Packages)
        {
            Assert.IsNotEmpty(id);
            Assert.IsNotEmpty(version);
        }
    }

    [TestMethod]
    public void A_logging_app_gets_the_file_and_the_packages()
    {
        var root = Path.Combine(Path.GetTempPath(), "log-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        try
        {
            var request = ScaffoldRequest.For(
                    TestModels.OneEntity(), "Shop", root, new ConsoleTarget())
                with { Customization = AppCustomization.Default with { Logging = true } };

            var result = new AppScaffolder().Scaffold(request);
            var csproj = File.ReadAllText(Path.Combine(result.ProjectPath, "Shop.Console.csproj"));

            Assert.IsTrue(File.Exists(Path.Combine(result.ProjectPath, "Logging", "AppLog.cs")));
            StringAssert.Contains(csproj, "Microsoft.Extensions.Logging");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }
}
```

**Note:** `The_logging_file_compiles` needs `Microsoft.Extensions.Logging` on the *test* project's reference set for `Compiler.Compile` to bind it. Check how `Compiler.Compile` assembles references first (`CodeGenerationEngine/Core/Compiler.cs`); if it uses only the loaded `AppDomain` assemblies, add the package to `CodeGenerator.Tests.csproj` so the types load. If that proves awkward, drop this one test and rely on the `dotnet build` verification in Task 14 — record which you chose in the test file's summary.

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter LoggingTests`

Expected: FAIL.

- [ ] **Step 4: Implement**

Create `AppScaffolding/LoggingTemplate.cs`:

```csharp
namespace AppScaffolding;

/// <summary>
/// Emits <c>Logging/AppLog.cs</c>: one static logger factory the generated app logs through.
/// </summary>
/// <remarks>
/// Deliberately minimal — no configuration file, no level switches. The generated app is a CRUD
/// scaffold, and a logging stack with its own configuration surface would be a second product
/// inside it.
/// </remarks>
public static class LoggingTemplate
{
    /// <summary>The packages a logging build needs. Versions are the ones `dotnet add package`
    /// resolved for net10.0 — see the plan's Task 11 Step 1.</summary>
    public static IReadOnlyList<(string Id, string Version)> Packages { get; } =
    [
        ("Microsoft.Extensions.Logging", "PLACEHOLDER"),
        ("Microsoft.Extensions.Logging.Console", "PLACEHOLDER"),
        ("Microsoft.Extensions.Logging.Debug", "PLACEHOLDER"),
    ];

    public static string Source(string appName) => $$"""
using Microsoft.Extensions.Logging;

namespace {{appName}}.Logging;

/// <summary>The application's logger factory. Console and Debug providers only.</summary>
public static class AppLog
{
    private static readonly ILoggerFactory Factory =
        LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
        });

    /// <summary>A logger categorised by <typeparamref name="T"/>.</summary>
    public static ILogger For<T>() => Factory.CreateLogger<T>();
}
""";
}
```

Replace both `PLACEHOLDER` values with the versions recorded in Step 1 **before** running the tests — a literal `PLACEHOLDER` shipping would fail every generated restore.

`RepositoryTemplate.Base` gains `bool logging = false`; when on it emits `using {{appName}}.Logging;`, a `private static readonly ILogger Log = AppLog.For<Repository<T>>();` field, and one `Log.LogInformation` line in each of `Create`, `Update` and `Delete`, plus one `Log.LogWarning` on a validation failure (only when validation is also on).

In `AppScaffolder.Scaffold(ScaffoldRequest)`, when `request.Customization.Logging`:

```csharp
            files.Add((Path.Combine(outputFolder, "Logging"), "AppLog.cs",
                LoggingTemplate.Source(projectName)));

            foreach (var (id, version) in LoggingTemplate.Packages)
                InjectPackage(csprojPath, id, version);
```

Generalize the existing `InjectEfCorePackage` into `InjectPackage(string csprojPath, string id, string version)` and have the EF path call it.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj`

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git -C D:\SmartCodeGenerator-Repository add AppScaffolding CodeGenerator.Tests/App/LoggingTests.cs
git -C D:\SmartCodeGenerator-Repository commit -m "feat(scaffold): add an optional logging layer to the generated app"
```

---

### Task 12: The wizard step and CLI wiring

**Files:**
- Create: `CodeGenerator/App/CustomizationWizard.cs`
- Modify: `CodeGenerator/App/BuildAppWizard.cs`, `CodeGenerator/App/BuildAppCommand.cs`
- Test: `CodeGenerator.Tests/App/CustomizationWizardTests.cs`

**Interfaces:**
- Consumes: `AppCustomization`, `CustomizationDefaults`, `InlineCustomization`.
- Produces: `CustomizationWizard.Run(TextReader reader, TextWriter writer, AppCustomization start)` returning `AppCustomization?` (null = cancel); `WizardOutcome` gains a `Customization` member.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/CustomizationWizardTests.cs`:

```csharp
using AppScaffolding;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>The opt-in gate and its eight prompts, driven through a TextReader like
/// BuildAppWizard is.</summary>
[TestClass]
public class CustomizationWizardTests
{
    private static AppCustomization? Run(string input, AppCustomization? start = null)
        => CustomizationWizard.Run(
            new StringReader(input), new StringWriter(), start ?? AppCustomization.Default);

    [TestMethod]
    public void Declining_the_gate_leaves_the_start_untouched()
        => Assert.AreEqual(AppCustomization.Default, Run("n\n"));

    [TestMethod]
    public void A_blank_answer_at_the_gate_declines()
        => Assert.AreEqual(AppCustomization.Default, Run("\n"));

    [TestMethod]
    public void Cancelling_at_the_gate_returns_null()
        => Assert.IsNull(Run("c\n"));

    [TestMethod]
    public void Closed_input_returns_null()
        => Assert.IsNull(Run(string.Empty));

    [TestMethod]
    public void Accepting_and_answering_collects_every_setting()
    {
        var result = Run("y\nCodis Ltd\ndark\n#0B6E4F\n\n\nyes\nyes\nno\n");

        Assert.IsNotNull(result);
        Assert.AreEqual("Codis Ltd", result.Company);
        Assert.AreEqual(AppTheme.Dark, result.Theme);
        Assert.AreEqual("#0B6E4F", result.Accent);
        Assert.IsTrue(result.Validation);
        Assert.IsTrue(result.Logging);
        Assert.IsFalse(result.SampleSeedData);
    }

    /// <summary>Enter on every prompt accepts the pre-filled profile, which is the point of
    /// remembering it.</summary>
    [TestMethod]
    public void Blank_answers_keep_the_prefilled_values()
    {
        // TitlePattern is set explicitly: a saved company with the bare "{app}" pattern is
        // OFFERED the company pattern, so the round trip would not be an identity.
        var saved = AppCustomization.Default with
        {
            Company = "Codis Ltd",
            Theme = AppTheme.Dark,
            TitlePattern = AppCustomization.CompanyTitlePattern,
        };

        var result = Run("y\n\n\n\n\n\n\n\n\n", saved);

        Assert.AreEqual(saved, result);
    }

    [TestMethod]
    public void A_malformed_accent_keeps_the_previous_one_rather_than_failing()
    {
        var result = Run("y\n\n\nnot-a-colour\n\n\n\n\n\n");

        Assert.IsNotNull(result);
        Assert.IsNull(result.Accent);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter CustomizationWizardTests`

Expected: FAIL — `CustomizationWizard` does not exist.

- [ ] **Step 3: Implement the wizard**

Create `CodeGenerator/App/CustomizationWizard.cs`:

```csharp
using AppScaffolding;

namespace CodeGenerator.App;

/// <summary>
/// The opt-in customization step: one gate, then eight prompts pre-filled from whatever the
/// caller already knows.
/// </summary>
/// <remarks>
/// Reads and writes only through the supplied reader and writer, never the console — the pattern
/// <c>BuildAppWizard</c> and <c>DataSourceInterview</c> established, and what makes the whole
/// flow drivable from a test.
/// <para>
/// An unparseable answer keeps the current value rather than re-prompting. Re-prompting would
/// be the stricter choice, but this step is optional decoration on a build the user has already
/// answered several questions for, and trapping them in a loop over a mistyped colour is worse
/// than ignoring it.
/// </para>
/// </remarks>
internal static class CustomizationWizard
{
    /// <summary>
    /// The customization to build with, or null if the user cancelled. Returns
    /// <paramref name="start"/> unchanged when they decline the gate.
    /// </summary>
    public static AppCustomization? Run(
        TextReader reader, TextWriter writer, AppCustomization start)
    {
        writer.WriteLine();
        writer.Write("Customize appearance and features? [y/N]> ");

        var gate = reader.ReadLine();

        // EOF is the absence of an answer, not a "no": conflating them would generate an app
        // off a closed pipe.
        if (gate is null)
            return null;

        var answer = gate.Trim();

        if (IsCancel(answer))
            return null;

        if (!IsYes(answer))
            return start;

        writer.WriteLine();

        var company = Ask(reader, writer, "Company name", start.Company);
        if (company is null) return null;

        var theme = Ask(reader, writer, "Theme [light/dark]", ThemeLabel(start.Theme));
        if (theme is null) return null;

        var accent = Ask(reader, writer, "Accent colour (#rrggbb)", start.Accent ?? "none");
        if (accent is null) return null;

        var icon = Ask(reader, writer, "Icon path (.ico or .png)", start.IconPath ?? "none");
        if (icon is null) return null;

        // Naming a company but leaving the pattern at "{app}" would hide the company the user
        // just typed, so the company pattern is what a named company is offered.
        var suggestedPattern = company.Length > 0 && start.TitlePattern == AppCustomization.AppPlaceholder
            ? AppCustomization.CompanyTitlePattern
            : start.TitlePattern;

        var title = Ask(reader, writer, "Window title", suggestedPattern);
        if (title is null) return null;

        var validation = AskYesNo(reader, writer, "Validation layer", start.Validation);
        if (validation is null) return null;

        var logging = AskYesNo(reader, writer, "Logging", start.Logging);
        if (logging is null) return null;

        var seed = AskYesNo(reader, writer, "Sample seed data", start.SampleSeedData);
        if (seed is null) return null;

        return start with
        {
            Company = company,
            Theme = ParseTheme(theme, start.Theme),
            Accent = ParseAccent(accent, start.Accent),
            IconPath = ParseOptional(icon, start.IconPath),
            TitlePattern = title.Length == 0 ? suggestedPattern : title,
            Validation = validation.Value,
            Logging = logging.Value,
            SampleSeedData = seed.Value,
        };
    }

    /// <summary>One prompt showing its current value. Blank keeps it; null means stdin closed.</summary>
    private static string? Ask(
        TextReader reader, TextWriter writer, string label, string current)
    {
        writer.Write($"  {label} [{current}]> ");

        var line = reader.ReadLine();

        if (line is null)
            return null;

        var text = line.Trim();

        return text.Length == 0 ? current : text;
    }

    /// <summary>A yes/no prompt. Null means stdin closed; an unrecognized answer keeps
    /// <paramref name="current"/>.</summary>
    private static bool? AskYesNo(
        TextReader reader, TextWriter writer, string label, bool current)
    {
        var answer = Ask(reader, writer, label, current ? "yes" : "no");

        if (answer is null)
            return null;

        if (IsYes(answer)) return true;
        if (IsNo(answer)) return false;

        return current;
    }

    private static string ThemeLabel(AppTheme theme)
        => theme == AppTheme.Default ? "none" : theme.ToString().ToLowerInvariant();

    private static AppTheme ParseTheme(string value, AppTheme current)
        => Enum.TryParse<AppTheme>(value, ignoreCase: true, out var parsed) ? parsed : current;

    /// <summary>The typed accent, or the current one when it is unusable. "none" clears it.</summary>
    private static string? ParseAccent(string value, string? current)
    {
        if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
            return null;

        return ThemePalette.IsValidHexColour(value) ? value : current;
    }

    private static string? ParseOptional(string value, string? current)
        => string.Equals(value, "none", StringComparison.OrdinalIgnoreCase) ? null
           : value.Length == 0 ? current
           : value;

    private static bool IsYes(string text)
        => string.Equals(text, "y", StringComparison.OrdinalIgnoreCase)
           || string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase);

    private static bool IsNo(string text)
        => string.Equals(text, "n", StringComparison.OrdinalIgnoreCase)
           || string.Equals(text, "no", StringComparison.OrdinalIgnoreCase);

    private static bool IsCancel(string text)
        => string.Equals(text, "c", StringComparison.OrdinalIgnoreCase)
           || string.Equals(text, "cancel", StringComparison.OrdinalIgnoreCase);
}
```

**Check against the test expectations before running:** `Blank_answers_keep_the_prefilled_values` feeds `y` then eight blank lines and expects the saved value back unchanged. With `Company = "Codis Ltd"` and the saved pattern already at `{app}`, `suggestedPattern` becomes the company pattern, so the returned record would differ from `saved`. Either seed that test's `saved` with `TitlePattern = AppCustomization.CompanyTitlePattern`, or drop the suggestion for a company that was already saved. Prefer the former — the suggestion is the behaviour you want.

- [ ] **Step 4: Wire it into BuildAppWizard**

Add `AppCustomization Customization` to `WizardOutcome`, and in `BuildAppWizard.Run` — after the data-source step, before returning — call the customization wizard with the profile-merged start value, returning `Cancelled` when it returns null.

`BuildAppWizard.Run` gains an `AppCustomization start` parameter so the caller supplies the inline-and-profile-merged value.

- [ ] **Step 5: Wire it into BuildAppCommand**

In `Handle`:

```csharp
        var tail = Tail(input);

        // Options come off the line BEFORE the data source is parsed: a location is taken as the
        // rest of the line, and would otherwise swallow them.
        var (remaining, inline) = InlineCustomization.Split(tail, AppCustomization.Default);

        var request = InlineDataSource.Parse(remaining);

        var customization = inline;
```

and inside the existing `if (!Console.IsInputRedirected)` block, merge the saved profile under the inline options before running the wizard, and save the result:

```csharp
            // The profile is read only here: a redirected-stdin run must not have its output
            // changed by a file, which is the same determinism `normalize` protects.
            var start = Merge(CustomizationDefaults.Load(workspaceRoot), inline);

            var outcome = BuildAppWizard.Run(
                request, Console.In, Console.Out, ApplicationTypeMenu.Choose, start);
            ...
            customization = outcome.Customization;
            CustomizationDefaults.Save(workspaceRoot, customization);
```

Add `Merge`, and thread `customization` through `HandleFromDescription`, `HandleFromData`, `RunReviewLoop` and `Generate`, where it becomes the `ScaffoldRequest`:

```csharp
        var scaffold = new AppScaffolder().Scaffold(new ScaffoldRequest(
            model, appName, workspaceRoot, target, persistence, importedData,
            customization, DateTime.Now.Year));
```

- [ ] **Step 6: Show it in the review loop**

Beside the existing `Target:` line in `RunReviewLoop`:

```csharp
            // Settled before discovery ran, like the target, so `approve` produces nothing the
            // user has not already seen.
            foreach (var line in CustomizationSummary.Lines(customization))
                Console.WriteLine(line);
```

Add `internal static class CustomizationSummary` with a pure `Lines(AppCustomization)` returning an empty list for `Default`, and a test asserting exactly that.

- [ ] **Step 7: Update the usage text**

`HandleFromDescription`'s usage lines (currently lines 118-119) become:

```csharp
            Console.WriteLine("Usage: build app: <description of the system you want>");
            Console.WriteLine("   or: build app from csv <path>   (also txt, json, excel)");
            Console.WriteLine("Options (last on the line): company \"<name>\"  theme light|dark  "
                              + "accent #rrggbb  icon <path>  title \"<pattern>\"  "
                              + "novalidation  logging  noseed");
```

- [ ] **Step 8: Run the whole suite**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj`

Expected: PASS. The piped E2E tests are the regression proof — none may change.

- [ ] **Step 9: Commit**

```bash
git -C D:\SmartCodeGenerator-Repository add CodeGenerator CodeGenerator.Tests/App
git -C D:\SmartCodeGenerator-Repository commit -m "feat(build-app): add the customization wizard step and wire it through the command"
```

---

### Task 13: Help catalog

**Files:**
- Modify: `CodeGenerationEngine/Core/CommandDictionary.cs`
- Test: `CodeGenerator.Tests` — locate the existing help-catalog test (the repo has a help-catalog audit from PR #56) with
  `rg -l "CommandDictionary" D:\SmartCodeGenerator-Repository --glob "*Tests*"` and extend it rather than adding a parallel one.

- [ ] **Step 1: Write the failing test**

Add to the existing help-catalog test class:

```csharp
    /// <summary>Every inline customization option the command accepts must be discoverable from
    /// `help`, or it may as well not exist.</summary>
    [TestMethod]
    public void The_help_catalog_documents_the_customization_options()
    {
        var buildApp = string.Join(
            "\n",
            CommandDictionary.All
                .Where(entry => entry.Category == "Build App")
                .Select(entry => entry.Command + " " + entry.Description + " " + entry.Example));

        foreach (var option in new[]
                 { "company", "theme", "accent", "icon", "title", "novalidation", "logging", "noseed" })
        {
            StringAssert.Contains(buildApp, option, $"'{option}' is not in the help catalog.");
        }
    }
```

Adjust `CommandDictionary.All` to the real accessor name — read the file first.

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj --filter Help`

Expected: FAIL — none of the options are documented.

- [ ] **Step 3: Add the catalog entries**

In `CommandDictionary.cs`, inside `#region "Build App"` after the existing `build app: <description> from <format> <location>` entry (around line 537):

```csharp
        new()
        {
            Category = "Build App",
            Command = "build app: <description> [customization options]",
            Description = "Customizes the generated application. Options go LAST on the line, "
                        + "after any data source: company \"<name>\" sets the company (used in "
                        + "the project metadata, the window title and a copyright header); "
                        + "theme light|dark and accent #rrggbb colour the desktop front ends; "
                        + "icon <path> supplies a .ico or .png; title \"<pattern>\" overrides the "
                        + "window title, where {company} and {app} are substituted.",
            Example = "build app: a shop company \"Codis Ltd\" theme dark accent #0B6E4F"
        },
        new()
        {
            Category = "Build App",
            Command = "build app: <description> [novalidation] [logging] [noseed]",
            Description = "Chooses which optional layers are generated. 'novalidation' omits the "
                        + "Validation folder and its per-model validators; 'logging' adds a "
                        + "logging layer (off by default); 'noseed' omits the sample seed rows "
                        + "(rows imported from your own data file are unaffected). Interactively "
                        + "these are asked by the 'Customize appearance and features?' step, "
                        + "which remembers your answers in .codegen/app-profile.json.",
            Example = "build app: a shop novalidation logging noseed"
        },
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test D:\SmartCodeGenerator-Repository\CodeGenerator.Tests\CodeGenerator.Tests.csproj`

Expected: PASS, including any existing catalog-shape audit (some audits assert every entry has a non-empty Example — the two above do).

- [ ] **Step 5: Commit**

```bash
git -C D:\SmartCodeGenerator-Repository add CodeGenerationEngine/Core/CommandDictionary.cs CodeGenerator.Tests
git -C D:\SmartCodeGenerator-Repository commit -m "docs(help): document the build app customization options"
```

---

### Task 14: Verify a real generated app, update the docs, correct the spec

Tests prove the emitted strings compile in isolation. This task proves a real customized app builds.

**Files:**
- Modify: `AppScaffolding/README.md`, `docs/superpowers/specs/2026-08-12-build-app-customization-design.md`

- [ ] **Step 1: Rebuild the CLI**

`dotnet test` leaves the CLI's `bin` stale, so a verification run can exercise old code:

```bash
dotnet build D:\SmartCodeGenerator-Repository\CodeGenerator\CodeGenerator.csproj -c Debug
```

- [ ] **Step 2: Generate a fully customized app**

Use a SHORT output path — the scratchpad blows MAX_PATH and reports MSB3030 "file not found" on files that exist:

```bash
mkdir C:\t\cust
cd C:\t\cust
echo "build app: a shop that sells books company \"Codis Ltd\" theme dark accent #0B6E4F logging" | D:\SmartCodeGenerator-Repository\CodeGenerator\bin\Debug\net10.0\CodeGenerator.exe
```

At the review prompt the piped run needs an `approve` line; drive it as `CodeGenerator.Tests`'s existing piped E2E test does — read that test for the exact input sequence.

- [ ] **Step 3: Build the generated app**

```bash
dotnet build C:\t\cust\Shop\Shop.csproj
```

Expected: build succeeds. Confirm by inspection:
- `Shop.csproj` contains `<Company>Codis Ltd</Company>` and the logging `PackageReference`s
- `Models/Order.cs` starts with the copyright banner
- `Views/Theme.xaml` exists and `App.xaml` merges it
- `Logging/AppLog.cs` exists

- [ ] **Step 4: Generate the opposite corner and build it**

Repeat with `novalidation noseed` and no company, into `C:\t\plain`, and build it. Expected: build succeeds, no `Validation` folder, no `Data/SeedData.cs`.

- [ ] **Step 5: Run the CI-parity build**

```bash
dotnet build D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx -c Release -warnaserror --no-incremental
```

Expected: no warnings, no errors. `--no-incremental` is required or analyzers do not rerun.

- [ ] **Step 6: Run every test project**

```bash
dotnet test D:\SmartCodeGenerator-Repository\SmartCodeGenerator\SmartCodeGenerator.slnx -c Release
```

Expected: all green. Record the counts.

- [ ] **Step 7: Update AppScaffolding/README.md**

Document the new files (`AppCustomization`, `ThemePalette`, `FileHeader`, `ScaffoldRequest`, `ThemeXamlTemplate`, `LoggingTemplate`), the `Assets/` and `Logging/` folders in the generated layout, and — where the README describes the Validation folder and `SeedData` — note that each can now be absent and what the emitted code looks like when it is.

- [ ] **Step 8: Correct the spec**

In `docs/superpowers/specs/2026-08-12-build-app-customization-design.md`, replace "the Web API OpenAPI title" with the root-endpoint behaviour Task 6 implemented, and note that the generated Web API has no OpenAPI document.

- [ ] **Step 9: Commit**

```bash
git -C D:\SmartCodeGenerator-Repository add AppScaffolding/README.md docs/superpowers/specs/2026-08-12-build-app-customization-design.md
git -C D:\SmartCodeGenerator-Repository commit -m "docs(scaffold): document customization and correct the Web API title claim"
```

- [ ] **Step 10: Open the PR**

```bash
git -C D:\SmartCodeGenerator-Repository push -u origin app-customization
gh pr create --repo rohit-sharma444/SmartCodeGenerator --base main --head app-customization --title "build app: application customization and company name" --body "..."
```

Wait for CI. Note from experience: CI runs the whole solution in Release with `-warnaserror` and needs `lfs: true` on checkout, which `ad3bb54` already added.
