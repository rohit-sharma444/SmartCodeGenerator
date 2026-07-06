using NaturalLanguageEngine.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace NaturalLanguageEngine.Abstractions;

public interface IAnalysisObserver
{
    void BeforeAnalyzer(IAnalyzer analyzer, AnalysisContext context);

    void AfterAnalyzer(IAnalyzer analyzer, AnalysisContext context);
}
