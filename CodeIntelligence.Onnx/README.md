# CodeIntelligence.Onnx

Turns text into a vector with a local ONNX model. A class library (`net10.0`) that implements
`IEmbeddingProvider` and `ITokenizer` from
[`CodeIntelligence.Abstractions`](../CodeIntelligence.Abstractions/README.md). It uses
`Microsoft.ML.OnnxRuntime` and `Microsoft.ML.Tokenizers`. Nothing leaves the machine.

## Types

| Type | Role |
| --- | --- |
| `OnnxEmbeddingProvider` | `new OnnxEmbeddingProvider(modelPath, vocabPath, modelId)`. `Embed(text)` returns a 384-value vector (`Dimension`). |
| `BertTokenizerAdapter` | Wraps the BERT tokenizer over `vocab.txt` as an `ITokenizer`. |
| `ModelRegistry` | `DefaultEmbeddingModel` (an `EmbeddingModelInfo`), `Validate(model)` and `Sha256(path)`. |
| `EmbeddingModelInfo` | `ModelId`, `ModelPath`, `VocabPath`, `Dimension`, `ExpectedSha256`. |

## The model

`Models/all-MiniLM-L6-v2-int8/` holds `model.onnx` and `vocab.txt` (a quantized
all-MiniLM-L6-v2). The project file copies `Models\**` to the output folder, and `ModelRegistry`
looks for it under `AppContext.BaseDirectory`. `Validate` returns a message, not an exception, when
a file is missing or the SHA-256 of `model.onnx` does not match, so `AiLayer` can report it and
fall back to "no AI".

## Who uses it

Only [`CodeIntelligence`](../CodeIntelligence/README.md) (`AiLayer`), which builds the provider from
`ModelRegistry.DefaultEmbeddingModel`. A native load failure (wrong architecture, missing Visual C++
runtime) surfaces there as `AiLayer.Unavailable`.
