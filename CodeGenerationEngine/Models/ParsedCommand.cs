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

    // Target name for `rename method <old> to <new>`.
    public string NewMethodName { get; set; } = string.Empty;

    public List<ParameterDefinition> Parameters { get; set; } = [];

    public List<PropertyDefinition> Properties { get; set; } = [];

    /// <summary>
    /// The entries in a property list that the parser threw away.
    /// </summary>
    /// <remarks>
    /// It travels here rather than being printed where it is found, because it is found in
    /// NaturalLanguageBridge and reported in this assembly, which the bridge references and not
    /// the other way round - and because an extractor that prints is an extractor no caller can
    /// use quietly. A property typed as another class is still dropped: emitting it would fail
    /// the isolated single-class compile the create-class path runs. What changes is that the
    /// user is told, and told where relationships are handled instead.
    /// </remarks>
    public List<string> IgnoredProperties { get; set; } = [];

    // Single-property member operations (add/remove property on an existing class).
    public string PropertyName { get; set; } = string.Empty;
    public string PropertyType { get; set; } = "string";

    // Target name for `rename property <old> to <new>`.
    public string NewPropertyName { get; set; } = string.Empty;

    // 1-based rule number for `remove validation <n> from <Class>`, as `show validation` prints it.
    public int? RuleNumber { get; set; }

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

        NewPropertyName = string.Empty;
        NewMethodName = string.Empty;
        RuleNumber = null;

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
        IgnoredProperties.Clear();
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