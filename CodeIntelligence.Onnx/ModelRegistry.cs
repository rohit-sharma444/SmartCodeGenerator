using System.Security.Cryptography;

namespace CodeIntelligence.Onnx;

public sealed record EmbeddingModelInfo(
    string ModelId, string ModelPath, string VocabPath, int Dimension, string ExpectedSha256);

/// <summary>Resolves model files relative to the running binary and identifies the
/// weights. The id ends up in the index: vectors from different models are not
/// comparable, so changing the model must invalidate the index rather than silently
/// return nonsense neighbours.</summary>
public static class ModelRegistry
{
    private const string FolderName = "all-MiniLM-L6-v2-int8";

    // sha256 of the model_quantized.onnx fetched from Xenova/all-MiniLM-L6-v2 on
    // 2026-08-07 (22,972,370 bytes). Empty disables the check.
    private const string ExpectedSha256 =
        "AFDB6F1A0E45B715D0BB9B11772F032C399BABD23BFC31FED1C170AFC848BDB1";

    public static EmbeddingModelInfo DefaultEmbeddingModel
    {
        get
        {
            var root = Path.Combine(AppContext.BaseDirectory, "Models", FolderName);

            return new EmbeddingModelInfo(
                ModelId: FolderName,
                ModelPath: Path.Combine(root, "model.onnx"),
                VocabPath: Path.Combine(root, "vocab.txt"),
                Dimension: 384,
                ExpectedSha256: ExpectedSha256);
        }
    }

    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>Null when usable, otherwise the reason — reported once by `ai status`,
    /// never per command.</summary>
    public static string? Validate(EmbeddingModelInfo model)
    {
        if (!File.Exists(model.ModelPath))
            return $"model file not found: {model.ModelPath}";

        if (!File.Exists(model.VocabPath))
            return $"vocab file not found: {model.VocabPath}";

        if (model.ExpectedSha256.Length > 0 &&
            !string.Equals(Sha256(model.ModelPath), model.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            return "model checksum mismatch - the file is corrupt or was replaced";

        return null;
    }
}
