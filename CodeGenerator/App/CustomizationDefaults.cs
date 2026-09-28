using System.Text.Json;
using System.Text.Json.Serialization;
using AppScaffolding;
using AppScaffolding.Screens;

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
        public bool? Login { get; set; }
        public string? Transactions { get; set; }
        public string? Masters { get; set; }
        public string? NamespacePrefix { get; set; }
        public string? Layout { get; set; }
        public string? EntryTemplate { get; set; }
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
                SampleSeedData: profile.SampleSeedData ?? fallback.SampleSeedData,
                Login: profile.Login ?? fallback.Login,
                Transactions: profile.Transactions ?? fallback.Transactions,
                Masters: profile.Masters ?? fallback.Masters,
                NamespacePrefix: ValidatedPrefix(profile.NamespacePrefix, fallback.NamespacePrefix),
                Layout: ParseLayout(profile.Layout),
                EntryTemplate: ParseEntryTemplate(profile.EntryTemplate));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return AppCustomization.Default;
        }
    }

    /// <summary>
    /// What to say when the loaded profile names a layout other than the default, or null when it
    /// names none or names that default.
    /// </summary>
    /// <remarks>
    /// <c>EffectiveLayout</c> is <c>Layout ?? Business</c>, so a remembered layout wins for good:
    /// a profile written before the business shell became the default keeps its owner on the old
    /// shape, and the only workspace the flip reaches is one with no profile — which is what every
    /// test is. Reported from real use as "I can't see the master and transaction screens", after
    /// three runs of a profile holding <c>dashboard</c>.
    /// <para>
    /// Printed rather than corrected: the profile records an answer the user gave, and quietly
    /// overriding it would be the same silence in the other direction. The file is named so it can
    /// be edited or deleted, because that is the only way to stop being asked.
    /// </para>
    /// </remarks>
    /// <param name="namedOnTheLine">
    /// The layout the command line named, if any. A layout typed just now is not a remembered one:
    /// crediting the profile for it would be wrong, and the user who typed it knows what they
    /// chose.
    /// </param>
    public static string? RememberedLayoutNotice(
        string workspaceRoot, AppCustomization saved, AppLayout? namedOnTheLine)
    {
        if (namedOnTheLine is not null)
            return null;

        if (saved.Layout is null || saved.Layout == AppLayout.Business)
            return null;

        var key = AppLayoutCatalog.KeyFor(saved.Layout.Value);
        var business = AppLayoutCatalog.KeyFor(AppLayout.Business);

        return $"  Using the {key} layout, remembered from {PathFor(workspaceRoot)}. "
             + $"The default is now {business} — {AppLayoutCatalog.Descriptions[AppLayout.Business]}."
             + $" Choose {business} at the Layout step to switch, or delete that file to forget it.";
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
                Login = customization.Login,
                Transactions = customization.Transactions,
                Masters = customization.Masters,
                NamespacePrefix = customization.NamespacePrefix,

                // Null stays absent from the file rather than being written as "tabs": a profile
                // that names no layout is what lets a later `layout tabs` read as typed.
                Layout = customization.Layout is null
                    ? null
                    : AppLayoutCatalog.KeyFor(customization.Layout.Value),

                // Null stays absent from the file rather than being written: a profile
                // that names no entry template is what lets a later `entry-template grouped` read as typed.
                EntryTemplate = customization.EntryTemplate is null
                    ? null
                    : EntryTemplateCatalog.KeyFor(customization.EntryTemplate.Value),
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

    /// <summary>The stored layout, or null when absent or unrecognized.</summary>
    /// <remarks>
    /// Re-validated here for the reason <see cref="ValidatedPrefix"/> gives: a hand-edited profile
    /// bypasses the inline parser and the wizard, and <c>Load</c> otherwise assigns straight from
    /// the JSON. There is no fallback parameter because null is already the "named nothing" value —
    /// an unreadable layout is indistinguishable from an absent one, and both mean the default
    /// layout downstream -- business, since the flip.
    /// </remarks>
    private static AppLayout? ParseLayout(string? value)
        => value is not null && AppLayoutCatalog.TryResolve(value, out var layout) ? layout : null;

    /// <summary>The stored entry template, or null when absent or unrecognized.</summary>
    /// <remarks>
    /// Re-validated here for the same reason as <see cref="ParseLayout"/>: a hand-edited profile
    /// bypasses the inline parser, and <c>Load</c> otherwise assigns straight from the JSON. Null
    /// is the "named nothing" value — an unreadable entry template is indistinguishable from an
    /// absent one, and both mean the default entry template downstream.
    /// </remarks>
    private static EntryTemplate? ParseEntryTemplate(string? value)
        => value is not null && EntryTemplateCatalog.TryResolve(value, out var template) ? template : null;

    /// <summary>
    /// <paramref name="value"/> when it is empty or a valid namespace prefix, or
    /// <paramref name="fallback"/> otherwise. Missing (null) also falls back.
    /// </summary>
    /// <remarks>
    /// Every other surface validates a namespace prefix — <c>InlineCustomization</c> refuses an
    /// invalid one and the wizard's <c>ParsePrefix</c> keeps the previous value — but a hand-edited
    /// profile bypasses both, and <c>Load</c> otherwise assigns it straight from the JSON. A stored
    /// value that would not compile after <c>namespace</c> must not reach
    /// <c>RootNamespaceFor</c>, the csproj writers, WPF's <c>x:Class</c> or the validator stubs. The
    /// re-validate-at-the-point-of-use precedent is <see cref="ThemePalette.Resolve"/>, which
    /// tolerates a corrupt <c>Accent</c> the same way.
    /// <para>
    /// The type is qualified (<see cref="AppScaffolding.NamespacePrefix"/>) because the
    /// <c>AppCustomization.NamespacePrefix</c> property and the <c>Profile.NamespacePrefix</c>
    /// property this file also touches share the same name.
    /// </para>
    /// </remarks>
    private static string? ValidatedPrefix(string? value, string? fallback)
    {
        if (value is null)
            return fallback;

        return value.Length == 0 || AppScaffolding.NamespacePrefix.IsValid(value)
            ? value
            : fallback;
    }
}
