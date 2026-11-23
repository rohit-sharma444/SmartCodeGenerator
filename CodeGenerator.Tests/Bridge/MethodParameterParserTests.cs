using NaturalLanguageBridge;
using static NaturalLanguageBridge.MethodParameterParser;

namespace CodeGenerator.Tests.Bridge;

/// <summary>
/// The parameter list of a method, in the C# form (type, then name) or the English form (name,
/// then type, as property lists are written).
/// </summary>
[TestClass]
public sealed class MethodParameterParserTests
{
    private static string Shown(IEnumerable<CodeGenerationEngine.Models.ParameterDefinition> parameters) =>
        string.Join("; ", parameters.Select(p => $"{p.Type} {p.Name}"));

    [TestMethod]
    [DataRow("decimal percent, string code", Order.TypeFirst, "decimal percent; string code")]
    [DataRow("percent decimal, code string", Order.NameFirst, "decimal percent; string code")]
    [DataRow("Dictionary<string, int> map", Order.TypeFirst, "Dictionary<string, int> map")]
    [DataRow("map Dictionary<string, int>", Order.NameFirst, "Dictionary<string, int> map")]
    [DataRow("  int  count  ", Order.TypeFirst, "int count")]
    [DataRow("", Order.TypeFirst, "")]
    public void A_list_is_read_in_its_order(string text, Order order, string expected)
    {
        Assert.IsTrue(TryParse(text, order, out var parameters, out var error), error);
        Assert.AreEqual(expected, Shown(parameters));
    }

    [TestMethod]
    [DataRow("decimal", Order.TypeFirst, "'decimal'")]
    [DataRow("string class", Order.TypeFirst, "'class' is not a valid parameter name")]
    [DataRow("int a, int A", Order.TypeFirst, "'A' is repeated")]
    [DataRow("2x int", Order.NameFirst, "'2x' is not a valid parameter name")]
    [DataRow("int a,", Order.TypeFirst, "empty")]
    public void A_bad_list_is_refused_by_name(string text, Order order, string expected)
    {
        Assert.IsFalse(TryParse(text, order, out _, out var error));
        StringAssert.Contains(error, expected);
    }
}
