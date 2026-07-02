using Microsoft.CodeAnalysis;

namespace CodeGenerationEngine.Models;

public class CompilationResult
{
    public bool Success { get; set; }

    public byte[] AssemblyBytes { get; set; }

    public List<Diagnostic> Diagnostics { get; set; }
        = [];
}