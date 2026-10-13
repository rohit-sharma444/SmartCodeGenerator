using System;
using System.Collections.Generic;
using System.Linq;
using AppScaffolding;
using CodeGenerationEngine.Models;
using DomainDiscovery.Model;

namespace CodeGenerator.Tests.App;

[TestClass]
public class DomainModelMapperTests
{
    private static CandidateDomainModel ModelOf(params string[] entityNames)
    {
        var model = new CandidateDomainModel();
        foreach (var name in entityNames)
            model.Entities.Add(new CandidateEntity { Name = name });
        return model;
    }

    [TestMethod]
    public void ToClassDefinitions_RenamesEntities_ThatCollideWithTypesUsedByGeneratedCode()
    {
        // Entities land in <App>.Models, but a name shared with a type the generated
        // app declares or imports by simple name binds to the wrong type.
        var classes = DomainModelMapper.ToClassDefinitions(
            ModelOf("Type", "Convert", "JsonStore", "Window"));

        CollectionAssert.AreEqual(
            new[] { "TypeItem", "ConvertItem", "JsonStoreItem", "WindowItem" },
            classes.Select(c => c.Name).ToList());
    }

    [TestMethod]
    public void ToClassDefinitions_RenamesEntitiesThatCollideWithGeneratedWpfTypes()
    {
        // `new EntityViewModel<EntityViewModel>(...)` would bind its type argument to
        // the view model rather than the model, since the current namespace beats a
        // using-imported one. `Application` is both a common noun and System.Windows.Application.
        var classes = DomainModelMapper.ToClassDefinitions(
            ModelOf("Application", "MainWindow", "EntityViewModel", "RelayCommand"));

        CollectionAssert.AreEqual(
            new[] { "ApplicationItem", "MainWindowItem", "EntityViewModelItem", "RelayCommandItem" },
            classes.Select(c => c.Name).ToList());
    }

    [TestMethod]
    public void ToClassDefinitions_RenamesEntitiesThatCollideWithTheGeneratedNamespaces()
    {
        // Discovering "point of sale" really does yield a class called Data, and the generated app
        // really did fail to compile on it: MainViewModel opens with `using <App>.Data;`, so the
        // simple name Data means the NAMESPACE there, and CS0118 says so -- 'Data' is a namespace
        // but is used like a type.
        var classes = DomainModelMapper.ToClassDefinitions(
            ModelOf("Data", "Models", "ViewModels", "Views"));

        CollectionAssert.AreEqual(
            new[] { "DataItem", "ModelsItem", "ViewModelsItem", "ViewsItem" },
            classes.Select(c => c.Name).ToList());
    }

    [TestMethod]
    public void An_entity_named_after_a_global_namespace_still_compiles()
    {
        // "a library management system" on the built-in-domain path kept a class called System,
        // and the generated app failed: SystemRepository declares `Repository<System>` inside
        // <App>.Data, where the simple name System finds the global NAMESPACE before any using
        // directive is consulted -- CS0118. Compiled, not substring-checked: the defect is a
        // name-lookup rule, and only the compiler applies it.
        var classes = DomainModelMapper.ToClassDefinitions(ModelOf("System", "Microsoft", "Book"));

        var sources = new List<(string Path, string Code)>
        {
            ("GlobalUsings.cs", "global using System;\nglobal using System.Collections.Generic;"),
            ("Data/Repository.cs", "namespace Acme.Data;\npublic class Repository<T> { }"),
        };
        foreach (var definition in classes)
        {
            sources.Add(("Models/" + definition.Name + ".cs",
                CodeGenerationEngine.Core.RoslynClassGenerator.Generate(definition, "Acme.Models")));
            sources.Add(("Data/" + definition.Name + "Repository.cs",
                RepositoryTemplate.Typed("Acme", definition.Name)));
        }

        var errors = CodeGenerationEngine.Core.Compiler.CompileSources(sources).Diagnostics
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();

        Assert.IsEmpty(errors, string.Join("\n", errors));
        CollectionAssert.Contains(classes.Select(c => c.Name).ToList(), "Book");
    }

