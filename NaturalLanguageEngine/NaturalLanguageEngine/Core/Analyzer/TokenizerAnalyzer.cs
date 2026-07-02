using NaturalLanguageEngine.Abstractions;
using NaturalLanguageEngine.Abstractions.Lexical;
using System;
using System.Collections.Generic;
using System.Text;

namespace NaturalLanguageEngine.Core.Analyzer;

public sealed class TokenizerAnalyzer : IAnalyzer
{
    private readonly ITokenizer _tokenizer;

    public TokenizerAnalyzer(ITokenizer tokenizer)
    {
        _tokenizer = tokenizer;
    }

    public string Name => "Tokenizer";

    public int Order => 100;

    public void Analyze(AnalysisContext context)
    {
        context.Tokens = _tokenizer.Tokenize(context.NormalizedText);
    }
}
