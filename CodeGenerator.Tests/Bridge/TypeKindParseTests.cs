using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

/// <summary>
/// record, struct and abstract/static/sealed in a create command are read into
/// <see cref="ParsedCommand.Kind"/> and <see cref="ParsedCommand.Modifier"/>.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TypeKindParseTests
{
    private string _workspace = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        SessionContext.Clear();
        ProjectManager.CurrentProject = null;
        LearnedIntentStore.UseInMemory();
        _workspace = Path.Combine(Path.GetTempPath(), "cg_typekind_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SessionContext.Clear();
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    [TestMethod]
    [DataRow("create record Money with properties Amount decimal and Currency string", "Money", DeclaredKind.Record, TypeModifier.None)]
    [DataRow("create struct Point with properties X int and Y int", "Point", DeclaredKind.Struct, TypeModifier.None)]
    [DataRow("create abstract class Shape with methods Area returns decimal", "Shape", DeclaredKind.Class, TypeModifier.Abstract)]
    [DataRow("create static class Helpers", "Helpers", DeclaredKind.Class, TypeModifier.Static)]
    [DataRow("create sealed class Token", "Token", DeclaredKind.Class, TypeModifier.Sealed)]
    [DataRow("create abstract record Entity", "Entity", DeclaredKind.Record, TypeModifier.Abstract)]
    [DataRow("create sealed record Money", "Money", DeclaredKind.Record, TypeModifier.Sealed)]
    [DataRow("create a record money", "Money", DeclaredKind.Record, TypeModifier.None)]
    [DataRow("create class Record", "Record", DeclaredKind.Class, TypeModifier.None)]
    [DataRow("create class Struct", "Struct", DeclaredKind.Class, TypeModifier.None)]
    public void Kind_and_modifier_are_read(string sentence, string name, DeclaredKind kind, TypeModifier modifier)
    {
        var cmd = CommandFront.Parse(sentence, _workspace);

        Assert.AreEqual(IntentType.CreateClass, cmd.Intent);
        Assert.AreEqual(name, cmd.ClassName);
        Assert.AreEqual(kind, cmd.Kind);
        Assert.AreEqual(modifier, cmd.Modifier);
        Assert.IsFalse(cmd.Metadata.ContainsKey(ParsedCommand.KindErrorKey));
    }

    [TestMethod]
    public void Record_keeps_its_properties()
    {
        var cmd = CommandFront.Parse("create record Money with properties Amount decimal and Currency string", _workspace);

        Assert.HasCount(2, cmd.Properties);
    }

    [TestMethod]
    public void Abstract_class_keeps_its_methods()
    {
        var cmd = CommandFront.Parse("create abstract class Shape with methods Area returns decimal", _workspace);

        Assert.HasCount(1, cmd.Methods);
    }

    [TestMethod]
    public void Record_keeps_its_interfaces()
    {
        var cmd = CommandFront.Parse("create record Money implements IEquatable", _workspace);

        CollectionAssert.AreEqual(new[] { "IEquatable" }, cmd.Interfaces);
        Assert.AreEqual(DeclaredKind.Record, cmd.Kind);
    }

    [TestMethod]
    [DataRow("create static struct P", "A struct cannot be static.")]
    [DataRow("create sealed abstract class S", "A class cannot be both sealed and abstract.")]
    [DataRow("create static record R", "A record cannot be static.")]
    [DataRow("create abstract struct P", "A struct cannot be abstract.")]
    public void Impossible_pairs_are_refused_with_the_rule(string sentence, string rule)
    {
        var cmd = CommandFront.Parse(sentence, _workspace);

        Assert.AreEqual(IntentType.CreateClass, cmd.Intent);
        Assert.IsTrue(cmd.Metadata.TryGetValue(ParsedCommand.KindErrorKey, out var error));
        StringAssert.Contains(error, rule);
    }

    [TestMethod]
    public void Record_and_add_method_is_one_command()
    {
        Assert.IsNull(CommandFront.TwoCommands("create record Money and add method Total to Money"));
        Assert.IsNull(CommandFront.TwoCommands("create abstract class Shape and add method Area to Shape"));
    }
}
