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
