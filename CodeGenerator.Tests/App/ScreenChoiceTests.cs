using AppScaffolding;
using AppScaffolding.Screens;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Recording a screen-split choice. The read side is <see cref="ScreenOverrides"/>; this is the
/// half that writes the two comma-joined strings it parses.
/// </summary>
/// <remarks>
/// Only DISAGREEMENTS are recorded, which is what the `transactions`/`masters` options mean —
/// "these name the ones it got wrong". Recording agreement too would put every class in the
/// replay line, and a line naming all of them says nothing about which the user actually moved.
/// </remarks>
[TestClass]
public class ScreenChoiceTests
{
    [TestMethod]
    public void Agreeing_with_the_heuristic_records_nothing()
    {
        var result = ScreenChoice.Set(
            AppCustomization.Default, "Order", EntityRole.Transaction, EntityRole.Transaction);

        Assert.AreEqual(string.Empty, result.Transactions);
        Assert.AreEqual(string.Empty, result.Masters);
    }

    [TestMethod]
    public void Disagreeing_records_the_class_under_the_role_chosen()
    {
        var result = ScreenChoice.Set(
            AppCustomization.Default, "Order", EntityRole.Master, EntityRole.Transaction);

        Assert.AreEqual("Order", result.Masters);
        Assert.AreEqual(string.Empty, result.Transactions);
    }

    /// <summary>
    /// The whole point of storing disagreements: changing your mind back has to leave no trace,
    /// or the class is pinned to a role the user has since abandoned.
    /// </summary>
    [TestMethod]
    public void Changing_back_to_the_heuristic_removes_the_earlier_choice()
    {
        var moved = ScreenChoice.Set(
            AppCustomization.Default, "Order", EntityRole.Master, EntityRole.Transaction);

        var back = ScreenChoice.Set(moved, "Order", EntityRole.Transaction, EntityRole.Transaction);

        Assert.AreEqual(string.Empty, back.Masters);
        Assert.AreEqual(string.Empty, back.Transactions);
    }

    /// <summary>
    /// A name in both strings is a contradiction <see cref="ScreenOverrides"/> resolves by
    /// silently preferring master — so a second choice must REPLACE the first rather than joining
    /// it, or choosing "transaction" after "master" would do nothing at all.
    /// </summary>
    [TestMethod]
    public void A_second_choice_replaces_the_first_rather_than_naming_it_twice()
    {
        var master = ScreenChoice.Set(
            AppCustomization.Default, "Customer", EntityRole.Master, EntityRole.Transaction);

        var transaction = ScreenChoice.Set(
            master, "Customer", EntityRole.Transaction, EntityRole.Master);

        Assert.AreEqual("Customer", transaction.Transactions);
        Assert.AreEqual(string.Empty, transaction.Masters);

        Assert.IsTrue(
            ScreenOverrides.Parse(transaction.Transactions, transaction.Masters)
                .IsTransaction("Customer"));
    }

    [TestMethod]
    public void Choices_about_other_classes_are_left_alone()
    {
        var start = AppCustomization.Default with { Masters = "Region,Product" };

        var result = ScreenChoice.Set(start, "Order", EntityRole.Master, EntityRole.Transaction);

        Assert.AreEqual("Region,Product,Order", result.Masters);
    }

    /// <summary>Matching <see cref="ScreenOverrides"/>, which compares names case-insensitively
    /// because a class name is also a file name on Windows.</summary>
    [TestMethod]
    public void A_name_is_matched_however_it_is_cased()
    {
        var start = AppCustomization.Default with { Masters = "order" };

        var result = ScreenChoice.Set(start, "Order", EntityRole.Transaction, EntityRole.Transaction);

        Assert.AreEqual(string.Empty, result.Masters);
    }

    /// <summary>
    /// The panel needs the role a class would take with the user's own answer taken away, so it
    /// can tell agreement from disagreement before recording the next one.
    /// </summary>
    [TestMethod]
    public void Clearing_a_class_takes_it_out_of_both_answers()
    {
        var start = AppCustomization.Default with
        {
            Transactions = "Order,Invoice", Masters = "Order,Region"
        };

        var result = ScreenChoice.Clear(start, "Order");

        Assert.AreEqual("Invoice", result.Transactions);
        Assert.AreEqual("Region", result.Masters);
    }

    /// <summary>
    /// Lines are not expressible: <c>EntityRoles</c> reaches DocumentLine in a second pass
    /// that only a MASTER override skips, so recording "make this a line" would be an answer
    /// the generator silently ignores. Refused loudly instead.
    /// </summary>
    [TestMethod]
    public void Asking_for_a_line_is_refused_rather_than_silently_dropped()
        => Assert.Throws<ArgumentOutOfRangeException>(() => ScreenChoice.Set(
            AppCustomization.Default, "OrderLine", EntityRole.DocumentLine, EntityRole.Master));
}
