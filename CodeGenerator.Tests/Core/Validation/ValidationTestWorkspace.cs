namespace CodeGenerator.Tests.Core.Validation;

/// <summary>
/// A temp workspace holding the classes the validation command tests name, each with every
/// property those tests' rules refer to.
/// </summary>
/// <remarks>
/// add validation refuses a class or property that is not there, so these tests need real classes;
/// they used to run against the whole temp folder with no class at all.
/// </remarks>
internal static class ValidationTestWorkspace
{
    public static readonly string[] Classes = ["Employee", "Customer", "Order", "Bill", "Item", "Booking", "Invoice"];

    public static readonly string[] Properties =
    [
        "Name", "Age", "Email", "Phone", "Quantity", "Qty", "Number", "Salary", "Website", "DateOfBirth",
        "Code", "Born", "Title", "Price", "Status", "Kind", "StartDate", "EndDate", "Total", "Amount",
        "BillingPostcode", "ShippingPostcode", "ShippedDate", "Discount", "Subtotal", "Tax",
    ];

    public static string Create()
    {
        // The class lookup searches the selected project before the workspace, so a project an
        // earlier test left selected hid these classes: 33 tests failed in the full suite only.
        CodeGenerationEngine.Core.ProjectManager.CurrentProject = null;

        var root = Path.Combine(Path.GetTempPath(), "cg_val_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        foreach (var name in Classes)
            File.WriteAllText(Path.Combine(root, name + ".cs"),
                "namespace S;" + Environment.NewLine
                + $"public class {name}" + Environment.NewLine + "{" + Environment.NewLine
                + string.Concat(Properties.Select(p => $"    public string {p} {{ get; set; }} = \"\";" + Environment.NewLine))
                + "}" + Environment.NewLine);

        return root;
    }

    public static void Delete(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }
}
