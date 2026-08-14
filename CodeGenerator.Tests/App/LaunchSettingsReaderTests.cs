using System.Collections.Generic;
using System.IO;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Finding the address to tell the user to open, out of what <c>dotnet new blazor</c> wrote.
/// </summary>
[TestClass]
public class LaunchSettingsReaderTests
{
    /// <summary>Exactly what `dotnet new blazor` writes, ports and all.</summary>
    private const string TemplateSettings = """
{
  "$schema": "https://json.schemastore.org/launchsettings.json",
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": true,
      "applicationUrl": "http://localhost:5156",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    },
    "https": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": true,
      "applicationUrl": "https://localhost:7217;http://localhost:5156",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
""";

    private readonly List<string> _tempFolders = new();

    private string NewProject(string? settings)
    {
        var folder = Path.Combine(Path.GetTempPath(), "ls_" + Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
        _tempFolders.Add(folder);

        if (settings is not null)
        {
            var properties = Path.Combine(folder, "Properties");
            Directory.CreateDirectory(properties);
            File.WriteAllText(Path.Combine(properties, "launchSettings.json"), settings);
        }

        return folder;
    }

    [TestCleanup]
    public void CleanupTempFolders()
    {
        foreach (var folder in _tempFolders)
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void HttpUrl_ReadsThePortTheTemplateChose()
    {
        // Not guessable: dotnet new picks a free port per project and records it only here.
        Assert.AreEqual("http://localhost:5156", LaunchSettingsReader.HttpUrl(NewProject(TemplateSettings)));
    }

    [TestMethod]
    public void HttpUrl_PrefersHttpOverHttps()
    {
        // A generated app has no certificate the user has trusted, so https would greet them with a
        // browser warning on their first look at their own application.
        const string httpsFirst = """
        {
          "profiles": {
            "https": {
              "commandName": "Project",
              "applicationUrl": "https://localhost:7217;http://localhost:5156"
            }
          }
        }
        """;

        Assert.AreEqual("http://localhost:5156", LaunchSettingsReader.HttpUrl(NewProject(httpsFirst)));
    }

    [TestMethod]
    public void HttpUrl_IsNullWhenThereIsNothingToRead()
    {
        // The caller then says less rather than saying something wrong -- the app prints its own
        // address in its own window either way.
        Assert.IsNull(LaunchSettingsReader.HttpUrl(NewProject(null)));
        Assert.IsNull(LaunchSettingsReader.HttpUrl(NewProject("{}")));
        Assert.IsNull(LaunchSettingsReader.HttpUrl(NewProject("""{ "profiles": {} }""")));
        Assert.IsNull(LaunchSettingsReader.HttpUrl(NewProject("not json at all")));
    }

    [TestMethod]
    public void HttpUrl_IsNullWhenEveryProfileIsHttpsOnly()
    {
        const string httpsOnly = """
        {
          "profiles": {
            "https": { "commandName": "Project", "applicationUrl": "https://localhost:7217" }
          }
        }
        """;

        Assert.IsNull(LaunchSettingsReader.HttpUrl(NewProject(httpsOnly)));
    }

    [TestMethod]
    public void HttpUrl_SkipsAProfileWithNoUrlRatherThanStopping()
    {
        // IIS Express and "Project" profiles sit side by side, and the first one listed need not be
        // the one with an address.
        const string mixed = """
        {
          "profiles": {
            "IIS Express": { "commandName": "IISExpress" },
            "http": { "commandName": "Project", "applicationUrl": "http://localhost:5099" }
          }
        }
        """;

        Assert.AreEqual("http://localhost:5099", LaunchSettingsReader.HttpUrl(NewProject(mixed)));
    }
}
