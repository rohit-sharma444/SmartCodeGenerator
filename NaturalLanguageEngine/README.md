# Natural Language Engine

A multi-layered natural language processing pipeline that analyzes English text to extract entities, relationships, and intent.

## 🏗️ Architecture

The engine processes text through seven sequential stages:

1. **Normalization** (Order: 10) - Standardizes text (whitespace, case handling)
2. **Tokenization** (Order: 20) - Breaks text into sentences and tokens
3. **Morphology** (Order: 30) - Lemmatization and stemming
4. **Part-of-Speech Tagging** (Order: 35) - Identifies word types (noun, verb, etc.)
5. **Phrase Detection** (Order: 40) - Identifies noun/verb phrases and their heads
6. **Dependency Parsing** (Order: 45) - Extracts syntactic relationships between words
7. **Semantic Analysis** (Order: 50) - Recognizes entities, derives intent, and extracts relationships

Each stage is isolated in its own layer and communicates via a shared `AnalysisContext`.

## 🚀 Quick Start

### Basic Usage

````````


**Output:**
````````


# Response
````````markdown

````````

### With Configuration Options

````````


**Output:**
````````


# Response
````````markdown

````````

### With Custom Analyzer

````````


# Response
````````markdown

````````

## 📊 Features

### Entity Recognition
- **Person**: Proper nouns and capitalized names
- **Organization**: Company names, institutions
- **Location**: Cities, countries, geographic locations
- **Date**: ISO format dates (2025-06-30), natural language dates
- **Time**: HH:MM format times (14:30), time ranges
- **Quantity**: Numbers with optional units
- **Value**: Email addresses, URLs
- **Action**: Verb phrases that indicate actions
- **Object**: Non-person, non-location entities

### Intent Derivation
- Extracts the first action verb and derives intent (e.g., "book" → "BookIntent")
- Falls back to "DescribeIntent" for noun-only sentences
- Confidence blended from action confidence

### Relationship Extraction
- **Subject**: The actor performing the action
- **Object**: The target or recipient of the action
- **Location**: Where the action occurs
- **Time**: When the action occurs
- **Modifier**: Descriptive relationships

Prefers syntactic dependency parsing when available; falls back to positional heuristics for edge cases.

### Diagnostics
- Comprehensive error and warning reporting
- Critical errors stop the pipeline gracefully
- Warnings preserve partial results (e.g., "NoEntities", "SyntacticRelationships")
- Diagnostic codes follow the pattern: `NLE.<Layer>.<Issue>`

## 🔧 Configuration

### EngineBuilderOptions

````````

## 📈 Performance

Typical performance on a modern system:

| Metric | Value |
|--------|-------|
| Simple sentence | 5-15 ms |
| Complex sentence (3+ entities) | 15-30 ms |
| Dependency parsing stage | ~70% of total time |
| Memory (base) | ~5 MB |

### Optimization Tips

1. **Enable Caching** - For batch processing of similar texts
2. **Use CachedSemanticAnalyzer** - Direct caching wrapper
3. **Enable Logging** - Monitor bottlenecks

## 🧪 Testing

Run all tests:
````````


# Response
````````markdown

````````

Run specific test class:
````````


# Response
````````markdown

````````

Run with verbosity:
````````


# Response
````````markdown

````````

### Test Organization

- **Core Tests** - Pipeline, engine integration
- **Lexical Tests** - Tokenization, sentence splitting
- **Morphology Tests** - Lemmatization, stemming
- **Syntax Tests** - POS tagging, phrase detection, dependency parsing
- **Semantics Tests** - Entity recognition, intent, relationships

## ⚠️ Known Limitations

1. **Passive Voice** - Relationships may be reversed
2. **Coordinated Subjects/Objects** - Limited support
3. **Negation** - Not explicitly handled
4. **Questions** - Basic support only
5. **Language** - English only
   - Uses English morphology rules, POS tagset, and knowledge base
   - Not suitable for multilingual input

6. **Single Verb Anchor** - Only first action verb drives intent

## 🔌 Extending the Engine

### Adding a Custom Stage

````````

### Using a Different Knowledge Base

````````


# Response
````````markdown

````````

## 📚 API Reference

### SemanticResult

````````

### Entity

````````

### Relationship

````````

### Intent

````````


# Response
````````markdown

````````

## 📋 License & Attribution

See repository root for license information.

## 🤝 Contributing

Contributions are welcome! Areas for improvement:

- [ ] Better handling of passive voice
- [ ] Support for coordinated verbs
- [ ] Explicit negation handling
- [ ] Extended entity types (Product, Event, etc.)
- [ ] Multi-language support
- [ ] Performance optimizations

---

**Last Updated**: 2026-07-02  
**Target Framework**: .NET 10.0