    [TestMethod]
    public void ToClassDefinitions_RenamesAnyEntityWhoseNameEndsInValidator()
    {
        // The generated validators live in <App>.Validation and are named <Model>Validator, so an
        // entity called OrderValidator collides with the validator generated for Order. C# resolves
        // a simple name against the enclosing namespace first, so typeof(OrderValidator) inside
        // ValidatorFactory would bind to Order's VALIDATOR, the arm would never match, and the
        // entity would silently degrade to NullValidator -- no error, no validation.
        var classes = DomainModelMapper.ToClassDefinitions(ModelOf("Order", "OrderValidator"));

        CollectionAssert.AreEqual(
            new[] { "Order", "OrderValidatorItem" },
            classes.Select(c => c.Name).ToList());
    }

    [TestMethod]
    public void ToClassDefinitions_RenamesEntity_NamedRepository()
    {
        // The generated app declares a base Repository<T> in <App>.Data. An entity also
        // called Repository would make `Repository<Repository>` bind its type argument to
        // that base type rather than the model, so the name is reserved.
        var classes = DomainModelMapper.ToClassDefinitions(ModelOf("Repository"));

        Assert.AreEqual("RepositoryItem", classes[0].Name);
    }

    [TestMethod]
    public void ToClassDefinitions_LeavesOrdinaryNamesAlone()
    {
        var classes = DomainModelMapper.ToClassDefinitions(ModelOf("Employee", "Payroll", "Deduction"));

        CollectionAssert.AreEqual(
            new[] { "Employee", "Payroll", "Deduction" },
            classes.Select(c => c.Name).ToList());
    }

    [TestMethod]
    public void ToClassDefinitions_KeepsClassNamesUnique_WhenRenamingWouldCollide()
    {
        // "Type" wants to become "TypeItem", but that name is already taken.
        var classes = DomainModelMapper.ToClassDefinitions(ModelOf("Type", "TypeItem"));

        var names = classes.Select(c => c.Name).ToList();
        Assert.AreEqual(2, names.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            "generated class names must be unique");
        CollectionAssert.Contains(names, "TypeItem");
    }

