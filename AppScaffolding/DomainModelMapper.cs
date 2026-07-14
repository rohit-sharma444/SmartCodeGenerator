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
        "Data", "Models", "ViewModels", "Views",

        // Types the generated app declares or references by simple name.
        "App", "Application", "MainWindow", "Window", "MainViewModel", "EntityViewModel",
        "IEntityViewModel", "ObservableObject", "RelayCommand", "ICommand", "JsonStore",
        "DataGrid", "TabControl", "ObservableCollection",

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

    /// <summary>The namespace [JsonIgnore] lives in. Declared only by a class that uses it.</summary>
    private const string JsonSerializationNamespace = "System.Text.Json.Serialization";

    /// <summary>
    /// Kept off the JSON file, not off the class. A navigation property is resolved from the
    /// foreign key at load time (see RelationshipResolverTemplate), and serializing it as well
    /// would make Order.Customer -> Customer.Orders -> Order a reference cycle, which
    /// System.Text.Json throws on rather than writes.
    /// </summary>
    private const string JsonIgnoreAttribute = "JsonIgnore";

    public static IReadOnlyList<ClassDefinition> ToClassDefinitions(CandidateDomainModel model)
    {
        var classes = new List<ClassDefinition>();

        // Class names double as file names, and Windows file names are case-insensitive.
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Entity name -> generated class name. A relationship names its target by ENTITY name, but
        // a nav property must be typed by the CLASS name, and ToUniqueClassName can rename one
        // ("Type" -> "TypeItem"). Without this map a renamed target would emit an uncompilable
        // reference to a type that does not exist.
        var classNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in model.Entities)
        {
            var className = ToUniqueClassName(entity.Name, usedNames);
            classNames[entity.Name] = className;

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

        AddNavigationProperties(model, classes, classNames);
        AddAuditFields(classes);

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

                AddNavigation(owner, targetName, targetName);
                AddNavigation(target, Pluralize(ownerName), $"List<{ownerName}>");
            }
        }
    }

    /// <summary>
    /// Adds one [JsonIgnore] navigation property, unless its name is already taken or would
    /// collide with the class's own name (CS0542).
    /// </summary>
    private static void AddNavigation(ClassDefinition definition, string name, string type)
    {
        if (string.Equals(name, definition.Name, StringComparison.OrdinalIgnoreCase))
            return;

        if (definition.Properties.Any(
                p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            return;

        definition.Properties.Add(new PropertyDefinition
        {
            Name = name,
            Type = type,
            Attributes = { JsonIgnoreAttribute }
        });

        if (!definition.Usings.Contains(JsonSerializationNamespace))
            definition.Usings.Add(JsonSerializationNamespace);
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
    /// English plural, well enough for a collection property name. Not a linguistics engine: the
    /// worst case is an inverse collection called "Companys", which compiles and reads oddly, and
    /// the user can rename it in the review loop.
    /// </summary>
    private static string Pluralize(string name)
    {
        if (name.EndsWith("s", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("x", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("ch", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("sh", StringComparison.OrdinalIgnoreCase))
            return name + "es";

        if (name.Length > 1
            && name.EndsWith("y", StringComparison.OrdinalIgnoreCase)
            && !"aeiou".Contains(char.ToLowerInvariant(name[^2])))
            return name[..^1] + "ies";

        return name + "s";
    }

    /// <summary>
    /// Turns <paramref name="entityName"/> into a class name that collides with neither a
    /// reserved identifier nor a name already taken by an earlier entity, and records it
    /// in <paramref name="usedNames"/>.
    /// </summary>
    private static string ToUniqueClassName(string entityName, HashSet<string> usedNames)
    {
        var baseName = ReservedNames.Contains(entityName)
            ? entityName + DisambiguatingSuffix
            : entityName;

        var candidate = baseName;
        for (var n = 2; !usedNames.Add(candidate); n++)
            candidate = baseName + n;

        return candidate;
    }
}
