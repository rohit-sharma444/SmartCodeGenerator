using NaturalLanguageEngine.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace NaturalLanguageEngine.Core;

public sealed class AnalyzerPipeline
{
    private readonly IReadOnlyList<IAnalyzer> _analyzers;

    public AnalyzerPipeline(IEnumerable<IAnalyzer> analyzers)
    {
        _analyzers = analyzers.ToList();
    }

    public AnalysisContext Execute(string text)
    {
        var context = new AnalysisContext(text);

        foreach (var analyzer in _analyzers)
            analyzer.Analyze(context);

        return context;
    }
}