    [TestMethod]
    public void ToClassDefinitions_KeepsClassNamesUnique_WhenTwoEntitiesDifferOnlyByCase()
    {
        // Each class is written to <Name>.cs, and Windows filenames are case-insensitive.
        var classes = DomainModelMapper.ToClassDefinitions(ModelOf("Invoice", "invoice"));

        Assert.AreEqual(2, classes.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [TestMethod]
    public void ToClassDefinitions_PrependsIntId_ThenMapsFields()
    {
        var model = new CandidateDomainModel();
        var employee = new CandidateEntity { Name = "Employee" };
        employee.Fields.Add(new CandidateField { Name = "Salary", Type = "decimal" });
        employee.Fields.Add(new CandidateField { Name = "FullName", Type = "string" });
        model.Entities.Add(employee);

        var classes = DomainModelMapper.ToClassDefinitions(model);

        Assert.HasCount(1, classes);
        Assert.AreEqual("Employee", classes[0].Name);

        // Id first, then the domain's own fields in order. The audit fields (CreatedOn,
        // ModifiedOn) are appended after these, so this checks the prefix rather than the
        // whole list -- the ordering it exists to pin down is unchanged.
        CollectionAssert.AreEqual(
            new[] { "Id", "Salary", "FullName" },
            classes[0].Properties.Select(p => p.Name).Take(3).ToList());
        Assert.AreEqual("int", classes[0].Properties[0].Type);
        Assert.AreEqual("decimal", classes[0].Properties[1].Type);
    }

    [TestMethod]
    public void ToClassDefinitions_ExistingIdField_NotDuplicated()
    {
        var model = new CandidateDomainModel();
        var order = new CandidateEntity { Name = "Order" };
        order.Fields.Add(new CandidateField { Name = "id", Type = "int" });
        order.Fields.Add(new CandidateField { Name = "Total", Type = "decimal" });
        model.Entities.Add(order);

        var classes = DomainModelMapper.ToClassDefinitions(model);

        var idCount = classes[0].Properties.Count(
            p => string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(1, idCount);

        // The entity's own Id is kept in place rather than a second one prepended; the audit
        // fields follow the domain's fields.
        CollectionAssert.AreEqual(
            new[] { "id", "Total" },
            classes[0].Properties.Select(p => p.Name).Take(2).ToList());
    }

    [TestMethod]
    public void ToClassDefinitions_FieldlessEntity_GetsIntId()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Payroll" });

        var classes = DomainModelMapper.ToClassDefinitions(model);

        // An entity with no fields of its own still gets a key -- and, now, the audit fields
        // every class carries. What matters here is that the key is a leading int Id.
        Assert.AreEqual("Id", classes[0].Properties[0].Name);
        Assert.AreEqual("int", classes[0].Properties[0].Type);
        CollectionAssert.AreEqual(
            new[] { "Id", "CreatedOn", "ModifiedOn" },
            classes[0].Properties.Select(p => p.Name).ToList());
    }

    [TestMethod]
    public void ToClassDefinitions_MultipleEntities_PreservesNamesAndOrder()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Payroll" });
        model.Entities.Add(new CandidateEntity { Name = "Deduction" });

        var classes = DomainModelMapper.ToClassDefinitions(model);

        CollectionAssert.AreEqual(
            new[] { "Payroll", "Deduction" },
            classes.Select(c => c.Name).ToList());
    }

    [TestMethod]
    public void An_entity_named_Validation_is_renamed_to_avoid_the_namespace()
    {
        var classes = DomainModelMapper.ToClassDefinitions(ModelOf("Validation"));

        Assert.HasCount(1, classes);
        Assert.AreEqual("ValidationItem", classes[0].Name);
    }
}

[TestClass]
public class DomainModelMapperNavigationTests
{
    private static CandidateDomainModel OrderReferencesCustomer()
    {
        var model = new CandidateDomainModel();

        var customer = new CandidateEntity { Name = "Customer" };
        customer.Fields.Add(new CandidateField { Name = "FullName", Type = "string" });

        var order = new CandidateEntity { Name = "Order" };
        order.Fields.Add(new CandidateField { Name = "CustomerId", Type = "int" });
        order.Relationships.Add(new CandidateRelationship { Target = "Customer", Role = "references" });

        model.Entities.Add(customer);
        model.Entities.Add(order);
        return model;
    }

    [TestMethod]
    public void Gives_the_referencing_class_a_navigation_property()
    {
        var order = DomainModelMapper.ToClassDefinitions(OrderReferencesCustomer())
            .Single(c => c.Name == "Order");

        var nav = order.Properties.Single(p => p.Name == "Customer");

        Assert.AreEqual("Customer", nav.Type);
        CollectionAssert.Contains(nav.Attributes, "JsonIgnore");
    }

    [TestMethod]
    public void Gives_the_referenced_class_the_inverse_collection()
    {
        var customer = DomainModelMapper.ToClassDefinitions(OrderReferencesCustomer())
            .Single(c => c.Name == "Customer");

        var inverse = customer.Properties.Single(p => p.Name == "Orders");

        Assert.AreEqual("List<Order>", inverse.Type);
        CollectionAssert.Contains(inverse.Attributes, "JsonIgnore");
    }

    // JsonStore persists with System.Text.Json, and Order.Customer -> Customer.Orders -> Order is
    // a reference cycle that throws on save. [JsonIgnore] is what keeps persistence FK-only, and
    // it needs this using or the generated file does not compile.
    [TestMethod]
    public void Declares_the_using_that_JsonIgnore_needs()
    {
        var order = DomainModelMapper.ToClassDefinitions(OrderReferencesCustomer())
            .Single(c => c.Name == "Order");

        CollectionAssert.Contains(order.Usings, "System.Text.Json.Serialization");
    }

