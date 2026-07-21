using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class DbContextTemplateTests
{
    private static string Sample() =>
        DbContextTemplate.Source("Shop", new[] { "Customer", "Order" }, "SHOP_DB");

    [TestMethod]
    public void Source_EmitsContextWithModelsUsingAndSqlite()
    {
        var source = Sample();

        StringAssert.Contains(source, "namespace Shop.Data");
        StringAssert.Contains(source, "using Shop.Models;");
        StringAssert.Contains(source, "using Microsoft.EntityFrameworkCore;");
        StringAssert.Contains(source, "class AppDbContext : DbContext");
        StringAssert.Contains(source, "optionsBuilder.UseSqlite(ConnectionString())");
    }

    [TestMethod]
    public void Source_EmitsOneDbSetPerEntity()
    {
        var source = Sample();

        StringAssert.Contains(source, "public DbSet<Customer> Customer => Set<Customer>();");
        StringAssert.Contains(source, "public DbSet<Order> Order => Set<Order>();");
    }

    [TestMethod]
    public void Source_KeepsKeysClientAssignedAndMapsScalarsOnly()
    {
        var source = Sample();

        StringAssert.Contains(source, "Property(\"Id\").ValueGeneratedNever();");
        StringAssert.Contains(source, "if (!IsScalar(prop.PropertyType))");
        StringAssert.Contains(source, "entity.Ignore(prop.Name);");
    }

    [TestMethod]
    public void Source_CreatesTheDatabaseAndHonorsTheEnvOverride()
    {
        var source = Sample();

        StringAssert.Contains(source, "Database.EnsureCreated();");
        StringAssert.Contains(source, "Environment.GetEnvironmentVariable(\"SHOP_DB\")");
        StringAssert.Contains(source, "\"Data Source=\" + Path.Combine(AppContext.BaseDirectory, \"Shop.db\")");
    }
}
