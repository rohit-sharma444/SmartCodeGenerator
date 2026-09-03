using System.Net;
using System.Text;
using DomainDiscovery;
using DomainDiscovery.Llm;
using Xunit;

namespace DomainDiscovery.Tests.Llm;

/// <summary>
/// A response body delivered in pieces, the way a real generation arrives — optionally never
/// finishing, which is the failure this whole path exists to survive.
/// </summary>
internal sealed class ScriptedStream : Stream
{
    private readonly IEnumerator<string> _pieces;
    private readonly bool _hangAtEnd;
    private byte[] _pending = [];
    private int _offset;

    public ScriptedStream(IEnumerable<string> pieces, bool hangAtEnd = false)
    {
        _pieces = pieces.GetEnumerator();
        _hangAtEnd = hangAtEnd;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_offset >= _pending.Length)
        {
            if (!_pieces.MoveNext())
            {
                if (_hangAtEnd)
                    // Exactly a model that has stopped saying anything but never says it is done.
                    await Task.Delay(Timeout.Infinite, cancellationToken);

                return 0;
            }

            _pending = Encoding.UTF8.GetBytes(_pieces.Current);
            _offset = 0;
        }

        var taken = Math.Min(buffer.Length, _pending.Length - _offset);
        _pending.AsSpan(_offset, taken).CopyTo(buffer.Span);
        _offset += taken;

        return taken;
    }

    public override int Read(byte[] buffer, int offset, int count)
        => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class StreamingHandler : HttpMessageHandler
{
    private readonly IEnumerable<string> _pieces;
    private readonly bool _hangAtEnd;

    public string? LastRequestBody { get; private set; }

    public StreamingHandler(IEnumerable<string> pieces, bool hangAtEnd = false)
    {
        _pieces = pieces;
        _hangAtEnd = hangAtEnd;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is not null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new ScriptedStream(_pieces, _hangAtEnd))
        };
    }
}

public class OllamaClientStreamingTests
{
    /// <summary>One NDJSON line of a streamed chat response, carrying one fragment of content.</summary>
    private static string Chunk(string content)
        => "{\"message\":{\"content\":" + System.Text.Json.JsonSerializer.Serialize(content) + "},\"done\":false}\n";

    private static string Last(string doneReason = "stop", int evalCount = 900)
        => "{\"message\":{\"content\":\"\"},\"done\":true,\"done_reason\":\"" + doneReason
           + "\",\"eval_count\":" + evalCount + "}\n";

    private const string ProductClass =
        "{\"name\":\"Product\",\"role\":\"Item\",\"purpose\":\"a thing sold\","
        + "\"properties\":[{\"name\":\"Id\",\"type\":\"int\"}],\"relationships\":[]}";

    private const string SaleClass =
        "{\"name\":\"Sale\",\"role\":\"Transaction\",\"purpose\":\"a sale\","
        + "\"properties\":[{\"name\":\"Id\",\"type\":\"int\"}],\"relationships\":[]}";

    private static OllamaClient Client(HttpMessageHandler handler, DiscoveryConfig? config = null)
        => new(config ?? new DiscoveryConfig(), new HttpClient(handler));

    [Fact]
    public void AsksForAStreamSoThereIsSomethingToShowAndSomethingToSalvage()
    {
        var handler = new StreamingHandler([Chunk("{\"classes\":[" + ProductClass + "]}"), Last()]);

        Client(handler).Ask("a shop");

        Assert.Contains("\"stream\":true", handler.LastRequestBody!);
    }

    [Fact]
    public void ReassemblesAStreamedAnswerIntoTheModel()
    {
        var handler = new StreamingHandler(
        [
            Chunk("{\"classes\":["),
            Chunk(ProductClass + ","),
            Chunk(SaleClass),
            Chunk("]}"),
            Last()
        ]);

        var answer = Client(handler).Ask("a shop");

        Assert.True(answer.Succeeded);
        Assert.Null(answer.Warning);
        Assert.Equal(["Product", "Sale"], answer.Model!.Classes.Select(c => c.Name));
    }

    [Fact]
    public void NamesEachClassAsItArrives()
    {
        var handler = new StreamingHandler(
        [
            Chunk("{\"classes\":["),
            Chunk(ProductClass + ","),
            Chunk(SaleClass),
            Chunk("]}"),
            Last()
        ]);

        var seen = new List<IReadOnlyList<string>>();

        Client(handler).Ask(
            "a shop",
            found => seen.Add(found.Select(PartialLlmJson.Describe).ToList()));

        // The point of the callback: the first class is reported before the second one exists, so
        // the panel can draw it rather than showing a blank wait for the whole generation.
        Assert.Contains(seen, s => s.Count == 1 && s[0] == "Product [Item]");
        Assert.Equal(["Product [Item]", "Sale [Transaction]"], seen[^1]);
    }

    [Fact]
    public void KeepsTheClassesItAlreadyHasWhenTheStreamEndsMidAnswer()
    {
        // The server stopped without ever sending done - a runner that died, or a connection cut.
        var handler = new StreamingHandler(
        [
            Chunk("{\"classes\":["),
            Chunk(ProductClass + ","),
            Chunk("{\"name\":\"Sal")
        ]);

        var answer = Client(handler).Ask("a shop");

        Assert.True(answer.Succeeded);
        Assert.Equal("Product", Assert.Single(answer.Model!.Classes).Name);
        Assert.NotNull(answer.Warning);
    }

