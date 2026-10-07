using CodeGenerator.BuildFix;

namespace CodeGenerator.Tests.BuildFix;

[TestClass]
public class DuplicatePartialRuleTests
{
    private const string Plain = "namespace Shop;\n\npublic class Order\n{\n    public int Id { get; set; }\n}\n";
    private const string Half = "namespace Shop;\n\npublic partial class Order\n{\n    public bool Ok() => Id > 0;\n}\n";

    private static BuildDiagnostic Diag(string file, int line = 3, int column = 14) => new(
        file, line, column, "CS0260",
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

        var result = new DuplicatePartialRule().Fix(Diag(plain, 1, 22), session);

        Assert.IsTrue(result);
        StringAssert.Contains(File.ReadAllText(plain), "public sealed partial class Order");
    }

    [TestMethod]
    public void Fix_ReturnsFalse_WhenAlreadyPartial()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Models/Order.cs", Half);
        var bytes = File.ReadAllBytes(file);
        var session = new FixSession(root);
        session.BeginRound(1);

        // Point to the identifier "Order" on line 3, column 24 (in "public partial class Order")
        Assert.IsFalse(new DuplicatePartialRule().Fix(Diag(file, 3, 24), session));
        Assert.AreEqual(Half, File.ReadAllText(file));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(file));
    }

    [TestMethod]
    public void Fix_ReturnsFalse_ForAnotherCode()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Models/Order.cs", Plain);
        var bytes = File.ReadAllBytes(file);
        var session = new FixSession(root);
        session.BeginRound(1);

        var other = Diag(file) with { Code = "CS0103" };

        Assert.IsFalse(new DuplicatePartialRule().Fix(other, session));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(file));
    }

    [TestMethod]
    public void Fix_ReturnsFalse_WhenTheTypeIsNotInTheFile()
    {
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Models/Other.cs", "public class Other { }\n");
        var bytes = File.ReadAllBytes(file);
        var session = new FixSession(root);
        session.BeginRound(1);

        Assert.IsFalse(new DuplicatePartialRule().Fix(Diag(file), session));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(file));
    }

    [TestMethod]
    public void Fix_PicksTheRightType_WhenNestedTypeComesFirst()
    {
        // OLD code (name-only) picks NESTED Order (first in document order).
        // NEW code (position-based) picks TOP-LEVEL Order (at diagnostic line/column).
        var code = "public class Holder\n{\n    public class Order { }\n}\n\npublic class Order\n{\n}\n";
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Models/Order.cs", code);
        var originalBytes = File.ReadAllBytes(file);
        var session = new FixSession(root);
        session.BeginRound(1);

        // Diagnostic points to line 6, column 14 (the top-level Order)
        var result = new DuplicatePartialRule().Fix(Diag(file, 6, 14), session);

        Assert.IsTrue(result);
        var after = File.ReadAllText(file);
        // Nested Order should be UNTOUCHED
        StringAssert.Contains(after, "public class Holder\n{\n    public class Order { }");
        // Top-level Order should be PARTIAL
        StringAssert.Contains(after, "public partial class Order\n{\n}");
        // Verify nested wasn't touched by checking position
        var afterBytes = File.ReadAllBytes(file);
        // The first 3 lines (nested part) should be unchanged
        var original = File.ReadAllBytes(file);
        var originalText = File.ReadAllText(file);
    }

    [TestMethod]
    public void Fix_PicksTheRightType_WhenInsidePartialOuter()
    {
        // Nested Order inside an already-PARTIAL outer Holder
        var code = "public partial class Holder\n{\n    public class Order { }\n}\n";
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Models/Order.cs", code);
        var session = new FixSession(root);
        session.BeginRound(1);

        // Diagnostic points to line 3, column 20 (the nested Order)
        var result = new DuplicatePartialRule().Fix(Diag(file, 3, 20), session);

        Assert.IsTrue(result);
        var after = File.ReadAllText(file);
        StringAssert.Contains(after, "public partial class Order { }");
        StringAssert.Contains(after, "public partial class Holder");
    }

    [TestMethod]
    public void Fix_HandlesGenericType()
    {
        var code = "namespace Shop;\n\npublic class Order<T>\n{\n    public T Value { get; set; }\n}\n";
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Models/Order.cs", code);
        var session = new FixSession(root);
        session.BeginRound(1);

        var diag = Diag(file) with { Message = "Missing partial modifier on declaration of type 'Order<T>'; another partial declaration of this type exists" };

        var result = new DuplicatePartialRule().Fix(diag, session);

        Assert.IsTrue(result);
        var after = File.ReadAllText(file);
        StringAssert.Contains(after, "public partial class Order<T>");
    }

    [TestMethod]
    public void Fix_Struct_BecomesPartial()
    {
        var code = "public struct Order { }\n";
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Models/Order.cs", code);
        var session = new FixSession(root);
        session.BeginRound(1);

        var result = new DuplicatePartialRule().Fix(Diag(file, 1, 15), session);

        Assert.IsTrue(result);
        StringAssert.Contains(File.ReadAllText(file), "public partial struct Order");
    }

    [TestMethod]
    public void Fix_Record_BecomesPartial()
    {
        var code = "public record Order { }\n";
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Models/Order.cs", code);
        var session = new FixSession(root);
        session.BeginRound(1);

        var result = new DuplicatePartialRule().Fix(Diag(file, 1, 15), session);

        Assert.IsTrue(result);
        StringAssert.Contains(File.ReadAllText(file), "public partial record Order");
    }

    [TestMethod]
    public void Fix_Interface_BecomesPartial()
    {
        var code = "public interface IOrder { }\n";
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Models/IOrder.cs", code);
        var session = new FixSession(root);
        session.BeginRound(1);

        var diag = Diag(file, 1, 18) with { Message = "Missing partial modifier on declaration of type 'IOrder'; another partial declaration of this type exists" };
        var result = new DuplicatePartialRule().Fix(diag, session);

        Assert.IsTrue(result);
        StringAssert.Contains(File.ReadAllText(file), "public partial interface IOrder");
    }

    [TestMethod]
    public void Fix_OnlyFixesTheDiagnosedType_InFileWithMultipleTypes()
    {
        var code = "public class A { }\npublic class Order { }\n";
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Models/Order.cs", code);
        var session = new FixSession(root);
        session.BeginRound(1);

        // Diagnostic points to line 2 (Order)
        var result = new DuplicatePartialRule().Fix(Diag(file, 2, 14), session);

        Assert.IsTrue(result);
        var after = File.ReadAllText(file);
        StringAssert.Contains(after, "public class A { }");
        StringAssert.Contains(after, "public partial class Order { }");
    }

    [TestMethod]
    public void Fix_PreservesCRLF_AndBOM()
    {
        var code = "public class Order { }\n";
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Models/Order.cs", code);

        // Simulate a CRLF file with UTF-8 BOM
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var crlf = System.Text.Encoding.UTF8.GetBytes(code.Replace("\n", "\r\n"));
        var originalBytes = bom.Concat(crlf).ToArray();
        File.WriteAllBytes(file, originalBytes);

        var session = new FixSession(root);
        session.BeginRound(1);

        var fixResult = new DuplicatePartialRule().Fix(Diag(file, 1, 14), session);

        Assert.IsTrue(fixResult);
        var resultBytes = File.ReadAllBytes(file);

        // Should still have BOM
        Assert.AreEqual(0xEF, resultBytes[0]);
        Assert.AreEqual(0xBB, resultBytes[1]);
        Assert.AreEqual(0xBF, resultBytes[2]);

        // Verify the exact bytes: BOM + "public partial class Order { }\r\n"
        var expectedWithoutBom = System.Text.Encoding.UTF8.GetBytes("public partial class Order { }\r\n");
        var expectedBytes = bom.Concat(expectedWithoutBom).ToArray();
        CollectionAssert.AreEqual(expectedBytes, resultBytes);
    }

    [TestMethod]
    public void Fix_PreservesAttributesAndComments()
    {
        var code = "// Comment\n[System.Obsolete]\npublic class Order { }\n";
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Models/Order.cs", code);
        var session = new FixSession(root);
        session.BeginRound(1);

        var result = new DuplicatePartialRule().Fix(Diag(file, 3, 14), session);

        Assert.IsTrue(result);
        var after = File.ReadAllText(file);
        StringAssert.Contains(after, "// Comment");
        StringAssert.Contains(after, "[System.Obsolete]");
        StringAssert.Contains(after, "public partial class Order");
    }

    [TestMethod]
    public void Fix_ReturnsFalse_WhenColumnBeyondLineEnd()
    {
        var code = "public class Order { }\n";
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Models/Order.cs", code);
        var bytes = File.ReadAllBytes(file);
        var session = new FixSession(root);
        session.BeginRound(1);

        // Column 100 is way beyond the line length
        var result = new DuplicatePartialRule().Fix(Diag(file, 1, 100), session);

        Assert.IsFalse(result);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(file));
    }

    [TestMethod]
    public void Fix_ReturnsFalse_WhenLinePastEndOfFile()
    {
        var code = "public class Order { }\n";
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Models/Order.cs", code);
        var bytes = File.ReadAllBytes(file);
        var session = new FixSession(root);
        session.BeginRound(1);

        // Line 10 doesn't exist (only 1 line)
        var result = new DuplicatePartialRule().Fix(Diag(file, 10, 14), session);

        Assert.IsFalse(result);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(file));
    }

    [TestMethod]
    public void Fix_ReturnsFalse_WhenColumnIsZero()
    {
        var code = "public class Order { }\n";
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Models/Order.cs", code);
        var bytes = File.ReadAllBytes(file);
        var session = new FixSession(root);
        session.BeginRound(1);

        // Column 0 is invalid (1-based)
        var result = new DuplicatePartialRule().Fix(Diag(file, 1, 0), session);

        Assert.IsFalse(result);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(file));
    }

    [TestMethod]
    public void Fix_PicksCorrectType_WithTwoSameNameInDifferentNamespaces()
    {
        // OLD code picks first non-partial Order (ns1).
        // NEW code picks the one at diagnostic position (ns2).
        var code = "namespace ns1 { public class Order { } }\nnamespace ns2 { public class Order { } }\n";
        var root = FixTestSupport.TempProject();
        var file = FixTestSupport.Write(root, "Models/Order.cs", code);
        var session = new FixSession(root);
        session.BeginRound(1);

        // Diagnostic points to line 2 (ns2.Order) - column 30 is "O" in "Order"
        var result = new DuplicatePartialRule().Fix(Diag(file, 2, 30), session);

        Assert.IsTrue(result);
        var after = File.ReadAllText(file);
        // ns1.Order should be UNTOUCHED
        StringAssert.Contains(after, "namespace ns1 { public class Order { } }");
        // ns2.Order should be PARTIAL
        StringAssert.Contains(after, "namespace ns2 { public partial class Order { } }");
    }
}
