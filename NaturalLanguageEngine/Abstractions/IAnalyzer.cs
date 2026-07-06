using NaturalLanguageEngine.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace NaturalLanguageEngine.Abstractions;

public interface IAnalyzer
{
    string Name { get; }

    int Order { get; }

    void Analyze(AnalysisContext context);
}