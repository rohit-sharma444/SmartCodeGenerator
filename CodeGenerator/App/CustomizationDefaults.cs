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
        public string? NamespacePrefix { get; set; }
        public string? Layout { get; set; }
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
                NamespacePrefix: ValidatedPrefix(profile.NamespacePrefix, fallback.NamespacePrefix),
                Layout: ParseLayout(profile.Layout));
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
                NamespacePrefix = customization.NamespacePrefix,

                // Null stays absent from the file rather than being written as "tabs": a profile
                // that names no layout is what lets a later `layout tabs` read as typed.
                Layout = customization.Layout is null
                    ? null
                    : AppLayoutCatalog.KeyFor(customization.Layout.Value),
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
    /// an unreadable layout is indistinguishable from an absent one, and both mean tabs downstream.
    /// </remarks>
    private static AppLayout? ParseLayout(string? value)
        => value is not null && AppLayoutCatalog.TryResolve(value, out var layout) ? layout : null;

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
    private static string ValidatedPrefix(string? value, string fallback)
    {
        if (value is null)
            return fallback;

        return value.Length == 0 || AppScaffolding.NamespacePrefix.IsValid(value)
            ? value
            : fallback;
    }
}
