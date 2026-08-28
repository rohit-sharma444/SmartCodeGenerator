# Customized Validation — Writer and Page Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Right-click a class in the chat page's tree, type the validation in plain English on the page that opens, and have the check land in that class's own source file inside a `Validate()` method.

**Architecture:** A writer in the engine (`ClassValidationWriter`) turns a parsed `IValidationRule` into a check spliced into the class's own file — located with Roslyn, inserted as text so nothing else in the file moves, compiled before it is saved and abandoned if the compile got worse. A page served under the chat host's existing token guard collects the sentence, parses it through `ValidationRuleCatalog`, and calls the writer. The tree raises the right-clicked class up to the chat page, which navigates carrying the session token.

**Tech Stack:** C#, .NET, Roslyn (`Microsoft.CodeAnalysis.CSharp`), Blazor Server (interactive server render mode), MSTest, bUnit.

**Spec:** `docs/superpowers/specs/2026-08-27-customized-validation-design.md`

**Depends on:** `docs/superpowers/plans/2026-08-27-customized-validation-catalog.md` — specifically `IValidationRule.Properties` and `IValidationRuleParser.Examples` from its Task 2. Do not start this plan until that one's Task 2 is committed.

## Global Constraints

- **The route must sit under `/chat`.** `LoopbackHost.TryStart` guards exactly one path segment (`ChatWebHost` passes `"chat"`) with `Path.StartsWithSegments`, 404-ing anything without the session token. A page at `/validation` would be served, unguarded, to every process on the machine — and this page writes source files. The route is `/chat/validation`.
- **`@rendermode InteractiveServer` on every page.** A page without it draws its whole DOM and wires none of it: every button inert, nothing failing, no bUnit test able to see it — bUnit renders interactively by construction. `ChatWebHostTests.Serves_the_page_as_an_interactive_component` is the shape of the test that does see it.
- **A Razor *string* parameter takes its attribute literally.** `Root="Session.Root"` roots a tree in those twelve characters; it must be `Root="@Session.Root"`.
- **Never write a file without preserving its BOM and its newline style.** `File.ReadAllText` + `File.WriteAllText` silently strips a BOM, and a Write-tool file lands LF-only in this CRLF checkout. Both have cost this repository real time.
- **One command at a time.** `ChatSession` runs commands serially because `OutputCapture` swaps the process-global `Console.Out` and the engine keeps static state. The page does not run commands through the session; it calls the writer directly, so this constraint bounds only what the page may do while `Session.Running` is true — which is nothing.
- **MSTest:** `Assert.HasCount(n, collection)`, never `Assert.AreEqual(n, collection.Count)` (MSTEST0037 is an error in the Release build). Tests that start a Kestrel are `[DoNotParallelize]`.
- **Test command:** `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~<TestClass>"` from `D:\SmartCodeGenerator-Repository`.
- **CI gives no verdict on this repository.** A local `dotnet test SmartCodeGenerator.slnx --configuration Release` is the verdict.

---

### Task 1: The writer creates `Validate()` on a class that has none

**Files:**
- Create: `CodeGenerationEngine/Core/Validation/ClassValidationWriter.cs`
- Create: `CodeGenerationEngine/Core/Validation/SourceFileText.cs`
- Test: `CodeGenerator.Tests/Core/Validation/ClassValidationWriterTests.cs`

**Interfaces:**
- Consumes: `IValidationRule` (`Describe`, `Emit`, `Properties`), `ValidationEmitContext`, `EntityExtractor.ExtractProperties(string)`, `Compiler.Compile(string)`.
- Produces:
  - `public sealed record ValidationWriteResult(bool Ok, string Message)`
  - `public static ValidationWriteResult ClassValidationWriter.Add(string classFile, string className, IValidationRule rule)`
  - `public static IReadOnlyList<string> ClassValidationWriter.ExistingRules(string classFile, string className)`
  - `internal static class SourceFileText` with `Read(string path, out bool hasBom, out string newline)` and `Write(string path, string text, bool hasBom)`

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/Core/Validation/ClassValidationWriterTests.cs`:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;

namespace CodeGenerator.Tests.Core.Validation;

/// <summary>
/// The writer edits a file someone may have written by hand. Every test here asserts both halves:
/// that the check arrived, and that nothing else did.
/// </summary>
[TestClass]
public class ClassValidationWriterTests
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void MakeFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "cvw-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void RemoveFolder()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private string WriteClass(string name, string source)
    {
        var path = Path.Combine(_folder, name + ".cs");
        File.WriteAllText(path, source);
        return path;
    }

    private static IValidationRule Rule(string fragment)
    {
        var ctx = new ValidationParseContext { Fragment = fragment, ClassName = "Employee" };
        var rules = ValidationRuleCatalog.Default.Parse(fragment, ValidationCategory.Data, ctx);
        Assert.HasCount(1, rules);
        return rules[0];
    }

    private static void AssertCompiles(string source)
    {
        var result = Compiler.Compile(source);
        Assert.IsTrue(result.Success,
            string.Join("\n", result.Diagnostics.Select(d => d.ToString())));
    }

    private const string PlainEmployee =
        "namespace Acme\r\n" +
        "{\r\n" +
        "    public class Employee\r\n" +
        "    {\r\n" +
        "        public int Age { get; set; }\r\n" +
        "\r\n" +
        "        public string Name { get; set; } = string.Empty;\r\n" +
        "    }\r\n" +
        "}\r\n";

    [TestMethod]
    public void Creates_a_validate_method_on_a_class_that_has_none()
    {
        var file = WriteClass("Employee", PlainEmployee);

        var result = ClassValidationWriter.Add(file, "Employee", Rule("Age must be at least 18"));

        Assert.IsTrue(result.Ok, result.Message);

        var updated = File.ReadAllText(file);
        StringAssert.Contains(updated, "public System.Collections.Generic.List<string> Validate()");
        StringAssert.Contains(updated, "// Age must be at least 18");
        StringAssert.Contains(updated, "__n < 18m");
        AssertCompiles(updated);
    }

    [TestMethod]
    public void The_lines_it_did_not_add_are_byte_identical()
    {
        var file = WriteClass("Employee", PlainEmployee);

        ClassValidationWriter.Add(file, "Employee", Rule("Age must be at least 18"));

        var updated = File.ReadAllText(file);

        foreach (var line in new[]
        {
            "    public class Employee",
            "        public int Age { get; set; }",
            "        public string Name { get; set; } = string.Empty;"
        })
            StringAssert.Contains(updated, line, $"the writer moved: {line}");
    }

    [TestMethod]
    public void The_newline_style_of_the_file_is_kept()
    {
        var file = WriteClass("Employee", PlainEmployee); // CRLF throughout

        ClassValidationWriter.Add(file, "Employee", Rule("Age must be at least 18"));

        var updated = File.ReadAllText(file);
        var loneFeeds = updated.Split('\n').Count(part => !part.EndsWith('\r'));

        // The last split part is after the final newline, so one is expected and no more.
        Assert.AreEqual(1, loneFeeds, "a bare LF was written into a CRLF file");
    }

    [TestMethod]
    public void A_byte_order_mark_survives()
    {
        var path = Path.Combine(_folder, "Employee.cs");
        File.WriteAllText(path, PlainEmployee, new System.Text.UTF8Encoding(true));

        ClassValidationWriter.Add(path, "Employee", Rule("Age must be at least 18"));

        var bytes = File.ReadAllBytes(path);
        Assert.IsTrue(bytes is [0xEF, 0xBB, 0xBF, ..], "the BOM was stripped");
    }

    [TestMethod]
    public void The_rules_on_a_class_can_be_read_back()
    {
        var file = WriteClass("Employee", PlainEmployee);

        ClassValidationWriter.Add(file, "Employee", Rule("Age must be at least 18"));

        var rules = ClassValidationWriter.ExistingRules(file, "Employee");

        Assert.HasCount(1, rules);
        Assert.AreEqual("Age must be at least 18", rules[0]);
    }

    [TestMethod]
    public void A_file_scoped_namespace_is_indented_correctly()
    {
        var file = WriteClass("Employee",
            "namespace Acme;\r\n" +
            "\r\n" +
            "public class Employee\r\n" +
            "{\r\n" +
            "    public int Age { get; set; }\r\n" +
            "}\r\n");

        var result = ClassValidationWriter.Add(file, "Employee", Rule("Age must be at least 18"));

        Assert.IsTrue(result.Ok, result.Message);
        AssertCompiles(File.ReadAllText(file));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ClassValidationWriterTests"`

Expected: FAIL to compile — `ClassValidationWriter` does not exist.

- [ ] **Step 3: Write the file-text helper**

Create `CodeGenerationEngine/Core/Validation/SourceFileText.cs`:

```csharp
using System.IO;
using System.Text;

namespace CodeGenerationEngine.Core.Validation;

/// <summary>
/// Reading and writing a source file without changing anything about it that is not the edit.
/// </summary>
/// <remarks>
/// <c>File.ReadAllText</c> followed by <c>File.WriteAllText</c> silently drops a byte-order mark,
/// and a file written with the default newline lands LF-only in a CRLF checkout. Both have been
/// real defects here, and neither shows up in a diff you skim.
/// </remarks>
internal static class SourceFileText
{
    public static string Read(string path, out bool hasBom, out string newline)
    {
        var bytes = File.ReadAllBytes(path);

        hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

        var text = new UTF8Encoding(false).GetString(hasBom ? bytes[3..] : bytes);

        newline = text.Contains("\r\n") ? "\r\n" : "\n";

        return text;
    }

    public static void Write(string path, string text, bool hasBom) =>
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: hasBom));
}
```

- [ ] **Step 4: Write the writer, creation path only**

Create `CodeGenerationEngine/Core/Validation/ClassValidationWriter.cs`:

