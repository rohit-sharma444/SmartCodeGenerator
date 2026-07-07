# NaturalLanguageEngine.Tests

Test suite for the [`NaturalLanguageEngine`](../NaturalLanguageEngine/README.md).
Uses **xUnit** (`net10.0`).

## Organization

Tests mirror the engine's layers:

| Folder | Focus |
| --- | --- |
| `Core/` | Engine + pipeline integration, `EngineBuilder`/`EngineBuildResult`, prompt store. |
| `Lexical/` | Tokenization and sentence splitting. |
| `Morphology/` | Normalization, lemmatization, stemming. |
| `Syntax/` | POS tagging, phrase detection, dependency parsing. |
| `Semantics/` | Entity recognition, intent, relationship extraction, command-verb lexicon. |
| `Knowledge/` | The English `LanguageRepository`. |
| `TestSupport/` | `EngineTestFactory` and shared fixtures. |

## Running

```bash
# from the repository root

# run this suite
dotnet test NaturalLanguageEngine.Tests/NaturalLanguageEngine.Tests.csproj

# a single class
dotnet test NaturalLanguageEngine.Tests/NaturalLanguageEngine.Tests.csproj \
  --filter "FullyQualifiedName~PosTaggerTests"
```

Or run the whole solution: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx`.
