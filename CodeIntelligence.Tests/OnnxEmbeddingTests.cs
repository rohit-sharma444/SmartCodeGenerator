using CodeIntelligence.Onnx;
using CodeIntelligence.Retrieval;
using Xunit;

namespace CodeIntelligence.Tests;

public sealed class OnnxEmbeddingTests
{
    private static OnnxEmbeddingProvider Create()
    {
        var model = ModelRegistry.DefaultEmbeddingModel;
        return new OnnxEmbeddingProvider(model.ModelPath, model.VocabPath, model.ModelId);
    }

    [Fact]
    public void Model_files_are_present_in_the_output()
    {
        var model = ModelRegistry.DefaultEmbeddingModel;

        Assert.True(File.Exists(model.ModelPath), $"missing {model.ModelPath}");
        Assert.True(File.Exists(model.VocabPath), $"missing {model.VocabPath}");
    }

    [Fact]
    public void Embedding_has_the_declared_dimension()
    {
        using var provider = Create();
        Assert.Equal(384, provider.Embed("list classes").Length);
    }

    [Fact]
    public void Embedding_is_unit_length()
    {
        using var provider = Create();
        var vector = provider.Embed("list classes");

        var magnitude = Math.Sqrt(vector.Sum(v => (double)v * v));
        Assert.Equal(1.0, magnitude, 3);
    }

    [Fact]
    public void Embedding_is_deterministic()
    {
        using var provider = Create();
        Assert.Equal(provider.Embed("list classes"), provider.Embed("list classes"));
    }

    [Fact]
    public void Paraphrases_score_higher_than_unrelated_text()
    {
        using var provider = Create();

        var anchor = provider.Embed("list the classes in the project");
        var near = provider.Embed("show me all classes");
        var far = provider.Embed("the weather in Paris is cold");

        var nearScore = Similarity.Cosine(anchor, near);
        var farScore = Similarity.Cosine(anchor, far);

        Assert.True(nearScore > farScore,
            $"a paraphrase must be nearer than unrelated text (near={nearScore:F4} far={farScore:F4})");

        // `>` alone is too weak to catch the failure this test exists for. Broken mean
        // pooling collapses every sentence toward one vector, and collapsed embeddings
        // still satisfy `>` by a rounding error. Measured on the committed weights:
        // near=0.6820, far=0.0346. A 0.25 gap is far below that and far above noise, so
        // it fails loudly on collapse without being brittle to a model refresh.
        Assert.True(nearScore - farScore > 0.25,
            $"separation collapsed: near={nearScore:F4} far={farScore:F4}");
    }

    [Fact]
    public void The_committed_model_matches_its_recorded_checksum()
    {
        // Guards the LFS round-trip as much as the bytes: if the *.onnx filter is ever
        // dropped from .gitattributes, `* text=auto` mangles the binary on checkout and
        // this is what says so, instead of a confusing ONNX parse error at runtime.
        Assert.Null(ModelRegistry.Validate(ModelRegistry.DefaultEmbeddingModel));
    }
}