    [TestMethod]
    public void Keeps_the_foreign_key_alongside_the_navigation_property()
    {
        var order = DomainModelMapper.ToClassDefinitions(OrderReferencesCustomer())
            .Single(c => c.Name == "Order");

        Assert.IsTrue(order.Properties.Any(p => p.Name == "CustomerId" && p.Type == "int"));
    }

    [TestMethod]
    public void Every_class_gets_audit_fields()
    {
        var order = DomainModelMapper.ToClassDefinitions(OrderReferencesCustomer())
            .Single(c => c.Name == "Order");

        Assert.IsTrue(order.Properties.Any(p => p.Name == "CreatedOn" && p.Type == "DateTime"));
        Assert.IsTrue(order.Properties.Any(p => p.Name == "ModifiedOn" && p.Type == "DateTime"));
    }

    [TestMethod]
    public void A_relationship_to_a_class_that_did_not_survive_is_ignored()
    {
        var model = new CandidateDomainModel();
        var order = new CandidateEntity { Name = "Order" };
        order.Relationships.Add(new CandidateRelationship { Target = "Ghost", Role = "references" });
        model.Entities.Add(order);

        var definition = DomainModelMapper.ToClassDefinitions(model).Single();

        // A nav property to a class that is not generated would not compile.
        Assert.IsFalse(definition.Properties.Any(p => p.Name == "Ghost"));
    }

    [TestMethod]
    public void A_class_with_no_relationships_declares_no_json_serialization_using()
    {
        // No relationship means no [JsonIgnore] nav property, so JsonSerializationNamespace is not
        // pulled in. (The class still gets System.ComponentModel.DataAnnotations from its inferred
        // [Key]/validation attributes -- see DomainModelMapperValidationTests -- so this checks the
        // absence of the relationship-only using rather than an empty Usings list.)
        var model = new CandidateDomainModel();
        var plain = new CandidateEntity { Name = "Note" };
        plain.Fields.Add(new CandidateField { Name = "Text", Type = "string" });
        model.Entities.Add(plain);

        CollectionAssert.DoesNotContain(
            DomainModelMapper.ToClassDefinitions(model).Single().Usings,
            "System.Text.Json.Serialization");
    }

    /// <summary>
    /// A navigation property is hidden from a property grid.
    /// </summary>
    /// <remarks>
    /// WinForms builds its record editor from a PropertyGrid over the draft, so every public
    /// property appears — including Order.Customer, which shows as a greyed, unfillable row
    /// because the copy the editor works on has it null ([JsonIgnore] drops it). WPF's emitted
    /// PropertyEditor omits non-scalars by construction, so the same model produced two different
    /// forms. [Browsable(false)] is the frameworks' own way of saying this is not a field, and it
    /// costs the other targets nothing: JSON never saw it, Blazor filters on scalars, and a
    /// DataGridView skips it for free.
    /// </remarks>
    [TestMethod]
    public void Hides_a_navigation_property_from_a_property_grid()
    {
        var classes = DomainModelMapper.ToClassDefinitions(OrderReferencesCustomer());

        var nav = classes.Single(c => c.Name == "Order").Properties.Single(p => p.Name == "Customer");
        var inverse = classes.Single(c => c.Name == "Customer").Properties.Single(p => p.Name == "Orders");

        CollectionAssert.Contains(nav.Attributes, "Browsable(false)");
        CollectionAssert.Contains(inverse.Attributes, "Browsable(false)");
    }

    /// <summary>The using it needs, or the generated class does not compile.</summary>
    [TestMethod]
    public void Declares_the_using_that_Browsable_needs()
    {
        var order = DomainModelMapper.ToClassDefinitions(OrderReferencesCustomer())
            .Single(c => c.Name == "Order");

        CollectionAssert.Contains(order.Usings, "System.ComponentModel");
    }

