using CodeGenerationEngine.Core;
using CodeGenerationEngine.Core.Relationships;
using CodeGenerationEngine.Core.Validation;
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
                definition.Properties.Add(new PropertyDefinition
                {
                    Name = field.Name,
                    Type = field.Type,
                    Facts = field.Facts,
                    Proposals = [.. field.Proposals]
                });

            classes.Add(definition);
        }

        AddNavigationProperties(model, classes, names);
        AddAuditFields(classes);
        AddValidation(classes);
        AddEntityValidation(classes, model, names);

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

            added |= definition.Properties.Any(
                p => p.Rules.Any(r => DataAnnotationRuleNames.Contains(r.Name)));

            if (added && !definition.Usings.Contains(DataAnnotationsNamespace))
                definition.Usings.Add(DataAnnotationsNamespace);
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

        switch (property.Type)
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

                return AddFormatAttribute(property);

            case "int":
                AddIntegerRange(property, facts?.DeclaredType);
                return false;

            case "decimal":
                AddDecimalRules(property, facts);
                return false;

            case "DateTime":
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
    private static bool NameContainsWord(string name, string word)
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

        if (facts?.Precision is not { } precision || facts.Scale is not { } places)
            return;

        var integerDigits = precision - places;

        if (integerDigits <= 0 || integerDigits > 28)
            return;

        var bound = decimal.Parse(
            new string('9', integerDigits) + "." + new string('9', places),
            System.Globalization.CultureInfo.InvariantCulture);

        property.Rules.Add(new PropertyRule { Name = "maxvalue", MaxValue = bound });
        property.Rules.Add(new PropertyRule { Name = "minvalue", MinValue = -bound });
    }

    /// <summary>The bounds SqlTypeMap collapses away when it maps every integer width to int.</summary>
    private static void AddIntegerRange(PropertyDefinition property, string? declaredType)
    {
        var (min, max) = declaredType?.ToLowerInvariant() switch
        {
            "tinyint" => (0m, 255m),
            "smallint" => (-32768m, 32767m),
            _ => (0m, 0m)
        };

        if (min == 0m && max == 0m)
            return;

        property.Rules.Add(new PropertyRule { Name = "minvalue", MinValue = min });
        property.Rules.Add(new PropertyRule { Name = "maxvalue", MaxValue = max });
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
