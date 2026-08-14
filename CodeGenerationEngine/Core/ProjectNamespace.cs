using System.Text;
using System.Xml.Linq;

namespace CodeGenerationEngine.Core;

/// <summary>
/// The namespace a class generated into a project should declare.
/// </summary>
/// <remarks>
/// Every class made by <c>create class</c> used to declare <c>GeneratedCode</c> whatever project it
/// was written into, so nothing in that project could see it without a <c>using</c> — and it
/// contradicted the root-namespace work done for <c>build app:</c>.
/// <para>
/// Lives here rather than reusing <c>AppScaffolding</c>'s helpers because AppScaffolding sits
/// <i>above</i> CodeGenerationEngine in the project graph; borrowing from it would invert the
/// reference.
/// </para>
/// </remarks>
public static class ProjectNamespace
{
    /// <summary>
    /// What a class declares when there is no project — <c>create class</c> with nothing selected
    /// writes into the workspace, where there is no project namespace to match.
    /// </summary>
    public const string Fallback = "GeneratedCode";

    /// <summary>
    /// The root namespace of <paramref name="csprojPath"/>: its <c>&lt;RootNamespace&gt;</c> if it
    /// declares one, otherwise the project file's name, which is MSBuild's own default and therefore
    /// what the compiler assumes for the rest of the project.
    /// </summary>
    public static string For(string? csprojPath)
    {
        if (string.IsNullOrWhiteSpace(csprojPath) || !File.Exists(csprojPath))
            return Fallback;

        var declared = DeclaredRootNamespace(csprojPath);

        var name = string.IsNullOrWhiteSpace(declared)
            ? Path.GetFileNameWithoutExtension(csprojPath)
            : declared;

        var identifier = AsNamespace(name);

        return identifier.Length == 0 ? Fallback : identifier;
    }

    /// <summary>
    /// The declared root namespace, or null. Malformed XML falls back rather than throwing:
    /// generating a class must not fail because the project file is broken.
    /// </summary>
    private static string? DeclaredRootNamespace(string csprojPath)
    {
        try
        {
            return XDocument.Load(csprojPath)
                .Descendants()
                .FirstOrDefault(x => x.Name.LocalName == "RootNamespace")
                ?.Value
                .Trim();
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException
                                      or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// <paramref name="name"/> as something that can follow the <c>namespace</c> keyword.
    /// </summary>
    /// <remarks>
    /// A project may legitimately be called "My Shop-App", which is not an identifier. Invalid
    /// characters become underscores — MSBuild's own behaviour — and dots survive, since a root
    /// namespace is genuinely dotted ("CodisLtd.Shop"). A leading digit is prefixed rather than
    /// dropped, so "7Eleven" stays recognisable.
    /// </remarks>
    private static string AsNamespace(string name)
    {
        var builder = new StringBuilder(name.Length + 1);

        foreach (var ch in name.Trim())
        {
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '.')
                builder.Append(ch);
            else
                builder.Append('_');
        }

        var text = builder.ToString().Trim('.');

        if (text.Length > 0 && char.IsDigit(text[0]))
            text = '_' + text;

        return text;
    }
}