```csharp
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeGenerationEngine.Core.Validation;

/// <summary>What the writer did, or why it did nothing.</summary>
/// <remarks>
/// A refusal carries its reason because the reason is what the person on the page reads. "Could
/// not add the rule" is the message this project has learned not to print.
/// </remarks>
public sealed record ValidationWriteResult(bool Ok, string Message)
{
    public static ValidationWriteResult Refused(string message) => new(false, message);

    public static ValidationWriteResult Written(string message) => new(true, message);
}

/// <summary>
/// Puts one validation check into a class's own source file.
/// </summary>
/// <remarks>
/// The check goes inside a <c>Validate()</c> the writer owns — created on the first rule, extended
/// on every one after. Each check is one <c>try</c> block preceded by the rule's own
/// <see cref="IValidationRule.Describe"/> as a comment, which is both what a reader sees and what
/// tells the writer a rule is already there.
/// <para>
/// The edit is located with Roslyn and applied as text. Rewriting the syntax tree and printing it
/// back would reformat code the user wrote; inserting at a span leaves every other byte where it
/// was, which is the only reason it is acceptable to edit a hand-written file at all.
/// </para>
/// </remarks>
public static class ClassValidationWriter
{
    private const string MethodName = "Validate";

    /// <summary>The rules already written into this class, as their own descriptions.</summary>
    public static IReadOnlyList<string> ExistingRules(string classFile, string className)
    {
        if (!File.Exists(classFile))
            return [];

        var source = SourceFileText.Read(classFile, out _, out _);

        var type = FindType(source, className);
        var method = type is null ? null : FindValidate(type);

        if (method?.Body is null)
            return [];

        return method.Body.DescendantTrivia()
            .Where(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia))
            .Select(t => t.ToString().TrimStart('/').Trim())
            .Where(text => text.Length > 0
                        && !text.StartsWith("business rule skipped", System.StringComparison.Ordinal))
            .ToList();
    }

    public static ValidationWriteResult Add(string classFile, string className, IValidationRule rule)
    {
        if (!File.Exists(classFile))
            return ValidationWriteResult.Refused($"There is no file at {classFile}.");

        var source = SourceFileText.Read(classFile, out var hasBom, out var newline);

        var type = FindType(source, className);
        if (type is null)
            return ValidationWriteResult.Refused(
                $"No class called '{className}' in {Path.GetFileName(classFile)}.");

        if (type.OpenBraceToken.IsKind(SyntaxKind.None))
            return ValidationWriteResult.Refused(
                $"'{className}' is declared without a body, so there is nowhere to put a check. "
                + "Give it a `{ }` body and try again.");

        var properties = EntityExtractor.ExtractProperties(classFile);

        var missing = rule.Properties
            .Where(name => !properties.Any(p =>
                string.Equals(p.Name, name, System.StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (missing.Count > 0)
            return ValidationWriteResult.Refused(
                $"'{className}' has no property called {string.Join(" or ", missing)}. "
                + $"It has: {string.Join(", ", properties.Select(p => p.Name))}.");

        if (ExistingRules(classFile, className)
                .Any(text => string.Equals(text, rule.Describe(), System.StringComparison.OrdinalIgnoreCase)))
            return ValidationWriteResult.Refused($"{className} already has that rule.");

        var indent = IndentOf(source, type);
        var block = CheckBlock(rule, properties, indent, newline);

        var method = FindValidate(type);

        string updated;

        if (method is null)
        {
            updated = source.Insert(
                type.CloseBraceToken.FullSpan.Start, NewMethod(block, indent, newline));
        }
        else
        {
            var unsuitable = Unsuitable(method, className);
            if (unsuitable is not null)
                return ValidationWriteResult.Refused(unsuitable);

            var exit = method.Body!.Statements.OfType<ReturnStatementSyntax>().Last();

            updated = source.Insert(exit.FullSpan.Start, newline + block);
        }

        var verdict = Verify(source, updated);
        if (verdict is not null)
            return ValidationWriteResult.Refused(verdict);

        SourceFileText.Write(classFile, updated, hasBom);

        return ValidationWriteResult.Written(
            $"{rule.Describe()} — added to {className} in {Path.GetFileName(classFile)}.");
    }

    private static TypeDeclarationSyntax? FindType(string source, string className) =>
        CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot()
            .DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => t.Identifier.ValueText == className);

    private static MethodDeclarationSyntax? FindValidate(TypeDeclarationSyntax type) =>
        type.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.ValueText == MethodName
                              && m.ParameterList.Parameters.Count == 0);

    /// <summary>How many spaces the type declaration itself is indented by.</summary>
    /// <remarks>
    /// Everything the writer emits is placed relative to this, so a file-scoped namespace (type at
    /// column 0) and a braced one (type at column 4) both come out looking like the code around
    /// them.
    /// </remarks>
    private static int IndentOf(string source, TypeDeclarationSyntax type)
    {
        var start = type.SpanStart;
        var lineStart = source.LastIndexOf('\n', System.Math.Max(0, start - 1)) + 1;

        var spaces = 0;
        for (var i = lineStart; i < start && source[i] == ' '; i++)
            spaces++;

        return spaces;
    }

    /// <summary>One rule as its comment, its try block and its catch.</summary>
    private static string CheckBlock(
        IValidationRule rule,
        IReadOnlyList<Models.PropertyDefinition> properties,
        int indent,
        string newline)
    {
        var emitted = new StringBuilder();
        rule.Emit(new ValidationEmitContext(emitted, properties));

        // ValidationEmitContext.Line writes a fixed twelve spaces, which is right for a class at
        // column 4. Everything shifts by the difference.
        var body = Reindent(emitted.ToString(), indent - 4, newline);

        var pad = new string(' ', indent + 8);

        var sb = new StringBuilder();
        sb.Append(pad).Append("// ").Append(rule.Describe()).Append(newline);
        sb.Append(pad).Append("try").Append(newline);
        sb.Append(pad).Append('{').Append(newline);
        sb.Append(body);
        sb.Append(pad).Append('}').Append(newline);
        sb.Append(pad).Append("catch (System.Exception) { }").Append(newline);

        return sb.ToString();
    }

    private static string Reindent(string block, int delta, string newline)
    {
        var lines = block.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();

        foreach (var line in lines)
        {
            if (line.Length == 0)
                continue;

            if (delta >= 0)
                sb.Append(new string(' ', delta)).Append(line).Append(newline);
            else
                sb.Append(line[System.Math.Min(-delta, LeadingSpaces(line))..]).Append(newline);
        }

        return sb.ToString();
    }

    private static int LeadingSpaces(string line)
    {
        var n = 0;
        while (n < line.Length && line[n] == ' ') n++;
        return n;
    }

    private static string NewMethod(string block, int indent, string newline)
    {
        var member = new string(' ', indent + 4);
        var body = new string(' ', indent + 8);

        var sb = new StringBuilder();
        sb.Append(newline);
        sb.Append(member).Append("/// <summary>Every validation rule on this class. Managed by SmartCodeGenerator.</summary>").Append(newline);
        sb.Append(member).Append("public System.Collections.Generic.List<string> ").Append(MethodName).Append("()").Append(newline);
        sb.Append(member).Append('{').Append(newline);
        sb.Append(body).Append("var errors = new System.Collections.Generic.List<string>();").Append(newline);
        sb.Append(newline);
        sb.Append(block);
        sb.Append(newline);
        sb.Append(body).Append("return errors;").Append(newline);
        sb.Append(member).Append('}').Append(newline);

        return sb.ToString();
    }

    private static string? Unsuitable(MethodDeclarationSyntax method, string className) => null;

    private static string? Verify(string before, string after) => null;
}
```

`Unsuitable` and `Verify` are stubs returning null in this task; Tasks 2 and 3 fill them in and have the tests that force them.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ClassValidationWriterTests"`

Expected: PASS, 6 tests. If `A_file_scoped_namespace_is_indented_correctly` fails to compile the result, print the updated file in the failure message and check `Reindent` against a negative delta.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerationEngine/Core/Validation/ClassValidationWriter.cs \
        CodeGenerationEngine/Core/Validation/SourceFileText.cs \
        CodeGenerator.Tests/Core/Validation/ClassValidationWriterTests.cs
