using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The usage message lists every option the parser accepts.
/// </summary>
/// <remarks>
/// It used to leave out namespace, layout and entry-template, all of which were accepted, and a
/// flag the help does not mention is a flag nobody finds.
/// </remarks>
[TestClass]
public class InlineOptionsHelpTests
{
    [TestMethod]
    public void Every_option_the_parser_accepts_is_in_the_help()
    {
        foreach (var option in InlineCustomization.ValueKeys.Concat(InlineCustomization.Flags))
            StringAssert.Contains(BuildAppCommand.InlineOptionsHelp, option, $"{option} is not listed");
    }

    [TestMethod]
    public void The_layout_and_entry_template_words_are_listed()
    {
        foreach (var key in AppScaffolding.AppLayoutCatalog.Keys)
            StringAssert.Contains(BuildAppCommand.InlineOptionsHelp, key);

        foreach (var key in AppScaffolding.Screens.EntryTemplateCatalog.Keys)
            StringAssert.Contains(BuildAppCommand.InlineOptionsHelp, key);
    }
}
