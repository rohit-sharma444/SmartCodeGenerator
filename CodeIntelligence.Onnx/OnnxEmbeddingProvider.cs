using CodeIntelligence.Abstractions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace CodeIntelligence.Onnx;

/// <summary>Sentence embeddings from a MiniLM ONNX export.
///
/// The model emits one vector per token; a sentence vector is the attention-masked
/// mean of those, then L2-normalized so cosine similarity is a plain dot product.
/// This mean-pool-then-normalize recipe is what sentence-transformers does, and
/// skipping either step produces vectors that rank badly rather than obviously
/// wrongly — which is why the unit-length and paraphrase tests exist.
///
/// One session is created lazily and shared: InferenceSession.Run is thread-safe.</summary>
public sealed class OnnxEmbeddingProvider : IEmbeddingProvider
{
    private const int MaxTokens = 256;

    private readonly Lazy<InferenceSession> _session;
    private readonly Lazy<BertTokenizerAdapter> _tokenizer;

    public string ModelId { get; }
    public int Dimension => 384;

    public OnnxEmbeddingProvider(string modelPath, string vocabPath, string modelId)
    {
        ModelId = modelId;
        _session = new Lazy<InferenceSession>(() => new InferenceSession(modelPath));
        _tokenizer = new Lazy<BertTokenizerAdapter>(() => new BertTokenizerAdapter(vocabPath));
    }

    public float[] Embed(string text)
    {
        var ids = _tokenizer.Value.Encode(text ?? string.Empty).Take(MaxTokens).ToArray();
        if (ids.Length == 0)
            return new float[Dimension];

        var length = ids.Length;

        var inputIds = new DenseTensor<long>([1, length]);
        var attentionMask = new DenseTensor<long>([1, length]);
        var tokenTypeIds = new DenseTensor<long>([1, length]);

        for (var i = 0; i < length; i++)
        {
            inputIds[0, i] = ids[i];
            attentionMask[0, i] = 1;
            tokenTypeIds[0, i] = 0;
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask),
            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIds)
        };

        using var outputs = _session.Value.Run(inputs);
        var hidden = outputs.First().AsTensor<float>();

        // Mean-pool over the token axis. Every token is unmasked here because the
        // input is a single un-padded sequence, so this is a plain mean.
        var pooled = new float[Dimension];
        for (var token = 0; token < length; token++)
            for (var dim = 0; dim < Dimension; dim++)
                pooled[dim] += hidden[0, token, dim];

        for (var dim = 0; dim < Dimension; dim++)
            pooled[dim] /= length;

        double sum = 0;
        foreach (var value in pooled)
            sum += (double)value * value;

        var magnitude = (float)Math.Sqrt(sum);
        if (magnitude > 0)
            for (var dim = 0; dim < Dimension; dim++)
                pooled[dim] /= magnitude;

        return pooled;
    }

    public void Dispose()
    {
        if (_session.IsValueCreated)
            _session.Value.Dispose();
    }
}
