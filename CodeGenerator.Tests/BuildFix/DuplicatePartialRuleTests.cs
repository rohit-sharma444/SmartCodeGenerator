using CodeGenerator.BuildFix;

namespace CodeGenerator.Tests.BuildFix;

[TestClass]
public class DuplicatePartialRuleTests
{
    private const string Plain = "namespace Shop;\n\npublic class Order\n{\n    public int Id { get; set; }\n}\n";
    private const string Half = "namespace Shop;\n\npublic partial class Order\n{\n    public bool Ok() => Id > 0;\n}\n";

    private static BuildDiagnostic Diag(string file) => new(
        file, 3, 14, "CS0260",
        "Missing partial modifier on declaration of type 'Order'; another partial declaration of this type exists");

    [TestMethod]
    public void Fix_MakesTheDeclarationPartial_AndTheProjectCompiles()
    {
        var root = FixTestSupport.TempProject();
        var plain = FixTestSupport.Write(root, "Models/Order.cs", Plain);
        FixTestSupport.Write(root, "Models/Order.Validation.cs", Half);
        Assert.IsTrue(FixTestSupport.CompileErrors(Plain, Half).Any(e => e.StartsWith("CS0260")));   // RED first
        var session = new FixSession(root);
        session.BeginRound(1);

        var fixedIt = new DuplicatePartialRule().Fix(Diag(plain), session);

        Assert.IsTrue(fixedIt);
        var after = File.ReadAllText(plain);
        StringAssert.Contains(after, "public partial class Order");
        Assert.HasCount(0, FixTestSupport.CompileErrors(after, Half));
    }

    [TestMethod]
    public void Fix_KeepsOtherModifiers()
    {
        var root = FixTestSupport.TempProject();
        var plain = FixTestSupport.Write(root, "Models/Order.cs", "public sealed class Order { }\n");
        var session = new FixSession(root);
        session.BeginRound(1);

        new DuplicatePartialRule().Fix(Diag(plain), session);

        StringAssert.Contains(File.ReadAllText(plain), "public sealed partial class Order");
    }

    [TestMethod]
    public void Fix_ReturnsFalse_WhenAlreadyPartial()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Models/Order.cs", Half);
        var session = new FixSession(root);
        session.BeginRound(1);

        Assert.IsFalse(new DuplicatePartialRule().Fix(Diag(file), session));
        Assert.AreEqual(Half, File.ReadAllText(file));
    }

    [TestMethod]
    public void Fix_ReturnsFalse_ForAnotherCode()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Models/Order.cs", Plain);
        var session = new FixSession(root);
        session.BeginRound(1);

        var other = Diag(file) with { Code = "CS0103" };

        Assert.IsFalse(new DuplicatePartialRule().Fix(other, session));
    }

    [TestMethod]
    public void Fix_ReturnsFalse_WhenTheTypeIsNotInTheFile()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Models/Other.cs", "public class Other { }\n");
        var session = new FixSession(root);
        session.BeginRound(1);

        Assert.IsFalse(new DuplicatePartialRule().Fix(Diag(file), session));
    }
}
