using NaturalLanguageEngine.Models.Diagnostics;
using System;
using System.Collections.Generic;
using System.Text;

namespace NaturalLanguageEngine.Models.Common;

public sealed class Diagnostic
{
    public DiagnosticSeverity Severity { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}
