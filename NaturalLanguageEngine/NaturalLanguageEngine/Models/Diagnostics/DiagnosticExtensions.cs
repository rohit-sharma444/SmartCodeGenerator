using NaturalLanguageEngine.Models.Diagnostics;

namespace NaturalLanguageEngine.Core.Diagnostics;

public static class DiagnosticExtensions
{
    public static void AddDiagnostic(
        this AnalysisContext context,
        string code,
        DiagnosticSeverity severity,
        string message,
        int? position = null,
        int? length = null)
    {
        context.Diagnostics.Add(new Diagnostic
        {
            Code = code,
            Severity = severity,
            Message = message,
            Position = position,
            Length = length
        });
    }
}