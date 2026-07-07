using System.Text;

namespace AppScaffolding;

/// <summary>Derives a PascalCase .NET project name from free-text.</summary>
public static class DomainAppName
{
    public static string FromDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return "GeneratedApp";

        var sb = new StringBuilder();
        bool startOfWord = true;

        foreach (var ch in description)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(startOfWord ? char.ToUpperInvariant(ch) : ch);
                startOfWord = false;
            }
            else
            {
                startOfWord = true;
            }
        }

        if (sb.Length == 0)
            return "GeneratedApp";

        // A project/type name must not start with a digit.
        if (char.IsDigit(sb[0]))
            sb.Insert(0, "App");

        return sb.ToString();
    }
}
