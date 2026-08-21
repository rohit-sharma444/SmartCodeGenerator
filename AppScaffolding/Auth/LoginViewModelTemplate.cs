namespace AppScaffolding.Auth;

/// <summary>Emits <c>ViewModels/LoginViewModel.cs</c>, shared by both desktop targets.</summary>
/// <remarks>
/// Nothing here is WPF- or Windows Forms-specific, for the reason
/// <see cref="Targets.DesktopViewModelFiles"/> gives: it uses only System.ComponentModel and
/// System.Windows.Input.ICommand, which every .NET application has.
/// </remarks>
public static class LoginViewModelTemplate
{
    /// <summary>Source of <c>ViewModels/LoginViewModel.cs</c>.</summary>
    public static string Source(string rootNamespace) => $$"""
using System;
using System.Windows.Input;
using {{rootNamespace}}.Auth;

namespace {{rootNamespace}}.ViewModels;

/// <summary>Drives the login screen. The view supplies the password, which is never held on the
/// view model beyond the attempt.</summary>
public sealed class LoginViewModel : ObservableObject
{
    private string _userName = string.Empty;
    private string _password = string.Empty;
    private string? _error;

    public LoginViewModel()
    {
        UserSeed.EnsureAdminSeeded();

        SignInCommand = new RelayCommand(SignIn);
        Hint = UserSeed.IsFirstRun() ? UserSeed.FirstRunHint : string.Empty;
    }

    /// <summary>Raised once the credentials were accepted, so the window can close itself.</summary>
    public event EventHandler? Succeeded;

    public string UserName
    {
        get => _userName;
        set => SetField(ref _userName, value);
    }

    public string Password
    {
        get => _password;
        set => SetField(ref _password, value);
    }

    /// <summary>The one message every failure shows, or null when nothing has failed yet.</summary>
    public string? Error
    {
        get => _error;
        private set
        {
            if (SetField(ref _error, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    /// <summary>Whether there is a message to show, so the view can collapse the strip.</summary>
    public bool HasError => _error is not null;

    /// <summary>The first-run credentials hint, or empty once the default password is changed.</summary>
    public string Hint { get; }

    public ICommand SignInCommand { get; }

    private void SignIn()
    {
        var result = AuthService.SignIn(UserName, Password);

        // Cleared whatever happens: a stale message beside a fresh attempt reads as a second
        // failure.
        Error = null;

        if (!result.Succeeded)
        {
            Error = result.Message;
            return;
        }

        Session.SignIn(result.User!);

        // Dropped as soon as it has been used. Nothing needs it again, and a view model outliving
        // the window would otherwise hold it.
        Password = string.Empty;

        Succeeded?.Invoke(this, EventArgs.Empty);
    }
}
""";
}
