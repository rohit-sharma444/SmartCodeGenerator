# CodeGenerator.Tests

Test suite for the code-generation stack — [`CodeGenerationEngine`](../CodeGenerationEngine/README.md)
and [`NaturalLanguageBridge`](../NaturalLanguageBridge/README.md). Uses **MSTest**
(`net10.0`).

## What's covered

| Folder | Focus |
| --- | --- |
| `Core/` | Intent analysis and NLP-driven parsing, entity extraction, the learning engine (`LearnedIntentMemory`/`Store`, `LearningCoordinator`), end-to-end pipeline runs, and workspace/save reproductions of past bugs. |
| `Bridge/` | The `NaturalLanguagePipeline` and `NlpActionProvider` glue between NLE and the code engine. |

## Running

```bash
# from the repository root

# run this suite
dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj

# a single class
dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj \
  --filter "FullyQualifiedName~PipelineEndToEndTests"
```

Or run the whole solution: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx`.
