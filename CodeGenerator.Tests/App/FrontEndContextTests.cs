using AppScaffolding;
using AppScaffolding.Screens;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The seam the screens need: a target that wants roles as well as names has somewhere to read
/// them.
/// </summary>
/// <remarks>
/// Nothing consumes <see cref="FrontEndContext.Roles"/> yet. It lands now because the alternative
/// is opening the screens work by changing an interface all seven targets implement; doing that
/// while it is a pure addition with no behaviour attached keeps that change out of a branch that
/// is already large.
/// </remarks>
[TestClass]
public class FrontEndContextTests
{
    private static readonly ITargetScaffolder[] EveryTarget =
    [
        new WpfTarget(), new WinFormsTarget(), new BlazorTarget(),
        new ConsoleTarget(), new WebApiTarget(), new WorkerTarget(),
        new ClassLibraryTarget(),
    ];

    private static FrontEndContext Context(AppCustomization? customization = null)
        => new("Shop", ["Order"], customization ?? AppCustomization.Default, EntityRoleMap.Empty);

    /// <summary>Every target answers the new overload, whether or not it knows about it — that is
    /// what makes it a default interface method rather than a breaking change.</summary>
    [TestMethod]
    public void Every_target_produces_the_same_files_through_either_overload()
    {
        foreach (var target in EveryTarget)
        {
            var viaContext = target.FrontEndFiles(Context()).Select(f => f.Name).ToList();

            var viaNames = target
                .FrontEndFiles("Shop", ["Order"], AppCustomization.Default)
                .Select(f => f.Name).ToList();

            CollectionAssert.AreEqual(viaNames, viaContext, target.Key);
        }
    }

    /// <summary>The customization travels, or a target reading the context would silently lose
    /// every option the user chose.</summary>
    [TestMethod]
    public void The_customization_reaches_the_target_through_the_context()
    {
        var noLogin = AppCustomization.Default with { Login = false };

        // Through the INTERFACE: a default interface method is not visible on the concrete
        // type, which is also why every caller of it holds an ITargetScaffolder.
        ITargetScaffolder wpf = new WpfTarget();

        var names = wpf.FrontEndFiles(Context(noLogin)).Select(f => f.Name).ToList();

        CollectionAssert.DoesNotContain(names, "LoginWindow.xaml");
    }

    /// <summary>Carried, not recomputed: classifying twice could disagree with the summary the
    /// build already printed.</summary>
    [TestMethod]
    public void The_roles_are_carried_rather_than_recomputed()
    {
        var roles = EntityRoles.Classify([]);

        Assert.AreSame(
            roles, new FrontEndContext("Shop", ["Order"], AppCustomization.Default, roles).Roles);
    }

    /// <summary>
    /// This record does NOT compare by value, and that is worth pinning.
    /// </summary>
    /// <remarks>
    /// It holds an <c>IReadOnlyList</c> and an <c>EntityRoleMap</c>, and a record compares
    /// both by reference — so two contexts describing the same build are not equal. Unlike
    /// <see cref="AppCustomization"/>, which IS compared by value across the suite, nothing
    /// here may be cached or deduplicated on equality. Asserting the opposite would have been
    /// a test that quietly holds only while the lists happen to be the same instance.
    /// </remarks>
    [TestMethod]
    public void Two_contexts_describing_the_same_build_are_not_equal()
        => Assert.AreNotEqual(Context(), Context());
}
