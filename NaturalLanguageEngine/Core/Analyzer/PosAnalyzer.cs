using NaturalLanguageEngine.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace NaturalLanguageEngine.Core.Analyzer
{
    public sealed class PosAnalyzer : IAnalyzer
    {
        public string Name => "Part Of Speech";

        public int Order => 300;

        public void Analyze(AnalysisContext context)
        {
        }
    }
}