    /// <summary>
    /// A real field keeps it off. Hiding one of those would take a column out of every list and a
    /// row out of every editor, which is the opposite of the point.
    /// </summary>
    [TestMethod]
    public void A_scalar_property_is_not_hidden()
    {
        var order = DomainModelMapper.ToClassDefinitions(OrderReferencesCustomer())
            .Single(c => c.Name == "Order");

        foreach (var property in order.Properties.Where(p => p.Name != "Customer"))
            CollectionAssert.DoesNotContain(property.Attributes, "Browsable(false)", property.Name);
    }
}

[TestClass]
public class DomainModelMapperValidationTests
{
    private static CandidateDomainModel EmployeeModel()
    {
        var model = new CandidateDomainModel();
        var employee = new CandidateEntity { Name = "Employee" };
        employee.Fields.Add(new CandidateField { Name = "Name", Type = "string" });
        employee.Fields.Add(new CandidateField { Name = "WorkEmail", Type = "string" });
        employee.Fields.Add(new CandidateField { Name = "Bio", Type = "string" });
        employee.Fields.Add(new CandidateField { Name = "Salary", Type = "decimal" });
        model.Entities.Add(employee);
        return model;
    }

    private static PropertyDefinition Prop(CandidateDomainModel model, string name) =>
        DomainModelMapper.ToClassDefinitions(model).Single()
            .Properties.Single(p => p.Name == name);

    [TestMethod]
    public void Id_GetsKeyAttribute()
    {
        CollectionAssert.Contains(Prop(EmployeeModel(), "Id").Attributes, "Key");
    }

    [TestMethod]
    public void NameField_IsLengthBounded_ButNotRequiredWithoutANullabilityFact()
    {
        // This model is a discovered one: no source declared anything about these columns, so
        // there is no IS_NULLABLE to read. `required` used to be given to any field literally
        // called Name or Title; it now follows the fact instead, and an absent fact infers
        // nothing rather than making the app stricter than the user described it. The 200
        // fallback survives, because a text box still needs a bound.
        var name = Prop(EmployeeModel(), "Name");
        Assert.IsFalse(name.Rules.Any(r => r.Name == "required"),
            "an unknown nullability infers no [Required]");
        Assert.IsTrue(name.Rules.Any(r => r.Name == "maxlength" && r.MaxLength == 200),
            "expected [StringLength(200)]");
    }

    [TestMethod]
    public void EmailField_GetsEmailAddressAttribute_AndLength()
    {
        var email = Prop(EmployeeModel(), "WorkEmail");
        CollectionAssert.Contains(email.Attributes, "EmailAddress");
        Assert.IsTrue(email.Rules.Any(r => r.Name == "maxlength" && r.MaxLength == 200));
        Assert.IsFalse(email.Rules.Any(r => r.Name == "required"),
            "a string with no nullability fact is not required");
    }

    [TestMethod]
    public void PlainString_IsOnlyLengthBounded()
    {
        var bio = Prop(EmployeeModel(), "Bio");
        Assert.IsTrue(bio.Rules.Any(r => r.Name == "maxlength" && r.MaxLength == 200));
        Assert.IsFalse(bio.Rules.Any(r => r.Name == "required"));
        Assert.IsEmpty(bio.Attributes);
    }

    [TestMethod]
    public void NonString_NonId_IsUntouched()
    {
        var salary = Prop(EmployeeModel(), "Salary");
        Assert.IsEmpty(salary.Rules);
        Assert.IsEmpty(salary.Attributes);
    }

