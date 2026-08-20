namespace AppScaffolding.Auth;

/// <summary>Emits <c>ViewModels/UsersViewModel.cs</c>: the account list, as an ordinary pane.</summary>
/// <remarks>
/// An <c>IEntityViewModel</c> like every other pane, so the four existing layouts show it with no
/// change of their own — which is what makes a login usable before the business shell exists. It
/// does NOT reuse <c>EntityViewModel&lt;T&gt;</c>: an account is not edited like a record, because
/// its password can never be a column.
/// </remarks>
public static class UsersViewModelTemplate
{
    /// <summary>Source of <c>ViewModels/UsersViewModel.cs</c>.</summary>
    public static string Source(string rootNamespace) => $$"""
using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using {{rootNamespace}}.Auth;
using {{rootNamespace}}.Data;
using {{rootNamespace}}.Models;
using {{rootNamespace}}.Screens;

namespace {{rootNamespace}}.ViewModels;

/// <summary>The accounts that can sign in. Visible to an administrator only.</summary>
public sealed class UsersViewModel : ObservableObject, IEntityViewModel
{
    private readonly IStore<User> _store = StoreFactory.Create<User>();

    private object? _selected;
    private string _status = string.Empty;
    private string _newPassword = string.Empty;

    public UsersViewModel()
    {
        Accounts = new ObservableCollection<User>(_store.GetAll());

        AddCommand = new RelayCommand(Add);
        DeleteCommand = new RelayCommand(Delete);
        SaveCommand = new RelayCommand(Save);
        SetPasswordCommand = new RelayCommand(SetPassword);
    }

    public string Title => "Users";

    /// <summary>Accounts are reference data: a list and an editor, like any other master.</summary>
    public EntityRole Role => EntityRole.Master;

    public int Count => Accounts.Count;

    /// <summary>The typed collection the grid binds to.</summary>
    public ObservableCollection<User> Accounts { get; }

    public IEnumerable Items => Accounts;

    public object? Selected
    {
        get => _selected;
        set => SetField(ref _selected, value);
    }

    /// <summary>The password to give the selected account. Never persisted, and cleared the
    /// moment it has been used.</summary>
    public string NewPassword
    {
        get => _newPassword;
        set => SetField(ref _newPassword, value);
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public ICommand AddCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SetPasswordCommand { get; }

    /// <summary>
    /// Adds an account that cannot sign in yet.
    /// </summary>
    /// <remarks>
    /// No password is set here, and an empty hash never verifies — so a new row is inert until an
    /// administrator gives it one. That is deliberate: a default password on a new account is a
    /// door everyone knows about.
    /// </remarks>
    private void Add()
    {
        var all = Accounts.ToList();

        var user = new User
        {
            Id = _store.NextId(all),
            UserName = "new.user",
            DisplayName = "New user",
            Role = AppRole.User,
            IsActive = true,
            CreatedOn = System.DateTime.Now,
            ModifiedOn = System.DateTime.Now,
        };

        Accounts.Add(user);
        Selected = user;
        Status = "Added. Set a password before it can sign in.";
    }

    /// <summary>
    /// Removes the selected account, unless it is the one signed in.
    /// </summary>
    /// <remarks>
    /// Refusing self-deletion is what keeps the running session pointing at a record that still
    /// exists. It is also why no separate "last administrator" rule is needed: an account's role
    /// can only be lost by deleting it, and the signed-in administrator can never delete
    /// themselves, so at least one administrator always remains.
    /// </remarks>
    private void Delete()
    {
        if (Selected is not User user)
        {
            Status = "Select an account first.";
            return;
        }

        if (Session.Current is { } current && current.Id == user.Id)
        {
            Status = "You cannot delete the account you are signed in with.";
            return;
        }

        Accounts.Remove(user);
        Selected = null;
        Save();
    }

    private void Save()
    {
        Status = _store.SaveAll(Accounts.ToList())
            ? "Saved."
            : "Could not save the accounts.";
    }

    /// <summary>
    /// Gives the selected account a password.
    /// </summary>
    /// <remarks>
    /// Through <c>AuthService</c> rather than by writing a hash here: hashing belongs in one place,
    /// and a view model that knew how to build a hash would be a second place to get it wrong.
    /// </remarks>
    private void SetPassword()
    {
        if (Selected is not User user)
        {
            Status = "Select an account first.";
            return;
        }

        if (NewPassword.Length == 0)
        {
            Status = "Type a password first.";
            return;
        }

        if (!AuthService.ChangePassword(user.Id, NewPassword))
        {
            Status = "Could not set the password.";
            return;
        }

        // Dropped as soon as it has been used, so it is not left sitting in a bound property.
        NewPassword = string.Empty;

        // The store now holds the new hash; the list in hand still holds the old one.
        Accounts.Clear();

        foreach (var account in _store.GetAll())
            Accounts.Add(account);

        Status = "Password set.";
    }
}
""";
}