git commit -m "feat: a writer that puts a validation check in the class's own file"
```

---

### Task 2: The writer extends a `Validate()` that is already there

**Files:**
- Modify: `CodeGenerationEngine/Core/Validation/ClassValidationWriter.cs` (`Unsuitable`)
- Test: `CodeGenerator.Tests/Core/Validation/ClassValidationWriterTests.cs`

**Interfaces:**
- Consumes: Task 1's `ClassValidationWriter.Add`.
- Produces: `Unsuitable` returns a reason string when the existing `Validate()` is not the tool's own shape, null when it is.

- [ ] **Step 1: Write the failing tests**

Append to `ClassValidationWriterTests`:

```csharp
    [TestMethod]
    public void A_second_rule_joins_the_first_in_one_method()
    {
        var file = WriteClass("Employee", PlainEmployee);

        ClassValidationWriter.Add(file, "Employee", Rule("Age must be at least 18"));
        var result = ClassValidationWriter.Add(file, "Employee", Rule("Name is required"));

        Assert.IsTrue(result.Ok, result.Message);

        var updated = File.ReadAllText(file);

        Assert.AreEqual(1, Occurrences(updated, "List<string> Validate()"),
            "a second method was added instead of the check joining the first");
        StringAssert.Contains(updated, "// Age must be at least 18");
        StringAssert.Contains(updated, "// Name is required");
        AssertCompiles(updated);
    }

    [TestMethod]
    public void The_same_rule_twice_is_refused_and_changes_nothing()
    {
        var file = WriteClass("Employee", PlainEmployee);

        ClassValidationWriter.Add(file, "Employee", Rule("Age must be at least 18"));
        var before = File.ReadAllText(file);

        var result = ClassValidationWriter.Add(file, "Employee", Rule("Age must be at least 18"));

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Message, "already has that rule");
        Assert.AreEqual(before, File.ReadAllText(file));
    }

    [TestMethod]
    public void Both_rules_are_read_back()
    {
        var file = WriteClass("Employee", PlainEmployee);

        ClassValidationWriter.Add(file, "Employee", Rule("Age must be at least 18"));
        ClassValidationWriter.Add(file, "Employee", Rule("Name is required"));

        var rules = ClassValidationWriter.ExistingRules(file, "Employee");

        CollectionAssert.AreEquivalent(
            new[] { "Age must be at least 18", "Name is required" }, rules.ToArray());
    }

    [TestMethod]
    public void A_hand_written_validate_of_another_shape_is_left_alone()
    {
        var file = WriteClass("Employee",
            "namespace Acme\r\n" +
            "{\r\n" +
            "    public class Employee\r\n" +
            "    {\r\n" +
            "        public int Age { get; set; }\r\n" +
            "\r\n" +
            "        public bool Validate() => Age > 0;\r\n" +
            "    }\r\n" +
            "}\r\n");

        var before = File.ReadAllText(file);
        var result = ClassValidationWriter.Add(file, "Employee", Rule("Age must be at least 18"));

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Message, "Validate()");
        Assert.AreEqual(before, File.ReadAllText(file), "a hand-written method was edited");
    }

    [TestMethod]
    public void A_validate_with_no_errors_list_is_left_alone()
    {
        var file = WriteClass("Employee",
            "namespace Acme\r\n" +
            "{\r\n" +
            "    public class Employee\r\n" +
            "    {\r\n" +
            "        public int Age { get; set; }\r\n" +
            "\r\n" +
            "        public System.Collections.Generic.List<string> Validate()\r\n" +
            "        {\r\n" +
            "            return new System.Collections.Generic.List<string>();\r\n" +
            "        }\r\n" +
            "    }\r\n" +
            "}\r\n");

        var before = File.ReadAllText(file);
        var result = ClassValidationWriter.Add(file, "Employee", Rule("Age must be at least 18"));

        Assert.IsFalse(result.Ok);
        Assert.AreEqual(before, File.ReadAllText(file));
    }

    private static int Occurrences(string text, string needle)
    {
        var count = 0;
        var at = text.IndexOf(needle, StringComparison.Ordinal);
        while (at >= 0)
        {
            count++;
            at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
        }
        return count;
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ClassValidationWriterTests"`

Expected: FAIL — the two "left alone" tests fail (the stubbed `Unsuitable` accepts everything). `A_hand_written_validate_of_another_shape_is_left_alone` will likely throw or write nonsense; that is the failure.

- [ ] **Step 3: Implement `Unsuitable`**

Replace the stub in `ClassValidationWriter.cs`:

```csharp
    /// <summary>
    /// Whether an existing <c>Validate()</c> is one this writer may extend, and why not.
    /// </summary>
    /// <remarks>
    /// The bar is deliberately high. A method that is not obviously the one this writer created is
    /// the user's, and a tool that splices statements into a method someone wrote is a tool people
    /// stop pointing at their code.
    /// </remarks>
    private static string? Unsuitable(MethodDeclarationSyntax method, string className)
    {
        if (method.Body is null)
            return $"{className} already has an expression-bodied Validate(). "
                 + "Give it a `{ }` body, or rename it, and try again.";

        if (!method.ReturnType.ToString().Replace(" ", "").EndsWith("List<string>",
                System.StringComparison.Ordinal))
            return $"{className} already has a Validate() returning "
                 + $"{method.ReturnType}. This writer only extends one returning List<string>.";

        var declaresErrors = method.Body.Statements
            .OfType<LocalDeclarationStatementSyntax>()
            .SelectMany(s => s.Declaration.Variables)
            .Any(v => v.Identifier.ValueText == "errors");

        if (!declaresErrors)
            return $"{className}'s Validate() does not declare an `errors` list, so it was "
                 + "written by hand. Rename it and try again.";

        if (!method.Body.Statements.OfType<ReturnStatementSyntax>().Any())
            return $"{className}'s Validate() has no return statement to insert before.";

        return null;
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ClassValidationWriterTests"`

Expected: PASS, 11 tests.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Core/Validation/ClassValidationWriter.cs \
        CodeGenerator.Tests/Core/Validation/ClassValidationWriterTests.cs
git commit -m "feat: a second rule joins the first, and a hand-written Validate is left alone"
```

---

### Task 3: Compile before saving, and refuse the rest

**Files:**
- Modify: `CodeGenerationEngine/Core/Validation/ClassValidationWriter.cs` (`Verify`)
- Test: `CodeGenerator.Tests/Core/Validation/ClassValidationWriterTests.cs`

**Interfaces:**
- Consumes: `Compiler.Compile(string)` → `CompilationResult { bool Success; List<Diagnostic> Diagnostics; }`.
- Produces: `Verify(string before, string after)` returns null when the edit is safe to save, a reason otherwise.

The gate compares against the file as it was. A class referencing types from other files will not compile on its own, and refusing to add a rule because of that would make the feature useless in a real project — so the rule is: **if it compiled before, it must still compile after.** If it did not compile before, only a parse error blocks the write.

- [ ] **Step 1: Write the failing tests**

Append to `ClassValidationWriterTests`:

```csharp
    /// <summary>
    /// The class refers to a type that is not in this file, so it never compiled on its own. That
    /// is the normal case in a real project, and it must not stop a rule being added.
    /// </summary>
    [TestMethod]
    public void A_class_that_did_not_compile_alone_can_still_take_a_rule()
    {
        var file = WriteClass("Order",
            "namespace Acme\r\n" +
            "{\r\n" +
            "    public class Order\r\n" +
            "    {\r\n" +
            "        public int Quantity { get; set; }\r\n" +
            "\r\n" +
            "        public Customer Buyer { get; set; }\r\n" + // Customer is elsewhere
            "    }\r\n" +
            "}\r\n");

        var ctx = new ValidationParseContext { Fragment = "Quantity must be positive", ClassName = "Order" };
        var rule = ValidationRuleCatalog.Default
            .Parse(ctx.Fragment, ValidationCategory.Data, ctx)[0];

        var result = ClassValidationWriter.Add(file, "Order", rule);

        Assert.IsTrue(result.Ok, result.Message);
        StringAssert.Contains(File.ReadAllText(file), "// Quantity must be positive");
    }

    [TestMethod]
    public void A_file_that_does_not_parse_is_refused_and_untouched()
    {
        var file = WriteClass("Broken",
            "namespace Acme\r\n" +
            "{\r\n" +
            "    public class Broken\r\n" +
            "    {\r\n" +
            "        public int Age { get; set; }\r\n" +
            // no closing braces at all
            "\r\n");

        var before = File.ReadAllText(file);
        var ctx = new ValidationParseContext { Fragment = "Age must be positive", ClassName = "Broken" };
        var rules = ValidationRuleCatalog.Default.Parse(ctx.Fragment, ValidationCategory.Data, ctx);

        var result = ClassValidationWriter.Add(file, "Broken", rules[0]);

        Assert.IsFalse(result.Ok);
        Assert.AreEqual(before, File.ReadAllText(file));
    }

    [TestMethod]
    public void A_rule_naming_a_property_the_class_does_not_have_is_refused()
    {
        var file = WriteClass("Employee", PlainEmployee);
        var before = File.ReadAllText(file);

        var ctx = new ValidationParseContext { Fragment = "Salary must be positive", ClassName = "Employee" };
        var rule = ValidationRuleCatalog.Default
            .Parse(ctx.Fragment, ValidationCategory.Data, ctx)[0];

        var result = ClassValidationWriter.Add(file, "Employee", rule);

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Message, "no property called Salary");
        StringAssert.Contains(result.Message, "Age");            // it says what there is
        Assert.AreEqual(before, File.ReadAllText(file));
    }

    [TestMethod]
    public void A_class_that_is_not_in_the_file_is_refused()
    {
        var file = WriteClass("Employee", PlainEmployee);

        var result = ClassValidationWriter.Add(file, "Customer", Rule("Age must be at least 18"));

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Message, "No class called 'Customer'");
    }

    [TestMethod]
    public void A_positional_record_with_no_body_is_refused()
    {
        var file = WriteClass("Money",
            "namespace Acme\r\n" +
            "{\r\n" +
            "    public record Money(decimal Amount);\r\n" +
            "}\r\n");

        var before = File.ReadAllText(file);

        var ctx = new ValidationParseContext { Fragment = "Amount must be positive", ClassName = "Money" };
        var rule = ValidationRuleCatalog.Default
            .Parse(ctx.Fragment, ValidationCategory.Data, ctx)[0];

        var result = ClassValidationWriter.Add(file, "Money", rule);

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Message, "body");
        Assert.AreEqual(before, File.ReadAllText(file));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ClassValidationWriterTests"`

Expected: FAIL — `A_file_that_does_not_parse_is_refused_and_untouched` fails (the stubbed `Verify` allows it). The other four may already pass from Task 1's guards; that is fine, they are here to pin the messages.

- [ ] **Step 3: Implement `Verify`**

Replace the stub in `ClassValidationWriter.cs`:

```csharp
    /// <summary>
    /// Whether the edit is safe to save, and why not.
    /// </summary>
    /// <remarks>
    /// Compared against the file as it was, not against nothing: a class referring to a type
    /// declared in another file has never compiled on its own, which is the normal case in a real
    /// project. Refusing there would make the feature useless. So the rule is that the writer may
    /// not make things worse — and a file that no longer parses is always worse.
    /// </remarks>
    private static string? Verify(string before, string after)
    {
        var parse = CSharpSyntaxTree.ParseText(after).GetDiagnostics()
            .FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);

        if (parse is not null)
            return $"The edit would not parse ({parse.GetMessage()}), so nothing was written.";

        if (!Compiler.Compile(before).Success)
            return null; // it did not compile before this either; not ours to judge

        var result = Compiler.Compile(after);
        if (result.Success)
            return null;

        var first = result.Diagnostics
            .FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);

        return $"The check would not compile ({first?.GetMessage()}), so nothing was written.";
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ClassValidationWriterTests"`

