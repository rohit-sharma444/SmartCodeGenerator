using NaturalLanguageBridge;

namespace CodeGenerator.Tests.Bridge;

[TestClass]
public class ApprovalReplyTests
{
    [TestMethod]
    [DataRow("y")]
    [DataRow("Y")]
    [DataRow("yes")]
    [DataRow("YES")]
    [DataRow("  y  ")]
    public void Parse_Yes_Approves(string line)
    {
        Assert.AreEqual(ApprovalReply.Approve, ApprovalReplyParser.Parse(line));
    }

    [TestMethod]
    [DataRow("n")]
    [DataRow("N")]
    [DataRow("no")]
    [DataRow("NO")]
    [DataRow("  n  ")]
    public void Parse_No_Rejects(string line)
    {
        Assert.AreEqual(ApprovalReply.Reject, ApprovalReplyParser.Parse(line));
    }

    [TestMethod]
    public void Parse_Null_Cancels()
    {
        // null means EOF / closed stdin, not "no answer".
        Assert.AreEqual(ApprovalReply.Cancel, ApprovalReplyParser.Parse(null));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Parse_Empty_Reprompts(string line)
    {
        // A bare Enter must never approve: a held-down Return key would
        // otherwise walk straight through a DeleteClass.
        Assert.AreEqual(ApprovalReply.Reprompt, ApprovalReplyParser.Parse(line));
    }

    [TestMethod]
    [DataRow("maybe")]
    [DataRow("yep")]
    [DataRow("1")]
    public void Parse_Garbage_Reprompts(string line)
    {
        Assert.AreEqual(ApprovalReply.Reprompt, ApprovalReplyParser.Parse(line));
    }
}
