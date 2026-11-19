using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Relationships;
using CodeGenerationEngine.Core.Validation;
using CodeGenerationEngine.Core.Validation.Rules;
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

        // The GLOBAL namespaces every generated app references. `Repository<System>` inside
        // <App>.Data finds namespace System at the global level before any using directive is
        // consulted, so a class with either name breaks the build the same way (CS0118). "a
        // library management system" on the built-in-domain path produced exactly that class.
        "System", "Microsoft",

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
    /// <summary>The cap a string property gets when nothing -- SQL schema or the source data
    /// itself -- says otherwise. Public so <see cref="DataIngestion.DataModelBuilder"/> can raise
    /// it for a column whose own longest value already exceeds it.</summary>
    public const int DefaultStringLength = 200;

    /// <summary>The value types a property can be made nullable in: the four of the five model
    /// types that cannot hold a missing value on their own.</summary>
    private static readonly HashSet<string> NullableCapableTypes =
        new(StringComparer.Ordinal) { "int", "decimal", "bool", "DateTime" };

    /// <summary>
    /// <paramref name="type"/> without a trailing <c>?</c>: "int?" is "int". Every consumer that
    /// asks what KIND of value a property holds asks this, so a nullable property gets the same
    /// rules, seed reading and screens as its non-nullable form.
    /// </summary>
    public static string BaseType(string type)
        => type.EndsWith('?') ? type[..^1] : type;

    /// <summary>Whether <paramref name="type"/> is a nullable value type ("int?").</summary>
    public static bool IsNullable(string type) => type.EndsWith('?');

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
                definition.Properties.Add(new PropertyDefinition
                {
                    Name = field.Name,
                    Type = field.Type,
                    Facts = field.Facts,
                    Proposals = [.. field.Proposals]
                });

            classes.Add(definition);
        }

        RenameKeysToRenamedClasses(classes, names);
        AddNavigationProperties(model, classes, names);
        AddNullability(classes);
        AddDefaults(classes);
        AddAuditFields(classes);
        AddValidation(classes);
        AddEntityValidation(classes, model, names);
        AddWorkflowValidation(classes, model, names);

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
    /// <summary>
    /// <c>&lt;Entity&gt;Id</c> as <c>&lt;Class&gt;Id</c> on every other class, for each entity
    /// <see cref="ToUniqueClassName"/> renamed.
    /// </summary>
    /// <remarks>
    /// The relationship and its navigation follow the class name, and the resolver, the seeder,
    /// the validators and the lookups all find a foreign key by <c>&lt;Class&gt;Id</c>. "a library
    /// that lends books" renames Library to LibraryItem, and Book kept <c>int LibraryId</c> beside a
    /// <c>LibraryItem</c> navigation nothing paired with it. The seed rows keep the old name;
    /// <see cref="SeedFileWriter"/> reads a renamed key's value from there.
    /// </remarks>
    private static void RenameKeysToRenamedClasses(
        List<ClassDefinition> classes, Dictionary<string, string> classNames)
    {
        foreach (var (entityName, className) in classNames)
        {
            if (string.Equals(entityName, className, StringComparison.Ordinal))
                continue;

            var oldKey = entityName + "Id";
            var newKey = className + "Id";

            foreach (var definition in classes)
            {
                if (string.Equals(definition.Name, className, StringComparison.OrdinalIgnoreCase)
                    || definition.Properties.Any(
                        p => string.Equals(p.Name, newKey, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var key = definition.Properties.FirstOrDefault(
                    p => string.Equals(p.Name, oldKey, StringComparison.OrdinalIgnoreCase)
                         && BaseType(p.Type) == "int");

                if (key is not null)
                    key.Name = newKey;
            }
        }
    }

    /// <summary>The old key name each renamed foreign key's seed value is still filed under,
    /// by the new name: <c>LibraryItemId</c> → <c>LibraryId</c>.</summary>
    internal static IReadOnlyDictionary<string, string> RenamedKeys(
        IReadOnlyDictionary<string, string> classNames)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (entityName, className) in classNames)
        {
            if (!string.Equals(entityName, className, StringComparison.Ordinal))
                map.TryAdd(className + "Id", entityName + "Id");
        }

        return map;
    }

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

                if (RoleKey(relationship, owner) is { } roleKey)
                {
                    AddRoleNavigation(owner, roleKey, targetName);
                    continue;
                }

                DropTextShadowingTheRelationship(owner, targetName);

                AddNavigation(owner, RelationshipConvention.Forward(targetName));
                AddNavigation(target, RelationshipConvention.InverseCollection(ownerName));
            }
        }
    }

    /// <summary>
    /// The key property a relationship read from the user's data is held in, when it is named for
    /// a role rather than for its target (<c>PaidById</c>, <c>ManagerId</c>); null for a key
    /// spelled <c>&lt;Target&gt;Id</c>, for one discovery inferred, and for one the class no longer
    /// has (removed on Review).
    /// </summary>
    private static PropertyDefinition? RoleKey(CandidateRelationship relationship, ClassDefinition owner)
    {
        if (relationship.ForeignKey is not { Length: > 2 } key
            || !key.EndsWith("Id", StringComparison.Ordinal)
            || string.Equals(key, relationship.Target + "Id", StringComparison.OrdinalIgnoreCase))
            return null;

        return owner.Properties.FirstOrDefault(
            p => string.Equals(p.Name, key, StringComparison.Ordinal) && p.Type == "int");
    }

    /// <summary>
    /// The navigation for a key named for a role: <c>Staff PaidBy</c> beside <c>PaidById</c>, and
    /// <c>Staff Manager</c> beside a member of staff's own <c>ManagerId</c>.
    /// </summary>
    /// <remarks>
    /// The generated app pairs a key with the navigation named for it - the resolver fills
    /// <c>PaidBy</c> from <c>PaidById</c>, the pickers and lookups read the same pair - so each
    /// key gets its own, and two keys to Staff are two navigations rather than one <c>Staff</c>
    /// neither was paired with. No inverse collection: the resolver fills one only from
    /// <c>&lt;Owner&gt;Id</c>, so a collection here would stay empty, and two roles would want the
    /// same name for it.
    /// </remarks>
    private static void AddRoleNavigation(ClassDefinition owner, PropertyDefinition key, string targetName)
    {
        var navigation = RelationshipConvention.Forward(targetName);
        navigation.Name = key.Name[..^2];

        AddNavigation(owner, navigation);
    }

    /// <summary>
    /// Drops a <c>string</c> field named for <paramref name="targetName"/> when the class also
    /// holds <c>&lt;Target&gt;Id</c>: discovery can give Shipment both <c>string Customer</c> and
    /// <c>int CustomerId</c>, and the text's name then blocked the navigation, so the app showed a
    /// free-text customer box beside a key to a real one. With the key there, the relationship is
    /// what the text was trying to say.
    /// </summary>
    private static void DropTextShadowingTheRelationship(ClassDefinition owner, string targetName)
    {
        var hasKey = owner.Properties.Any(
            p => string.Equals(p.Name, targetName + "Id", StringComparison.Ordinal)
                 && BaseType(p.Type) == "int");

        if (!hasKey)
            return;

        owner.Properties.RemoveAll(
            p => string.Equals(p.Name, targetName, StringComparison.Ordinal) && p.Type == "string");
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
    /// Makes a number, flag or date property NULLABLE when its source rows hold a missing value
    /// (<see cref="TypeFacts.HasMissingValues"/>), so an empty cell or a SQL NULL stays missing
    /// rather than becoming 0, False or 01-Jan-0001 -- values that look exactly like real data.
    /// </summary>
    /// <remarks>
    /// Two exceptions. The Id key, which the store assigns. And a foreign key: a blank one already
    /// reads as "no parent" at 0 (seeded Ids start at 1), and every relationship consumer -- the
    /// seeder's ordering, the resolver, the lookups -- matches an <c>int</c> key, so it stays one.
    /// </remarks>
    private static void AddNullability(List<ClassDefinition> classes)
    {
        var names = ForeignKeys.ClassNameSet(classes);

        foreach (var definition in classes)
        {
            var foreignKeys = ForeignKeys.LinksOf(definition, names)
                .Select(link => link.Property)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var property in definition.Properties)
            {
                if (property.Facts?.HasMissingValues != true
                    || !NullableCapableTypes.Contains(property.Type)
                    || string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase)
                    || foreignKeys.Contains(property.Name))
                    continue;

                property.Type += "?";
            }
        }
    }

    /// <summary>
    /// Starts each property a new record has at the DEFAULT its source column declared, when that
    /// default fits the property's type: <c>IsActive = true</c> for a <c>bit DEFAULT 1</c>.
    /// </summary>
    /// <remarks>
    /// After <see cref="AddNullability"/>, because the type decides what the default can be
    /// written as. A default that does not fit (the user retyped the field on Review) is left
    /// out rather than guessed at. The Id key and the foreign keys are never given one: the
    /// store numbers the first, and a default link would point at whichever row has that number.
    /// </remarks>
    private static void AddDefaults(List<ClassDefinition> classes)
    {
        var names = ForeignKeys.ClassNameSet(classes);

        foreach (var definition in classes)
        {
            var foreignKeys = ForeignKeys.LinksOf(definition, names)
                .Select(link => link.Property)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var property in definition.Properties)
            {
                if (property.Facts is not { } facts
                    || string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase)
                    || foreignKeys.Contains(property.Name))
                    continue;

                property.Initializer = Initializer(BaseType(property.Type), facts.DefaultValue, facts.DefaultFunction);
            }
        }
    }

    /// <summary>Whether a default of <paramref name="value"/> or <paramref name="function"/>
    /// can start a new record's property of <paramref name="type"/>. A reader that knows the
    /// source's default asks, so a default the app will leave out is said rather than dropped in
    /// silence.</summary>
    public static bool DefaultFits(string type, string? value, string? function)
        => Initializer(BaseType(type), value, function) is not null;

    /// <summary>The C# expression a default of <paramref name="value"/> or
    /// <paramref name="function"/> is for a <paramref name="type"/>, or null when it does not fit.</summary>
    private static string? Initializer(string type, string? value, string? function)
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;

        if (function is not null)
            return (type, function) switch
            {
                ("DateTime", "now") => "global::System.DateTime.Now",
                ("DateTime", "utcnow") => "global::System.DateTime.UtcNow",
                ("string", "newguid") => "global::System.Guid.NewGuid().ToString().ToUpperInvariant()",
                _ => null
            };

        if (value is null)
            return null;

        switch (type)
        {
            case "string":
                return Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(value, quote: true);

            case "int":
                return int.TryParse(value, System.Globalization.NumberStyles.AllowLeadingSign, invariant, out var i)
                    ? i.ToString(invariant)
                    : null;

            case "decimal":
                return decimal.TryParse(value, System.Globalization.NumberStyles.Number, invariant, out var d)
                    ? d.ToString(invariant) + "m"
                    : null;

            case "bool":
                return value.Trim() switch
                {
                    "1" or "true" or "True" => "true",
                    "0" or "false" or "False" => "false",
                    _ => null
                };

            case "DateTime":
                return DateTime.TryParse(value, invariant, System.Globalization.DateTimeStyles.None, out var date)
                    ? $"new global::System.DateTime({date.Ticks}L)"
                    : null;

            default:
                return null;
        }
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
    /// Rule names <see cref="CodeGenerationEngine.Core.RoslynClassGenerator"/> renders as
    /// DataAnnotations attributes on the emitted model, rather than as a check in the generated
    /// validator.
    /// </summary>
    /// <remarks>
    /// A class carrying one of these needs the DataAnnotations using even though nothing added an
    /// entry to <see cref="PropertyDefinition.Attributes"/> — the attribute is derived from the
    /// rule inside <c>RoslynClassGenerator.CreateProperty</c>, which never sees this list. Without
    /// the scan below, an entity that declares its own non-int Id (so no <c>[Key]</c>) and no field
    /// named for an email or a phone reaches the generator with a <c>[StringLength]</c> it cannot
    /// resolve, and the generated app does not compile.
    /// </remarks>
    private static readonly HashSet<string> DataAnnotationRuleNames =
        new(StringComparer.OrdinalIgnoreCase) { "required", "maxlength", "minlength" };

    /// <summary>
    /// Adds inferred DataAnnotations to each property from its declared type and the facts its
    /// source supplied: a key on Id, and the rules of the kinds
    /// <see cref="ValidatorTemplate.EmittableRules"/> lists. Declarative only -- the
    /// interactive front-ends run Validator.TryValidateObject to enforce them. A class that gains
    /// any attribute, or any rule the model emitter renders as one, also gains the DataAnnotations
    /// using.
    /// </summary>
    private static void AddValidation(List<ClassDefinition> classes)
    {
        foreach (var definition in classes)
        {
            var added = false;
            foreach (var property in definition.Properties)
                added |= InferValidation(property);

            AddDeclaredUniqueness(definition);

            added |= definition.Properties.Any(
                p => p.Rules.Any(r => DataAnnotationRuleNames.Contains(r.Name)));

            if (added && !definition.Usings.Contains(DataAnnotationsNamespace))
                definition.Usings.Add(DataAnnotationsNamespace);
        }
    }

    /// <summary>
    /// The uniqueness the source declared (<see cref="TypeFacts.UniqueKeys"/>): a key over one
    /// property becomes its unique rule, and a key over several becomes one entry in
    /// <see cref="ClassDefinition.UniqueTogether"/>.
    /// </summary>
    /// <remarks>
    /// A declared key is a fact the source stated, so it is enforced whatever the entity is: the
    /// proposals withhold uniqueness from an entity that links to another, because a repeating
    /// value there is usually the design, but a UNIQUE the database declared says otherwise. A key
    /// some of whose columns are no longer properties (removed on Review, or computed) is not
    /// enforced: over fewer columns it would refuse records the source accepts.
    /// </remarks>
    private static void AddDeclaredUniqueness(ClassDefinition definition)
    {
        var keys = definition.Properties
            .Where(p => !string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase))
            .SelectMany(p => (p.Facts?.UniqueKeys ?? []).Select(k => (Property: p, Key: k)))
            .GroupBy(pair => pair.Key.Name, StringComparer.Ordinal);

        foreach (var key in keys)
        {
            var members = key.Select(pair => pair.Property).Distinct().ToList();

            if (members.Count != key.First().Key.Columns)
                continue;

            if (members.Count > 1)
            {
                definition.UniqueTogether.Add([.. members.Select(p => p.Name)]);
                continue;
            }

            var single = members[0];

            if (ValidatorTemplate.CanEmitEntityRule(single.Type, "unique")
                && !single.Rules.Any(r => string.Equals(r.Name, "unique", StringComparison.OrdinalIgnoreCase)))
                single.Rules.Add(new PropertyRule { PropertyName = single.Name, Name = "unique", IsUnique = true });
        }
    }

    /// <summary>
    /// Merges hand-stated business rules with accepted entity-scoped proposals onto the class
    /// that matches each entity, so both reach the Business stage through the one list
    /// ValidatorTemplate.Generated reads.
    /// </summary>
    /// <remarks>
    /// The one shared seam both AppScaffolder and IncrementalScaffolder need: IncrementalScaffolder
    /// never calls ValidatorTemplate.Generated directly, it delegates its whole build to a fresh
    /// AppScaffolder run, which reaches this same method. ValidationRegistry is a process-wide
    /// static, so both paths see identical rules with no extra plumbing.
    /// </remarks>
    private static void AddEntityValidation(
        List<ClassDefinition> classes, CandidateDomainModel model, Dictionary<string, string> names)
    {
        foreach (var entity in model.Entities)
        {
            if (!names.TryGetValue(entity.Name, out var className))
                continue;

            var definition = classes.FirstOrDefault(c => c.Name == className);
            if (definition is null)
                continue;

            var rules = new List<IValidationRule>(
                ValidationRegistry.Get(className, ValidationCategory.Business));

            foreach (var proposal in entity.Proposals)
            {
                if (!proposal.Accepted)
                    continue;

                rules.Add(proposal.Rule);
            }

            definition.BusinessRules = rules;
        }
    }

    /// <summary>
    /// Builds one WorkflowRule per accepted, ordered Status proposal, so a status column's
    /// transitions reach the Workflow stage through the one list ValidatorTemplate.Generated
    /// reads. Mirrors AddEntityValidation's shape: reads the model's own proposals, writes onto
    /// the matching class, no scan of its own.
    /// </summary>
    private static void AddWorkflowValidation(
        List<ClassDefinition> classes, CandidateDomainModel model, Dictionary<string, string> names)
    {
        foreach (var entity in model.Entities)
        {
            if (!names.TryGetValue(entity.Name, out var className))
                continue;

            var definition = classes.FirstOrDefault(c => c.Name == className);
            if (definition is null)
                continue;

            foreach (var field in entity.Fields)
            {
                foreach (var proposal in field.Proposals)
                {
                    if (proposal.Kind != RuleProposalKind.Status || !proposal.Accepted
                        || proposal.Order is null)
                        continue;

                    definition.WorkflowRules.Add(
                        new WorkflowRule(field.Name, proposal.Order, proposal.FinalValues));
                }
            }
        }
    }

    /// <summary>
    /// Applies the inference rules to one property, mutating its Rules/Attributes in place, and
    /// returns whether it added a DataAnnotations attribute.
    /// </summary>
    /// <remarks>
    /// Driven by the property's declared type and the facts the source supplied, not by its name.
    /// A navigation property (typed as another class or a List&lt;&gt;) matches none of the five
    /// emitted types and is left alone. Every rule recorded here must appear in
    /// <see cref="ValidatorTemplate.EmittableRules"/> PAIRED WITH THE TYPE it is recorded on, or
    /// it is dropped silently: the emitter guards every check on the property's type, so a rule
    /// on the wrong type emits nothing at all.
    /// </remarks>
    private static bool InferValidation(PropertyDefinition property)
    {
        if (string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase)
            && property.Type == "int")
        {
            property.Attributes.Add("Key");
            return true;
        }

        // Accepted proposals first, so a rule the user picked is recorded whatever the declared
        // type then adds. They are separate registers: a maxlength from an nvarchar(50) and an
        // allowed-values from a scan are both true of the same column.
        foreach (var proposal in property.Proposals)
        {
            if (!proposal.Accepted)
                continue;

            property.Rules.Add(proposal.Rule);
        }

        var facts = property.Facts;

        // The base type: a nullable property ("int?") takes its kind's rules. "required" stays a
        // string-only rule, so a nullable number is never made required by its missing values.
        switch (BaseType(property.Type))
        {
            case "string":
                // A column the source declared NOT NULL is required, whatever it is called.
                // Unknown nullability infers nothing: a discovered domain must not become
                // stricter than the user described it.
                //
                // Recorded for a string and no other type, which is why it sits inside this arm
                // rather than above the switch. string.IsNullOrWhiteSpace is the only meaningful
                // emptiness test among the five types the generator emits: a non-nullable int
                // cannot be null and 0 is a legitimate value, so a required rule there asserts
                // nothing true while still putting an asterisk on a field the user may correctly
                // leave at zero. ValidatorTemplate.EmittableRules pairs "required" with "string"
                // alone for the same reason, and the guard test holds the two together.
                if (facts?.IsNullable == false)
                    property.Rules.Add(new PropertyRule { Name = "required" });

                if (!(facts?.IsUnbounded ?? false))
                    property.Rules.Add(new PropertyRule
                    {
                        Name = "maxlength",
                        MaxLength = facts?.MaxLength ?? DefaultStringLength
                    });

                // The values a CHECK constraint lists, as a rule the source declared rather than
                // one proposed from the rows.
                if (facts?.AllowedValues is { Count: > 0 } allowed
                    && !property.Rules.Any(r => string.Equals(r.Name, "allowedvalues", StringComparison.OrdinalIgnoreCase)))
                    property.Rules.Add(new PropertyRule { Name = "allowedvalues", AllowedValues = [.. allowed] });

                return AddFormatAttribute(property);

            case "int":
                AddIntegerRange(property, facts);
                return false;

            case "decimal":
                AddDecimalRules(property, facts);
                return false;

            case "DateTime":
                // A date the source declared NOT NULL is required, as a NOT NULL string is: the
                // non-nullable model holds a blank as 0001-01-01, which such a column would store
                // as though it were a real date. Unknown nullability infers nothing, as above.
                if (facts?.IsNullable == false && !IsNullable(property.Type))
                    property.Rules.Add(new PropertyRule { Name = "required" });

                // SQL Server's own floor, and only for the type that actually carries it:
                // datetime2 starts at year 1, and a rule from the wrong type would refuse
                // dates the column accepts.
                if (string.Equals(facts?.DeclaredType, "datetime", StringComparison.OrdinalIgnoreCase))
                    property.Rules.Add(new PropertyRule { Name = "mindate", MinDate = "1753-01-01" });

                if (string.Equals(facts?.DeclaredType, "date", StringComparison.OrdinalIgnoreCase))
                    property.Rules.Add(new PropertyRule { Name = "dateonly" });

                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// Adds the DataAnnotations attribute a string property's shape calls for, and returns
    /// whether it added one.
    /// </summary>
    /// <remarks>
    /// These two are the only name-driven rules left -- every other rule now comes from the
    /// column's type -- and they are matched on whole words. Substring matching is what put a
    /// phone-number rule on Headphones, Microphone and Saxophone, refusing every value the user
    /// could type into them.
    /// </remarks>
    private static bool AddFormatAttribute(PropertyDefinition property)
    {
        // False, not null, is what withholds the attribute: null means no data was available to
        // check (a discovered domain, or an entity with none yet), and the name is all there is
        // to go on there, same as before this fact existed.
        if (property.Facts?.NameImpliedFormatHolds == false)
            return false;

        if (NameContainsWord(property.Name, "email"))
        {
            property.Attributes.Add("EmailAddress");
            return true;
        }

        if (NameContainsWord(property.Name, "phone"))
        {
            property.Attributes.Add("Phone");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="word"/> is one of the words in <paramref name="name"/>, or is
    /// contained in one of its shouted runs.
    /// </summary>
    /// <remarks>
    /// The shouted-run allowance is not a loophole, it is the only reading available.
    /// <see cref="DomainDiscovery.SeedExtraction.NameNormalizer.ToFieldName"/> capitalizes each word's first letter and leaves
    /// the rest alone, so a SQL Server column named EMAIL or PHONE_NUMBER arrives here as EMAIL
    /// and PHONENUMBER -- and all-capitals column names are the norm in real databases. A run of
    /// capitals carries no word boundaries whatsoever, so containment within the run is the whole
    /// of the evidence; HEADPHONES is indistinguishable from PHONENUMBER by any rule and matches,
    /// which is no worse than the substring matching this replaces and reaches shouted names only.
    /// </remarks>
    /// <summary>Public so <see cref="DataIngestion.DataModelBuilder"/> can ask the same question
    /// of a column's data as this class asks of its name, before <see cref="AddFormatAttribute"/>
    /// commits to a format the source's own values might not hold.</summary>
    public static bool NameContainsWord(string name, string word)
        => Words(name).Any(candidate =>
            string.Equals(candidate, word, StringComparison.OrdinalIgnoreCase)
            || (IsShouted(candidate) && candidate.Contains(word, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Whether a word is written in capitals throughout, so it may be several.</summary>
    private static bool IsShouted(string word) => word.Length > 1 && !word.Any(char.IsLower);

    /// <summary>
    /// The words in an identifier: "BillingEmailAddress" becomes Billing, Email, Address.
    /// A capital following another capital does not start a word, so a shouted run stays whole
    /// rather than becoming one word per letter.
    /// </summary>
    private static IReadOnlyList<string> Words(string name)
    {
        var words = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var character in name)
        {
            if (!char.IsLetterOrDigit(character))
            {
                Flush();
                continue;
            }

            // "MobilePhone" breaks before the P; "PHONENUMBER" does not break inside the run.
            if (char.IsUpper(character) && current.Length > 0 && !char.IsUpper(current[^1]))
                Flush();

            current.Append(character);
        }

        Flush();
        return words;

        void Flush()
        {
            if (current.Length > 0)
                words.Add(current.ToString());

            current.Clear();
        }
    }

    /// <summary>
    /// What decimal(P,S) actually constrains: S digits after the point, and P-S before it.
    /// </summary>
    /// <remarks>
    /// The integer-part bound is the half that is easy to forget and the one the database will
    /// enforce anyway — decimal(18,2) overflows at 10^16, and without this rule the app accepts
    /// the value and the insert fails. A file source supplies a scale from evidence but no
    /// precision, so it gets the scale rule alone.
    /// </remarks>
    private static void AddDecimalRules(PropertyDefinition property, TypeFacts? facts)
    {
        if (facts?.Scale is { } scale)
            property.Rules.Add(new PropertyRule { Name = "scale", Scale = scale });

        // money and smallmoney are 64- and 32-bit integers counting ten-thousandths, not
        // decimal(19,4) and decimal(10,4): their limits are the integer's, ±922,337,203,685,477.58
        // and ±214,748.36, and the 999... bound their precision implies is one the column refuses.
        // A bigint held as a decimal (its values were beyond int) is bounded by long the same way.
        var (lowest, highest) = DeclaredRange(facts?.DeclaredType) is { } range
            ? ((decimal?)range.Lowest, (decimal?)range.Highest)
            : PrecisionRange(facts);

        AddRange(property, lowest, highest, facts, maxFirst: true);
    }

    /// <summary>What decimal(P,S)'s integer digits allow, or no bound when P or S is unknown.</summary>
    private static (decimal? Lowest, decimal? Highest) PrecisionRange(TypeFacts? facts)
    {
        if (facts?.Precision is not { } precision || facts.Scale is not { } places)
            return (null, null);

        var integerDigits = precision - places;

        if (integerDigits <= 0 || integerDigits > 28)
            return (null, null);

        var bound = decimal.Parse(
            new string('9', integerDigits) + "." + new string('9', places),
            System.Globalization.CultureInfo.InvariantCulture);

        return (-bound, bound);
    }

    /// <summary>
    /// Records the type's range, narrowed by any a CHECK constraint declared
    /// (<see cref="TypeFacts.MinValue"/>, <see cref="TypeFacts.MaxValue"/>): a tinyint age checked
    /// to 16..99 is 16..99, not 0..255. Either end may be absent.
    /// </summary>
    private static void AddRange(
        PropertyDefinition property, decimal? lowest, decimal? highest, TypeFacts? facts, bool maxFirst)
    {
        if (facts?.MinValue is { } checkedLowest)
            lowest = lowest is { } l ? Math.Max(l, checkedLowest) : checkedLowest;

        if (facts?.MaxValue is { } checkedHighest)
            highest = highest is { } h ? Math.Min(h, checkedHighest) : checkedHighest;

        var min = lowest is { } low ? new PropertyRule { Name = "minvalue", MinValue = low } : null;
        var max = highest is { } high ? new PropertyRule { Name = "maxvalue", MaxValue = high } : null;

        foreach (var rule in maxFirst ? new[] { max, min } : [min, max])
            if (rule is not null)
                property.Rules.Add(rule);
    }

    /// <summary>The range SQL Server's money or smallmoney holds, or a bigint held as a decimal
    /// because its values are beyond int; null for any other type.</summary>
    private static (decimal Lowest, decimal Highest)? DeclaredRange(string? declaredType)
        => declaredType?.ToLowerInvariant() switch
        {
            "bigint" => (long.MinValue, long.MaxValue),
            "money" => (long.MinValue / 10000m, long.MaxValue / 10000m),
            "smallmoney" => (int.MinValue / 10000m, int.MaxValue / 10000m),
            _ => null
        };

    /// <summary>The bounds SqlTypeMap collapses away when it maps every integer width to int,
    /// narrowed by any CHECK constraint.</summary>
    private static void AddIntegerRange(PropertyDefinition property, TypeFacts? facts)
    {
        var (min, max) = facts?.DeclaredType?.ToLowerInvariant() switch
        {
            "tinyint" => ((decimal?)0m, (decimal?)255m),
            "smallint" => (-32768m, 32767m),
            _ => (null, null)
        };

        AddRange(property, min, max, facts, maxFirst: false);
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
