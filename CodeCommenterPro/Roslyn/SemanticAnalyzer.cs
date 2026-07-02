using Microsoft.CodeAnalysis;

namespace CodeCommenterPro.Roslyn;

public class SemanticAnalyzer
{
    private readonly SemanticModel _model;

    public SemanticAnalyzer(SemanticModel model)
    {
        _model = model;
    }

    public string GetReturnTypeDescription(IMethodSymbol method)
    {
        return method.ReturnType.Name switch
        {
            "Task" => "Asynchronous operation.",
            "String" => "String value.",
            "Boolean" => "True if successful; otherwise false.",
            _ => method.ReturnType.Name
        };
    }
}