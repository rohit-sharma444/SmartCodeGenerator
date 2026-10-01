using CodeGenerator.App.Scripts;

namespace CodeGenerator.Tests.App.Scripts;

[TestClass]
public sealed class ScriptFileTests
{
    [TestMethod]
    public void Commands_are_read_in_order_with_their_line_numbers_skipping_blanks_and_comments()
    {
        var parse = ScriptFile.Parse("# a library\r\n\r\ncreate class Book\r\n   # indented note\r\nadd property Pages int to Book  \r\n");

        Assert.IsNull(parse.Refusal);
        Assert.HasCount(2, parse.Steps);
        Assert.AreEqual(new CommandStep(3, "create class Book"), parse.Steps[0]);
        Assert.AreEqual(new CommandStep(5, "add property Pages int to Book"), parse.Steps[1]);
    }

    [TestMethod]
    public void A_byte_order_mark_is_not_part_of_the_first_command()   // Review Focus 3
    {
        var parse = ScriptFile.Parse("﻿create class Book");

        Assert.AreEqual(new CommandStep(1, "create class Book"), parse.Steps.Single());
    }

    [TestMethod]
    [DataRow("undo")]
    [DataRow("redo")]
    [DataRow("exit")]
    [DataRow("batch")]
    [DataRow("demo")]
    [DataRow("chat")]
    [DataRow("show relationships")]
    [DataRow("run other.cgs")]
    [DataRow("save session x.cgs")]
    [DataRow("help")]
    [DataRow("status")]
    [DataRow("history")]
    [DataRow("clear")]
    public void A_command_that_cannot_run_in_a_script_refuses_the_whole_file_naming_the_line(string command)
    {
        var parse = ScriptFile.Parse("create class Book\n\n" + command + "\ncreate class Member");

        Assert.AreEqual($"Line 3: '{command}' can't be used in a script. Nothing was run.", parse.Refusal);
        Assert.IsEmpty(parse.Steps);
    }

    [TestMethod]
    public void A_build_app_takes_its_answers_up_to_and_including_approve()
    {
        var parse = ScriptFile.Parse(
            "build app from csv books.csv\n# the review\nrename Books to Library\napprove console\ncreate class Extra");

        Assert.IsNull(parse.Refusal);
        Assert.HasCount(2, parse.Steps);
        var build = (BuildAppStep)parse.Steps[0];
        Assert.AreEqual(1, build.Line);
        CollectionAssert.AreEqual(
            new[] { "build app from csv books.csv", "rename Books to Library", "approve console" },
            build.Lines.ToArray());
        Assert.AreEqual(new CommandStep(5, "create class Extra"), parse.Steps[1]);
    }

    [TestMethod]
    public void Cancel_also_ends_a_build_app()
    {
        var parse = ScriptFile.Parse("build app: a library\ncancel");

        CollectionAssert.AreEqual(new[] { "build app: a library", "cancel" },
            ((BuildAppStep)parse.Steps.Single()).Lines.ToArray());
    }

    // S2: the review generated on "a", so the lines after it are the script's own commands, not
    // answers piped to a review that has already ended.
    [TestMethod]
    public void The_approve_shortcut_ends_a_build_app_and_what_follows_is_a_command()
    {
        var parse = ScriptFile.Parse("build app from csv books.csv\na\nadd property X int to Nope\ncancel");

        Assert.HasCount(3, parse.Steps);
        CollectionAssert.AreEqual(new[] { "build app from csv books.csv", "a" },
            ((BuildAppStep)parse.Steps[0]).Lines.ToArray());
        Assert.AreEqual(new CommandStep(3, "add property X int to Nope"), parse.Steps[1]);
    }

    [TestMethod]
    public void A_build_app_with_no_approve_or_cancel_is_refused()
    {
        var parse = ScriptFile.Parse("create class Book\nbuild app: a library\nrename Book to Volume");

        Assert.AreEqual("Line 2: 'build app' needs an 'approve …' or 'cancel' line after it. Nothing was run.",
            parse.Refusal);
    }

    [TestMethod]
    public void An_empty_file_has_no_steps_and_no_refusal()
    {
        var parse = ScriptFile.Parse("# only a comment\n\n");

        Assert.IsNull(parse.Refusal);
        Assert.IsEmpty(parse.Steps);
    }

    [TestMethod]
    [DataRow("approve", true)]
    [DataRow("approve console", true)]
    [DataRow("APPROVE wpf sqlite", true)]
    [DataRow("cancel", true)]
    [DataRow("approved", false)]
    // The review reads only the first word, so whatever it ends on, the block ends on too —
    // including the shortcuts its prompt advertises ("[a]pprove ... [c]ancel").
    [DataRow("cancel it", true)]
    [DataRow("a", true)]
    [DataRow("A console", true)]
    [DataRow("c", true)]
    [DataRow("add Author", false)]
    [DataRow("accept all", false)]
    public void What_ends_a_build(string line, bool ends) => Assert.AreEqual(ends, ScriptFile.EndsBuild(line));
}