    [TestMethod]
    public void AClassWhoseOnlyAnnotationComesFromARule_StillDeclaresTheUsing()
    {
        // RoslynClassGenerator renders the required/maxlength/minlength RULES as
        // [Required]/[StringLength]/[MinLength] on the emitted model, so a class carrying one
        // needs the using even though nothing put an entry in PropertyDefinition.Attributes.
        // An entity that declares its own non-int Id gets no [Key], and a string field with no
        // "email" or "phone" in its name gets no format attribute -- so this class reaches the
        // generator with attributes and, without the rule scan, no using to resolve them.
        var model = new CandidateDomainModel();
        var entity = new CandidateEntity { Name = "Ledger" };
        entity.Fields.Add(new CandidateField { Name = "Id", Type = "string" });
        entity.Fields.Add(new CandidateField { Name = "Reference", Type = "string" });
        model.Entities.Add(entity);

        var definition = DomainModelMapper.ToClassDefinitions(model).Single();
        var source = CodeGenerationEngine.Core.RoslynClassGenerator.Generate(definition, "Probe.Models");

        Assert.Contains("[StringLength(200)]", source, "the rule reaches the model as an attribute");
        CollectionAssert.Contains(definition.Usings, "System.ComponentModel.DataAnnotations");
    }

    [TestMethod]
    public void ClassWithValidation_DeclaresDataAnnotationsUsing()
    {
        var employee = DomainModelMapper.ToClassDefinitions(EmployeeModel()).Single();
        CollectionAssert.Contains(employee.Usings, "System.ComponentModel.DataAnnotations");
    }

    [TestMethod]
    public void NavigationProperty_IsNotGivenValidation()
    {
        // Order -> Customer: the Customer nav property carries what a nav property is given and
        // nothing a field would be.
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity { Name = "Customer" });
        var order = new CandidateEntity { Name = "Order" };
        order.Relationships.Add(new CandidateRelationship { Target = "Customer", Role = "references" });
        model.Entities.Add(order);

        var nav = DomainModelMapper.ToClassDefinitions(model)
            .Single(c => c.Name == "Order").Properties.Single(p => p.Name == "Customer");

        CollectionAssert.AreEqual(new[] { "JsonIgnore", "Browsable(false)" }, nav.Attributes);
        Assert.IsEmpty(nav.Rules);
    }


    /// <summary>A model with no relationships declares neither the attribute nor its using.</summary>
    [TestMethod]
    public void A_class_with_no_navigation_declares_no_component_model_using()
    {
        var employee = DomainModelMapper.ToClassDefinitions(EmployeeModel()).Single();

        CollectionAssert.DoesNotContain(employee.Usings, "System.ComponentModel");
    }
}

[TestClass]
public class DomainModelMapperProposalTests
{
    /// <summary>
    /// A Customer entity shaped like <see cref="ValidatorFixtures.PlainCustomer"/> -- Id:int,
    /// Code:string, Status:string -- with a Status-field proposal task 10 would have produced,
    /// accepted or not per <paramref name="accepted"/>.
    /// </summary>
    private static CandidateDomainModel ModelWithProposal(bool accepted)
    {
        var model = new CandidateDomainModel();
        var customer = new CandidateEntity { Name = "Customer" };
        customer.Fields.Add(new CandidateField { Name = "Id", Type = "int" });
        customer.Fields.Add(new CandidateField { Name = "Code", Type = "string" });

        var status = new CandidateField { Name = "Status", Type = "string" };
        status.Proposals.Add(new RuleProposal
        {
            Kind = RuleProposalKind.Status,
            Rule = new PropertyRule { Name = "allowedvalues", AllowedValues = ["Draft", "Posted"] },
            Evidence = "holds in every row",
            Accepted = accepted
        });
        customer.Fields.Add(status);

        model.Entities.Add(customer);
        return model;
    }

    [TestMethod]
    public void An_accepted_proposal_becomes_a_rule()
    {
        var model = ModelWithProposal(accepted: true);

        var classes = DomainModelMapper.ToClassDefinitions(model, out _);

        var status = classes.Single().Properties.Single(p => p.Name == "Status");
        var rule = status.Rules.Single(r => r.Name == "allowedvalues");
        CollectionAssert.AreEquivalent(new[] { "Draft", "Posted" }, rule.AllowedValues.ToArray());
    }