Expected: PASS, 16 tests.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerationEngine/Core/Validation/ClassValidationWriter.cs \
        CodeGenerator.Tests/Core/Validation/ClassValidationWriterTests.cs
git commit -m "feat: the writer compiles the edit before saving and refuses to make things worse"
```

---

### Task 4: `generate validator` stops emitting a partial half for a non-partial class

`EntityExtractor.GenerateValidator` writes `<Class>.Validation.cs` containing `public partial class <Class>`. `create class` writes `public class {className}` (`CodeGenerationEngine/Core/CodeGenerator.cs:59`). Together that is CS0260 and the project does not build. No test catches it because every existing test hand-writes the declaring half already marked `partial`.

**Files:**
- Modify: `CodeGenerationEngine/Core/Validation/ClassValidationWriter.cs` (add `EnsurePartial`)
- Modify: `CodeGenerationEngine/Core/EntityExtractor.cs:46-70`
- Test: `CodeGenerator.Tests/Core/Validation/GenerateValidatorEndToEndTests.cs`

**Interfaces:**
- Consumes: `SourceFileText`, `FindType` (make it `internal static` or duplicate the two lines).
- Produces: `public static bool ClassValidationWriter.EnsurePartial(string classFile, string className)` — true when it added the keyword, false when it was already there or the class was not found.

- [ ] **Step 1: Write the failing test**

Append to `GenerateValidatorEndToEndTests.cs`:

```csharp
    /// <summary>
    /// The pair that has never been compiled together. Every other test in this file supplies the
    /// declaring half itself, already marked partial — so the fact that `create class` does not
    /// mark it has been invisible.
    /// </summary>
    [TestMethod]
    public void The_class_and_its_generated_validator_compile_together()
    {
        var folder = Path.Combine(Path.GetTempPath(), "gv-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        try
        {
            var classFile = Path.Combine(folder, "Booking.cs");
            File.WriteAllText(classFile,
                "namespace Acme\r\n" +
                "{\r\n" +
                "    public class Booking\r\n" +          // NOT partial, as create class writes it
                "    {\r\n" +
                "        public int Nights { get; set; }\r\n" +
                "    }\r\n" +
                "}\r\n");

            ValidationRegistry.Clear();

            var ctx = new ValidationParseContext
            {
                Fragment = "Nights must be positive",
                ClassName = "Booking"
            };

            foreach (var rule in ValidationRuleCatalog.Default
                         .Parse(ctx.Fragment, ValidationCategory.Data, ctx))
                ValidationRegistry.Add("Booking", rule);

            var command = new ParsedCommand { ClassName = "Booking" };

            Assert.IsTrue(EntityExtractor.GenerateValidator(command, folder));

            var validatorFile = Path.Combine(folder, "Booking.Validation.cs");
            Assert.IsTrue(File.Exists(validatorFile));

            var result = Compiler.CompileSources(
            [
                (classFile, File.ReadAllText(classFile)),
                (validatorFile, File.ReadAllText(validatorFile))
            ]);

            Assert.IsTrue(result.Success,
                string.Join("\n", result.Diagnostics.Select(d => d.ToString())));
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }
```

Confirm `ParsedCommand`'s property names and how the existing tests in this file construct one before writing this — copy their construction rather than inventing it.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~GenerateValidatorEndToEndTests"`

Expected: FAIL with CS0260 — "Missing partial modifier on declaration of type 'Acme.Booking'".

- [ ] **Step 3: Add `EnsurePartial` to the writer**

```csharp
    /// <summary>
    /// Marks a class <c>partial</c> when it is not, so a sibling partial file can compile beside it.
    /// </summary>
    /// <remarks>
    /// <c>create class</c> writes <c>public class X</c> and <c>generate validator</c> writes
    /// <c>public partial class X</c> next to it. That pair is CS0260, and it shipped because every
    /// test of the generated half supplied a declaring half it had marked partial itself.
    /// </remarks>
    public static bool EnsurePartial(string classFile, string className)
    {
        if (!File.Exists(classFile))
            return false;

        var source = SourceFileText.Read(classFile, out var hasBom, out _);

        var type = FindType(source, className);
        if (type is null)
            return false;

        if (type.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
            return false;

        var updated = source.Insert(type.Keyword.SpanStart, "partial ");

        SourceFileText.Write(classFile, updated, hasBom);

        return true;
    }
```

- [ ] **Step 4: Call it from `GenerateValidator`**

In `EntityExtractor.cs`, immediately after `classFile` is found and before `ExtractProperties` is called:

```csharp
        // The generated file declares `public partial class <Class>`. A class this tool created is
        // not partial, and that pair does not compile — so the declaring half is marked here rather
        // than emitting a file that cannot build beside it.
        ClassValidationWriter.EnsurePartial(classFile, command.ClassName);
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~GenerateValidator"`

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerationEngine/Core/Validation/ClassValidationWriter.cs \
        CodeGenerationEngine/Core/EntityExtractor.cs \
        CodeGenerator.Tests/Core/Validation/GenerateValidatorEndToEndTests.cs
git commit -m "fix: generate validator marks the class partial so its output compiles"
```

---

### Task 5: The page's target, and splitting a box of text into fragments

Two small pieces the page needs, both testable without a renderer.

**Files:**
- Create: `CodeGenerator/Web/Chat/ValidationTarget.cs`
- Modify: `CodeGenerationEngine/Core/Validation/ValidationRuleCatalog.cs`
- Test: `CodeGenerator.Tests/Web/Chat/ValidationTargetTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record ValidationTarget(string ClassName, string File)`
  - `public static bool ValidationTargetReader.TryResolve(string? className, string? file, string workspaceRoot, out ValidationTarget? target, out string problem)`
  - `public static IReadOnlyList<string> ValidationRuleCatalog.Fragments(string text)` — the existing private `Split`, made public so the page can name which fragment failed.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/Web/Chat/ValidationTargetTests.cs`:

```csharp
using CodeGenerationEngine.Core.Validation;
using CodeGenerator.Web.Chat;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// The page takes a file path in its query string. Everything about that path is checked before
/// anything is written, because loopback is reachable by every process on this machine and the
/// token is the only other thing standing in front of a file write.
/// </summary>
[TestClass]
public class ValidationTargetTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void MakeRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), "vt-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void RemoveRoot()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string File_(string name)
    {
        var path = Path.Combine(_root, name);
        System.IO.File.WriteAllText(path, "namespace A { public class B { } }");
        return path;
    }

    [TestMethod]
    public void Resolves_a_class_file_under_the_workspace()
    {
        var file = File_("Employee.cs");

        Assert.IsTrue(ValidationTargetReader.TryResolve(
            "Employee", file, _root, out var target, out var problem), problem);

        Assert.AreEqual("Employee", target!.ClassName);
        Assert.AreEqual(Path.GetFullPath(file), target.File);
    }

    [TestMethod]
    public void Refuses_a_file_outside_the_workspace()
    {
        var outside = Path.Combine(Path.GetTempPath(), "outside-" + Guid.NewGuid().ToString("N")[..8] + ".cs");
        System.IO.File.WriteAllText(outside, "namespace A { public class B { } }");

        try
        {
            Assert.IsFalse(ValidationTargetReader.TryResolve(
                "B", outside, _root, out _, out var problem));
            StringAssert.Contains(problem, "outside");
        }
        finally
        {
            System.IO.File.Delete(outside);
        }
    }

    [TestMethod]
    public void Refuses_a_traversal_out_of_the_workspace()
    {
        var sneaky = Path.Combine(_root, "..", "elsewhere.cs");

        Assert.IsFalse(ValidationTargetReader.TryResolve("X", sneaky, _root, out _, out _));
    }

    [TestMethod]
    public void Refuses_a_file_that_is_not_csharp()
    {
        var path = Path.Combine(_root, "notes.txt");
        System.IO.File.WriteAllText(path, "hello");

        Assert.IsFalse(ValidationTargetReader.TryResolve("notes", path, _root, out _, out var problem));
        StringAssert.Contains(problem, ".cs");
    }

    [TestMethod]
    public void Refuses_a_file_that_is_not_there()
    {
        Assert.IsFalse(ValidationTargetReader.TryResolve(
            "Ghost", Path.Combine(_root, "Ghost.cs"), _root, out _, out _));
    }

    [TestMethod]
    [DataRow(null, "Employee.cs")]
    [DataRow("Employee", null)]
    [DataRow("", "")]
    public void Refuses_a_missing_half(string? className, string? file)
    {
        Assert.IsFalse(ValidationTargetReader.TryResolve(
            className, file is null ? null : Path.Combine(_root, file), _root, out _, out _));
    }

    [TestMethod]
    public void A_box_of_text_splits_into_one_fragment_per_line()
    {
        var fragments = ValidationRuleCatalog.Fragments(
            "Age must be at least 18\r\nName is required; Email must be a valid email\r\n");

        Assert.HasCount(3, fragments);
        Assert.AreEqual("Age must be at least 18", fragments[0]);
        Assert.AreEqual("Name is required", fragments[1]);
        Assert.AreEqual("Email must be a valid email", fragments[2]);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ValidationTargetTests"`

Expected: FAIL to compile — `ValidationTargetReader` does not exist and `Fragments` is private.

- [ ] **Step 3: Expose `Fragments` on the catalog**

In `ValidationRuleCatalog.cs`, rename the private `Split` to a public `Fragments` and have `Parse` call it. Keep the comment that explains the trailing-period trim:

```csharp
    /// <summary>
    /// One rule per line, or per semicolon.
    /// </summary>
    /// <remarks>
    /// Public because the validation page parses fragment by fragment: it has to be able to say
    /// which of the three lines someone typed was the one it could not understand.
    /// </remarks>
    // Split on newlines and ';', trim, and drop a trailing sentence period without
    // harming decimals mid-string (e.g. "between 3.5 and 10").
    public static IReadOnlyList<string> Fragments(string text) =>
        (text ?? string.Empty).Replace("\r", "")
            .Split(new[] { '\n', ';' }, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().TrimEnd('.').Trim())
            .Where(s => s.Length > 0)
            .ToList();
```

Update `Parse`'s `foreach (var fragment in Split(text))` to `foreach (var fragment in Fragments(text))`.

- [ ] **Step 4: Write the target reader**

Create `CodeGenerator/Web/Chat/ValidationTarget.cs`:

```csharp
using System.IO;

namespace CodeGenerator.Web.Chat;

/// <summary>The class a validation page is about, and the file it lives in.</summary>
/// <remarks>
/// The file, not only the name: two projects in one solution can each declare a <c>Customer</c>,
/// which is the same reason the tree keys its open classes by path.
/// </remarks>
public sealed record ValidationTarget(string ClassName, string File);

/// <summary>
/// Turns the page's query string into a target, or says why it will not.
/// </summary>
/// <remarks>
/// The path arrives from a URL. The host binds loopback, which every process on this machine can
/// reach, and the session token is the only thing in front of it — so the path is resolved and
/// confined to the workspace before anything is opened, let alone written.
/// </remarks>
public static class ValidationTargetReader
{
    public static bool TryResolve(
        string? className,
        string? file,
        string workspaceRoot,
        out ValidationTarget? target,
        out string problem)
    {
        target = null;
        problem = string.Empty;

        if (string.IsNullOrWhiteSpace(className))
        {
            problem = "No class was named.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(file))
        {
            problem = $"No file was given for {className}.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            problem = "No workspace is loaded.";
            return false;
        }

        string full;
        string root;

        try
        {
            full = Path.GetFullPath(file);
            root = Path.GetFullPath(workspaceRoot);
        }
        catch (System.Exception ex) when (ex is System.ArgumentException or PathTooLongException
                                             or NotSupportedException)
        {
            problem = "That is not a path this tool can open.";
            return false;
        }

        var confined = full.StartsWith(
            root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar,
            System.StringComparison.OrdinalIgnoreCase);

        if (!confined)
        {
            problem = "That file is outside the loaded workspace.";
            return false;
        }

        if (!full.EndsWith(".cs", System.StringComparison.OrdinalIgnoreCase))
        {
            problem = "That is not a .cs file.";
            return false;
        }

        if (!File.Exists(full))
        {
            problem = $"{Path.GetFileName(full)} is not there any more.";
            return false;
        }

        target = new ValidationTarget(className, full);
        return true;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ValidationTargetTests|FullyQualifiedName~ValidationRuleCatalogTests"`

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerator/Web/Chat/ValidationTarget.cs \
        CodeGenerationEngine/Core/Validation/ValidationRuleCatalog.cs \
        CodeGenerator.Tests/Web/Chat/ValidationTargetTests.cs
git commit -m "feat: the validation page's target is resolved and confined to the workspace"
```

---

### Task 6: The page

**Files:**
- Create: `CodeGenerator/Web/Components/ValidationPage.razor`
- Create: `CodeGenerator/Web/Chat/ValidationAdd.cs`
- Test: `CodeGenerator.Tests/Web/Chat/ValidationAddTests.cs`
- Test: `CodeGenerator.Tests/Web/Chat/ValidationPageTests.cs`
- Test: `CodeGenerator.Tests/Web/Chat/ChatWebHostTests.cs` (add three route tests)

**Interfaces:**
- Consumes: `ValidationTargetReader.TryResolve`, `ValidationRuleCatalog.Fragments` / `.Parse`, `ClassValidationWriter.Add` / `.ExistingRules`, `EntityExtractor.ExtractProperties`, `ChatSession.Root` / `.Narrate`, `ValidationRegistry.Add`.
- Produces:
  - `public sealed record ValidationOutcome(IReadOnlyList<string> Added, IReadOnlyList<string> Refused)`
  - `public static ValidationOutcome ValidationAdd.Apply(ValidationTarget target, string text)`
  - `public static IReadOnlyList<string> ValidationAdd.Phrasings()`
  - the routable component `ValidationPage` at `/chat/validation`

Keeping the whole decision in `ValidationAdd` rather than in the component's `@code` block is what makes it testable without a renderer — the component is then markup plus one call.

- [ ] **Step 1: Write the failing test for the decision**

Create `CodeGenerator.Tests/Web/Chat/ValidationAddTests.cs`:

```csharp
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;
using CodeGenerator.Web.Chat;

namespace CodeGenerator.Tests.Web.Chat;

[TestClass]
[DoNotParallelize]
public class ValidationAddTests
{
    private string _folder = string.Empty;
    private string _file = string.Empty;

    [TestInitialize]
    public void MakeClass()
    {
        ValidationRegistry.Clear();

        _folder = Path.Combine(Path.GetTempPath(), "va-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_folder);

        _file = Path.Combine(_folder, "Employee.cs");
        File.WriteAllText(_file,
            "namespace Acme\r\n" +
            "{\r\n" +
            "    public class Employee\r\n" +
            "    {\r\n" +
            "        public int Age { get; set; }\r\n" +
            "\r\n" +
            "        public string Name { get; set; } = string.Empty;\r\n" +
            "    }\r\n" +
            "}\r\n");
    }

    [TestCleanup]
    public void RemoveFolder()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private ValidationTarget Target => new("Employee", _file);

    [TestMethod]
    public void Adds_a_data_rule()
    {
        var outcome = ValidationAdd.Apply(Target, "Age must be at least 18");

        Assert.HasCount(1, outcome.Added);
        Assert.IsEmpty(outcome.Refused);
        StringAssert.Contains(File.ReadAllText(_file), "// Age must be at least 18");
    }

    [TestMethod]
    public void Adds_a_business_rule_without_being_told_which_it_is()
    {
        var outcome = ValidationAdd.Apply(Target, "Name must be different from Age");

        Assert.HasCount(1, outcome.Added);
        StringAssert.Contains(File.ReadAllText(_file), "// Name must be different from Age");
    }

    [TestMethod]
    public void Registers_the_rule_so_show_validation_agrees_with_the_file()
    {
        ValidationAdd.Apply(Target, "Age must be at least 18");

        Assert.HasCount(1, ValidationRegistry.Get("Employee", ValidationCategory.Data));
    }

    [TestMethod]
    public void Names_the_fragment_it_could_not_understand_and_writes_nothing()
    {
        var outcome = ValidationAdd.Apply(Target, "Age should be sensible really");

        Assert.IsEmpty(outcome.Added);
        Assert.HasCount(1, outcome.Refused);
        StringAssert.Contains(outcome.Refused[0], "Age should be sensible really");
        StringAssert.DoesNotMatch(File.ReadAllText(_file),
            new System.Text.RegularExpressions.Regex("Validate"));
    }

    [TestMethod]
    public void Adds_what_it_understood_and_names_what_it_did_not()
    {
        var outcome = ValidationAdd.Apply(
            Target, "Age must be at least 18\r\nName ought to be nice");

        Assert.HasCount(1, outcome.Added);
        Assert.HasCount(1, outcome.Refused);
        StringAssert.Contains(File.ReadAllText(_file), "// Age must be at least 18");
    }

    [TestMethod]
    public void A_rule_naming_an_absent_property_is_refused_by_name()
    {
        var outcome = ValidationAdd.Apply(Target, "Salary must be positive");

        Assert.IsEmpty(outcome.Added);
        StringAssert.Contains(outcome.Refused[0], "Salary");
    }

    [TestMethod]
    public void The_accepted_phrasings_come_from_the_catalog()
    {
        var phrasings = ValidationAdd.Phrasings();

        Assert.IsGreaterThan(20, phrasings.Count);
        CollectionAssert.Contains(phrasings.ToList(), "Name is required");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ValidationAddTests"`

Expected: FAIL to compile — `ValidationAdd` does not exist.

- [ ] **Step 3: Write the decision**

Create `CodeGenerator/Web/Chat/ValidationAdd.cs`:

```csharp
using System.Linq;
using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Validation;

namespace CodeGenerator.Web.Chat;

/// <summary>What one submission of the box did.</summary>
public sealed record ValidationOutcome(IReadOnlyList<string> Added, IReadOnlyList<string> Refused);

/// <summary>
/// One box of plain English, applied to one class.
/// </summary>
/// <remarks>
/// Here rather than in the page's code block so it can be tested without a renderer — the page is
/// then markup and one call.
/// <para>
/// Data first, business second, and never a choice the user has to make: the parser that
/// recognizes the sentence is the thing that knows which category it belongs to.
/// </para>
/// </remarks>
public static class ValidationAdd
{
    public static ValidationOutcome Apply(ValidationTarget target, string text)
    {
        var added = new List<string>();
        var refused = new List<string>();

        var properties = EntityExtractor.ExtractProperties(target.File);
        var known = properties.Select(p => p.Name).ToList();

        foreach (var fragment in ValidationRuleCatalog.Fragments(text))
        {
            var ctx = new ValidationParseContext
            {
                Fragment = fragment,
                ClassName = target.ClassName,
                KnownProperties = known
            };

            var rules = ValidationRuleCatalog.Default.Parse(fragment, ValidationCategory.Data, ctx);

            if (rules.Count == 0)
                rules = ValidationRuleCatalog.Default.Parse(fragment, ValidationCategory.Business, ctx);

            if (rules.Count == 0)
            {
                refused.Add($"I could not turn this into a rule: \"{fragment}\"");
                continue;
            }

            foreach (var rule in rules)
            {
                var result = ClassValidationWriter.Add(target.File, target.ClassName, rule);

                if (result.Ok)
                {
                    // Registered as well as written, so `show validation <Class>` in the same
                    // session agrees with what is on disk.
                    ValidationRegistry.Add(target.ClassName, rule);
                    added.Add(result.Message);
                }
                else
                {
                    refused.Add(result.Message);
                }
            }
        }

        return new ValidationOutcome(added, refused);
    }

    /// <summary>Every phrasing the catalog accepts, asked of the catalog.</summary>
    /// <remarks>
    /// Not a list kept here. A hand-written list beside a growing catalog is a page telling people
    /// to type things the tool no longer understands.
    /// </remarks>
    public static IReadOnlyList<string> Phrasings() =>
        ValidationRuleCatalog.Default.Parsers
            .SelectMany(p => p.Examples)
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, System.StringComparer.OrdinalIgnoreCase)
            .ToList();
}
```

- [ ] **Step 4: Run the decision tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ValidationAddTests"`

Expected: PASS, 7 tests.

- [ ] **Step 5: Write the page**

Create `CodeGenerator/Web/Components/ValidationPage.razor`:

```razor
@* Validation for one class, in plain English.

   Served at /chat/validation and not at /validation: LoopbackHost guards exactly one path segment
   with the session token, ChatWebHost passes "chat", and the check is StartsWithSegments — so a
   top-level route would be served, unguarded, to every process on this machine. This page writes
   source files. *@
@page "/chat/validation"
@rendermode InteractiveServer
@using CodeGenerationEngine.Core
@using CodeGenerator.Web.Chat
@inject ChatSession Session
@inject NavigationManager Nav

<PageTitle>Validation — @ClassName</PageTitle>

<div class="val-page">
    <header class="val-head">
        <div>
            <h1>Validation</h1>
            @* The class, and which copy of it: the name alone says nothing on a solution where two
               projects each declare one. *@
            <p class="val-target">@ClassName<span class="val-path">@(_target?.File)</span></p>
        </div>
        <a class="val-back" href="@BackToChat()">Back to the chat</a>
    </header>

    @if (_target is null)
    {
        <p class="val-problem">@_problem</p>
    }
    else
    {
        <section class="val-props">
            <h2>Properties</h2>
            <ul>
                @foreach (var property in _properties)
                {
                    <li><span class="val-prop-name">@property.Name</span>
                        <span class="val-prop-type">@property.Type</span></li>
                }
            </ul>
        </section>

        <section class="val-existing">
            <h2>Rules on this class</h2>
            @if (_existing.Count == 0)
            {
                <p class="val-none">None yet.</p>
            }
            else
            {
                <ul>
                    @foreach (var rule in _existing)
                    {
                        <li>@rule</li>
                    }
                </ul>
            }
        </section>

        <section class="val-write">
            <h2>Add a validation</h2>
            <p class="val-hint">
                One rule per line. Name the property in the sentence — for example
                <em>@(_properties.Count > 0 ? _properties[0].Name : "Age") is required</em>.
            </p>

            @* Not a form: a form posts, and this page is interactive. *@
            <textarea class="val-box" rows="4" @bind="_text" @bind:event="oninput"
                      placeholder="Age must be at least 18"></textarea>

            <button type="button" class="val-add" disabled="@(_busy || _text.Trim().Length == 0)"
                    @onclick="Add">Add validation</button>
        </section>

        @if (_added.Count > 0)
        {
            <section class="val-added">
                @foreach (var line in _added)
                {
                    <p>@line</p>
                }
            </section>
        }

        @if (_refused.Count > 0)
        {
            <section class="val-refused">
                @foreach (var line in _refused)
                {
                    <p>@line</p>
                }
                <p class="val-refused-help">Nothing was written for those.</p>
            </section>
        }

        <details class="val-phrasings">
            <summary>Phrasings I understand (@ValidationAdd.Phrasings().Count)</summary>
            <ul>
                @foreach (var phrasing in ValidationAdd.Phrasings())
                {
                    <li>@phrasing</li>
                }
            </ul>
        </details>
    }
</div>

@code {
    private string _text = string.Empty;
    private bool _busy;
    private string _problem = string.Empty;
    private ValidationTarget? _target;
    private IReadOnlyList<CodeGenerationEngine.Models.PropertyDefinition> _properties = [];
    private IReadOnlyList<string> _existing = [];
    private List<string> _added = [];
    private List<string> _refused = [];

    /// <summary>The class to validate. `class` is reserved in C#, hence the query name.</summary>
    [Parameter, SupplyParameterFromQuery(Name = "class")]
    public string? ClassName { get; set; }

    [Parameter, SupplyParameterFromQuery(Name = "file")]
    public string? File { get; set; }

    protected override void OnParametersSet()
    {
        if (ValidationTargetReader.TryResolve(
                ClassName, File, Session.Root, out var target, out var problem))
        {
            _target = target;
            Reread();
        }
        else
        {
            _target = null;
            _problem = problem;
        }
    }

    private void Reread()
    {
        if (_target is null) return;

        _properties = EntityExtractor.ExtractProperties(_target.File);
        _existing = ClassValidationWriter.ExistingRules(_target.File, _target.ClassName);
    }

    /// <summary>
    /// Applies the box.
    /// </summary>
    /// <remarks>
    /// The page stays where it is afterwards, with the new rule now listed: a second rule should
    /// not mean navigating back and right-clicking again. The transcript gets a line as well, so
    /// the conversation remains the record of what the tool did.
    /// </remarks>
    private void Add()
    {
        if (_target is null) return;

        _busy = true;

        try
        {
            var outcome = ValidationAdd.Apply(_target, _text);

            _added = outcome.Added.ToList();
            _refused = outcome.Refused.ToList();

            if (_added.Count > 0)
            {
                _text = string.Empty;
                Session.Narrate(string.Join(" ", _added));
            }

            Reread();
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// The chat, with the token this page was reached by.
    /// </summary>
    /// <remarks>
    /// Carried rather than rebuilt: the host mints a token per run and 404s without it, so a bare
    /// "/chat" href is a link to a 404.
    /// </remarks>
    private string BackToChat() => "/chat" + new Uri(Nav.Uri).Query;
}
```

- [ ] **Step 6: Write the route tests**

Append to `CodeGenerator.Tests/Web/Chat/ChatWebHostTests.cs`:

```csharp
    /// <summary>
    /// The validation page writes source files, and loopback is reachable by every process on this
    /// machine. It has to be behind the same token as the chat page — which is why it is served
    /// under /chat rather than at /validation.
    /// </summary>
    [TestMethod]
    public async Task Refuses_the_validation_page_to_a_visitor_with_no_token()
    {
        using var host = Start(out _);
        using var client = new HttpClient();

        var response = await client.GetAsync(
            new Uri(host.Url).GetLeftPart(UriPartial.Authority) + "/chat/validation");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Serves_the_validation_page_to_a_visitor_carrying_the_token()
    {
        using var host = Start(out _);
        using var client = new HttpClient();

        var url = new Uri(host.Url);
        var response = await client.GetAsync(
            url.GetLeftPart(UriPartial.Authority) + "/chat/validation" + url.Query);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task Serves_the_validation_page_as_an_interactive_component()
    {
        using var host = Start(out _);
        using var client = new HttpClient();

        var url = new Uri(host.Url);
        var html = await client.GetStringAsync(
            url.GetLeftPart(UriPartial.Authority) + "/chat/validation" + url.Query);

        var body = html[html.IndexOf("<body>", StringComparison.Ordinal)..];

        StringAssert.Contains(
            body,
            "\"type\":\"server\"",
            "the validation page is statically rendered: the box draws and the button does nothing");
    }
```

- [ ] **Step 7: Write the page render tests**

Create `CodeGenerator.Tests/Web/Chat/ValidationPageTests.cs`:

```csharp
using Bunit;
using CodeGenerator.App;
using CodeGenerator.Web.Chat;
using CodeGenerator.Web.Components;
using CodeIntelligence;
using Microsoft.Extensions.DependencyInjection;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// The page, as a page. What it decides is asserted in <see cref="ValidationAddTests"/> without a
/// renderer; what is here is only what needs one.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ValidationPageTests
{
    private string _root = string.Empty;
    private string _file = string.Empty;

    [TestInitialize]
    public void MakeClass()
    {
        _root = Path.Combine(Path.GetTempPath(), "vp-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);

        _file = Path.Combine(_root, "Employee.cs");
        File.WriteAllText(_file,
            "namespace Acme\r\n{\r\n    public class Employee\r\n    {\r\n" +
            "        public int Age { get; set; }\r\n    }\r\n}\r\n");
    }

    [TestCleanup]
    public void RemoveRoot()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private BunitContext NewContext()
    {
        var context = new BunitContext();

        context.Services.AddSingleton(new ChatSession(
            new Workspace(_root, projectPathSet: false), AiLayer.Create(), []));

        return context;
    }

    [TestMethod]
    public void Lists_the_properties_of_the_class()
    {
        using var context = NewContext();

        var page = context.Render<ValidationPage>(p => p
            .Add(c => c.ClassName, "Employee")
            .Add(c => c.File, _file));

        StringAssert.Contains(page.Markup, "Age");
    }

    [TestMethod]
    public void Says_so_when_the_file_is_outside_the_workspace()
    {
        using var context = NewContext();

        var page = context.Render<ValidationPage>(p => p
            .Add(c => c.ClassName, "Elsewhere")
            .Add(c => c.File, Path.Combine(Path.GetTempPath(), "Elsewhere.cs")));

        StringAssert.Contains(page.Markup, "outside the loaded workspace");
    }

    [TestMethod]
    public void Names_the_sentence_it_could_not_understand()
    {
        using var context = NewContext();

        var page = context.Render<ValidationPage>(p => p
            .Add(c => c.ClassName, "Employee")
            .Add(c => c.File, _file));

        page.Find("textarea").Change("Age ought to be sensible");
        page.Find("button.val-add").Click();

        StringAssert.Contains(page.Markup, "Age ought to be sensible");
        StringAssert.Contains(page.Markup, "Nothing was written");
    }

    [TestMethod]
    public void Lists_the_rule_after_it_is_added()
    {
        using var context = NewContext();

        var page = context.Render<ValidationPage>(p => p
            .Add(c => c.ClassName, "Employee")
            .Add(c => c.File, _file));

        page.Find("textarea").Change("Age must be at least 18");
        page.Find("button.val-add").Click();

        StringAssert.Contains(page.Markup, "Age must be at least 18");
        StringAssert.Contains(File.ReadAllText(_file), "// Age must be at least 18");
    }
}
```

Match `NewContext` to whatever `ChatPageTests` already does — bUnit's context type and render call differ between major versions, and that file is the version in use here. Copy its shape rather than this one if they differ.

- [ ] **Step 8: Run all the page tests**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ValidationPageTests|FullyQualifiedName~ValidationAddTests|FullyQualifiedName~ChatWebHostTests"`

Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add CodeGenerator/Web/Components/ValidationPage.razor \
        CodeGenerator/Web/Chat/ValidationAdd.cs \
        CodeGenerator.Tests/Web/Chat/
git commit -m "feat: a page that turns plain English into a check on one class"
```

---

### Task 7: Right-click a class in the tree

**Files:**
- Modify: `CodeGenerator/Web/Components/TreeFolderView.razor`
- Modify: `CodeGenerator/Web/Components/ProjectTreeView.razor`
- Modify: `CodeGenerator/Web/Components/ChatPage.razor`
- Test: `CodeGenerator.Tests/Web/Chat/ProjectTreeMenuTests.cs`

**Interfaces:**
- Consumes: `TreeClass(string Name, string File)` (existing).
- Produces: `TreeFolderView.OnValidate` and `ProjectTreeView.OnValidate`, both `EventCallback<TreeClass>`; `ChatPage` handles it by navigating to `/chat/validation` carrying the token.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/Web/Chat/ProjectTreeMenuTests.cs`:

```csharp
using Bunit;
using CodeGenerator.Web.Chat;
using CodeGenerator.Web.Components;

namespace CodeGenerator.Tests.Web.Chat;

/// <summary>
/// The right-click that opens the validation page. The menu is one item because there is one
/// thing to do; it exists at all because a right-click that navigates without asking is a
/// right-click people learn to avoid.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ProjectTreeMenuTests
{
    private static TreeFolder OneClass(string name, string file) =>
        new(name: "Models", Path: "/models", Folders: [], Classes: [new TreeClass(name, file)]);

    [TestMethod]
    public void Right_clicking_a_class_raises_it()
    {
        using var context = new BunitContext();

        TreeClass? raised = null;

        var view = context.Render<TreeFolderView>(p => p
            .Add(c => c.Folder, OneClass("Employee", @"C:\ws\Employee.cs"))
            .Add(c => c.Tree, new ProjectTree())
            .Add(c => c.OpenFolders, new HashSet<string>())
            .Add(c => c.OpenClasses, new HashSet<string>())
            .Add(c => c.OnValidate, (TreeClass entry) => raised = entry));

        view.Find(".tree-class").ContextMenu();

        Assert.IsNotNull(raised);
        Assert.AreEqual("Employee", raised!.Name);
    }

    [TestMethod]
    public void A_disabled_tree_raises_nothing()
    {
        using var context = new BunitContext();

        TreeClass? raised = null;

        var view = context.Render<TreeFolderView>(p => p
            .Add(c => c.Folder, OneClass("Employee", @"C:\ws\Employee.cs"))
            .Add(c => c.Tree, new ProjectTree())
            .Add(c => c.OpenFolders, new HashSet<string>())
            .Add(c => c.OpenClasses, new HashSet<string>())
            .Add(c => c.Disabled, true)
            .Add(c => c.OnValidate, (TreeClass entry) => raised = entry));

        view.Find(".tree-class").ContextMenu();

        Assert.IsNull(raised, "a command is in flight; the tree must start nothing");
    }
}
```

Check `TreeFolder`'s real constructor shape in `CodeGenerator/Web/Chat/ProjectTree.cs:42` and use it — the record's parameter names and order are what the test has to match. If bUnit's `ContextMenu()` helper is not available in the version in use, dispatch the event directly: `view.Find(".tree-class").TriggerEvent("oncontextmenu", new Microsoft.AspNetCore.Components.Web.MouseEventArgs())`.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ProjectTreeMenuTests"`

Expected: FAIL to compile — `TreeFolderView` has no `OnValidate`.

- [ ] **Step 3: Raise the right-click out of `TreeFolderView`**

In the class loop of `TreeFolderView.razor`, add the handler to the row and a parameter to the code block:

```razor
        @* preventDefault, or the browser's own menu covers ours. The event is on the row rather
           than the name button so the whole row is the target — a right-click has no obvious
           affordance and a two-character hit area would be a feature nobody finds. *@
        <div class="tree-row tree-class"
             @oncontextmenu="() => Validate(entry)"
             @oncontextmenu:preventDefault="true">
```

```csharp
    /// <summary>Raised with the class that was right-clicked.</summary>
    [Parameter]
    public EventCallback<TreeClass> OnValidate { get; set; }

    /// <summary>
    /// The one place a right-click leaves this component, and the one place it can be refused.
    /// </summary>
    /// <remarks>
    /// Same guard as <see cref="Run"/>: a page opened from a tree while a command is rewriting
    /// that tree is a page about a class that may not be there by the time it draws.
    /// </remarks>
    private Task Validate(TreeClass entry) =>
        Disabled || entry.File.Length == 0 ? Task.CompletedTask : OnValidate.InvokeAsync(entry);
```

Pass it down the recursion too — add `OnValidate="OnValidate"` to the nested `<TreeFolderView ... />`.

- [ ] **Step 4: Pass it through `ProjectTreeView`**

Add the parameter and forward it to `TreeFolderView`:

```csharp
    /// <summary>Raised with the class the user right-clicked.</summary>
    [Parameter]
    public EventCallback<TreeClass> OnValidate { get; set; }
```

```razor
                                        <TreeFolderView Folder="Tree.FolderOf(project)"
                                                        Tree="Tree"
                                                        OpenFolders="_openFolders"
                                                        OpenClasses="_openClasses"
                                                        Disabled="Disabled"
                                                        OnCommand="Run"
                                                        OnValidate="OnValidate"
                                                        OnToggleFolder="ToggleFolder"
                                                        OnToggleClass="ToggleClass" />
```

- [ ] **Step 5: Show the menu and navigate from `ChatPage`**

In `ChatPage.razor`, add `@inject NavigationManager Nav`, wire the tree, and draw the menu:

```razor
            <ProjectTreeView Tree="_tree"
                             Root="@Session.Root"
                             Solution="@Session.Solution"
                             Projects="ProjectTree.Projects()"
                             Disabled="Session.Running || Session.Ended"
                             OnCommand="Send"
                             OnValidate="OfferValidation" />
```

```razor
@* The one-item menu. Drawn at the end of the frame so it is not clipped by the tree's own
   scrolling box, and dismissed by a click anywhere. *@
@if (_menuFor is not null)
{
    <div class="tree-menu-veil" @onclick="CloseMenu"></div>
    <div class="tree-menu">
        <button type="button" @onclick="OpenValidation">Add validation…</button>
    </div>
}
```

```csharp
    /// <summary>The class the menu is open for, or null.</summary>
    private TreeClass? _menuFor;

    private void OfferValidation(TreeClass entry) => _menuFor = entry;

    private void CloseMenu() => _menuFor = null;

    /// <summary>
    /// Opens the validation page for the menu's class.
    /// </summary>
    /// <remarks>
    /// The token is carried out of this page's own address rather than rebuilt: the host mints one
    /// per run and 404s without it, so a bare path is a link to a 404. The file travels too — the
    /// name alone does not say which Customer, on a solution where two projects declare one.
    /// </remarks>
    private void OpenValidation()
    {
        if (_menuFor is null) return;

        var entry = _menuFor;
        _menuFor = null;

        Nav.NavigateTo(
            "/chat/validation" + new Uri(Nav.Uri).Query
            + "&class=" + Uri.EscapeDataString(entry.Name)
            + "&file=" + Uri.EscapeDataString(entry.File));
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ProjectTreeMenuTests|FullyQualifiedName~ChatPageTests|FullyQualifiedName~ProjectTreeTests"`

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add CodeGenerator/Web/Components/TreeFolderView.razor \
        CodeGenerator/Web/Components/ProjectTreeView.razor \
        CodeGenerator/Web/Components/ChatPage.razor \
        CodeGenerator.Tests/Web/Chat/ProjectTreeMenuTests.cs
git commit -m "feat: right-click a class in the tree to open its validation page"
```

---

### Task 8: Styling, and looking at it

Four defects on the Blazor chat branch compiled clean, passed their tests, and appeared only in a running page. This task is the one that catches those, and it is not optional.

**Files:**
- Modify: `CodeGenerator/wwwroot/chat.css`

**Interfaces:** none.

- [ ] **Step 1: Write the page's styles**

Append to `chat.css`. Scope every colour token to `.val-page`, **never** to `:root` — `App.razor` links every stylesheet on every page, so a `:root` block here repaints the chat page and the workflow page too:

```css
/* The validation page. Its own tokens on its own root class, for the reason above. */
.val-page {
    --val-bg: #ffffff;
    --val-ink: #1c1c1c;
    --val-muted: #666666;
    --val-line: #e2e2e2;
    --val-good: #1a7f37;
    --val-bad: #b3261e;

    max-width: 60rem;
    margin: 0 auto;
    padding: 1.5rem;
    color: var(--val-ink);
    background: var(--val-bg);
    font-family: system-ui, -apple-system, "Segoe UI", sans-serif;
}

.val-head { display: flex; justify-content: space-between; align-items: flex-start; gap: 1rem; }
.val-head h1 { margin: 0 0 .25rem; font-size: 1.35rem; }
.val-target { margin: 0; font-weight: 600; }
.val-path { display: block; font-weight: 400; font-size: .8rem; color: var(--val-muted); word-break: break-all; }
.val-back { align-self: center; white-space: nowrap; }

.val-page section { margin-top: 1.5rem; border-top: 1px solid var(--val-line); padding-top: 1rem; }
.val-page h2 { font-size: .95rem; margin: 0 0 .5rem; }
.val-page ul { margin: 0; padding-left: 1.1rem; }

.val-prop-name { font-weight: 600; }
.val-prop-type { color: var(--val-muted); margin-left: .5rem; font-size: .85rem; }
.val-none, .val-hint { color: var(--val-muted); font-size: .875rem; }

.val-box { width: 100%; box-sizing: border-box; font: inherit; padding: .6rem;
           border: 1px solid var(--val-line); border-radius: 6px; resize: vertical; }
.val-add { margin-top: .6rem; padding: .5rem 1rem; cursor: pointer; }
.val-add:disabled { cursor: default; opacity: .5; }

.val-added p { color: var(--val-good); margin: .25rem 0; }
.val-refused p { color: var(--val-bad); margin: .25rem 0; }
.val-refused-help { color: var(--val-muted) !important; font-size: .875rem; }
.val-problem { color: var(--val-bad); }

.val-phrasings { margin-top: 1.5rem; }
.val-phrasings summary { cursor: pointer; font-size: .9rem; }
.val-phrasings ul { columns: 2; margin-top: .5rem; font-size: .875rem; }

@media (max-width: 40rem) { .val-phrasings ul { columns: 1; } }

/* The tree's right-click menu. The veil is what dismisses it, and it sits under the menu. */
.tree-menu-veil { position: fixed; inset: 0; z-index: 40; }
.tree-menu { position: fixed; z-index: 41; background: #ffffff; color: #1c1c1c;
             border: 1px solid #d0d0d0; border-radius: 6px; box-shadow: 0 4px 14px rgba(0,0,0,.18);
             padding: .25rem; }
.tree-menu button { display: block; width: 100%; text-align: left; padding: .4rem .9rem;
                    background: none; border: 0; font: inherit; cursor: pointer; white-space: nowrap; }
.tree-menu button:hover { background: #f0f0f0; }
```

The menu is `position: fixed` with no coordinates, so it lands at the top-left of the viewport. Positioning it at the pointer needs the `MouseEventArgs` from the right-click — change `OfferValidation(TreeClass)` to `OfferValidation(TreeClass, MouseEventArgs)`, keep `_menuAt` on the page, and set `style="left:@(_menuAt.X)px; top:@(_menuAt.Y)px"`. Do that here, in this task, and update `TreeFolderView`'s callback type to `EventCallback<(TreeClass Entry, MouseEventArgs Mouse)>` along with the test from Task 7.

- [ ] **Step 2: Build and run the whole suite**

Run: `dotnet test SmartCodeGenerator.slnx --configuration Release`

Expected: PASS across all test projects. Record the green count.

- [ ] **Step 3: Rebuild the CLI before looking at anything**

Run: `dotnet build CodeGenerator/CodeGenerator.csproj --configuration Debug`

`dotnet test` leaves the CLI's `bin` stale, and a fix that is green can look dead in the running tool.

- [ ] **Step 4: Open the page and look at it**

`chat` refuses piped and argv runs, so the page cannot be reached by driving the CLI from here. Start it from a throwaway MSTest probe that starts `ChatWebHost` against a workspace with real classes in it, print `host.Url`, and hold it open:

```csharp
[TestClass]
[DoNotParallelize]
public class ValidationProbe
{
    [TestMethod]
    [Ignore] // a probe, not a test — run it by name when looking at the page
    public void Hold_the_page_open()
    {
        var root = @"D:\SampleProjects\<AnAppWithClasses>";

        var session = new ChatSession(
            new Workspace(root, projectPathSet: false), AiLayer.Create(), []);

        Assert.IsTrue(ChatWebHost.TryStart(session, out var host));

        Console.WriteLine(host.Url);
        Thread.Sleep(TimeSpan.FromMinutes(10));
    }
}
```

Run it with `dotnet test ... --filter "FullyQualifiedName~ValidationProbe" -- MSTest.Execution.TreatIgnoredAsPassing=true`, or drop the `[Ignore]` while probing. Then use the `browser-automation` skill to open the printed URL and check, with a screenshot each time:

1. The tree draws, and right-clicking a class row shows the menu **at the pointer**, not at the corner, and the browser's own context menu does not appear.
2. Clicking *Add validation…* opens the validation page, still carrying the token, listing that class's properties.
3. Typing `Age must be at least 18` and pressing the button reports the rule added, lists it under "Rules on this class", and the class file on disk contains the check.
4. Typing nonsense reports the sentence back and writes nothing.
5. *Back to the chat* returns to a transcript that still has the conversation in it and now carries the narration line.
6. The page is readable on a machine set to dark mode — it declares its own light palette and must not inherit the chat page's dark one.

- [ ] **Step 5: Fix what looking at it found, then look again**

Any defect found here gets its own commit and, where a test could have caught it, its own test.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerator/wwwroot/chat.css CodeGenerator/Web/Components/
git commit -m "feat: styling for the validation page and the tree's right-click menu"
```

---

### Task 9: The manual

**Files:**
- Modify: `docs/user-manual-source.html`
- Modify: `docs/commands.md`

- [ ] **Step 1: Read how the manual is built**

Read `docs/user-manual-source.html` for the section structure and find the chapter covering the chat page. The `.docx` is produced from this HTML through Word COM — do not edit the `.docx`.

- [ ] **Step 2: Add the section**

Write a short section under the chat page's chapter: what right-clicking a class does, what the page shows, that one rule goes on each line, that the check lands in the class's own file inside `Validate()`, and that a sentence it cannot parse writes nothing. Include four example phrasings drawn from `ValidationAdd.Phrasings()`.

- [ ] **Step 3: Note the widened vocabulary in `commands.md`**

Add the new phrasings to the `add validation` and `add business validation` entries, matching the descriptions written in the catalog plan's Task 11 so the two do not disagree.

- [ ] **Step 4: Regenerate the .docx**

Follow whatever the repository already does to produce `docs/SmartCodeGenerator-User-Manual.docx` from the HTML — check the most recent commit that touched both files for the command.

- [ ] **Step 5: Commit**

```bash
git add docs/
git commit -m "docs: the validation page in the manual"
```

---

### Task 10: Finish the branch

- [ ] **Step 1: Full suite, Release**

Run: `dotnet test SmartCodeGenerator.slnx --configuration Release`

Expected: PASS. Record the count.

- [ ] **Step 2: Confirm the working tree holds only this work**

Run: `git status` and `git log --oneline main..HEAD`

Never `git add -u` — it sweeps in whatever else is lying about.

- [ ] **Step 3: Push and open the PR**

```bash
git push -u origin custom-validation-page
gh pr create --title "Customized validation from the chat tree" --body "..."
```

The PR body says what the branch does, names the two defects it fixed on the way (the allowed-values catch-all, the non-partial class beside a partial validator), and says plainly that CI will not run — the account's spending limit kills every job in seconds — so the evidence is the local Release run and the screenshots from Task 8.

---

## Self-Review

**Spec coverage.** Writer creating and extending `Validate()` → Tasks 1 and 2. The refusal table → Tasks 2 and 3 (absent property, foreign `Validate()`, bodiless record, duplicate, outside the workspace via Task 5, compile failure via Task 3). Compile-before-write and rollback → Task 3. `generate validator`'s `partial` fix → Task 4. Route under the token guard → Tasks 5 and 6. Target in the query, confined to the workspace → Task 5. Page contents, categories tried not chosen, registry write, transcript line, staying put → Task 6. Right-click on class rows only → Task 7. Styling and looking at it → Task 8. Manual → Task 9.

**Placeholder scan.** No TBDs. Four places instruct reading existing code first: `ParsedCommand`'s shape (Task 4), `TreeFolder`'s constructor and bUnit's API version (Tasks 6 and 7), the manual's build command (Task 9). Each names the file to read.

**Type consistency.** `ValidationWriteResult(bool Ok, string Message)` is returned by `ClassValidationWriter.Add` and read as `.Ok` / `.Message` in Tasks 2, 3 and 6. `ValidationTarget(string ClassName, string File)` is produced by `ValidationTargetReader.TryResolve` and consumed by `ValidationAdd.Apply` and the page. `ValidationOutcome(Added, Refused)` is produced by `ValidationAdd.Apply` and read by the page as `.Added` / `.Refused`. `ExistingRules` returns `IReadOnlyList<string>` in both its definition and both its callers. `ValidationRuleCatalog.Fragments` is used by `ValidationAdd` and tested in Task 5.

**One inconsistency deliberately left for Task 8 to resolve:** Task 7 defines `OnValidate` as `EventCallback<TreeClass>`, and Task 8 changes it to carry the `MouseEventArgs` needed to place the menu at the pointer. It is written that way round because the menu works before it is positioned, and a reviewer should be able to reject the positioning without rejecting the right-click.
