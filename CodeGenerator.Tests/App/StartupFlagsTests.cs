using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The command-line flags every tool is expected to answer.
/// </summary>
/// <remarks>
/// <c>Program.cs</c> joins its arguments into one command string and hands it to the parser, so
/// before this existed <c>CodeGenerator --version</c> reported "Command not recognized" — and did
/// so more embarrassingly once the tool actually had a version to report.
/// </remarks>
[TestClass]
public class StartupFlagsTests
{
    [TestMethod]
    public void EveryCommonSpellingOfVersionIsRecognized()
    {
        foreach (var flag in new[] { "--version", "-v", "/version", "--VERSION" })
            Assert.AreEqual(StartupFlag.Version, StartupFlags.Parse([flag]), flag);
    }

    [TestMethod]
    public void EveryCommonSpellingOfHelpIsRecognized()
    {
        // /? is the Windows convention and this is a Windows-first tool; -h and --help are what
        // everyone else types.
        foreach (var flag in new[] { "--help", "-h", "-?", "/?", "/help", "--HELP" })
            Assert.AreEqual(StartupFlag.Help, StartupFlags.Parse([flag]), flag);
    }

    [TestMethod]
    public void OrdinaryCommandsAreNotFlags()
    {
        // The whole point is to intercept before the parser, so anything that is a real command
        // must fall through untouched.
        foreach (var args in new[]
                 {
                     new[] { "list", "projects" },
                     ["build app: a shop"],
                     ["help"],
                     ["status"],
                     new string[0],
                 })
        {
            Assert.AreEqual(StartupFlag.None, StartupFlags.Parse(args), string.Join(' ', args));
        }
    }

    [TestMethod]
    public void AFlagIsOnlyAFlagWhenItIsTheWholeInvocation()
    {
        // `build app: --version of the API` is a description, not a request for the version.
        Assert.AreEqual(StartupFlag.None, StartupFlags.Parse(["build app: the --version endpoint"]));
        Assert.AreEqual(StartupFlag.None, StartupFlags.Parse(["--version", "extra"]));
    }

    [TestMethod]
    public void VersionOutputIsTheBareVersionAndNothingElse()
    {
        // Scripts parse this. A banner, a label or a trailing hint would all have to be stripped
        // by every caller, so it is the version alone.
        var text = StartupFlags.VersionText();

        Assert.AreEqual(VersionInfo.Current, text.Trim());
        Assert.DoesNotContain("Smart Code Generator", text);
    }

    [TestMethod]
    public void HelpOutputIsTheSameOverviewTheReplGives()
    {
        // Two ways of asking the same question must not produce two different answers.
        Assert.AreEqual(HelpCommand.Render("help"), StartupFlags.HelpText());
    }
}
