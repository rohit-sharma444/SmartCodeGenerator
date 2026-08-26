using CodeGenerationEngine.Core.Relationships;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace AppScaffolding;

/// <summary>Maps an approved domain model to CodeGenerationEngine class definitions.</summary>
public static class DomainModelMapper
{
    /// <summary>Appended to an entity name that cannot be used as a class name verbatim.</summary>
    private const string DisambiguatingSuffix = "Item";

    /// <summary>
    /// Identifiers already in scope in the generated app's own sources. An entity class
    /// sharing one of these names makes uses of it ambiguous, or silently binds to the
    /// wrong type, so the entity is renamed instead.
    /// C# keywords need no guard: entity names arrive PascalCased and keywords are lowercase.
    /// </summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // The generated app's OWN namespace segments -- <App>.Data, <App>.Models, and so on. A
        // class may not share a name with a namespace that is in scope where it is used: MainViewModel
        // opens with `using <App>.Data;`, so a model class called Data makes the simple name mean the
        // namespace and the app does not compile (CS0118). Discovering "point of sale" yields exactly
        // that class, so this is a name the real pipeline really does produce, not a hypothetical.
        "Data", "Models", "ViewModels", "Views", "Validation",

        // Types the generated app declares or references by simple name.
        "App", "Application", "MainWindow", "Window", "MainViewModel", "EntityViewModel",
        "IEntityViewModel", "ObservableObject", "RelayCommand", "ICommand", "JsonStore",
        "DataGrid", "TabControl", "ObservableCollection", "Repository",

        // Types the generated Validation/ folder and validating repository declare.
        "IValidator", "IEntityLookup", "StoreEntityLookup", "ValidatorFactory", "NullValidator",
        "SaveResult",

        // Commonly harvested words that are also System types.
        "Activator", "Array", "Attribute", "Buffer", "Comparer", "Convert", "Delegate",
        "Enum", "Environment", "Exception", "Guid", "Index", "Math", "Object", "Random",
        "Range", "String", "Task", "Timer", "Tuple", "Uri", "Version",
        "Boolean", "Byte", "Char", "DateTime", "Decimal", "Double", "Int16", "Int32", "Int64",
        "Single", "TimeSpan", "Type", "PropertyInfo",

