using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class EfStoreTemplateTests
{
    [TestMethod]
    public void Source_EmitsEfStoreImplementingIStore()
    {
        var source = EfStoreTemplate.Source("Payroll");

        StringAssert.Contains(source, "namespace Payroll.Data");
        StringAssert.Contains(source, "using Microsoft.EntityFrameworkCore;");
        StringAssert.Contains(source, "class EfStore<T> : IStore<T> where T : class, new()");
    }

    [TestMethod]
    public void Source_UsesAFreshDbContextPerCall()
    {
        var source = EfStoreTemplate.Source("Payroll");

        StringAssert.Contains(source, "using var context = new AppDbContext();");
        StringAssert.Contains(source, "context.Set<T>().AsNoTracking().ToList()");
    }

    [TestMethod]
    public void Source_SaveAllReplacesTheWholeStore()
    {
        var source = EfStoreTemplate.Source("Payroll");

        // Faithful to JsonStore.SaveAll's overwrite contract: clear then insert.
        StringAssert.Contains(source, "set.RemoveRange(set.ToList());");
        StringAssert.Contains(source, "set.AddRange(all);");
        StringAssert.Contains(source, "context.SaveChanges();");
    }

    [TestMethod]
    public void Source_AssignsIdsClientSideLikeJsonStore()
    {
        var source = EfStoreTemplate.Source("Payroll");

        StringAssert.Contains(source, "ids.Count == 0 ? 1 : ids.Max() + 1");
    }
}
