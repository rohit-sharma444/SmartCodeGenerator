using System.Text.Json;

namespace CodeGenerator.App;

/// <summary>
/// Reads the address a generated web application will listen on out of its
/// <c>Properties/launchSettings.json</c>.
/// </summary>
/// <remarks>
/// Only used to tell the user where to point their browser. <c>dotnet new blazor</c> picks a random
/// free port per project and records it here, so there is nothing to hard-code and nothing to guess.
/// <para>
/// Every failure returns null and the caller says less rather than saying something wrong: a missing
/// file, a hand-edited one, a profile with only an https URL. The application still starts either
/// way, and it prints its own address in its own window.
/// </para>
/// </remarks>
internal static class LaunchSettingsReader
{
    /// <summary>
    /// The first <c>http://</c> address in the project's launch profiles, or null.
    /// </summary>
    /// <remarks>
    /// http rather than https on purpose: a generated app has no certificate the user has trusted,
    /// so https would greet them with a browser warning on their first look at their own app.
    /// </remarks>
    public static string? HttpUrl(string projectPath)
    {
        try
        {
            var path = Path.Combine(projectPath, "Properties", "launchSettings.json");

            if (!File.Exists(path))
                return null;

            using var document = JsonDocument.Parse(File.ReadAllText(path));

            if (!document.RootElement.TryGetProperty("profiles", out var profiles)
                || profiles.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var profile in profiles.EnumerateObject())
            {
                if (profile.Value.ValueKind != JsonValueKind.Object
                    || !profile.Value.TryGetProperty("applicationUrl", out var urls)
                    || urls.ValueKind != JsonValueKind.String)
                    continue;

                // The value is a semicolon-separated list ("https://localhost:7217;http://localhost:5156").
                var http = urls.GetString()!
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault(url => url.StartsWith("http://", StringComparison.OrdinalIgnoreCase));

                if (http is not null)
                    return http;
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
