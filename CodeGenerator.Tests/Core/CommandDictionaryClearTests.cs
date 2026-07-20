using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.Core;

[TestClass]
public class CommandDictionaryClearTests
{
    [TestMethod]
    public void Commands_IncludeBothClearAndClsAliases()
    {
        var names = CommandDictionary.Commands.Select(c => c.Command).ToList();
        CollectionAssert.Contains(names, "clear");
        CollectionAssert.Contains(names, "cls");
    }
}
