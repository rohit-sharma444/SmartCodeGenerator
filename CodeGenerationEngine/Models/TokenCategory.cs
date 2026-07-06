namespace CodeGenerationEngine.Models;

public enum TokenCategory
{
    Unknown = 0,
    Action,
    Artifact,
    ClassName,
    MethodName,
    PropertyName,
    ProjectName,
    DataType,
    Identifier,
    StopWord
}