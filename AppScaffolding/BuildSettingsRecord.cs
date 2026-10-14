using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using AppScaffolding.Screens;

namespace AppScaffolding;

/// <summary>
/// The settings an app was generated with, kept beside it so a later add can generate the same
/// way.
/// </summary>
/// <remarks>
/// An add generates the new classes' files and the lines that list them. They have to be written
/// with the namespace, company and features the app already has, not with whatever the current
/// run's answers say: an app built with no namespace prefix, then added to from a workspace whose
/// profile names one, had its files rewritten into a namespace nothing else in it used, and did
/// not compile (88 errors).
/// <para>
/// Kept under <c>.codegen/</c> in the project folder, beside <see cref="GeneratedFilesManifest"/>,
/// for the same reason: the SDK leaves dot-folders out of every item glob. An app generated before
/// this record existed has none, and <see cref="For"/> reads what it can from the app itself.
/// </para>
/// </remarks>
public static class BuildSettingsRecord
{
    public const string FileName = "build-settings.json";

    /// <summary>The shape on disk. Nullable throughout, so a record written by an older build
    /// that lacks a field a newer one added falls back for that field alone.</summary>
    private sealed class Record
    {
        public string? Persistence { get; set; }
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

    /// <summary>Where the record for <paramref name="projectFolder"/> lives.</summary>
    public static string PathFor(string projectFolder)
        => Path.Combine(projectFolder, GeneratedFilesManifest.FolderName, FileName);

    /// <summary>Records what <paramref name="request"/> generated <paramref name="projectFolder"/>
    /// with. Never throws: a record that cannot be written costs a later add its certainty, not
    /// this generate its result.</summary>
    public static void Save(string projectFolder, ScaffoldRequest request)
    {
        var c = request.Customization;

        var record = new Record
        {
            Persistence = request.Persistence.ToString(),
            Company = c.Company,
            Theme = c.Theme.ToString(),
            Accent = c.Accent,
            TitlePattern = c.TitlePattern,
            IconPath = c.IconPath,
            Validation = c.Validation,
            Logging = c.Logging,
            SampleSeedData = c.SampleSeedData,
            Login = c.Login,
            Transactions = c.Transactions,
            Masters = c.Masters,

            // Empty, not null, when there is none: null would read back as "not recorded".
            NamespacePrefix = c.EffectiveNamespacePrefix,
            Layout = c.Layout?.ToString(),
            EntryTemplate = c.EntryTemplate?.ToString(),
        };

        try
        {
            var path = PathFor(projectFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(record, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// <paramref name="request"/> with the persistence and customization the app in
    /// <paramref name="projectFolder"/> was generated with.
    /// </summary>
    /// <remarks>
    /// From the record when there is one. Without one, from the app itself: the namespace from
    /// the project file's <c>RootNamespace</c>, the company from its <c>Company</c>, and each
    /// feature from whether the file it emits is there. Settings that leave no trace in the
    /// files (theme, title, layout) keep the request's answers; an add copes with those by
    /// merging lines rather than copying files.
    /// </remarks>
    public static ScaffoldRequest For(string projectFolder, ScaffoldRequest request)
        => Read(projectFolder, request) ?? Derive(projectFolder, request);

    private static ScaffoldRequest? Read(string projectFolder, ScaffoldRequest request)
    {
        Record? record;

        try
        {
            var path = PathFor(projectFolder);

            if (!File.Exists(path))
                return null;

            record = JsonSerializer.Deserialize<Record>(File.ReadAllText(path), Options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }

        if (record is null)
            return null;

        var c = request.Customization;

        var customization = c with
        {
            Company = record.Company ?? c.Company,
            Theme = Enum.TryParse<AppTheme>(record.Theme, out var theme) ? theme : c.Theme,
            Accent = record.Accent,
            TitlePattern = record.TitlePattern ?? c.TitlePattern,
            IconPath = record.IconPath,
            Validation = record.Validation ?? c.Validation,
            Logging = record.Logging ?? c.Logging,
            SampleSeedData = record.SampleSeedData ?? c.SampleSeedData,
            Login = record.Login ?? c.Login,
            Transactions = record.Transactions ?? c.Transactions,
            Masters = record.Masters ?? c.Masters,
            NamespacePrefix = record.NamespacePrefix ?? c.NamespacePrefix,
            Layout = Enum.TryParse<AppLayout>(record.Layout, out var layout) ? layout : null,
            EntryTemplate = Enum.TryParse<EntryTemplate>(record.EntryTemplate, out var entry)
                ? entry
                : null,
        };

        var persistence = Enum.TryParse<PersistenceKind>(record.Persistence, out var kind)
            ? kind
            : request.Persistence;

        return request with { Customization = customization, Persistence = persistence };
    }

    private static ScaffoldRequest Derive(string projectFolder, ScaffoldRequest request)
    {
        var c = request.Customization;
        var projectName = GeneratedProjectPath.ProjectName(request.AppName, request.Target);
        var csproj = Path.Combine(projectFolder, projectName + ".csproj");

        string? rootNamespace = null;
        string? company = null;

        try
        {
            if (File.Exists(csproj))
            {
                var document = XDocument.Load(csproj);
                rootNamespace = Element(document, "RootNamespace");
                company = Element(document, "Company");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or System.Xml.XmlException)
        {
        }

        // No RootNamespace element means no prefix: the scaffolder writes one only for a prefix.
        // One that does not end in the project name was not written by the scaffolder, so it
        // says nothing about a prefix and the request's answer stands.
        var prefix = rootNamespace is null
            ? string.Empty
            : rootNamespace.EndsWith("." + projectName, StringComparison.Ordinal)
                ? rootNamespace[..^(projectName.Length + 1)]
                : string.Equals(rootNamespace, projectName, StringComparison.Ordinal)
                    ? string.Empty
                    : c.NamespacePrefix;

        bool Has(params string[] parts) => File.Exists(Path.Combine([projectFolder, .. parts]));

        var customization = c with
        {
            NamespacePrefix = prefix,
            Company = company ?? string.Empty,
            Validation = Has("Validation", "ValidatorFactory.cs"),
            Logging = Has(LoggingTemplate.FolderName, LoggingTemplate.FileName),
            SampleSeedData = Has("Data", "SeedData.cs"),
            Login = Has("Auth", "AuthService.cs"),
        };

        var persistence = Has("Data", "AppDbContext.cs") ? PersistenceKind.Sqlite
            : Has("Data", "JsonStore.cs") ? PersistenceKind.Json
            : request.Persistence;

        return request with { Customization = customization, Persistence = persistence };
    }

    private static string? Element(XDocument document, string name)
        => document.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() is { Length: > 0 } value
            ? value
            : null;
}
