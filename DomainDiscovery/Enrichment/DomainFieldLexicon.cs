namespace DomainDiscovery.Enrichment;

/// <summary>
/// The properties a known domain entity carries, curated by hand.
/// </summary>
/// <remarks>
/// <para>
/// Article prose cannot supply per-entity fields. Two independent strategies were measured
/// against the Recipe, Payroll and Library articles and both fail: "<i>X</i> of <i>Y</i>" and
/// possessive patterns resolve to adjectives and sentence fragments, and the verb-mediated
/// has-a relation the NLE does extract ("<i>X</i> contains <i>A</i>, <i>B</i>") yields zero
/// hits on the Recipe article, because the subject of its one composition sentence is the
/// relative pronoun "that", which the dependency parser cannot resolve back to "recipe".
/// </para>
/// <para>
/// So the knowledge is curated, exactly as the English lexicon and the business-noun list
/// already are. Knowing an entity's fields is also what makes it an entity at all: a harvested
/// noun with no known shape ("Century", "Tablet") is prose, not a domain class.
/// </para>
/// </remarks>
public static class DomainFieldLexicon
{
    /// <summary>A field's name and its C# type, as declared on the generated class.</summary>
    public readonly record struct FieldShape(string Name, string Type);

    /// <summary>The fields of <paramref name="entityName"/>, or empty when it is not known.</summary>
    public static IReadOnlyList<FieldShape> Fields(string entityName)
        => Entities.TryGetValue(entityName ?? string.Empty, out var fields)
            ? fields
            : Array.Empty<FieldShape>();

    /// <summary>Whether <paramref name="entityName"/> names a domain entity we can shape.</summary>
    public static bool Knows(string entityName) => Fields(entityName).Count > 0;

    private static FieldShape Text(string name) => new(name, "string");
    private static FieldShape Money(string name) => new(name, "decimal");
    private static FieldShape Count(string name) => new(name, "int");
    private static FieldShape When(string name) => new(name, "DateTime");
    private static FieldShape Flag(string name) => new(name, "bool");

    /// <summary>Keyed on the singular entity name, as <c>NameNormalizer.ToEntityName</c> emits it.</summary>
    private static readonly Dictionary<string, FieldShape[]> Entities =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // --- Cooking -------------------------------------------------------------
            ["Recipe"] = [Text("Name"), Count("Servings"), Count("PrepMinutes"), Text("Instructions")],
            ["Ingredient"] = [Text("Name"), Money("Quantity"), Text("Unit")],
            ["Cookbook"] = [Text("Title"), Text("Author"), When("Published")],
            ["Meal"] = [Text("Name"), When("ServedOn")],
            ["Dish"] = [Text("Name"), Text("Course")],

            // --- Payroll and accounting ---------------------------------------------
            ["Payroll"] = [Text("Reference"), When("PeriodStart"), When("PeriodEnd"), Money("Total")],
            ["Employee"] = [Text("FullName"), Text("Email"), When("HireDate"), Money("Salary")],
            ["Employer"] = [Text("Name"), Text("Address")],
            ["Company"] = [Text("Name"), Text("Address")],
            ["Department"] = [Text("Name"), Text("Manager")],
            ["Salary"] = [Money("Amount"), Text("Currency")],
            ["Wage"] = [Money("HourlyRate"), Money("Hours")],
            ["Overtime"] = [Money("Hours"), Money("Rate")],
            ["Deduction"] = [Text("Name"), Money("Amount")],
            ["Garnishment"] = [Text("Reason"), Money("Amount")],
            ["Tax"] = [Text("Name"), Money("Rate")],
            ["Bonus"] = [Text("Reason"), Money("Amount"), When("AwardedOn")],
            ["Benefit"] = [Text("Name"), Text("Description")],
            ["Income"] = [Text("Source"), Money("Amount")],
            ["Timesheet"] = [When("WeekStarting"), Money("HoursWorked"), Flag("Approved")],

            // --- Commerce ------------------------------------------------------------
            ["Invoice"] = [Text("Number"), Money("Amount"), When("DueDate"), Flag("Paid")],
            ["Payment"] = [Money("Amount"), When("PaidOn"), Text("Method")],
            ["Receipt"] = [Text("Number"), Money("Amount"), When("IssuedOn")],
            ["Refund"] = [Money("Amount"), Text("Reason"), When("IssuedOn")],
            ["Expense"] = [Text("Description"), Money("Amount"), When("IncurredOn")],
            ["Budget"] = [Text("Name"), Money("Amount")],
            ["Order"] = [Text("Reference"), When("PlacedOn"), Money("Total")],
            ["Product"] = [Text("Name"), Text("Sku"), Money("Price")],
            ["Customer"] = [Text("FullName"), Text("Email"), Text("Phone")],
            ["Client"] = [Text("FullName"), Text("Email"), Text("Phone")],
            ["Supplier"] = [Text("Name"), Text("Email")],
            ["Vendor"] = [Text("Name"), Text("Email")],
            ["Account"] = [Text("Name"), Money("Balance")],
            ["Transaction"] = [When("OccurredOn"), Money("Amount"), Text("Description")],
            ["Contract"] = [Text("Reference"), When("StartDate"), When("EndDate")],
            ["Subscription"] = [Text("Plan"), When("RenewsOn"), Money("Price")],
            ["Shipment"] = [Text("Reference"), When("ShippedOn"), Text("Carrier")],
            ["Inventory"] = [Text("Sku"), Count("Quantity")],
            ["Warehouse"] = [Text("Name"), Text("Address")],

            // --- Library -------------------------------------------------------------
            ["Library"] = [Text("Name"), Text("Address")],
            ["Book"] = [Text("Title"), Text("Author"), Text("Isbn"), When("Published")],
            ["Material"] = [Text("Title"), Text("Format")],
            ["Member"] = [Text("FullName"), Text("Email"), When("JoinDate")],
            ["Loan"] = [When("BorrowedOn"), When("DueDate"), Flag("Returned")],
            ["Author"] = [Text("FullName")],
            ["Publisher"] = [Text("Name"), Text("Address")],
            ["Catalog"] = [Text("Name"), Text("Description")],

            // --- Access control ------------------------------------------------------
            ["User"] = [Text("UserName"), Text("Email"), Flag("Active")],
            ["Role"] = [Text("Name"), Text("Description")],
            ["Permission"] = [Text("Name"), Text("Description")],
        };
}
