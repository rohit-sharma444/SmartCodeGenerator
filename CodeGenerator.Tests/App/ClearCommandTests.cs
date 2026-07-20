using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

[TestClass]
public class ClearCommandTests
{
    [TestMethod]
    [DataRow("clear")]
    [DataRow("cls")]
    [DataRow("CLEAR")]
    [DataRow("Cls")]
    [DataRow("  clear  ")]
    [DataRow("\tcls\t")]
    public void IsClear_RecognizesClearAndCls(string input)
        => Assert.IsTrue(ClearCommand.IsClear(input));

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("clearall")]
    [DataRow("clear foo")]
    [DataRow("cls now")]
    [DataRow("create class")]
    public void IsClear_RejectsEverythingElse(string input)
        => Assert.IsFalse(ClearCommand.IsClear(input));
}
