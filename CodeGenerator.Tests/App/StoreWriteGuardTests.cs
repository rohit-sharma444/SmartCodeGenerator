using AppScaffolding;

namespace CodeGenerator.Tests.App;

[TestClass]
public class StoreWriteGuardTests
{
    [TestMethod]
    public void SaveAllCarriesARefusal_RatherThanABareBool()
    {
        // A bool made a permission refusal surface as "Could not write Customer.json", which is
        // a lie: nothing was wrong with the file.
        var source = StoreSeamTemplate.Source("Acme");

        StringAssert.Contains(source, "StoreWrite SaveAll(List<T> all)");
        StringAssert.Contains(source, "record StoreWrite(bool Success, string? Refusal)");
    }

    [TestMethod]
    public void TheStoreAsksTheMatrixBeforeItWrites()
    {
        // The unbypassable point. Coarse - it can only ask "may this user write this type at
        // all", because SaveAll is handed a whole list and cannot tell an insert from a delete -
        // but no front end can go round it.
        // JsonStoreTemplate.Source takes the FULL namespace, not the root: every caller
        // passes $"{rootNamespace}.Data".
        var source = JsonStoreTemplate.Source("Acme.Data");

        StringAssert.Contains(source, "Permissions.Allows");
    }

    [TestMethod]
    public void TheGuardIsHonestAboutWhatItCannotSee()
    {
        // Under the default matrix this refuses nothing except the User entity, because a User
        // may write every business entity and is barred only from DELETING a master. Recorded in
        // the emitted comment so the next reader does not assume it is the whole enforcement.
        var source = JsonStoreTemplate.Source("Acme.Data");

        StringAssert.Contains(source, "cannot tell an insert from a delete");
    }
}
