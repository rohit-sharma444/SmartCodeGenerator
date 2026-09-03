namespace DomainDiscovery.Llm;

/// <summary>
/// What the model is told, for each of the three ways it is asked.
/// </summary>
/// <remarks>
/// Lifted out of <see cref="OllamaClient"/> because there are three of them now and they are the
/// part of this code that is hardest to get right and easiest to make worse: every clause was put
/// in by measuring real output, and no unit test can see any of it. A schema-validity test passes
/// whatever the wording says.
/// <para>
/// <b>If you edit any of these, re-measure against a running model.</b>
/// </para>
/// </remarks>
internal static class OllamaPrompts
{
    /// <summary>
    /// Rules about naming, shared by the one-shot ask and the roster ask, because both of them
    /// choose the names and both got them wrong in the same ways.
    /// </summary>
    /// <remarks>
    /// Measured, clause by clause, on qwen2.5:3b:
    /// <list type="bullet">
    /// <item>Naming the roles without forbidding them as names returned classes literally called
    /// Item and Transaction.</item>
    /// <item>The "never glue the role word on" clause exists because the roster ask — which does
    /// not write properties, and so has less to say about each class — returned ProductItem,
    /// SaleLineItem and PaymentMethod where the one-shot ask returned Product, SaleLine and
    /// Payment. Adding this clause returned all three to the short forms.</item>
    /// <item>The "name the thing, not the system" clause exists because a terse description made
    /// the model prefix everything: InventoryItem, InventoryLog, and — from the roster ask —
    /// RetailShop for what should be Store.</item>
    /// </list>
    /// A known weakness this does NOT fix, and which the one-shot ask has always shared: a domain
    /// whose central verb has no good noun ("a library lending system") still yields
    /// LendingTransaction. Measured on both asks, so it is the model, not the split.
    /// </remarks>
    private const string NamingRules =
        "The name must be a concrete business noun (Product, Supplier, PurchaseOrder, "
        + "StockMovement, Warehouse). It must NEVER be one of the role words below, which are "
        + "classification tags and not names: a class named exactly Item, Transaction, Document, "
        + "Party, Event, Device or Place is always wrong. Name the real thing instead: Product "
        + "not Item, StockMovement not Transaction, Supplier not Party. "
        + "Never glue the role word onto the name either: write Product, not ProductItem; "
        + "SaleLine, not SaleLineItem; Payment, not PaymentMethod. The role is a separate field "
        + "and must not appear in the name at all. "
        + "Name each class for the thing itself, never for the system or the business it belongs "
        + "to: write Store, not RetailShop; Product, not InventoryProduct. Do not prefix or suffix "
        + "every class with the system name. Use the shortest name that is still unambiguous. "
        + "Do not emit several near-duplicate classes that mean the same thing. ";

    private const string Roles =
        "The role is a separate field, chosen from exactly this list: Party (a person or "
        + "organisation), Device, Place, Item (a thing bought or held), Document (a record of "
        + "something that happened), Transaction (value changing hands), Event (something "
        + "scheduled). ";

    private const string Thoroughness =
        "Be thorough: cover the things the system tracks, the people and organisations "
        + "involved, the documents and transactions it records, and the reference data it needs. "
        + "Do not stop at the two or three most obvious classes. ";

    private const string Example =
        "Example for a shop: Product [Item], Supplier [Party], Customer [Party], Sale "
        + "[Transaction], SaleLine [Document], Store [Place], Employee [Party]. ";

    /// <summary>
    /// The single-call ask: names, roles, purposes, properties and relationships at once.
    /// </summary>
    /// <remarks>
    /// Measured at ~158 seconds and ten classes for a point-of-sale system. Still the faster route
    /// to a <em>finished</em> model, which is why <c>discovery.json</c> can still select it.
    /// </remarks>
    public const string OneShot =
        "You are a domain modelling assistant. Given a description of a business system, return "
        + "the complete data model a developer would need to build it. "
        + Thoroughness
        + "Each class needs a name, a role, a one-line purpose, its properties with C# types, and "
        + "its relationships. "
        + NamingRules
        + Roles
        + Example
        + "Return only JSON matching the schema.";

    /// <summary>
    /// Phase one: which classes exist, and nothing else.
    /// </summary>
    /// <remarks>
    /// Measured at 42-68 seconds — a complete class list on screen in well under half the time the
    /// one-shot ask takes to produce anything complete at all. It also finds MORE classes (twelve
    /// against ten for the same description), which is the same effect from the other side: with
    /// no properties to write, the whole answer is names.
    /// </remarks>
    public const string Roster =
        "You are a domain modelling assistant. Given a description of a business system, name the "
        + "classes a developer would need to build it, with a role and a one-line purpose for "
        + "each. Do not list properties or relationships - those come later. "
        + Thoroughness
        + NamingRules
        + Roles
        + Example
        + "Return only JSON matching the schema.";

    /// <summary>
    /// Phase two: the contents of classes that have already been decided.
    /// </summary>
    /// <remarks>
    /// The class list is given in the user message rather than here, so this stays a constant. The
    /// "do not add, remove or rename" instruction is what makes <see cref="TwoPhaseModel.Merge"/>
    /// a merge rather than a reconciliation — and the merge enforces it anyway, because a 3B model
    /// obeys it most of the time rather than always.
    /// </remarks>
    public static string Detail(IEnumerable<string> classNames) =>
        "You are a domain modelling assistant. The classes of this system have already been "
        + "decided. For EVERY class the user lists, give its properties with C# types and its "
        + "relationships to the other classes. Do not add classes, do not remove classes, do not "
        + "rename them: return exactly the classes listed, in the same order, with the same roles. "
        + "Every class needs an identifier property. "
        + "Only a class whose role is Document, Transaction or Event may hold a relationship to "
        + "another class; a relationship's target must be one of these class names: "
        + string.Join(", ", classNames) + ". "
        + "Return only JSON matching the schema.";

    /// <summary>How the class list is written into the detail ask's user message.</summary>
    public static string Roll(IEnumerable<LlmClass> classes) =>
        string.Join("\n", classes.Select(c => $"{c.Name} [{c.Role}]"));
}