        // System.IO / System.Collections names pulled in by the templates.
        "Directory", "File", "Path", "Stream", "Dictionary", "List", "Queue", "Stack",
        "Enumerable", "IEnumerable",
    };

    /// <summary>The namespace DataAnnotations attributes live in. Declared only by a class that uses one.</summary>
    private const string DataAnnotationsNamespace = "System.ComponentModel.DataAnnotations";

    /// <summary>Default upper bound for a generated string column: generous, but not unbounded.</summary>
    private const int DefaultStringLength = 200;

    public static IReadOnlyList<ClassDefinition> ToClassDefinitions(CandidateDomainModel model)
        => ToClassDefinitions(model, out _);

    /// <summary>
    /// As <see cref="ToClassDefinitions(CandidateDomainModel)"/>, also yielding the entity-name to
    /// class-name map this method already builds.
    /// </summary>
    /// <remarks>
    /// <see cref="SeedFileWriter"/> needs it: imported rows are keyed by ENTITY name, seed files are
    /// named for the CLASS, and <see cref="ToUniqueClassName"/> can rename one ("Type" ->
    /// "TypeItem"). Without the map a renamed entity's rows would be written to a file no class ever
    /// reads, and the app would start empty with nothing to explain why.
    /// </remarks>
    public static IReadOnlyList<ClassDefinition> ToClassDefinitions(
        CandidateDomainModel model, out IReadOnlyDictionary<string, string> classNames)
        => ToClassDefinitions(model, out classNames, reserved: []);

    /// <summary>
    /// As above, with <paramref name="reserved"/> naming classes the generated app already
    /// declares, so a discovered entity of the same name is renamed rather than colliding.
    /// </summary>
    /// <remarks>
    /// The auth core emits its own <c>User</c>. Without this, a model that discovered a User
    /// entity had that class OVERWRITTEN by the auth model -- its repository, validator, seed
    /// rows and view model then compiled against the wrong shape -- and under SQLite the entity
    /// list gained a second "User" and AppDbContext emitted DbSet&lt;User&gt; twice (CS0102).
    /// <para>
    /// Passed in rather than added to <see cref="ReservedNames"/>, because the collision only
    /// exists when the app HAS an auth core: a `nologin` build must still emit the entity under
    /// the name the user described, or its output stops matching what the generator produced
    /// before any of this existed.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ClassDefinition> ToClassDefinitions(
        CandidateDomainModel model,
        out IReadOnlyDictionary<string, string> classNames,
        IReadOnlyCollection<string> reserved)
    {
        var classes = new List<ClassDefinition>();

        // Class names double as file names, and Windows file names are case-insensitive.
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);



        // Entity name -> generated class name. A relationship names its target by ENTITY name, but
        // a nav property must be typed by the CLASS name, and ToUniqueClassName can rename one
        // ("Type" -> "TypeItem"). Without this map a renamed target would emit an uncompilable
        // reference to a type that does not exist.
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in model.Entities)
        {
            var className = ToUniqueClassName(entity.Name, usedNames, reserved);
            names[entity.Name] = className;

            var definition = new ClassDefinition { Name = className };

            bool hasId = entity.Fields.Any(
                f => string.Equals(f.Name, "Id", StringComparison.OrdinalIgnoreCase));

            // Guarantee an int Id key for CRUD. If the entity already declares an
            // Id field, keep it in place; otherwise prepend one. A field-less
            // entity therefore ends up with a single int Id.
            if (!hasId)
                definition.Properties.Add(new PropertyDefinition { Name = "Id", Type = "int" });

            foreach (var field in entity.Fields)
                definition.Properties.Add(
                    new PropertyDefinition { Name = field.Name, Type = field.Type });

            classes.Add(definition);
        }

        AddNavigationProperties(model, classes, names);
        AddAuditFields(classes);
        AddValidation(classes);

        classNames = names;
        return classes;
    }

    /// <summary>
    /// Turns each mined relationship into a pair of navigation properties: the forward reference on
    /// the class that carries the foreign key, and the inverse collection on the class it points at.
    /// </summary>
    /// <remarks>
    /// RelationshipMiner has populated <see cref="CandidateEntity.Relationships"/> since it was
    /// written, and nothing ever read it — only the <c>&lt;Target&gt;Id</c> field it also emits
    /// reached the generated class. This is where the rest of that work finally lands.
    /// <para>
    /// A relationship whose target did not survive shaping is skipped: a property typed as a class
    /// that is never generated does not compile.
    /// </para>
    /// </remarks>
    private static void AddNavigationProperties(
        CandidateDomainModel model,
        List<ClassDefinition> classes,
        Dictionary<string, string> classNames)
    {
        var byName = classes.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var entity in model.Entities)
        {
            if (!classNames.TryGetValue(entity.Name, out var ownerName)
                || !byName.TryGetValue(ownerName, out var owner))
                continue;

            foreach (var relationship in entity.Relationships)
            {
                if (!classNames.TryGetValue(relationship.Target, out var targetName)
                    || !byName.TryGetValue(targetName, out var target))
                    continue;

                AddNavigation(owner, RelationshipConvention.Forward(targetName));
                AddNavigation(target, RelationshipConvention.InverseCollection(ownerName));
            }
        }
    }

    /// <summary>
    /// Adds one navigation property built by <see cref="RelationshipConvention"/>, unless its
    /// name is already taken or would collide with the class's own name (CS0542).
    /// </summary>
    private static void AddNavigation(ClassDefinition definition, PropertyDefinition navigation)
    {
        if (!RelationshipConvention.CanAdd(definition, navigation.Name))
            return;

        definition.Properties.Add(navigation);

        if (!definition.Usings.Contains(RelationshipConvention.JsonSerializationNamespace))
            definition.Usings.Add(RelationshipConvention.JsonSerializationNamespace);

        if (!definition.Usings.Contains(RelationshipConvention.ComponentModelNamespace))
            definition.Usings.Add(RelationshipConvention.ComponentModelNamespace);
    }

    /// <summary>
    /// What a running app needs and the domain never said: when the record was created and last
    /// touched. Added here rather than in discovery, so the boundary between "what Wikipedia says"
    /// and "what a CRUD app needs" stays where it is.
    /// </summary>
    private static void AddAuditFields(List<ClassDefinition> classes)
    {
        foreach (var definition in classes)
        {
            foreach (var name in new[] { "CreatedOn", "ModifiedOn" })
            {
                if (string.Equals(name, definition.Name, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (definition.Properties.Any(
                        p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                    continue;

                definition.Properties.Add(new PropertyDefinition { Name = name, Type = "DateTime" });
            }
        }
    }

    /// <summary>
    /// Adds inferred DataAnnotations to each property from its name and type: a key on Id, a
    /// required, length-bounded name field, and length limits (plus an email/phone shape) on
    /// strings. Declarative only -- the interactive front-ends run Validator.TryValidateObject to
    /// enforce them. A class that gains any attribute also gains the DataAnnotations using.
    /// </summary>
    private static void AddValidation(List<ClassDefinition> classes)
    {
        foreach (var definition in classes)
        {
            var added = false;
            foreach (var property in definition.Properties)
                added |= InferValidation(property);

            if (added && !definition.Usings.Contains(DataAnnotationsNamespace))
                definition.Usings.Add(DataAnnotationsNamespace);
        }
    }

    /// <summary>
    /// Applies the inference rules to one property, mutating its Rules/Attributes in place, and
    /// returns whether it added a DataAnnotations attribute. A navigation property (typed as
    /// another class or a List&lt;&gt;) is not a string and carries no Id name, so it is left alone.
    /// </summary>
    private static bool InferValidation(PropertyDefinition property)
    {
        if (string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase)
            && property.Type == "int")
        {
            property.Attributes.Add("Key");
            return true;
        }

        if (property.Type != "string")
            return false;

        var isNameField = string.Equals(property.Name, "Name", StringComparison.OrdinalIgnoreCase)
            || string.Equals(property.Name, "Title", StringComparison.OrdinalIgnoreCase);

        if (isNameField)
            property.Rules.Add(new PropertyRule { Name = "required" });

        property.Rules.Add(new PropertyRule { Name = "maxlength", MaxLength = DefaultStringLength });

        if (property.Name.Contains("email", StringComparison.OrdinalIgnoreCase))
            property.Attributes.Add("EmailAddress");
        else if (property.Name.Contains("phone", StringComparison.OrdinalIgnoreCase))
            property.Attributes.Add("Phone");

        return true;
    }

    /// <summary>
    /// Turns <paramref name="entityName"/> into a class name that collides with neither a
    /// reserved identifier nor a name already taken by an earlier entity, and records it
    /// in <paramref name="usedNames"/>.
    /// </summary>
    private static string ToUniqueClassName(
        string entityName, HashSet<string> usedNames, IReadOnlyCollection<string> reserved)
    {
        // The caller's reserved names take the same suffix the built-in ones do, rather than
        // the numeric fallback: "User" becomes "UserItem" like "Type" becomes "TypeItem",
        // because "User2" reads as a second user rather than as a renamed class.
        var isReserved = ReservedNames.Contains(entityName)
            || reserved.Contains(entityName, StringComparer.OrdinalIgnoreCase)
            || IsValidatorName(entityName);

        var baseName = isReserved ? entityName + DisambiguatingSuffix : entityName;

        var candidate = baseName;
        for (var n = 2; !usedNames.Add(candidate); n++)
            candidate = baseName + n;

        return candidate;
    }

    /// <summary>
    /// Whether this name would collide with a generated validator's class name.
    /// </summary>
    /// <remarks>
    /// The generated validators live in <c>&lt;App&gt;.Validation</c> and are named
    /// <c>&lt;Model&gt;Validator</c>. C# resolves a simple name against the enclosing namespace
    /// before any using directive, so an entity called <c>OrderValidator</c> makes
    /// <c>typeof(OrderValidator)</c> inside ValidatorFactory bind to <em>Order's validator</em>
    /// rather than to the model — the factory arm then never matches and the entity silently
    /// degrades to NullValidator. Renaming the entity is the only fix that also protects the
    /// validator's own <c>IValidator&lt;T&gt;</c> argument. A suffix cannot be expressed in
    /// <see cref="ReservedNames"/>, hence the separate test.
    /// </remarks>
    private static bool IsValidatorName(string entityName) =>
        entityName.EndsWith("Validator", StringComparison.OrdinalIgnoreCase);
}
