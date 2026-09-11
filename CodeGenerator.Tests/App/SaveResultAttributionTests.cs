using AppScaffolding;
using CodeGenerationEngine.Core;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

[TestClass]
public class SaveResultAttributionTests
{
    private const string Driver = """
using System.Collections.Generic;
using Acme.Data;
using Acme.Validation;

namespace Acme.Probe;

public static class Probe
{
    public static Dictionary<string, string[]> TwoFieldsAndOneWholeRecord()
        => new SaveResult<string>
        {
            Success = false,
            Failures =
            [
                new ValidationError("Surname", "Surname is required."),
                new ValidationError("Surname", "Surname must be 50 characters or fewer."),
                new ValidationError(null, "This record is not permitted."),
            ],
        }.AsDictionary();
}
""";

    [TestMethod]
    public void TheDictionaryIsKeyedByProperty_WithWholeRecordErrorsUnderTheEmptyKey()
    {
        // The standard ASP.NET 400 payload. Keeping every message under one empty key, as an
        // earlier draft of the spec did, would leave the Web API unable to say which field was
        // rejected - which is most of what attribution is for.
        var probe = Compile();

        var dictionary = (System.Collections.IDictionary)probe.GetType("Acme.Probe.Probe")!
            .GetMethod("TwoFieldsAndOneWholeRecord")!.Invoke(null, null)!;

        Assert.HasCount(2, dictionary);
        Assert.HasCount(2, (System.Array)dictionary["Surname"]!);
        Assert.HasCount(1, (System.Array)dictionary[string.Empty]!);
    }

    [TestMethod]
    public void ErrorsStaysAListOfSentences()
    {
        // Compatibility for everything that only prints them - the console menu, the desktop
        // status line, the generated tests.
        var source = SaveResultTemplate.Source("Acme");

        StringAssert.Contains(source, "IReadOnlyList<string> Errors");
    }

    private static System.Reflection.Assembly Compile()
    {
        var result = Compiler.CompileSources(new (string Path, string Code)[]
        {
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Data/SaveResult.cs", SaveResultTemplate.Source("Acme")),
            ("Probe/Probe.cs", Driver),
        });

        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error).Select(e => e.ToString())));

        return System.Reflection.Assembly.Load(result.AssemblyBytes!);
    }
}
