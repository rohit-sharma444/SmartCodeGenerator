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
/// already are. That measurement is still the reason this table exists and is hand-maintained
/// rather than derived. What curation no longer decides is whether an entity exists at all: a
/// harvested noun with no entry here ("Century", "Tablet") used to be silently deleted as
/// prose, which is what deleted sixteen of twenty candidates from the Point of sale article.
/// This table is now the FIRST of two shape sources, not the only one — <see
/// cref="ArchetypeLexicon"/> shapes anything this table does not know from the candidate's own
/// Wikipedia definition, and only an actually abstract definition ("a system is a group of
/// interacting elements") rejects a candidate. Absent from this table just means "look further,"
/// never "not a class."
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

    /// <summary>
    /// The curated <see cref="EntityRole"/> of <paramref name="entityName"/>, or
    /// <see cref="EntityRole.None"/> when it is not annotated. A curated class can never
    /// resolve an archetype (it is shaped here first, so <see cref="ArchetypeLexicon"/> never
    /// runs for it) and its kind cannot be recovered from Wikipedia either — <c>Order</c> is a
    /// disambiguation page with no definition at all, and it is half the strongest pair in the
    /// Point of sale article. So every curated entity's Role must be given explicitly, the same
    /// way its fields are.
    /// </summary>
    public static EntityRole Role(string entityName)
        => Roles.TryGetValue(entityName ?? string.Empty, out var role) ? role : EntityRole.None;

    /// <summary>
    /// Whether <paramref name="entityName"/> has an explicit entry in the Role table, as
    /// distinct from merely resolving to <see cref="EntityRole.None"/> by lookup miss. Two
    /// curated entities (Role, Permission) are deliberately annotated None — they are not
    /// something another class references — and that must be indistinguishable in result from
    /// a forgotten entry only by looking at <em>presence</em>, not value. Exists solely so a
    /// test can enumerate every curated entity and catch one added to <see cref="Entities"/>
    /// without a matching entry here, before it silently ships role-less.
    /// </summary>
    public static bool HasAnnotatedRole(string entityName) => Roles.ContainsKey(entityName ?? string.Empty);

    /// <summary>Every entity name this table curates, so a test can walk the full set without
    /// hand-maintaining a duplicate list that could quietly drift out of sync.</summary>
    public static IReadOnlyCollection<string> KnownEntityNames => Entities.Keys;

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

    /// <summary>
    /// Every curated entity's kind, keyed exactly as <see cref="Entities"/> is. Kept as its
    /// own table (not folded into <see cref="FieldShape"/>) so that adding a role never touches
    /// the field shapes, and so <see cref="HasAnnotatedRole"/> can tell "annotated None" apart
    /// from "never annotated" by checking key presence rather than the resolved value.
    /// </summary>
    private static readonly Dictionary<string, EntityRole> Roles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // --- Party -----------------------------------------------------------------
            ["Employee"] = EntityRole.Party,
            ["Customer"] = EntityRole.Party,
            ["Client"] = EntityRole.Party,
            ["Supplier"] = EntityRole.Party,
            ["Vendor"] = EntityRole.Party,
            ["Member"] = EntityRole.Party,
            ["Author"] = EntityRole.Party,
            ["User"] = EntityRole.Party,

            // --- Place -------------------------------------------------------------------
            ["Employer"] = EntityRole.Place,
            ["Company"] = EntityRole.Place,
            ["Department"] = EntityRole.Place,
            ["Library"] = EntityRole.Place,
            ["Publisher"] = EntityRole.Place,
            ["Warehouse"] = EntityRole.Place,

            // --- Document ------------------------------------------------------------------
            ["Recipe"] = EntityRole.Document,
            ["Cookbook"] = EntityRole.Document,
            ["Payroll"] = EntityRole.Document,
            ["Timesheet"] = EntityRole.Document,
            ["Invoice"] = EntityRole.Document,
            ["Receipt"] = EntityRole.Document,
            ["Budget"] = EntityRole.Document,
            ["Order"] = EntityRole.Document,
            ["Contract"] = EntityRole.Document,
            ["Subscription"] = EntityRole.Document,
            ["Shipment"] = EntityRole.Document,
            ["Loan"] = EntityRole.Document,
            ["Catalog"] = EntityRole.Document,
            ["Account"] = EntityRole.Document,
            ["Meal"] = EntityRole.Document,

            // --- Transaction ---------------------------------------------------------------
            ["Salary"] = EntityRole.Transaction,
            ["Wage"] = EntityRole.Transaction,
            ["Overtime"] = EntityRole.Transaction,
            ["Deduction"] = EntityRole.Transaction,
            ["Garnishment"] = EntityRole.Transaction,
            ["Tax"] = EntityRole.Transaction,
            ["Bonus"] = EntityRole.Transaction,
            ["Income"] = EntityRole.Transaction,
            ["Payment"] = EntityRole.Transaction,
            ["Refund"] = EntityRole.Transaction,
            ["Expense"] = EntityRole.Transaction,
            ["Transaction"] = EntityRole.Transaction,

            // --- Item ------------------------------------------------------------------
            ["Ingredient"] = EntityRole.Item,
            ["Dish"] = EntityRole.Item,
            ["Product"] = EntityRole.Item,
            ["Inventory"] = EntityRole.Item,
            ["Book"] = EntityRole.Item,
            ["Material"] = EntityRole.Item,
            ["Benefit"] = EntityRole.Item,

            // --- None: not something another class references --------------------------
            ["Role"] = EntityRole.None,
            ["Permission"] = EntityRole.None,
        };
}
