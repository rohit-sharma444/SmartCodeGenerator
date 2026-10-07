Overview

CodeGenerationEngine is the library that executes parsed commands against a workspace of C# projects. It creates, changes, analyzes and checks classes, interfaces, properties, methods, validation rules and relationships on disk, and it uses Roslyn (Microsoft.CodeAnalysis 5.3.0) to build and check the code it writes.

It does not read raw text. The NaturalLanguageBridge turns a sentence into a ParsedCommand (an IntentType plus fields), and this library runs it. The CodeGenerator console application and the Blazor chat page are the callers.

Target framework: .NET 10.0 (class library, no entry point). The only package references are Microsoft.CodeAnalysis.Common and Microsoft.CodeAnalysis.CSharp.

Command flow

1. A caller (normally the bridge, through CommandFront.RunDetailed) builds a ParsedCommand.
2. CommandHandlingService.Execute(command, ref currentProject, configuredRules, workspaceRoot, setLastCreatedProject) looks up command.Intent in its handler table and runs the handler.
3. The handler uses the services in Core/ (generate, edit, delete, validate) and writes files.
4. Execute passes the outcome to LearningCoordinator.RecordOutcome, then returns true or false.
5. CommandHandlingService.IsSupported(intent) tells a caller whether an intent has a handler. An intent with no handler (for example GenerateCrud) fails with a report.

Folders

Core/ - the services and generators.
- Parsing helpers: CommandParser, CommandNormalizer, CommandDictionary, EntityExtractor, ClassExtractor, NamespaceExtractor, RuleParser, SentenceFramer (in Helper/).
- Generators: CodeGenerator, RoslynClassGenerator, InterfaceGenerator, MethodBodyGenerator, CodeFormatter, DesignDocumentGenerator.
- Class services: ClassDeletionService, ClassRestoreService (with DeletedClassRegistry and DeletedClassListingService), ClassRenameService, ClassInheritanceService, ClassMemberEditor, ClassPropertyService, ClassPropertyRenameService, ClassPropertyTypeService, ClassMethodService, ClassMethodEditService, ClassMethodLister, MemberRemoval, MemberReferences, StaleValidators.
- Project services: ProjectManager (the registry of projects and the current one), ProjectLocator, ProjectCompiler, CsProjUpdater, DependencyGraphBuilder, CircularDependencyDetector, AnalyzeClassCommand, AnalyzeProjectCommand.
- Checks: ClassValidator, ClassNameValidator, PropertyValidator, MethodValidator, TypeValidator, NamespaceValidator, DuplicateClassDetector, DuplicatePropertyValidator, SolutionDuplicateValidator, GenericTypeValidator, SemanticValidator, SemanticCompilerRunner, Compiler (Compile for one source, CompileSources for a set).
- Learning: LearningCoordinator, LearnedIntentStore, LearnedIntentMemory, PhraseCandidates. IntentRepository holds the default intent rules; the recognition path in the bridge does not read it.
- Validation/ - the validation rule registry: IValidationRule, IValidationRuleParser, ValidationRuleCatalog, ValidationCategory (Data, Business, Workflow), 20 rule files in Validation/Rules/, ClassValidationWriter, ValidationRuleFile. ValidationRegistry and ValidationEngine are in Core/.
- Relationships/ - RelationshipDetector, RelationshipConvention, RelationshipWriter, JoinClassWriter, AttributedPropertyWriter, ClassSnapshot, RelationshipKind, RelationshipProposal.
- Undo/ - UndoJournal (capacity 50), UndoScope, SessionState, SessionRecorder. WorkspaceSnapshot (in Core/) is the file copy it restores from.

Models/ - ParsedCommand, IntentType, ClassDefinition, PropertyDefinition, MethodDefinition, ProjectMetaData, PropertyRule, SessionContext, and the semantic and result types.
Service/ - CommandHandlingService and GeneratedCodeService.
Factory/ - DotNetProjectFactory.CreateProject scaffolds a project from a ProjectRequest.
Rules/ - IPropertyRule and DefaultTypeRules.
Helper/ - FileHelper, FileReader, PathHelper, ProcessRunner, Output, OutputCapture, AnsiCodes, CloseNames, CommandHistoryFile and similar.

Handled intents

Projects: CreateProject, UseProject, ListProjects, ShowProject, CurrentProject.
Classes: CreateClass, ShowClass, RenameClass, DeleteClass, RestoreClass, RestoreLastClass, ListDeletedClasses, ClassInherit, ListClasses, ListClassesInProject.
Interfaces: CreateInterface.
Properties: CreateProperty, ListProperties, DeleteProperty, RenameProperty, ChangePropertyType.
Methods: CreateMethod, ListMethods, DeleteMethod, RenameMethod.
Validation: AddValidation, ShowValidation, RemoveValidation, AddBusinessValidation, ShowBusinessValidation, RemoveBusinessValidation, GenerateValidator.
Analysis and documents: AnalyzeClass, AnalyzeProject, GenerateDocumentation, GenerateDesignDocument, ValidateProject, CompileProject.
Other: Help, ShowLearning, ResetLearning, Unknown.

Validation

Rules are discovered by reflection. ValidationRuleCatalog finds every IValidationRuleParser in the assembly and orders them by Priority. A rule knows how to Describe itself and how to Emit its own C# check. ValidationRegistry keeps the rules for each class and saves them to .codegen/validation-rules.json in the workspace (as the text that was typed, parsed again on load). ValidationEngine.EmitValidationPartial writes one Validate() partial class with a data section and a business section. To add a rule type, add one rule file and one parser to Core/Validation/Rules/.

Relationships and undo

RelationshipDetector proposes the relationships a set of classes is missing. RelationshipWriter writes one and compiles the whole workspace to check it, or leaves the files as they were. JoinClassWriter makes the third class of a many-to-many relationship.

UndoJournal records the files and the session state a command changed, so that Undo and Redo can put them back. It keeps at most 50 entries.

Safety

Duplicate class and property checks, namespace and type checks, a compile check after a class is written, backups of deleted classes that ClassRestoreService restores, and a workspace snapshot for undo.

How to run

Build CodeGenerationEngine/CodeGenerationEngine.csproj (for example, dotnet build). The library has no executable. Run the CodeGenerator project to use it. Its behavior is tested in CodeGenerator.Tests.
