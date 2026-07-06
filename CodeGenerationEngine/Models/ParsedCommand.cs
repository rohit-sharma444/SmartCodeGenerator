namespace CodeGenerationEngine.Models;

public sealed class ParsedCommand
{
    public IntentType Intent { get; set; } = IntentType.Unknown;

    // Project
    public string ProjectName { get; set; } = string.Empty;
    public string OutputFolder { get; set; } = string.Empty;
    public DotNetProjectType? ProjectType { get; set; }
    public string Framework { get; set; } = string.Empty;

    // Class
    public string ClassName { get; set; } = string.Empty;
    public string NewClassName { get; set; } = string.Empty;
    public List<string> Interfaces { get; set; } = [];

    // Members
    public string MethodName { get; set; } = string.Empty;
    public string ReturnType { get; set; } = "void";

    public List<ParameterDefinition> Parameters { get; set; } = [];

    public List<PropertyDefinition> Properties { get; set; } = [];

    // Single-property member operations (add/remove property on an existing class).
    public string PropertyName { get; set; } = string.Empty;
    public string PropertyType { get; set; } = "string";

    public List<MethodDefinition> Methods { get; set; } = [];

    // Validation
    public string ValidationText { get; set; } = string.Empty;

    // Artifacts
    public string? InterfaceName { get; set; }
    public string? ServiceName { get; set; }
    public string? RepositoryName { get; set; }
    public string? ControllerName { get; set; }
    public string? ApiName { get; set; }

    // Natural language metadata
    public string OriginalSentence { get; set; } = string.Empty;

    public double Confidence { get; set; }

    public List<string> Tags { get; set; } = [];

    public Dictionary<string, string> Metadata { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    // Context
    public bool UseCurrentClass { get; set; }

    public bool UseCurrentProject { get; set; }

    // Flags
    public bool GenerateValidation { get; set; }

    public bool GenerateConstructor { get; set; }

    public bool GenerateCrudMethods { get; set; }

    public bool GenerateRepository { get; set; }

    public bool GenerateService { get; set; }

    public bool GenerateController { get; set; }

    public bool HasProject => !string.IsNullOrWhiteSpace(ProjectName);

    public bool HasClass => !string.IsNullOrWhiteSpace(ClassName);

    public bool HasMethod => !string.IsNullOrWhiteSpace(MethodName);

    public bool HasProperties => Properties.Count > 0;

    public bool HasMethods => Methods.Count > 0;

    public string? BaseClass { get; set; }

    public void Clear()
    {
        Intent = IntentType.Unknown;

        ProjectName = string.Empty;
        OutputFolder = string.Empty;
        Framework = string.Empty;
        ProjectType = null;

        ClassName = string.Empty;
        NewClassName = string.Empty;
        BaseClass = string.Empty;

        MethodName = string.Empty;
        ReturnType = "void";

        ValidationText = string.Empty;

        InterfaceName = null;
        ServiceName = null;
        RepositoryName = null;
        ControllerName = null;
        ApiName = null;

        OriginalSentence = string.Empty;

        Confidence = 0;

        GenerateValidation = false;
        GenerateConstructor = false;
        GenerateCrudMethods = false;
        GenerateRepository = false;
        GenerateService = false;
        GenerateController = false;

        Interfaces.Clear();
        Parameters.Clear();
        Properties.Clear();
        Methods.Clear();
        Tags.Clear();
        Metadata.Clear();
    }

    public override string ToString()
    {
        return
            $"{Intent} | " +
            $"Project={ProjectName} | " +
            $"Class={ClassName} | " +
            $"Properties={Properties.Count} | " +
            $"Methods={Methods.Count}";
    }
}