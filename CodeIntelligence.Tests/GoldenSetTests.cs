using CodeGenerationEngine.Core;
using CodeGenerationEngine.Models;
using CodeIntelligence.Abstractions;
using CodeIntelligence.Corpus;
using CodeIntelligence.Onnx;
using CodeIntelligence.Resolution;
using CodeIntelligence.Storage;
using Xunit;
using Xunit.Abstractions;

namespace CodeIntelligence.Tests;

public sealed class GoldenSetTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ci-golden-" + Guid.NewGuid().ToString("N"));

    private OnnxEmbeddingProvider? _embedder;
    private SqliteVectorStore? _store;

    public GoldenSetTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        _embedder?.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static IReadOnlyList<(string Text, IntentType Expected)> GoldenSet()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-set.tsv");
        Assert.True(File.Exists(path), $"golden set not copied to output: {path}");

        return File.ReadAllLines(path)
            .Where(line => line.Trim().Length > 0)
            .Select(line => line.Split('\t'))
            .Select((parts, i) =>
            {
                Assert.True(parts.Length == 2,
                    $"golden-set.tsv line {i + 1} is not two tab-separated fields: '{string.Join("|", parts)}'");

                var name = parts[1].Trim();
                Assert.True(Enum.TryParse<IntentType>(name, out var intent),
                    $"golden-set.tsv line {i + 1} names an intent that does not exist: '{name}'");

                return (parts[0].Trim(), intent);
            })
            .ToList();
    }

    /// <summary>Index built ONCE and shared across the sweep. Neither threshold affects
    /// the embeddings, so rebuilding per candidate value would re-embed all 171 corpus
    /// documents for nothing — and the sweep is two-dimensional now.</summary>
    private RetrievalIntentResolver Resolver(double floor, double minTopScore, int topK = 5)
    {
        if (_store is null)
        {
            IntentRepository.Initialize();

            var model = ModelRegistry.DefaultEmbeddingModel;
            _embedder = new OnnxEmbeddingProvider(model.ModelPath, model.VocabPath, model.ModelId);
            _store = new SqliteVectorStore(Path.Combine(_dir, "golden.db"));

            var documents = CorpusHarvester.Harvest();
            _store.ReplaceAll(_embedder.ModelId, _embedder.Dimension,
                [.. documents.Select(d => (d, _embedder.Embed(d.Text)))]);
        }

        return new RetrievalIntentResolver(_store, _embedder!, topK: topK, floor: floor,
            minTopScore: minTopScore, budget: TimeSpan.FromSeconds(30));
    }

    private (int Correct, int WrongConfident, int Declined) Measure(
        double floor, double minTopScore, int topK = 5)
    {
        var golden = GoldenSet();
        var resolver = Resolver(floor, minTopScore, topK);

        int correct = 0, wrongConfident = 0, declined = 0;

        foreach (var (text, expected) in golden)
        {
            var result = resolver.Resolve(text, CancellationToken.None);

            if (result is null) declined++;
            else if (result.Intent == expected) correct++;
            else wrongConfident++;
        }

        return (correct, wrongConfident, declined);
    }

    [Fact]
    public void The_golden_set_is_not_drawn_from_the_corpus()
    {
        // A golden set that overlaps the corpus measures nothing — retrieval would be
        // scoring exact matches of its own index. The plan says these must be
        // paraphrases; this makes that executable rather than a promise.
        IntentRepository.Initialize();

        var corpus = CorpusHarvester.Harvest()
            .Select(d => d.Text.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var borrowed = GoldenSet()
            .Where(g => corpus.Contains(g.Text.Trim()))
            .Select(g => g.Text)
            .ToList();

        Assert.Empty(borrowed);
    }

    [Fact]
    public void The_golden_set_is_large_enough_and_spread_across_intents()
    {
        var golden = GoldenSet();

        Assert.True(golden.Count >= 60, $"golden set has only {golden.Count} entries");
        Assert.True(golden.Select(g => g.Expected).Distinct().Count() >= 20,
            "too few distinct intents to say anything about accuracy");
    }

    [Fact]
    public void Report_accuracy_across_candidate_thresholds()
    {
        var golden = GoldenSet();
        _output.WriteLine($"golden set: {golden.Count} phrasings, " +
                          $"{golden.Select(g => g.Expected).Distinct().Count()} distinct intents");

        // topK is swept alongside the two gates because the corpus has wildly uneven
        // per-intent document counts (ListProperties has 2 phrasings, CreateProperty
        // has many). Under a score-weighted vote, a document-rich intent can outvote a
        // sparse one whose single nearest neighbour was the correct answer — so topK,
        // not the gates, may be the dominant parameter here.
        foreach (var topK in new[] { 1, 2, 3, 5 })
        {
            foreach (var minTopScore in new[] { 0.0, 0.35, 0.45, 0.55 })
            {
                foreach (var floor in new[] { 0.0, 0.5, 0.55, 0.6 })
                {
                    var (correct, wrongConfident, declined) = Measure(floor, minTopScore, topK);
                    var offered = correct + wrongConfident;

                    _output.WriteLine(
                        $"topK {topK}  minTopScore {minTopScore:0.00}  floor {floor:0.00}: " +
                        $"correct {correct}/{golden.Count}, wrong {wrongConfident}, " +
                        $"declined {declined}, precision " +
                        $"{(offered == 0 ? "n/a" : $"{(double)correct / offered:P0}")}");
                }
            }
        }
    }

    [Fact]
    public void Report_the_misses_at_the_shipped_thresholds()
    {
        // Names the failures so a regression is diagnosable from the log rather than
        // just a count going the wrong way.
        var resolver = Resolver(
            AiConfig.DefaultConfidenceFloor, AiConfig.DefaultMinTopScore, AiConfig.DefaultTopK);

        foreach (var (text, expected) in GoldenSet())
        {
            var result = resolver.Resolve(text, CancellationToken.None);

            if (result is null)
                _output.WriteLine($"DECLINED  {text}  (wanted {expected})");
            else if (result.Intent != expected)
                _output.WriteLine(
                    $"WRONG     {text}  (wanted {expected}, got {result.Intent} " +
                    $"at {result.Confidence:P0}, closest \"{result.Evidence[0].Text}\")");
        }
    }

    /// <summary>
    /// The learned phrasings make the shipped score no worse than it is without them.
    /// </summary>
    /// <remarks>
    /// The gate every learned phrase is committed through. A phrasing learned from one user's
    /// retry is evidence for THAT phrasing; this is the check that it does not pull a nearby
    /// golden paraphrase over to the wrong intent, or split a vote that used to succeed.
    /// Checked line by line as well as in total: a batch that wins one golden answer and loses
    /// another scores level, and a vote split into a decline adds no wrong answer either -
    /// "what project am I on" did exactly that to "what projects do i have".
    /// The_golden_set_is_not_drawn_from_the_corpus separately refuses a learned line that
    /// simply copies a golden phrasing, which would raise this score by memorising the test.
    /// </remarks>
    [Fact]
    public void The_learned_phrases_do_not_lower_the_score()
    {
        IntentRepository.Initialize();

        var everything = CorpusHarvester.Harvest();
        var withoutLearned = everything.Where(d => d.SourceKind != CorpusSourceKind.Learned).ToList();

        var with = MeasureOver(everything, "with");
        var without = MeasureOver(withoutLearned, "without");

        _output.WriteLine($"learned phrasings: {everything.Count - withoutLearned.Count}");
        _output.WriteLine($"without: correct {without.Correct}, wrong {without.WrongConfident}");
        _output.WriteLine($"with:    correct {with.Correct}, wrong {with.WrongConfident}");

        var lost = without.CorrectTexts.Except(with.CorrectTexts).ToList();
        Assert.True(lost.Count == 0,
            $"the learned phrasings cost golden answer(s) that were right without them: {string.Join("; ", lost)}");
        Assert.True(with.Correct >= without.Correct,
            $"the learned phrasings cost {without.Correct - with.Correct} correct answer(s)");
        Assert.True(with.WrongConfident <= without.WrongConfident,
            $"the learned phrasings added {with.WrongConfident - without.WrongConfident} wrong answer(s)");
    }

    /// <summary>The shipped-threshold score of a resolver over exactly <paramref name="documents"/>,
    /// in an index of its own so the shared one the sweep uses is left alone.</summary>
    private (int Correct, int WrongConfident, IReadOnlyList<string> CorrectTexts) MeasureOver(IReadOnlyList<CorpusDocument> documents, string name)
    {
        var model = ModelRegistry.DefaultEmbeddingModel;
        _embedder ??= new OnnxEmbeddingProvider(model.ModelPath, model.VocabPath, model.ModelId);

        var store = new SqliteVectorStore(Path.Combine(_dir, $"golden-{name}.db"));
        store.ReplaceAll(_embedder.ModelId, _embedder.Dimension,
            [.. documents.Select(d => (d, _embedder.Embed(d.Text)))]);

        var resolver = new RetrievalIntentResolver(store, _embedder, topK: AiConfig.DefaultTopK,
            floor: AiConfig.DefaultConfidenceFloor, minTopScore: AiConfig.DefaultMinTopScore,
            budget: TimeSpan.FromSeconds(30));

        var correct = new List<string>();
        var wrong = 0;

        foreach (var (text, expected) in GoldenSet())
        {
            var result = resolver.Resolve(text, CancellationToken.None);

            if (result is null) continue;
            if (result.Intent == expected) correct.Add(text);
            else wrong++;
        }

        return (correct.Count, wrong, correct);
    }

    [Fact]
    public void Accuracy_does_not_regress_below_the_calibrated_thresholds()
    {
        // Measured at the shipped defaults (topK 2, minTopScore 0.55, floor 0.55):
        // 17 correct, 1 wrong-and-confident, 51 declined out of 69 — 94% precision on
        // the quarter of inputs it answers at all. Gate set to measured-minus-2 and
        // measured-plus-1, per the plan. A regression gate, not a target: the point is
        // to notice the day a corpus or model change makes this worse.
        const int minimumCorrect = 15;
        const int maximumWrongConfident = 2;

        var (correct, wrongConfident, _) = Measure(
            AiConfig.DefaultConfidenceFloor, AiConfig.DefaultMinTopScore, AiConfig.DefaultTopK);

        Assert.True(correct >= minimumCorrect,
            $"top-1 correct fell to {correct}, below the calibrated {minimumCorrect}");
        Assert.True(wrongConfident <= maximumWrongConfident,
            $"wrong-and-confident rose to {wrongConfident}, above the calibrated {maximumWrongConfident}");
    }
}