    [Fact]
    public void ReportsTruncationAsAWarningRatherThanAFailureWhenOllamaCutsItShort()
    {
        var handler = new StreamingHandler(
        [
            Chunk("{\"classes\":[" + ProductClass + ","),
            Chunk("{\"name\":\"Sal"),
            Last("length", 3072)
        ]);

        var answer = Client(handler).Ask("a shop");

        Assert.True(answer.Succeeded);
        Assert.Equal("Product", Assert.Single(answer.Model!.Classes).Name);
        Assert.Contains("cut", answer.Warning!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StopsAStalledGenerationInsteadOfWaitingOutTheWholeTimeout()
    {
        // The measured failure: the model keeps a connection open producing nothing that ever
        // closes the answer. Before this, the client sat there for its entire ten-minute timeout
        // and reported nothing at all.
        var config = new DiscoveryConfig { StallSeconds = 1, TimeoutMinutes = 10 };

        var handler = new StreamingHandler(
            [Chunk("{\"classes\":[" + ProductClass + ","), Chunk("{\"name\":\"Sal")],
            hangAtEnd: true);

        var started = DateTime.UtcNow;
        var answer = Client(handler, config).Ask("a shop");
        var took = DateTime.UtcNow - started;

        Assert.True(took < TimeSpan.FromSeconds(30), $"gave up only after {took.TotalSeconds:0.#}s");
        Assert.True(answer.Succeeded);
        Assert.Equal("Product", Assert.Single(answer.Model!.Classes).Name);
        Assert.Contains("stopped", answer.Warning!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailsWithAReasonWhenAStalledGenerationProducedNothingUsable()
    {
        var config = new DiscoveryConfig { StallSeconds = 1 };

        var handler = new StreamingHandler([Chunk("{\"classes\":[{\"na")], hangAtEnd: true);

        var answer = Client(handler, config).Ask("a shop");

        Assert.False(answer.Succeeded);
        Assert.NotNull(answer.Error);
        Assert.Contains("stopped", answer.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheRosterAskLeavesNoRoomForPropertiesInItsSchema()
    {
        // Ollama constrains decoding to the schema, so a field the schema does not have is one the
        // model cannot spend tokens on however the prompt is worded. That is what makes phase one
        // fast enough to be worth doing on its own - measured at 42s against 158s for the
        // single-call ask.
        var handler = new StreamingHandler(
            [Chunk("{\"classes\":[{\"name\":\"Product\",\"role\":\"Item\",\"purpose\":\"a thing\"}]}"), Last()]);

        var answer = Client(handler).AskForClasses("a shop");

        Assert.True(answer.Succeeded);
        Assert.Equal("Product", Assert.Single(answer.Model!.Classes).Name);

        var body = handler.LastRequestBody!;

        // The SCHEMA, not the whole body: the prompt legitimately uses the words "properties" and
        // "relationships" to tell the model not to write any, and asserting over the whole request
        // matches that instruction instead of the thing under test.
        using var request = System.Text.Json.JsonDocument.Parse(body);

        var fields = request.RootElement
            .GetProperty("format").GetProperty("properties").GetProperty("classes")
            .GetProperty("items").GetProperty("properties")
            .EnumerateObject().Select(f => f.Name).ToList();

        Assert.Equal(["name", "role", "purpose"], fields);
        Assert.Contains("\"num_predict\":1024", body);
    }

    [Fact]
    public void TheDetailAskCarriesTheClassesAlreadyChosen()
    {
        var handler = new StreamingHandler([Chunk("{\"classes\":[" + ProductClass + "]}"), Last()]);

        var roster = new LlmDomainModel();
        roster.Classes.Add(new LlmClass { Name = "Product", Role = "Item" });
        roster.Classes.Add(new LlmClass { Name = "Sale", Role = "Transaction" });

        Client(handler).AskForDetail("a shop", roster);

        var body = handler.LastRequestBody!;

        // Both names, so the second pass returns the list it was given rather than inventing one,
        // and the description too - a class called Sale means different things in a shop and in a
        // securities system, and this is the pass choosing its fields.
        Assert.Contains("Product", body);
        Assert.Contains("Sale", body);
        Assert.Contains("a shop", body);
    }

    [Fact]
    public void CapsTheResponseToFitTheSmallestWindowARunnerMayAlreadyHave()
    {
        // Ollama starts a CPU runner at 4096 by default, and reuses a loaded one. With
        // --context-shift on (it is), a generation that overruns the window does not stop - it
        // discards the start of its own answer and generates forever. num_predict plus the prompt
        // must therefore fit inside 4096, not merely inside the num_ctx we ask for.
        var handler = new StreamingHandler([Chunk("{\"classes\":[" + ProductClass + "]}"), Last()]);

        Client(handler).Ask("a shop");

        var body = handler.LastRequestBody!;
        var predict = int.Parse(
            System.Text.RegularExpressions.Regex.Match(body, "\"num_predict\":(\\d+)").Groups[1].Value);

        Assert.True(predict <= 3072, $"num_predict {predict} leaves no room for the prompt in a 4096 window");
    }
}
