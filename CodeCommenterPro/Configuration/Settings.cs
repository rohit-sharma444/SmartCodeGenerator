using System.Text.Json;

namespace CodeCommenterPro.Configuration;

public class Settings
{
    public bool CreateBackups { get; set; } = true;

    public bool GenerateHtmlReport { get; set; } = true;

    public bool DryRun { get; set; }

    public string RootFolder { get; set; }
        = string.Empty;

    public static Settings Load()
    {
        try
        {
            string file =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "settings.json");

            if (!File.Exists(file))
            {
                return new Settings();
            }

            string json =
                File.ReadAllText(file);

            if (string.IsNullOrWhiteSpace(json))
            {
                return new Settings();
            }

            return JsonSerializer.Deserialize<Settings>(
                       json,
                       new JsonSerializerOptions
                       {
                           PropertyNameCaseInsensitive = true
                       })
                   ?? new Settings();
        }
        catch
        {
            return new Settings();
        }
    }

    public void Save()
    {
        string file =
            Path.Combine(
                AppContext.BaseDirectory,
                "settings.json");

        string json =
            JsonSerializer.Serialize(
                this,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

        File.WriteAllText(
            file,
            json);
    }
}