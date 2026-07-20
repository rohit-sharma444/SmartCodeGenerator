using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;

namespace CodeGenerator.Tests.Core;

[TestClass]
public class MethodBodyGeneratorTests
{
    private static List<string> Body(string method, string returnType)
        => MethodBodyGenerator.Generate(
               "Employee",
               new MethodDefinition { Name = method, ReturnType = returnType },
               []).ToList();

    [TestMethod]
    public void Get_HasGuidanceAndAValidDefaultReturn()
    {
        var body = Body("GetActive", "Employee");
        Assert.IsTrue(body.Any(l => l.Contains("//")), "expected a guidance comment");
        Assert.IsTrue(body.Any(l => l.Contains("return default!;")));
    }

    [TestMethod]
    public void Update_NonVoid_EmitsAReturn_SoItCompiles()
    {
        var body = Body("UpdateEmployee", "bool");
        Assert.IsTrue(body.Any(l => l.Contains("return false;")),
            "a bool update must return a value");
    }

    [TestMethod]
    public void Update_Void_HasNoReturn()
    {
        var body = Body("UpdateEmployee", "void");
        Assert.IsFalse(body.Any(l => l.TrimStart().StartsWith("return")));
    }

    [TestMethod]
    public void Delete_NonVoid_EmitsAReturn()
        => Assert.IsTrue(Body("DeleteEmployee", "int").Any(l => l.Contains("return 0;")));

    [TestMethod]
    public void Count_ReturnsZeroForInt()
        => Assert.IsTrue(Body("CountEmployees", "int").Any(l => l.Contains("return 0;")));

    [TestMethod]
    public void Exists_ReturnsFalseForBool()
        => Assert.IsTrue(Body("HasEmployee", "bool").Any(l => l.Contains("return false;")));

    [TestMethod]
    public void NoBody_ReferencesAnUndeclaredItemsField()
    {
        foreach (var verb in new[] { "GetById", "FindByName", "GetAll", "UpdateX", "DeleteX" })
            Assert.IsFalse(
                Body(verb, "Employee").Any(l => !l.TrimStart().StartsWith("//") && l.Contains("_items")),
                $"{verb}: no executable statement may reference _items");
    }
}
