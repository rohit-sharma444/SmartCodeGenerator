using System.Reflection;

namespace CodeGenerator.App;

/// <summary>
/// The version the tool reports, on the banner and in <c>status</c>.
/// </summary>
/// <remarks>
/// Not a one-liner because of one gotcha: .NET writes the source revision into
/// <see cref="AssemblyInformationalVersionAttribute"/> as <c>1.0.0+&lt;40-char sha&gt;</c>, so
/// reading it straight prints a commit hash at the user.
/// </remarks>
internal static class VersionInfo
{
    /// <summary>Shown when the assembly carries no version at all, rather than a dangling space
    /// after the product name.</summary>
    private const string Unknown = "unknown version";

    /// <summary>The running assembly's version, cleaned.</summary>
    public static string Current { get; } = Clean(
        typeof(VersionInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion);

    /// <summary>
    /// <paramref name="informationalVersion"/> without the <c>+sha</c> build metadata.
    /// </summary>
    /// <remarks>
    /// Splits on '+' only. A pre-release suffix ('-beta.4') is part of the version proper and is
    /// kept — semver puts build metadata after '+' and nowhere else.
    /// </remarks>
    public static string Clean(string? informationalVersion)
    {
        var text = (informationalVersion ?? string.Empty).Trim();

        if (text.Length == 0)
            return Unknown;

        var plus = text.IndexOf('+');

        var version = (plus < 0 ? text : text[..plus]).Trim();

        return version.Length == 0 ? Unknown : version;
    }
}
