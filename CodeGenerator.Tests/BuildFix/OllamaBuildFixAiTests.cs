using System.Net;
using System.Text;
using System.Text.Json;
using CodeGenerator.BuildFix;
using DomainDiscovery;

namespace CodeGenerator.Tests.BuildFix;

[TestClass]
public class OllamaBuildFixAiTests
{
    private sealed class Stub(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        public string? LastUrl { get; private set; }

        public int Calls { get; private set; }

        public Stub(Func<HttpRequestMessage, HttpResponseMessage> reply)
            : this((r, _) => Task.FromResult(reply(r)))
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            LastUrl = request.RequestUri?.ToString();
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return await reply(request, ct);
        }
    }

    private static HttpResponseMessage Ok(string content, string? doneReason = null) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { message = new { role = "assistant", content }, done_reason = doneReason }),
            Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage Raw(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static BuildDiagnostic Diag() => new(@"C:\secret\proj\A.cs", 3, 5, "CS1002", "; expected");

    private static OllamaBuildFixAi Make(Stub stub, TimeSpan? timeout = null)
        => new(new DiscoveryConfig(), new HttpClient(stub), available: () => true, timeout: timeout);

    private static string? Propose(string reply, string excerpt = "int a = 1")
        => Make(new Stub(_ => Ok(reply))).Propose(Diag(), excerpt);

    [TestMethod]
    public void Propose_ReturnsTheFencedBlock_FromProseWrappedReply()
    {
        Assert.AreEqual("int a = 1;", Propose("Here you go:\n```csharp\nint a = 1;\n```\nDone."));
    }

    [TestMethod]
    public void Propose_AcceptsAFenceWithNoLanguage()
    {
        Assert.AreEqual("int a = 1;", Propose("```\nint a = 1;\n```"));
    }

    [TestMethod]
    public void Propose_KeepsMultipleLinesAndIndentation()
    {
        var reply = "```csharp\nif (x)\n{\n    y();\n}\n```";

        Assert.AreEqual("if (x)\n{\n    y();\n}", Propose(reply, "if (x)\n{\n    y()\n}"));
    }

    [TestMethod]
    public void Propose_UsesTheExcerptsCrLfLineEndings()
    {
        Assert.AreEqual("a;\r\nb;", Propose("```csharp\na;\nb;\n```", "a\r\nb"));
    }

    [TestMethod]
    public void Propose_ReturnsNull_WhenThereIsNoFencedBlock()
    {
        Assert.IsNull(Propose("Just add a semicolon."));
    }

    [TestMethod]
    public void Propose_ReturnsNull_ForAnEmptyOrBlankBlock()
    {
        Assert.IsNull(Propose("```csharp\n\n```"));
        Assert.IsNull(Propose("```csharp\n   \n```"));
    }

    [TestMethod]
    public void Propose_ReturnsNull_ForAnEmptyReply()
    {
        Assert.IsNull(Propose(string.Empty));
    }

    [TestMethod]
    public void Propose_ReturnsNull_ForAnUnclosedFence()
    {
        Assert.IsNull(Propose("```csharp\nint a = 1;"));
    }

    [TestMethod]
    public void Propose_ReturnsNull_ForTwoBlocks()
    {
        Assert.IsNull(Propose("```csharp\nint a = 1;\n```\nand also\n```csharp\nint b = 2;\n```"));
    }

    [TestMethod]
    public void Propose_ReturnsNull_WhenTheReplyNamesAnotherFile()
    {
        Assert.IsNull(Propose("```csharp\n// File: ..\\..\\Other.cs\nint a = 1;\n```"));
        Assert.IsNull(Propose("```csharp\n// path = C:\\Windows\\x.cs\nint a = 1;\n```"));
    }

    [TestMethod]
    public void Propose_ReturnsNull_WhenTheReplyEqualsTheExcerpt()
    {
        Assert.IsNull(Propose("```csharp\nint a = 1\n```"));
    }

    [TestMethod]
    public void Propose_ReturnsNull_WhenTheReplyIsFarLongerThanTheExcerpt()
    {
        var huge = string.Join("\n", Enumerable.Range(0, 60).Select(i => $"int v{i} = {i};"));

        Assert.IsNull(Propose($"```csharp\n{huge}\n```", "int a = 1\nint b = 2"));
    }

    [TestMethod]
    public void Propose_ReturnsNull_WhenTheModelStoppedAtItsLimit()
    {
        var ai = Make(new Stub(_ => Ok("```csharp\nint a = 1;\n```", doneReason: "length")));

        Assert.IsNull(ai.Propose(Diag(), "int a = 1"));
    }

    [TestMethod]
    public void Propose_ReturnsNull_OnAServerError()
    {
        var ai = Make(new Stub(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        Assert.IsNull(ai.Propose(Diag(), "int a = 1"));
    }

    [TestMethod]
    public void Propose_ReturnsNull_ForNonJsonOrWrongShapedBodies()
    {
        foreach (var body in new[] { "not json", "{}", "{\"message\":{}}", "{\"message\":{\"content\":5}}", "[]" })
            Assert.IsNull(Make(new Stub(_ => Raw(body))).Propose(Diag(), "int a = 1"), body);
    }

    [TestMethod]
    public void Propose_ReturnsNull_WhenTheCallThrows()
    {
        var ai = Make(new Stub(_ => throw new HttpRequestException("down")));

        Assert.IsNull(ai.Propose(Diag(), "int a = 1"));
    }

    [TestMethod]
    public void Propose_ReturnsNull_WhenTheCallIsCancelled()
    {
        var ai = Make(new Stub(_ => throw new TaskCanceledException()));

        Assert.IsNull(ai.Propose(Diag(), "int a = 1"));
    }

    [TestMethod]
    public void Propose_ReturnsNull_WhenTheServerOutlastsTheTimeout()
    {
        // The handler waits until the per-call deadline cancels it; a real hang would be minutes.
        var stub = new Stub(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Ok("```csharp\nint a = 1;\n```");
        });
        var ai = Make(stub, TimeSpan.FromMilliseconds(150));
        var clock = System.Diagnostics.Stopwatch.StartNew();

        Assert.IsNull(ai.Propose(Diag(), "int a = 1"));
        Assert.IsLessThan(10_000, clock.ElapsedMilliseconds);
        Assert.AreEqual(1, stub.Calls);
    }

    [TestMethod]
    public void Propose_SendsTheErrorTheCodeAndTheConfiguredModel_Unstreamed()
    {
        var stub = new Stub(_ => Ok("```\nx\n```"));
        var config = new DiscoveryConfig { Model = "my-model:7b", Endpoint = "http://box:1234/" };
        var ai = new OllamaBuildFixAi(config, new HttpClient(stub), available: () => true);

        ai.Propose(Diag(), "int a = 1");

        using var doc = JsonDocument.Parse(stub.LastBody!);
        var root = doc.RootElement;
        Assert.AreEqual("my-model:7b", root.GetProperty("model").GetString());
        Assert.IsFalse(root.GetProperty("stream").GetBoolean());
        Assert.AreEqual("http://box:1234/api/chat", stub.LastUrl);

        var user = root.GetProperty("messages")[1].GetProperty("content").GetString()!;
        StringAssert.Contains(user, "CS1002");
        StringAssert.Contains(user, "; expected");
        StringAssert.Contains(user, "int a = 1");
        StringAssert.Contains(user, "A.cs");
    }

    [TestMethod]
    public void Propose_NeverSendsTheFullPath()
    {
        var stub = new Stub(_ => Ok("```\nx\n```"));

        Make(stub).Propose(Diag(), "int a = 1");

        Assert.DoesNotContain("secret", stub.LastBody!);
    }

    [TestMethod]
    public void IsAvailable_UsesTheInjectedProbe()
    {
        var no = new OllamaBuildFixAi(new DiscoveryConfig(), new HttpClient(new Stub(_ => Ok(""))), available: () => false);
        var yes = new OllamaBuildFixAi(new DiscoveryConfig(), new HttpClient(new Stub(_ => Ok(""))), available: () => true);

        Assert.IsFalse(no.IsAvailable());
        Assert.IsTrue(yes.IsAvailable());
    }

    [TestMethod]
    public void IsAvailable_IsFalse_WhenTheProbeThrows()
    {
        var ai = new OllamaBuildFixAi(
            new DiscoveryConfig(), new HttpClient(new Stub(_ => Ok(""))), available: () => throw new IOException("x"));

        Assert.IsFalse(ai.IsAvailable());
    }

    [TestMethod]
    public void IsAvailable_IsFalse_WhenNothingListensOnTheEndpoint()
    {
        // Port 1 on loopback refuses at once; no Ollama is contacted.
        var ai = new OllamaBuildFixAi(new DiscoveryConfig { Endpoint = "http://127.0.0.1:1" });

        Assert.IsFalse(ai.IsAvailable());
        ai.Dispose();
    }
}
