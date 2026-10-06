using System.Text.RegularExpressions;

namespace CodeGenerationEngine.Core;

/// <summary>
/// The name <c>create interface</c> gives an interface from what was typed.
/// </summary>
/// <remarks>
/// It used to add <c>I</c> only when the name did not already start with one, so
/// <c>create interface invoice</c> made <c>Invoice</c> and <c>Item</c> stayed <c>Item</c>, neither an
/// interface name, while <c>ishape</c> made <c>Ishape</c>. The rule now: a name that already reads
/// <c>I</c> followed by a capital letter (<c>IShape</c>) is kept; anything else is PascalCased and
/// given the prefix (<c>Item</c> to <c>IItem</c>, <c>invoice</c> to <c>IInvoice</c>). All lower case
/// is therefore always prefixed - <c>ishape</c> makes <c>IIshape</c> - since <c>item</c> and
/// <c>ishape</c> cannot be told apart; the reply names the interface made and says to type
/// <c>IShape</c> for that name.
/// </remarks>
public static class InterfaceNaming
{
    private static readonly Regex AlreadyNamed = new("^I[A-Z]", RegexOptions.CultureInvariant);

    public static string NameFor(string typed)
    {
        var name = Pascal(typed);
        return AlreadyNamed.IsMatch(typed.Trim()) ? name : "I" + name;
    }

    // Each part's first letter capitalised and the rest kept, so IRepository survives.
    private static string Pascal(string value) =>
        string.Concat(Regex.Split(value.Trim(), @"[\s_\-]+")
            .Where(p => p.Length > 0)
            .Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
}
