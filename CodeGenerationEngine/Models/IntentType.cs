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
    CreateEnum,
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
    GenerateRepository,

    // Appended at the end to keep existing integer values stable (intents are
    // persisted as integers by the learned-intent store).
    ShowValidation,

    // Business validation (entity-level). Appended last to keep persisted
    // integer values stable.
    AddBusinessValidation,
    ShowBusinessValidation,

    // `change type of <property> to <type> in <class>`. Appended last to keep persisted
    // integer values stable.
    ChangePropertyType,

    // `remove business validation <n> from <class>`; the data-rule form is RemoveValidation.
    RemoveBusinessValidation
}