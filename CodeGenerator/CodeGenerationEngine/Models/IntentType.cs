namespace CodeGenerationEngine.Models;

public enum IntentType
{
    Unknown,
    Help,

    // Project
    CreateProject,
    UseProject,
    ListProjects,
    CurrentProject,

    // Class
    CreateClass,
    ShowClass,
    RenameClass,
    DeleteClass,
    RestoreClass,
    RestoreLastClass,
    ListDeletedClasses,
    ClassInherit,

    // Property
    CreateProperty,
    ListProperties,
    DeleteProperty,
    RenameProperty,

    // Method
    CreateMethod,
    ListMethods,
    DeleteMethod,
    RenameMethod,

    // Validation
    AddValidation,
    RemoveValidation,
    GenerateValidator,

    // Architecture
    CreateInterface,
    CreateService,
    CreateRepository,
    CreateController,
    CreateApi,

    // Analysis
    AnalyzeClass,
    AnalyzeProject,
    FindUnusedClasses,
    FindUnusedMethods,
    FindCircularDependencies,

    // Refactoring
    GenerateConstructor,
    GenerateToString,
    GenerateEquals,
    GenerateGetHashCode,
    GenerateCrud,

    // Documentation
    GenerateDocumentation,
    GenerateComments,

    // Navigation
    ListClasses,
    ListClassesInProject,
    ShowProject,

    // Misc
    CompileProject,
    ValidateProject,

    // Learning
    ShowLearning,
    ResetLearning,
    GenerateDesignDocument,
    GenerateRepository
}