    [TestMethod]
    public void An_unaccepted_proposal_becomes_nothing()
    {
        var model = ModelWithProposal(accepted: false);

        var classes = DomainModelMapper.ToClassDefinitions(model, out _);

        // The decision of this phase: nothing inferred from the CONTENTS of data reaches a generated
        // app unless someone picked it. A scripted build gets none of it.
        Assert.IsFalse(
            classes.Single().Properties.Single(p => p.Name == "Status").Rules
                .Any(r => r.Name == "allowedvalues"));
    }

    /// <summary>A Book that belongs to a Library, as discovery shapes it: the key and the
    /// relationship both name the ENTITY.</summary>
    private static CandidateDomainModel LibraryModel()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity
        {
            Name = "Library",
            Fields = { new CandidateField { Name = "Name", Type = "string" } },
        });
        model.Entities.Add(new CandidateEntity
        {
            Name = "Book",
            Fields =
            {
                new CandidateField { Name = "Title", Type = "string" },
                new CandidateField { Name = "LibraryId", Type = "int" },
            },
            Relationships = { new CandidateRelationship { Target = "Library", Role = "references" } },
        });
        return model;
    }

    /// <summary>
    /// "a library that lends books" names its app Library, so the Library entity becomes
    /// LibraryItem — and Book kept <c>int LibraryId</c> beside a <c>LibraryItem LibraryItem</c>
    /// navigation that the resolver, which looks for LibraryItemId, never paired with it. No
    /// lookup, no FK rule, no inverse load.
    /// </summary>
    [TestMethod]
    public void A_renamed_class_takes_the_foreign_keys_to_it_along()
    {
        var classes = DomainModelMapper.ToClassDefinitions(
            LibraryModel(), out _, reserved: ["Library"]);

        var book = classes.Single(c => c.Name == "Book");
        CollectionAssert.AreEqual(
            new[] { "LibraryItem" },
            ForeignKeys.TargetsOf(book, ForeignKeys.ClassNameSet(classes)).ToList());
        Assert.IsFalse(book.Properties.Any(p => p.Name == "LibraryId"), "the old key is still there");
    }

    /// <summary>The seed rows still say LibraryId — they were read under the entity's name — so
    /// the renamed key has to find its value there or every book loses its library.</summary>
    [TestMethod]
    public void A_renamed_foreign_key_still_reads_its_seed_value()
    {
        var model = LibraryModel();
        var classes = DomainModelMapper.ToClassDefinitions(model, out var names, reserved: ["Library"]);

        var data = new ImportedData(
            new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Library"] = [new Dictionary<string, string?> { ["Name"] = "Central" }],
                ["Book"] = [new Dictionary<string, string?> { ["Title"] = "Emma", ["LibraryId"] = "1" }],
            },
            []);

        var book = SeedFileWriter.Files(classes, names, data, out _).Single(f => f.FileName == "Book.json");

        StringAssert.Contains(book.Json, "\"LibraryItemId\": 1");
    }

    /// <summary>
    /// Discovery can give Shipment both <c>string Customer</c> and <c>int CustomerId</c> with a
    /// relationship to Customer. The string's name blocked the navigation, so the app showed a
    /// free-text box for a customer it also kept a key to. The relationship wins.
    /// </summary>
    [TestMethod]
    public void A_text_field_named_for_a_related_class_gives_way_to_the_relationship()
    {
        var model = new CandidateDomainModel();
        model.Entities.Add(new CandidateEntity
        {
            Name = "Customer",
            Fields = { new CandidateField { Name = "Name", Type = "string" } },
        });
        model.Entities.Add(new CandidateEntity
        {
            Name = "Shipment",
            Fields =
            {
                new CandidateField { Name = "Customer", Type = "string" },
                new CandidateField { Name = "CustomerId", Type = "int" },
            },
            Relationships = { new CandidateRelationship { Target = "Customer", Role = "references" } },
        });

        var shipment = DomainModelMapper.ToClassDefinitions(model).Single(c => c.Name == "Shipment");

        Assert.AreEqual("Customer", shipment.Properties.Single(p => p.Name == "Customer").Type);
    }
}
