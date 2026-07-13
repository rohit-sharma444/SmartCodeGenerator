using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class RelationshipResolverTemplateTests
{
    [TestMethod]
    public void Emits_a_resolver_in_the_apps_data_namespace()
    {
        var source = RelationshipResolverTemplate.Source("MyApp");

        StringAssert.Contains(source, "namespace MyApp.Data;");
        StringAssert.Contains(source, "public static class RelationshipResolver");
        StringAssert.Contains(source, "public static void Resolve(");
    }

    [TestMethod]
    public void The_resolver_is_reflection_driven_so_it_needs_no_per_entity_code()
    {
        var source = RelationshipResolverTemplate.Source("MyApp");

        StringAssert.Contains(source, "GetProperties()");
    }
}
