namespace CodeGenerator.Tests.App;

/// <summary>
/// Stand-ins for the view-model files a generated app emits, so an emitted view can be compiled
/// on its own.
/// </summary>
/// <remarks>
/// The real types come from <c>DesktopViewModelFiles</c>. Stubs rather than the real emitted
/// source because the real <c>EntityViewModel&lt;T&gt;</c> pulls in JsonStore, the validators and
/// the models — a compile test of a VIEW should fail for reasons in the view.
/// </remarks>
internal static class ViewModelStubs
{
    /// <summary>Everything a WinForms MainForm binds to.</summary>
    public const string WinFormsHost = """
        using System.Collections;
        using System.Collections.ObjectModel;
        using System.Windows.Input;

        namespace Payroll.WinForms.ViewModels;

        public interface IEntityViewModel
        {
            string Title { get; }
            IEnumerable Items { get; }
            object? Selected { get; set; }
            ICommand AddCommand { get; }
            ICommand DeleteCommand { get; }
            ICommand SaveCommand { get; }
            string Status { get; }
            EntityRole Role { get; }
            int Count { get; }
        }

        public enum EntityRole { Master, Transaction, DocumentLine }

        public sealed class MainViewModel
        {
            public ObservableCollection<IEntityViewModel> Entities { get; } = new();

            // Bound by the pane's sign-out strip, which every layout emits when the app has a
            // login. Without it these compile tests only ever cover the `nologin` shape.
            public string SignedInUser => string.Empty;
        }
        """;

    /// <summary>The static session the emitted sign-out handler calls.</summary>
    /// <remarks>
    /// A stub rather than the real SessionTemplate: these tests compile a LAYOUT, and pulling in
    /// the auth core would make them fail for reasons that have nothing to do with the view.
    /// </remarks>
    public const string WinFormsSession = """
        namespace Payroll.WinForms.Auth;

        public static class Session
        {
            public static bool SignOutRequested { get; private set; }
            public static void RequestSignOut() => SignOutRequested = true;
        }
        """;
}
