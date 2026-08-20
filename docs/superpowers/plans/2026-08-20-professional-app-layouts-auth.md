# Auth Core and Login Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every generated WPF, Windows Forms and Blazor application starts at a real login screen, backed by hashed passwords, two roles and a seeded admin account, with a Users screen for managing accounts.

**Architecture:** The auth core (`User`, `AppRole`, `PasswordHasher`, `AuthService`, `UserSeed`) is emitted into the shared core beside `Models/` and `Data/`, so all three targets receive identical code and only the login *view* differs. The user record persists through the existing `IStore<T>` seam, so JSON and SQLite both work with no new persistence code. Desktop targets hold the signed-in user in a static `Session`; Blazor deliberately does not, and uses cookie authentication instead.

**Tech Stack:** .NET 10, C# raw string literal templates, MSTest, Roslyn (`Compiler.Compile`) for compile-testing emitted code, WPF/XAML, Windows Forms, Blazor Server with cookie authentication.

**Spec:** `docs/superpowers/specs/2026-08-20-professional-app-layouts-design.md`

## Global Constraints

- **No new NuGet packages.** PBKDF2 comes from `System.Security.Cryptography`, which is in the framework. Cookie auth comes from `Microsoft.AspNetCore.Authentication.Cookies`, which the ASP.NET Core shared framework already provides.
- **Emitted code is compile-tested with Roslyn**, never by substring assertion alone. Use `EmittedSource.AssertCompiles` / `EmittedSource.Flatten` for core C#, and `EmittedDesktopSource.AssertCompilesTogether` for anything referencing WPF or Windows Forms types.
- **Behavioural claims about emitted code are proved by executing it.** `Compiler.Compile(source).AssemblyBytes` plus `Assembly.Load` and reflection — the precedent is `ValidatorTemplateTests.Factory_falls_back_to_a_no_op_validator_for_an_unmapped_type`.
- **The regression proof:** a build that names a layout explicitly **and** passes `nologin` must be byte-identical to what the generator produces today. Any existing golden-output test that has to change under that combination is a bug in this work.
- **Without `nologin`, the four existing layouts change by exactly two additions** — a login window/page and a Users pane. A third difference is a bug.
- **Raw string literals carry the source file's line endings.** This checkout is CRLF; the `Write` tool produces LF. When adding a template, match the surrounding file rather than assuming.
- **`User` is emitted, not discovered.** It must never reach the "Generated N class(es)" summary, the navigation list, the dashboard, the seeded sample rows, or `ValidatorFactory`.
- **MSTest conventions:** `[TestClass]` / `[TestMethod]`, method names as sentences with underscores, `Assert.IsEmpty` / `Assert.Contains` / `StringAssert.Contains`. `MSTEST0037` rejects `Assert.AreEqual(n, x.Count)` — use `Assert.HasCount(n, x)`.
- **The fixture app is always named `Acme`** in emitted-source tests, because `EmittedSource.Flatten` strips `Acme.Models` / `Acme.Data` / `Acme.Validation`.
- **CI builds `SmartCodeGenerator.slnx` in Release with `-warnaserror`.** Analyzer errors that a local Debug `CodeGenerator` build hides will surface there. Run `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror --no-incremental` before claiming a task is done.

---

## File Structure

**New production files** (all under `AppScaffolding/Auth/`, namespace `AppScaffolding.Auth`):

| File | Responsibility |
| --- | --- |
| `PasswordHasherTemplate.cs` | Emits `Auth/PasswordHasher.cs` — PBKDF2 create/verify |
| `AuthModelTemplates.cs` | Emits `Auth/AppRole.cs` and `Models/User.cs` |
| `AuthServiceTemplate.cs` | Emits `Auth/AuthService.cs` — sign-in and change-password over `IStore<User>` |
| `UserSeedTemplate.cs` | Emits `Auth/UserSeed.cs` — the seeded admin account |
| `SessionTemplate.cs` | Emits `Auth/Session.cs` — desktop-only signed-in user |
| `LoginViewModelTemplate.cs` | Emits `ViewModels/LoginViewModel.cs` — shared by WPF and Windows Forms |
| `UsersViewModelTemplate.cs` | Emits `ViewModels/UsersViewModel.cs` — the Users pane as an `IEntityViewModel` |
| `WpfLoginTemplate.cs` | Emits `Views/LoginWindow.xaml`, `Views/LoginWindow.xaml.cs`, `App.xaml.cs` |
| `WinFormsLoginTemplate.cs` | Emits `Views/LoginForm.cs` |
| `BlazorLoginTemplate.cs` | Emits `Components/Pages/Login.razor`, `Components/Pages/UsersPage.razor`, `Auth/AuthEndpoints.cs` |
| `AuthCoreFiles.cs` | The one list of auth-core `GeneratedFile`s, so `AppScaffolder` and the targets cannot drift |

**Modified production files:**

| File | Change |
| --- | --- |
| `AppScaffolding/AppCustomization.cs` | `bool Login` property, defaulting true |
| `AppScaffolding/AppScaffolder.cs` | Emit the auth core; add `User` to the SQLite `DbSet` list |
| `AppScaffolding/Targets/DesktopViewModelFiles.cs` | Emit `Session`, `LoginViewModel`, `UsersViewModel` when login is on |
| `AppScaffolding/MainViewModelTemplate.cs` | Append the Users pane when login is on |
| `AppScaffolding/WinFormsProgramTemplate.cs` | The sign-in loop in `Main` |
| `AppScaffolding/WpfProjectFixup.cs` | Remove `StartupUri` when login is on |
| `AppScaffolding/BlazorProjectFixup.cs` | `EnableAuthentication` — three insertions into `Program.cs` |
| `AppScaffolding/BlazorShellTemplate.cs` | Users nav item behind `AuthorizeView`; sign-out in the header |
| `AppScaffolding/Targets/WpfTarget.cs`, `WinFormsTarget.cs`, `BlazorTarget.cs` | Add the login views |
| `CodeGenerator/App/InlineCustomization.cs` | The `nologin` flag |
| `CodeGenerator/App/BuildAppCommand.cs` | The inline-options help line |
| `CodeGenerator/App/CustomizationSummary.cs` | "no login" in the features line |
| `CodeGenerator/App/CustomizationWizard.cs`, `CustomizationMenus.cs`, `CustomizationDefaults.cs` | The wizard question and remembered answer |
| `CodeGenerator/Web/Workflow/BuildCommandEcho.cs` | Echo `nologin` |
| `CodeGenerationEngine/Core/CommandDictionary.cs` | The `build app:` catalogue entry |
| `docs/commands.md` | The manual |

**New test files** (all in `CodeGenerator.Tests/App/`): `PasswordHasherTemplateTests.cs`, `AuthModelTemplateTests.cs`, `AuthServiceTemplateTests.cs`, `UserSeedTemplateTests.cs`, `SessionTemplateTests.cs`, `LoginFlagTests.cs`, `AuthCoreEmissionTests.cs`, `WpfLoginTemplateTests.cs`, `WinFormsLoginTemplateTests.cs`, `BlazorLoginTemplateTests.cs`, `UsersPaneTests.cs`.

---

### Task 1: The password hasher

**Files:**
- Create: `AppScaffolding/Auth/PasswordHasherTemplate.cs`
- Test: `CodeGenerator.Tests/App/PasswordHasherTemplateTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `AppScaffolding.Auth.PasswordHasherTemplate.Source(string rootNamespace) -> string`, emitting a static class `PasswordHasher` in `<root>.Auth` with `public static (string Hash, string Salt) Create(string password)` and `public static bool Verify(string password, string hash, string salt)`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Reflection;
using AppScaffolding.Auth;
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The hasher is the one emitted file where a compile test proves almost nothing: a hasher that
/// returns the password unchanged compiles perfectly. Every test here loads the emitted assembly
/// and calls it.
/// </summary>
[TestClass]
public class PasswordHasherTemplateTests
{
    private static (object? Hasher, MethodInfo Create, MethodInfo Verify) Emit()
    {
        var source = PasswordHasherTemplate.Source("Acme");

        EmittedSource.AssertCompiles(source);

        var assembly = Assembly.Load(Compiler.Compile(source).AssemblyBytes!);
        var type = assembly.GetType("Acme.Auth.PasswordHasher")!;

        return (null, type.GetMethod("Create")!, type.GetMethod("Verify")!);
    }

    private static (string Hash, string Salt) Create(MethodInfo create, string password)
    {
        var tuple = create.Invoke(null, [password])!;
        var hash = (string)tuple.GetType().GetField("Item1")!.GetValue(tuple)!;
        var salt = (string)tuple.GetType().GetField("Item2")!.GetValue(tuple)!;
        return (hash, salt);
    }

    [TestMethod]
    public void A_created_hash_verifies_against_its_own_password()
    {
        var (_, create, verify) = Emit();
        var (hash, salt) = Create(create, "correct horse");

        Assert.IsTrue((bool)verify.Invoke(null, ["correct horse", hash, salt])!);
    }

    [TestMethod]
    public void A_wrong_password_does_not_verify()
    {
        var (_, create, verify) = Emit();
        var (hash, salt) = Create(create, "correct horse");

        Assert.IsFalse((bool)verify.Invoke(null, ["wrong horse", hash, salt])!);
    }

    /// <summary>The point of the salt. Two accounts sharing a password must not share a hash, or
    /// one cracked hash reveals every account that reused the password.</summary>
    [TestMethod]
    public void The_same_password_hashes_differently_for_two_users()
    {
        var (_, create, _) = Emit();

        var first = Create(create, "same");
        var second = Create(create, "same");

        Assert.AreNotEqual(first.Salt, second.Salt);
        Assert.AreNotEqual(first.Hash, second.Hash);
    }

    /// <summary>The stored password must not be recoverable from what is written to disk.</summary>
    [TestMethod]
    public void The_stored_hash_does_not_contain_the_password()
    {
        var (_, create, _) = Emit();
        var (hash, salt) = Create(create, "plaintext");

        Assert.IsFalse(hash.Contains("plaintext", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(salt.Contains("plaintext", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A store hand-edited to nonsense must fail the sign-in, not crash the app.</summary>
    [TestMethod]
    public void A_malformed_stored_hash_fails_instead_of_throwing()
    {
        var (_, _, verify) = Emit();

        Assert.IsFalse((bool)verify.Invoke(null, ["anything", "not base64!", "also not"])!);
        Assert.IsFalse((bool)verify.Invoke(null, ["anything", "", ""])!);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter PasswordHasherTemplateTests`
Expected: FAIL — `PasswordHasherTemplate` does not exist (CS0246).

- [ ] **Step 3: Write the implementation**

Create `AppScaffolding/Auth/PasswordHasherTemplate.cs`:

```csharp
namespace AppScaffolding.Auth;

/// <summary>Emits <c>Auth/PasswordHasher.cs</c>: PBKDF2 password hashing for a generated app.</summary>
/// <remarks>
/// Framework only — <c>Rfc2898DeriveBytes.Pbkdf2</c> is in System.Security.Cryptography, so a
/// generated app takes no package reference for this. The parameters are named constants in the
/// emitted file rather than inlined, because a future app that needs to raise the iteration count
/// has to be able to find it.
/// </remarks>
public static class PasswordHasherTemplate
{
    /// <summary>Source of <c>Auth/PasswordHasher.cs</c> in <paramref name="rootNamespace"/>.Auth.</summary>
    public static string Source(string rootNamespace) => $$"""
using System;
using System.Security.Cryptography;

namespace {{rootNamespace}}.Auth;

/// <summary>Hashes and verifies passwords with PBKDF2. Nothing reversible is ever stored.</summary>
public static class PasswordHasher
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int Iterations = 100_000;

    /// <summary>A fresh hash and salt for <paramref name="password"/>, both Base64.</summary>
    public static (string Hash, string Salt) Create(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);

        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password ?? string.Empty, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);

        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    /// <summary>
    /// Whether <paramref name="password"/> is the one <paramref name="hash"/> was made from.
    /// </summary>
    /// <remarks>
    /// False rather than an exception for a stored value that is not usable Base64 or is the wrong
    /// length: a hand-edited store should refuse the sign-in, not crash the application on its
    /// login screen. The comparison is fixed-time, so the answer takes the same time whether the
    /// first byte differs or the last.
    /// </remarks>
    public static bool Verify(string password, string hash, string salt)
    {
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(salt))
            return false;

        byte[] saltBytes;
        byte[] expected;

        try
        {
            saltBytes = Convert.FromBase64String(salt);
            expected = Convert.FromBase64String(hash);
        }
        catch (FormatException)
        {
            return false;
        }

        if (expected.Length != HashBytes || saltBytes.Length == 0)
            return false;

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            password ?? string.Empty, saltBytes, Iterations, HashAlgorithmName.SHA256, HashBytes);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
""";
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter PasswordHasherTemplateTests`
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/Auth/PasswordHasherTemplate.cs CodeGenerator.Tests/App/PasswordHasherTemplateTests.cs
git commit -m "feat(auth): a generated app hashes passwords with PBKDF2"
```

---

### Task 2: The user record and the role

**Files:**
- Create: `AppScaffolding/Auth/AuthModelTemplates.cs`
- Test: `CodeGenerator.Tests/App/AuthModelTemplateTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `AppScaffolding.Auth.AuthModelTemplates.Role(string rootNamespace) -> string` (emits `Auth/AppRole.cs`, `public enum AppRole { Admin, User }`) and `AuthModelTemplates.User(string rootNamespace) -> string` (emits `Models/User.cs`, class `User` with `Id`, `UserName`, `DisplayName`, `PasswordHash`, `PasswordSalt`, `Role`, `IsActive`, `CreatedOn`, `ModifiedOn`).

- [ ] **Step 1: Write the failing test**

```csharp
using System.Reflection;
using AppScaffolding.Auth;
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.App;

/// <summary>The user record rides IStore&lt;T&gt; like any other entity, so it must satisfy the
/// same constraint every store method declares: <c>where T : class, new()</c>.</summary>
[TestClass]
public class AuthModelTemplateTests
{
    private static Assembly EmitBoth()
    {
        var source = AuthModelTemplates.Role("Acme") + "\n" + AuthModelTemplates.User("Acme");

        // Flatten strips the file-scoped namespaces and the Acme.Auth using, so the two fragments
        // become one compilation unit. Compiling them apart would not prove User can see AppRole.
        var flattened = EmittedSource.Flatten(
            AuthModelTemplates.Role("Acme").Replace("namespace Acme.Auth;", string.Empty),
            AuthModelTemplates.User("Acme")
                .Replace("namespace Acme.Models;", string.Empty)
                .Replace("using Acme.Auth;", string.Empty));

        EmittedSource.AssertCompiles(flattened);

        return Assembly.Load(Compiler.Compile(flattened).AssemblyBytes!);
    }

    [TestMethod]
    public void The_role_has_exactly_two_values()
    {
        var role = EmitBoth().GetType("AppRole")!;

        Assert.IsTrue(role.IsEnum);
        CollectionAssert.AreEquivalent(new[] { "Admin", "User" }, Enum.GetNames(role));
    }

    [TestMethod]
    public void The_user_carries_the_fields_the_login_needs()
    {
        var user = EmitBoth().GetType("User")!;

        foreach (var name in new[]
                 {
                     "Id", "UserName", "DisplayName", "PasswordHash", "PasswordSalt",
                     "Role", "IsActive",
                 })
        {
            Assert.IsNotNull(user.GetProperty(name), $"User is missing {name}.");
        }
    }

    /// <summary>IStore&lt;T&gt; declares <c>where T : class, new()</c>. A User with only a
    /// non-default constructor would fail to compile at every StoreFactory.Create call site, and
    /// nothing in this file would have said so.</summary>
    [TestMethod]
    public void The_user_has_a_parameterless_constructor()
        => Assert.IsNotNull(EmitBoth().GetType("User")!.GetConstructor(Type.EmptyTypes));

    /// <summary>A new account must be usable, and must not be an administrator by accident.</summary>
    [TestMethod]
    public void A_new_user_defaults_to_the_plain_role_and_is_active()
    {
        var assembly = EmitBoth();
        var user = Activator.CreateInstance(assembly.GetType("User")!)!;

        Assert.AreEqual("User", user.GetType().GetProperty("Role")!.GetValue(user)!.ToString());
        Assert.IsTrue((bool)user.GetType().GetProperty("IsActive")!.GetValue(user)!);
    }

    /// <summary>The password itself is never a property. If one is ever added, the store writes
    /// plaintext to disk.</summary>
    [TestMethod]
    public void The_user_has_no_plaintext_password_property()
    {
        var user = EmitBoth().GetType("User")!;

        Assert.IsNull(user.GetProperty("Password"));
        Assert.IsNull(user.GetProperty("PlainPassword"));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter AuthModelTemplateTests`
Expected: FAIL — `AuthModelTemplates` does not exist.

- [ ] **Step 3: Write the implementation**

Create `AppScaffolding/Auth/AuthModelTemplates.cs`:

```csharp
namespace AppScaffolding.Auth;

/// <summary>Emits the two types the auth core stores: the role enum and the user record.</summary>
/// <remarks>
/// <c>User</c> goes into <c>Models/</c> beside the discovered classes and persists through
/// <c>IStore&lt;User&gt;</c>, so JSON and SQLite both work with no new persistence code. It is
/// EMITTED, not DISCOVERED: it must never reach the "Generated N class(es)" summary, the
/// navigation list, the seeded sample rows or ValidatorFactory.
/// <para>
/// The audit fields are present because <c>DomainModelMapper.AddAuditFields</c> gives every
/// discovered class the same pair, and a User that lacked them would be the one model in the
/// folder with a different shape.
/// </para>
/// </remarks>
public static class AuthModelTemplates
{
    /// <summary>Source of <c>Auth/AppRole.cs</c>.</summary>
    public static string Role(string rootNamespace) => $$"""
namespace {{rootNamespace}}.Auth;

/// <summary>What a signed-in user may do. Exactly two values: an Admin reaches the Users screen
/// and may delete master records; a User may do everything else.</summary>
public enum AppRole
{
    Admin,
    User
}
""";

    /// <summary>Source of <c>Models/User.cs</c>.</summary>
    public static string User(string rootNamespace) => $$"""
using System;
using {{rootNamespace}}.Auth;

namespace {{rootNamespace}}.Models;

/// <summary>An account that can sign in. The password is never stored — only a PBKDF2 hash and
/// the salt it was made with.</summary>
public class User
{
    public int Id { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string PasswordSalt { get; set; } = string.Empty;

    public AppRole Role { get; set; } = AppRole.User;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedOn { get; set; }

    public DateTime ModifiedOn { get; set; }
}
""";
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter AuthModelTemplateTests`
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/Auth/AuthModelTemplates.cs CodeGenerator.Tests/App/AuthModelTemplateTests.cs
git commit -m "feat(auth): a generated app has a user record and two roles"
```

---

### Task 3: The sign-in service

**Files:**
- Create: `AppScaffolding/Auth/AuthServiceTemplate.cs`
- Test: `CodeGenerator.Tests/App/AuthServiceTemplateTests.cs`

**Interfaces:**
- Consumes: `PasswordHasherTemplate.Source`, `AuthModelTemplates.Role/User` (Tasks 1–2).
- Produces: `AppScaffolding.Auth.AuthServiceTemplate.Source(string rootNamespace) -> string`, emitting `Auth/AuthService.cs` containing `enum SignInOutcome { Succeeded, UnknownUser, WrongPassword, Disabled }`, `sealed record SignInResult(SignInOutcome Outcome, User? User)` with `bool Succeeded`, `const string FailureMessage`, and `static class AuthService` with `SignInResult SignIn(string userName, string password)` and `bool ChangePassword(int userId, string newPassword)`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections;
using System.Reflection;
using AppScaffolding;
using AppScaffolding.Auth;
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Sign-in decides who gets in, so every test here runs the emitted code against a real in-memory
/// store. A compile test would pass just as happily on a service that returns Succeeded always.
/// </summary>
[TestClass]
public class AuthServiceTemplateTests
{
    /// <summary>An IStore&lt;T&gt; over a static list, standing in for JsonStore. The emitted
    /// AuthService reaches its store through StoreFactory, so the fake replaces StoreFactory
    /// rather than being injected — the generated app has no container.</summary>
    private const string StoreStub = """
using System.Collections.Generic;
using System.Linq;

namespace Acme.Data;

public interface IStore<T> where T : new()
{
    List<T> GetAll();
    bool SaveAll(List<T> all);
    int NextId(IEnumerable<T> existing);
    T NewItem(IEnumerable<T> existing);
}

public sealed class MemoryStore<T> : IStore<T> where T : class, new()
{
    public static readonly List<T> Rows = new();
    public List<T> GetAll() => Rows.ToList();
    public bool SaveAll(List<T> all) { Rows.Clear(); Rows.AddRange(all); return true; }
    public int NextId(IEnumerable<T> existing) => Rows.Count + 1;
    public T NewItem(IEnumerable<T> existing) => new();
}

public static class StoreFactory
{
    public static IStore<T> Create<T>() where T : class, new() => new MemoryStore<T>();
}
""";

    private static Assembly Emit()
    {
        var source = EmittedSource.Flatten(
            StoreStub.Replace("namespace Acme.Data;", string.Empty),
            AuthModelTemplates.Role("Acme").Replace("namespace Acme.Auth;", string.Empty),
            AuthModelTemplates.User("Acme")
                .Replace("namespace Acme.Models;", string.Empty)
                .Replace("using Acme.Auth;", string.Empty),
            PasswordHasherTemplate.Source("Acme").Replace("namespace Acme.Auth;", string.Empty),
            AuthServiceTemplate.Source("Acme").Replace("namespace Acme.Auth;", string.Empty));

        EmittedSource.AssertCompiles(source);

        return Assembly.Load(Compiler.Compile(source).AssemblyBytes!);
    }

    /// <summary>Puts one account into the MemoryStore the emitted StoreFactory hands out.</summary>
    private static void GivenUser(
        Assembly assembly, string userName, string password, string role = "User",
        bool active = true)
    {
        var userType = assembly.GetType("User")!;
        var user = Activator.CreateInstance(userType)!;

        var created = assembly.GetType("PasswordHasher")!
            .GetMethod("Create")!.Invoke(null, [password])!;

        userType.GetProperty("Id")!.SetValue(user, 1);
        userType.GetProperty("UserName")!.SetValue(user, userName);
        userType.GetProperty("PasswordHash")!
            .SetValue(user, created.GetType().GetField("Item1")!.GetValue(created));
        userType.GetProperty("PasswordSalt")!
            .SetValue(user, created.GetType().GetField("Item2")!.GetValue(created));
        userType.GetProperty("Role")!
            .SetValue(user, Enum.Parse(assembly.GetType("AppRole")!, role));
        userType.GetProperty("IsActive")!.SetValue(user, active);

        var rows = (IList)assembly.GetType("MemoryStore`1")!
            .MakeGenericType(userType).GetField("Rows")!.GetValue(null)!;

        rows.Clear();
        rows.Add(user);
    }

    private static object SignIn(Assembly assembly, string userName, string password)
        => assembly.GetType("AuthService")!.GetMethod("SignIn")!
            .Invoke(null, [userName, password])!;

    private static string Outcome(object result)
        => result.GetType().GetProperty("Outcome")!.GetValue(result)!.ToString()!;

    [TestMethod]
    public void The_right_password_signs_in()
    {
        var assembly = Emit();
        GivenUser(assembly, "admin", "letmein");

        var result = SignIn(assembly, "admin", "letmein");

        Assert.AreEqual("Succeeded", Outcome(result));
        Assert.IsTrue((bool)result.GetType().GetProperty("Succeeded")!.GetValue(result)!);
    }

    [TestMethod]
    public void The_wrong_password_does_not_sign_in()
    {
        var assembly = Emit();
        GivenUser(assembly, "admin", "letmein");

        Assert.AreEqual("WrongPassword", Outcome(SignIn(assembly, "admin", "guess")));
    }

    [TestMethod]
    public void An_unknown_user_does_not_sign_in()
    {
        var assembly = Emit();
        GivenUser(assembly, "admin", "letmein");

        Assert.AreEqual("UnknownUser", Outcome(SignIn(assembly, "nobody", "letmein")));
    }

    /// <summary>A disabled account must be refused even with the right password — otherwise
    /// IsActive is decoration.</summary>
    [TestMethod]
    public void A_disabled_account_does_not_sign_in()
    {
        var assembly = Emit();
        GivenUser(assembly, "admin", "letmein", active: false);

        Assert.AreEqual("Disabled", Outcome(SignIn(assembly, "admin", "letmein")));
    }

    /// <summary>The user name is not case-sensitive; a login that rejects "Admin" for "admin"
    /// reads as a broken password.</summary>
    [TestMethod]
    public void The_user_name_is_matched_case_insensitively()
    {
        var assembly = Emit();
        GivenUser(assembly, "admin", "letmein");

        Assert.AreEqual("Succeeded", Outcome(SignIn(assembly, "ADMIN", "letmein")));
    }

    /// <summary>Three different failures, one message. Telling the screen which of them happened
    /// tells an attacker which user names exist.</summary>
    [TestMethod]
    public void Every_failure_reports_the_same_message()
    {
        var assembly = Emit();
        GivenUser(assembly, "admin", "letmein", active: false);

        var expected = (string)assembly.GetType("SignInResult")!
            .GetField("FailureMessage")!.GetValue(null)!;

        foreach (var (user, password) in new[]
                 { ("admin", "guess"), ("nobody", "letmein"), ("admin", "letmein") })
        {
            var result = SignIn(assembly, user, password);
            Assert.AreEqual(
                expected, result.GetType().GetProperty("Message")!.GetValue(result));
        }
    }

    /// <summary>A failed sign-in must not hand back the record it refused.</summary>
    [TestMethod]
    public void A_failed_sign_in_returns_no_user()
    {
        var assembly = Emit();
        GivenUser(assembly, "admin", "letmein");

        var result = SignIn(assembly, "admin", "guess");

        Assert.IsNull(result.GetType().GetProperty("User")!.GetValue(result));
    }

    [TestMethod]
    public void A_changed_password_replaces_the_old_one()
    {
        var assembly = Emit();
        GivenUser(assembly, "admin", "letmein");

        Assert.IsTrue((bool)assembly.GetType("AuthService")!
            .GetMethod("ChangePassword")!.Invoke(null, [1, "newsecret"])!);

        Assert.AreEqual("WrongPassword", Outcome(SignIn(assembly, "admin", "letmein")));
        Assert.AreEqual("Succeeded", Outcome(SignIn(assembly, "admin", "newsecret")));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter AuthServiceTemplateTests`
Expected: FAIL — `AuthServiceTemplate` does not exist.

- [ ] **Step 3: Write the implementation**

Create `AppScaffolding/Auth/AuthServiceTemplate.cs`:

```csharp
namespace AppScaffolding.Auth;

/// <summary>Emits <c>Auth/AuthService.cs</c>: sign-in and change-password over the user store.</summary>
/// <remarks>
/// Static, like <c>SeedData</c> and <c>StoreFactory</c>, because a generated app has no container
/// to resolve a service from.
/// </remarks>
public static class AuthServiceTemplate
{
    /// <summary>Source of <c>Auth/AuthService.cs</c> in <paramref name="rootNamespace"/>.Auth.</summary>
    public static string Source(string rootNamespace) => $$"""
using System;
using System.Linq;
using {{rootNamespace}}.Data;
using {{rootNamespace}}.Models;

namespace {{rootNamespace}}.Auth;

/// <summary>Why a sign-in attempt ended the way it did. Kept apart from the message the screen
/// shows, which is the same for every failure.</summary>
public enum SignInOutcome
{
    Succeeded,
    UnknownUser,
    WrongPassword,
    Disabled
}

/// <summary>The result of one sign-in attempt.</summary>
/// <remarks>
/// Three failures collapse into one message on purpose. "No such user" and "wrong password" as
/// separate messages tell anyone who asks which user names exist.
/// </remarks>
public sealed record SignInResult(SignInOutcome Outcome, User? User)
{
    /// <summary>The only thing a failed sign-in ever tells the person at the keyboard.</summary>
    public const string FailureMessage = "User name or password not recognised.";

    public bool Succeeded => Outcome == SignInOutcome.Succeeded;

    /// <summary>The message to show, or null when the sign-in worked.</summary>
    public string? Message => Succeeded ? null : FailureMessage;
}

/// <summary>Signs users in and changes their passwords.</summary>
public static class AuthService
{
    /// <summary>Attempts a sign-in. Never throws for bad input.</summary>
    public static SignInResult SignIn(string userName, string password)
    {
        var user = StoreFactory.Create<User>().GetAll()
            .FirstOrDefault(u => string.Equals(
                u.UserName, userName ?? string.Empty, StringComparison.OrdinalIgnoreCase));

        if (user is null)
            return new SignInResult(SignInOutcome.UnknownUser, null);

        if (!user.IsActive)
            return new SignInResult(SignInOutcome.Disabled, null);

        if (!PasswordHasher.Verify(password ?? string.Empty, user.PasswordHash, user.PasswordSalt))
            return new SignInResult(SignInOutcome.WrongPassword, null);

        return new SignInResult(SignInOutcome.Succeeded, user);
    }

    /// <summary>Replaces the password of the account with <paramref name="userId"/>. False when
    /// there is no such account or the store could not be written.</summary>
    public static bool ChangePassword(int userId, string newPassword)
    {
        if (string.IsNullOrEmpty(newPassword))
            return false;

        var store = StoreFactory.Create<User>();
        var all = store.GetAll();
        var user = all.FirstOrDefault(u => u.Id == userId);

        if (user is null)
            return false;

        var (hash, salt) = PasswordHasher.Create(newPassword);

        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        user.ModifiedOn = DateTime.Now;

        return store.SaveAll(all);
    }
}
""";
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter AuthServiceTemplateTests`
Expected: PASS, 8 tests.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/Auth/AuthServiceTemplate.cs CodeGenerator.Tests/App/AuthServiceTemplateTests.cs
git commit -m "feat(auth): a generated app can sign a user in"
```

---

### Task 4: The seeded admin account

**Files:**
- Create: `AppScaffolding/Auth/UserSeedTemplate.cs`
- Test: `CodeGenerator.Tests/App/UserSeedTemplateTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3.
- Produces: `AppScaffolding.Auth.UserSeedTemplate.Source(string rootNamespace) -> string`, emitting `Auth/UserSeed.cs` with `static class UserSeed` exposing `const string DefaultUserName = "admin"`, `const string DefaultPassword = "admin"` and `static void EnsureAdminSeeded()`. Also `UserSeedTemplate.FirstRunHint -> string`, the sentence the login screens show, so the view templates cannot drift from the seeded credentials.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections;
using System.Reflection;
using AppScaffolding.Auth;
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.App;

/// <summary>Seeding runs at every application start, so "does it run twice" is the whole
/// test.</summary>
[TestClass]
public class UserSeedTemplateTests
{
    private static Assembly Emit()
    {
        var source = EmittedSource.Flatten(
            AuthServiceTemplateTests.StoreStubSource,
            AuthModelTemplates.Role("Acme").Replace("namespace Acme.Auth;", string.Empty),
            AuthModelTemplates.User("Acme")
                .Replace("namespace Acme.Models;", string.Empty)
                .Replace("using Acme.Auth;", string.Empty),
            PasswordHasherTemplate.Source("Acme").Replace("namespace Acme.Auth;", string.Empty),
            AuthServiceTemplate.Source("Acme").Replace("namespace Acme.Auth;", string.Empty),
            UserSeedTemplate.Source("Acme").Replace("namespace Acme.Auth;", string.Empty));

        EmittedSource.AssertCompiles(source);

        var assembly = Assembly.Load(Compiler.Compile(source).AssemblyBytes!);

        Rows(assembly).Clear();

        return assembly;
    }

    private static IList Rows(Assembly assembly)
        => (IList)assembly.GetType("MemoryStore`1")!
            .MakeGenericType(assembly.GetType("User")!)
            .GetField("Rows")!.GetValue(null)!;

    private static void Seed(Assembly assembly)
        => assembly.GetType("UserSeed")!.GetMethod("EnsureAdminSeeded")!.Invoke(null, null);

    [TestMethod]
    public void The_first_run_creates_the_admin_account()
    {
        var assembly = Emit();

        Seed(assembly);

        Assert.HasCount(1, Rows(assembly));
    }

    /// <summary>The seeded account must be able to reach the Users screen; seeding a plain User
    /// would lock the first run out of account management entirely.</summary>
    [TestMethod]
    public void The_seeded_account_is_an_administrator_and_can_sign_in()
    {
        var assembly = Emit();
        Seed(assembly);

        var result = assembly.GetType("AuthService")!.GetMethod("SignIn")!
            .Invoke(null, ["admin", "admin"])!;

        Assert.AreEqual(
            "Succeeded", result.GetType().GetProperty("Outcome")!.GetValue(result)!.ToString());

        var user = result.GetType().GetProperty("User")!.GetValue(result)!;
        Assert.AreEqual("Admin", user.GetType().GetProperty("Role")!.GetValue(user)!.ToString());
    }

    /// <summary>Seeding runs on every start. A second admin on the second run would be a new
    /// account with the default password, which is a back door.</summary>
    [TestMethod]
    public void A_second_run_creates_nothing()
    {
        var assembly = Emit();

        Seed(assembly);
        Seed(assembly);

        Assert.HasCount(1, Rows(assembly));
    }

    /// <summary>Once someone has changed the admin password, a later start must not put the
    /// default back.</summary>
    [TestMethod]
    public void A_changed_password_survives_the_next_start()
    {
        var assembly = Emit();
        Seed(assembly);

        assembly.GetType("AuthService")!.GetMethod("ChangePassword")!
            .Invoke(null, [1, "chosen"]);

        Seed(assembly);

        var result = assembly.GetType("AuthService")!.GetMethod("SignIn")!
            .Invoke(null, ["admin", "admin"])!;

        Assert.AreEqual(
            "WrongPassword",
            result.GetType().GetProperty("Outcome")!.GetValue(result)!.ToString());
    }

    /// <summary>The hint on the login screen names the seeded credentials. Two literals would
    /// drift the day someone changes the default.</summary>
    [TestMethod]
    public void The_first_run_hint_names_the_seeded_credentials()
    {
        StringAssert.Contains(UserSeedTemplate.FirstRunHint, "admin");
        StringAssert.Contains(
            UserSeedTemplate.Source("Acme"), "DefaultUserName = \"admin\"");
    }
}
```

Add to `AuthServiceTemplateTests` so the store stub is shared rather than copied:

```csharp
    /// <summary>Exposed for UserSeedTemplateTests, which needs the same in-memory store.</summary>
    internal static string StoreStubSource => StoreStub.Replace("namespace Acme.Data;", string.Empty);
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter UserSeedTemplateTests`
Expected: FAIL — `UserSeedTemplate` does not exist.

- [ ] **Step 3: Write the implementation**

Create `AppScaffolding/Auth/UserSeedTemplate.cs`:

```csharp
namespace AppScaffolding.Auth;

/// <summary>Emits <c>Auth/UserSeed.cs</c>: the account that makes the first run possible.</summary>
/// <remarks>
/// Separate from <c>SeedData</c>, and it runs earlier. Entity seeding happens when the view models
/// load, which is now AFTER the login screen — an admin account seeded there would not exist yet
/// when it is needed.
/// </remarks>
public static class UserSeedTemplate
{
    /// <summary>The sentence the login screens show on a store with no accounts yet. Lives here
    /// so the view templates cannot name credentials the seeder does not create.</summary>
    public const string FirstRunHint = "First run: sign in as admin / admin, then change it.";

    /// <summary>Source of <c>Auth/UserSeed.cs</c> in <paramref name="rootNamespace"/>.Auth.</summary>
    public static string Source(string rootNamespace) => $$"""
using System;
using System.Linq;
using {{rootNamespace}}.Data;
using {{rootNamespace}}.Models;

namespace {{rootNamespace}}.Auth;

/// <summary>Creates the first administrator, once.</summary>
public static class UserSeed
{
    public const string DefaultUserName = "admin";
    public const string DefaultPassword = "admin";

    /// <summary>The hint the login screen shows while the default account is still in place.</summary>
    public const string FirstRunHint = "{{FirstRunHint}}";

    /// <summary>
    /// Creates the default administrator when the user store is empty, and does nothing otherwise.
    /// </summary>
    /// <remarks>
    /// The guard is "any account at all", not "no account called admin". Someone who renamed the
    /// administrator would otherwise get a fresh admin/admin back on the next start, which is a
    /// back door into an application that looked locked down.
    /// </remarks>
    public static void EnsureAdminSeeded()
    {
        var store = StoreFactory.Create<User>();
        var all = store.GetAll();

        if (all.Count > 0)
            return;

        var (hash, salt) = PasswordHasher.Create(DefaultPassword);
        var now = DateTime.Now;

        all.Add(new User
        {
            Id = store.NextId(all),
            UserName = DefaultUserName,
            DisplayName = "Administrator",
            PasswordHash = hash,
            PasswordSalt = salt,
            Role = AppRole.Admin,
            IsActive = true,
            CreatedOn = now,
            ModifiedOn = now,
        });

        store.SaveAll(all);
    }

    /// <summary>Whether the default account is still signing in with its default password, so a
    /// screen can say so.</summary>
    public static bool IsFirstRun()
        => AuthService.SignIn(DefaultUserName, DefaultPassword).Succeeded;
}
""";
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "UserSeedTemplateTests|AuthServiceTemplateTests"`
Expected: PASS, 13 tests.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/Auth/UserSeedTemplate.cs CodeGenerator.Tests/App/UserSeedTemplateTests.cs CodeGenerator.Tests/App/AuthServiceTemplateTests.cs
git commit -m "feat(auth): the first run seeds an administrator"
```

---

### Task 5: The desktop session and login view model

**Files:**
- Create: `AppScaffolding/Auth/SessionTemplate.cs`, `AppScaffolding/Auth/LoginViewModelTemplate.cs`
- Test: `CodeGenerator.Tests/App/SessionTemplateTests.cs`

**Interfaces:**
- Consumes: Tasks 1–4.
- Produces: `SessionTemplate.Source(string rootNamespace)` emitting `Auth/Session.cs` — `static class Session` with `User? Current`, `bool IsAdmin`, `bool SignOutRequested`, `SignIn(User)`, `RequestSignOut()`, `Clear()`. `LoginViewModelTemplate.Source(string rootNamespace)` emitting `ViewModels/LoginViewModel.cs` — `sealed class LoginViewModel : ObservableObject` with `string UserName`, `string Password`, `string? Error`, `string Hint`, `bool SignedIn`, `ICommand SignInCommand`, `event EventHandler? Succeeded`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Reflection;
using AppScaffolding.Auth;
using CodeGenerationEngine.Core;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Session is a STATIC holder of the signed-in user, which is correct for a desktop process and
/// catastrophic in a server one — it would be shared by every visitor. These tests pin it to the
/// desktop targets.
/// </summary>
[TestClass]
public class SessionTemplateTests
{
    private static Assembly Emit()
    {
        var source = EmittedSource.Flatten(
            AuthServiceTemplateTests.StoreStubSource,
            AuthModelTemplates.Role("Acme").Replace("namespace Acme.Auth;", string.Empty),
            AuthModelTemplates.User("Acme")
                .Replace("namespace Acme.Models;", string.Empty)
                .Replace("using Acme.Auth;", string.Empty),
            SessionTemplate.Source("Acme")
                .Replace("namespace Acme.Auth;", string.Empty)
                .Replace("using Acme.Models;", string.Empty));

        EmittedSource.AssertCompiles(source);

        return Assembly.Load(Compiler.Compile(source).AssemblyBytes!);
    }

    [TestMethod]
    public void A_fresh_session_holds_nobody_and_is_not_an_admin()
    {
        var session = Emit().GetType("Session")!;

        session.GetMethod("Clear")!.Invoke(null, null);

        Assert.IsNull(session.GetProperty("Current")!.GetValue(null));
        Assert.IsFalse((bool)session.GetProperty("IsAdmin")!.GetValue(null)!);
    }

    /// <summary>Sign-out is a flag the shell reads on close, which is what lets App.xaml.cs loop
    /// back to the login instead of restarting the process.</summary>
    [TestMethod]
    public void Requesting_sign_out_raises_the_flag_and_clearing_lowers_it()
    {
        var session = Emit().GetType("Session")!;

        session.GetMethod("RequestSignOut")!.Invoke(null, null);
        Assert.IsTrue((bool)session.GetProperty("SignOutRequested")!.GetValue(null)!);

        session.GetMethod("Clear")!.Invoke(null, null);
        Assert.IsFalse((bool)session.GetProperty("SignOutRequested")!.GetValue(null)!);
    }

    [TestMethod]
    public void An_admin_signs_in_as_an_admin()
    {
        var assembly = Emit();
        var session = assembly.GetType("Session")!;
        var userType = assembly.GetType("User")!;
        var user = Activator.CreateInstance(userType)!;

        userType.GetProperty("Role")!
            .SetValue(user, Enum.Parse(assembly.GetType("AppRole")!, "Admin"));

        session.GetMethod("SignIn")!.Invoke(null, [user]);

        Assert.IsTrue((bool)session.GetProperty("IsAdmin")!.GetValue(null)!);

        session.GetMethod("Clear")!.Invoke(null, null);
    }

    /// <summary>The guard that keeps a per-process static out of the web target. Blazor Server
    /// runs every visitor in one process; a static Current would sign them all in as whoever
    /// signed in last.</summary>
    [TestMethod]
    public void The_web_target_emits_no_session()
    {
        var files = new AppScaffolding.Targets.BlazorTarget()
            .FrontEndFiles("Shop", ["Order"], AppScaffolding.AppCustomization.Default);

        Assert.IsFalse(
            files.Any(f => f.Name == "Session.cs"),
            "A static signed-in user in a server process is shared by every visitor.");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter SessionTemplateTests`
Expected: FAIL — `SessionTemplate` does not exist.

- [ ] **Step 3: Write the implementation**

Create `AppScaffolding/Auth/SessionTemplate.cs`:

```csharp
namespace AppScaffolding.Auth;

/// <summary>Emits <c>Auth/Session.cs</c>: who is signed in, for a DESKTOP application.</summary>
/// <remarks>
/// Emitted by the WPF and Windows Forms targets only. A static current-user is exactly right in a
/// process that serves one person and exactly wrong in a server that serves many — Blazor holds
/// the signed-in user in an authentication cookie instead, and never receives this file.
/// </remarks>
public static class SessionTemplate
{
    /// <summary>Source of <c>Auth/Session.cs</c> in <paramref name="rootNamespace"/>.Auth.</summary>
    public static string Source(string rootNamespace) => $$"""
using {{rootNamespace}}.Models;

namespace {{rootNamespace}}.Auth;

/// <summary>The signed-in user for this desktop process.</summary>
public static class Session
{
    /// <summary>The signed-in user, or null before a sign-in.</summary>
    public static User? Current { get; private set; }

    /// <summary>Whether the shell closed because the user asked to sign out, rather than to quit.
    /// The entry point reads this to decide whether to show the login again.</summary>
    public static bool SignOutRequested { get; private set; }

    /// <summary>Whether the signed-in user may reach the Users screen and delete master records.</summary>
    public static bool IsAdmin => Current is { Role: AppRole.Admin };

    /// <summary>The name to show in the shell header.</summary>
    public static string DisplayName
        => Current is null
            ? string.Empty
            : Current.DisplayName.Length > 0 ? Current.DisplayName : Current.UserName;

    public static void SignIn(User user)
    {
        Current = user;
        SignOutRequested = false;
    }

    public static void RequestSignOut() => SignOutRequested = true;

    public static void Clear()
    {
        Current = null;
        SignOutRequested = false;
    }
}
""";
}
```

Create `AppScaffolding/Auth/LoginViewModelTemplate.cs`:

```csharp
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

/// <summary>Drives the login screen. The view supplies the password, which is never stored on the
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
        private set => SetField(ref _error, value);
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
            OnPropertyChanged(nameof(HasError));
            return;
        }

        Session.SignIn(result.User!);

        // Dropped as soon as it has been used. Nothing needs it again, and a view model outliving
        // the window would otherwise hold it.
        Password = string.Empty;

        OnPropertyChanged(nameof(HasError));
        Succeeded?.Invoke(this, EventArgs.Empty);
    }
}
""";
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter SessionTemplateTests`
Expected: PASS, 4 tests. `The_web_target_emits_no_session` passes trivially now and becomes meaningful after Task 11 — leave it in place.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/Auth/SessionTemplate.cs AppScaffolding/Auth/LoginViewModelTemplate.cs CodeGenerator.Tests/App/SessionTemplateTests.cs
git commit -m "feat(auth): the desktop targets hold a signed-in session"
```

---

### Task 6: `AppCustomization.Login` and the `nologin` flag

**Files:**
- Modify: `AppScaffolding/AppCustomization.cs`
- Modify: `CodeGenerator/App/InlineCustomization.cs:40`, `:103-105`
- Test: `CodeGenerator.Tests/App/LoginFlagTests.cs`
- Test: `CodeGenerator.Tests/App/InlineCustomizationTests.cs` (add cases)

**Interfaces:**
- Consumes: nothing.
- Produces: `AppCustomization.Login` (`bool`, positioned after `SampleSeedData`, default `true`). Every later task reads `customization.Login`.

- [ ] **Step 1: Write the failing test**

Create `CodeGenerator.Tests/App/LoginFlagTests.cs`:

```csharp
using AppScaffolding;
using CodeGenerator.App;

namespace CodeGenerator.Tests.App;

/// <summary>`nologin` is the switch the whole regression proof hangs on: with it, a named layout
/// must still produce exactly what the generator produced before this feature.</summary>
[TestClass]
public class LoginFlagTests
{
    [TestMethod]
    public void Login_is_on_by_default()
        => Assert.IsTrue(AppCustomization.Default.Login);

    [TestMethod]
    public void The_nologin_flag_turns_it_off()
    {
        var (remaining, customization) =
            InlineCustomization.Split("a shop nologin", AppCustomization.Default);

        Assert.AreEqual("a shop", remaining);
        Assert.IsFalse(customization.Login);
    }

    /// <summary>Options are stripped right to left, so a flag must not swallow the word before
    /// it when that word is a value key's value.</summary>
    [TestMethod]
    public void The_flag_reads_beside_the_other_options()
    {
        var (remaining, customization) = InlineCustomization.Split(
            "a shop company Codis layout sidebar nologin noseed", AppCustomization.Default);

        Assert.AreEqual("a shop", remaining);
        Assert.IsFalse(customization.Login);
        Assert.IsFalse(customization.SampleSeedData);
        Assert.AreEqual("Codis", customization.Company);
        Assert.AreEqual(AppLayout.Sidebar, customization.Layout);
    }

    /// <summary>The word must not be eaten out of ordinary prose. "nologin" is not a word anyone
    /// types by accident, but "a shop with no login" is a sentence someone might.</summary>
    [TestMethod]
    public void Prose_about_logins_is_left_alone()
    {
        var (remaining, customization) =
            InlineCustomization.Split("a shop with no login", AppCustomization.Default);

        Assert.AreEqual("a shop with no login", remaining);
        Assert.IsTrue(customization.Login);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter LoginFlagTests`
Expected: FAIL — `AppCustomization` has no `Login` member.

- [ ] **Step 3: Write the implementation**

In `AppScaffolding/AppCustomization.cs`, add the parameter after `SampleSeedData`:

```csharp
    bool SampleSeedData,
    bool Login,
    string NamespacePrefix,
```

and in `Default`:

```csharp
        SampleSeedData: true,
        Login: true,
        NamespacePrefix: string.Empty,
```

Add the doc comment above the record's `Login` usage site — put it on the property by documenting the parameter in the record's XML block:

```csharp
/// <param name="Login">
/// Whether the application gates itself behind a login screen. True by default: an application
/// with screens is expected to have accounts. `nologin` turns it off, and that combination is the
/// only one under which a named layout still produces byte-identical output to the generator's
/// pre-auth behaviour.
/// </param>
```

In `CodeGenerator/App/InlineCustomization.cs`, extend the flags:

```csharp
    /// <summary>Options that are a bare word.</summary>
    private static readonly string[] Flags =
        ["novalidation", "logging", "noseed", "nologin"];
```

and the switch:

```csharp
            "noseed" => customization with { SampleSeedData = false },
            "nologin" => customization with { Login = false },
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "LoginFlagTests|InlineCustomizationTests|CustomizationMergeTests|CustomizationDefaultsTests"`
Expected: PASS. Any positional-construction call sites of `AppCustomization` that now fail to compile must be fixed by naming the new argument — do not reorder the record.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/AppCustomization.cs CodeGenerator/App/InlineCustomization.cs CodeGenerator.Tests/App/LoginFlagTests.cs
git commit -m "feat(build app): nologin turns the login screen off"
```

---

### Task 7: The remaining `nologin` surfaces

**Files:**
- Modify: `CodeGenerator/App/BuildAppCommand.cs:226`
- Modify: `CodeGenerator/App/CustomizationSummary.cs:57`
- Modify: `CodeGenerator/App/CustomizationWizard.cs`, `CustomizationMenus.cs`, `CustomizationDefaults.cs`
- Modify: `CodeGenerator/Web/Workflow/BuildCommandEcho.cs:83`
- Modify: `CodeGenerationEngine/Core/CommandDictionary.cs:603-610`
- Modify: `docs/commands.md:34`
- Test: `CodeGenerator.Tests/App/LoginFlagTests.cs` (extend), `CodeGenerator.Tests/Core/CommandDictionaryDataTests.cs:86`

**Interfaces:**
- Consumes: `AppCustomization.Login` (Task 6).
- Produces: no new API. This task makes the flag discoverable everywhere `noseed` is.

- [ ] **Step 1: Write the failing test**

Append to `CodeGenerator.Tests/App/LoginFlagTests.cs`:

```csharp
    /// <summary>Every surface `noseed` reaches, `nologin` must reach too — a flag the help does
    /// not mention is a flag nobody finds.</summary>
    [TestMethod]
    public void The_inline_options_help_line_lists_the_flag()
        => StringAssert.Contains(BuildAppCommand.InlineOptionsHelp, "nologin");

    [TestMethod]
    public void The_summary_reports_a_build_with_no_login()
    {
        var lines = CustomizationSummary.Describe(
            AppCustomization.Default with { Login = false });

        Assert.IsTrue(
            lines.Any(l => l.Contains("no login", StringComparison.Ordinal)),
            "The summary is where a user checks that the flag took effect.");
    }

    [TestMethod]
    public void The_summary_stays_silent_when_login_is_on()
    {
        var lines = CustomizationSummary.Describe(AppCustomization.Default);

        Assert.IsFalse(lines.Any(l => l.Contains("login", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>The web workflow echoes back the command that would repeat the build. An echoed
    /// command that omits the flag repeats a DIFFERENT build.</summary>
    [TestMethod]
    public void The_web_echo_repeats_the_flag()
        => StringAssert.Contains(
            BuildCommandEcho.For(AppCustomization.Default with { Login = false }, "a shop"),
            "nologin");
```

Note: match the real signatures of `CustomizationSummary.Describe` and `BuildCommandEcho.For` when writing these — read both files first and adapt the call, keeping the assertions.

In `CodeGenerator.Tests/Core/CommandDictionaryDataTests.cs:86`, add `"nologin"` to the expected-token list beside `"noseed"`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "LoginFlagTests|CommandDictionaryDataTests"`
Expected: FAIL — the help line, summary, echo and catalogue do not mention `nologin`.

- [ ] **Step 3: Write the implementation**

`BuildAppCommand.cs:226` — extend the options line:

```csharp
                              + "novalidation  logging  noseed  nologin");
```

`CustomizationSummary.cs`, beside the other feature entries:

```csharp
        if (!customization.SampleSeedData) features.Add("no sample data");
        if (!customization.Login) features.Add("no login");
```

`CommandDictionary.cs:603-610`:

```csharp
            Command = "build app: <description> [novalidation] [logging] [noseed] [nologin]",
```

and in the description, after the `noseed` clause:

```csharp
                        + "'nologin' omits the login screen and the user accounts "
                        + "(a generated app requires a sign-in by default) ",
```

and the example:

```csharp
            Example = "build app: a shop novalidation logging noseed nologin"
```

`BuildCommandEcho.cs`, beside the `noseed` echo:

```csharp
        if (!customization.Login)
            options += " nologin";
```

`CustomizationWizard.cs` / `CustomizationMenus.cs` / `CustomizationDefaults.cs` — add the question alongside the sample-data one, worded as a positive so the remembered answer reads correctly in `.codegen/app-profile.json`:

> `Require a login? (Y/n)`

Follow whatever shape the neighbouring sample-data question uses in each file; do not invent a new prompt mechanism.

`docs/commands.md:34` — extend the row's command cell to `[nologin]` and add to the description: *"'nologin' omits the login screen, the user accounts and the Users screen; by default a generated app requires a sign-in, seeded as admin / admin on first run."*

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "LoginFlagTests|CommandDictionaryDataTests|CustomizationWizardTests|CustomizationMenuTests|HelpCommandTests|CommandReferenceDocTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add CodeGenerator/ CodeGenerationEngine/Core/CommandDictionary.cs docs/commands.md CodeGenerator.Tests/
git commit -m "docs(build app): nologin appears everywhere the other flags do"
```

---

### Task 8: Emit the auth core

**Files:**
- Create: `AppScaffolding/Auth/AuthCoreFiles.cs`
- Modify: `AppScaffolding/AppScaffolder.cs` (the shared-core file list, ~line 376; the SQLite branch, ~line 421)
- Test: `CodeGenerator.Tests/App/AuthCoreEmissionTests.cs`

**Interfaces:**
- Consumes: Tasks 1–6.
- Produces: `AppScaffolding.Auth.AuthCoreFiles.For(string rootNamespace) -> IReadOnlyList<GeneratedFile>` — the five shared files (`Models/User.cs`, `Auth/AppRole.cs`, `Auth/PasswordHasher.cs`, `Auth/AuthService.cs`, `Auth/UserSeed.cs`). `AuthCoreFiles.EntityName` is the constant `"User"`.

- [ ] **Step 1: Write the failing test**

```csharp
using AppScaffolding;
using AppScaffolding.Auth;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>The auth core is shared, so it is emitted once by the scaffolder rather than three
/// times by the targets — and it must be absent, entirely, under `nologin`.</summary>
[TestClass]
public class AuthCoreEmissionTests
{
    private static readonly AppCustomization NoLogin =
        AppCustomization.Default with { Login = false };

    private static string Scaffold(AppCustomization customization, PersistenceKind persistence)
    {
        var root = Path.Combine(Path.GetTempPath(), "auth-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        var request = ScaffoldRequest.For(
                TestModels.OneEntity(), "Shop", root, new WpfTarget())
            with { Customization = customization, Persistence = persistence };

        return new AppScaffolder().Scaffold(request).ProjectPath;
    }

    private static bool Has(string projectPath, params string[] parts)
        => File.Exists(Path.Combine(new[] { projectPath }.Concat(parts).ToArray()));

    [TestMethod]
    public void A_default_build_emits_the_auth_core()
    {
        var path = Scaffold(AppCustomization.Default, PersistenceKind.Json);

        Assert.IsTrue(Has(path, "Models", "User.cs"));
        Assert.IsTrue(Has(path, "Auth", "AppRole.cs"));
        Assert.IsTrue(Has(path, "Auth", "PasswordHasher.cs"));
        Assert.IsTrue(Has(path, "Auth", "AuthService.cs"));
        Assert.IsTrue(Has(path, "Auth", "UserSeed.cs"));
    }

    [TestMethod]
    public void Nologin_emits_no_auth_folder_at_all()
    {
        var path = Scaffold(NoLogin, PersistenceKind.Json);

        Assert.IsFalse(Directory.Exists(Path.Combine(path, "Auth")));
        Assert.IsFalse(Has(path, "Models", "User.cs"));
    }

    /// <summary>The SQLite backend needs a table. Without the DbSet the first sign-in throws at
    /// the store, and nothing before runtime says so.</summary>
    [TestMethod]
    public void The_sqlite_context_gains_a_user_table()
    {
        var path = Scaffold(AppCustomization.Default, PersistenceKind.Sqlite);

        StringAssert.Contains(
            File.ReadAllText(Path.Combine(path, "Data", "AppDbContext.cs")),
            "DbSet<User>");
    }

    [TestMethod]
    public void A_nologin_sqlite_context_has_no_user_table()
    {
        var path = Scaffold(NoLogin, PersistenceKind.Sqlite);

        Assert.IsFalse(
            File.ReadAllText(Path.Combine(path, "Data", "AppDbContext.cs"))
                .Contains("DbSet<User>", StringComparison.Ordinal));
    }

    /// <summary>User is EMITTED, not DISCOVERED. Counting it would tell the user their model has
    /// a class they never described.</summary>
    [TestMethod]
    public void The_user_record_is_not_counted_as_a_discovered_class()
    {
        var root = Path.Combine(Path.GetTempPath(), "auth-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        var withLogin = new AppScaffolder().Scaffold(
            ScaffoldRequest.For(TestModels.OneEntity(), "Shop", root, new WpfTarget()));

        Assert.AreEqual(1, withLogin.ClassCount);
    }

    /// <summary>No validator, no repository, no seed rows: User is not part of the domain the
    /// user described, and generating those would put it on screens it does not belong on.</summary>
    [TestMethod]
    public void The_user_record_gets_no_validator_no_repository_and_no_seed_rows()
    {
        var path = Scaffold(AppCustomization.Default, PersistenceKind.Json);

        Assert.IsFalse(Has(path, "Validation", "UserValidator.Generated.cs"));
        Assert.IsFalse(Has(path, "Data", "UserRepository.cs"));

        var seed = Path.Combine(path, "Data", "SeedData.cs");
        if (File.Exists(seed))
        {
            Assert.IsFalse(
                File.ReadAllText(seed).Contains("new User", StringComparison.Ordinal),
                "Sample rows are for the discovered model; accounts come from UserSeed.");
        }
    }
}
```

Check `ScaffoldResult` for the real name of the class count before writing `ClassCount`, and use whatever it is.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter AuthCoreEmissionTests`
Expected: FAIL — no `Auth` folder is emitted.

- [ ] **Step 3: Write the implementation**

Create `AppScaffolding/Auth/AuthCoreFiles.cs`:

```csharp
namespace AppScaffolding.Auth;

/// <summary>
/// The one list of shared auth-core files, so the scaffolder and the targets cannot drift.
/// </summary>
/// <remarks>
/// These are target-agnostic: all three UI targets receive exactly these, and only the login VIEW
/// differs. <see cref="SessionTemplate"/> and <see cref="LoginViewModelTemplate"/> are deliberately
/// NOT here — they are desktop-only.
/// </remarks>
public static class AuthCoreFiles
{
    /// <summary>The emitted entity name. Emitted, not discovered: it must not reach the class
    /// count, the navigation list, the seeded rows or ValidatorFactory.</summary>
    public const string EntityName = "User";

    /// <summary>The shared auth files for an app rooted at <paramref name="rootNamespace"/>.</summary>
    public static IReadOnlyList<GeneratedFile> For(string rootNamespace) =>
    [
        new("Models", "User.cs", AuthModelTemplates.User(rootNamespace)),
        new("Auth", "AppRole.cs", AuthModelTemplates.Role(rootNamespace)),
        new("Auth", "PasswordHasher.cs", PasswordHasherTemplate.Source(rootNamespace)),
        new("Auth", "AuthService.cs", AuthServiceTemplate.Source(rootNamespace)),
        new("Auth", "UserSeed.cs", UserSeedTemplate.Source(rootNamespace)),
    ];
}
```

In `AppScaffolder.cs`, after the `SaveResult.cs` line and before the validation block:

```csharp
        // The auth core, when the app has a login. Emitted here rather than by each target because
        // all three UI targets get identical files -- only the login VIEW differs. The screenless
        // targets never ask for it: `customization.Login` is only consulted by targets with a
        // screen, and AuthCoreFiles is skipped for the rest.
        if (customization.Login && target.HasScreens)
        {
            foreach (var file in AuthCoreFiles.For(rootNamespace))
                files.Add((Path.Combine(outputFolder, file.RelativeFolder), file.Name, file.Source));
        }
```

Add `bool HasScreens => false;` as a default interface member on `ITargetScaffolder`, overridden to `true` on `WpfTarget`, `WinFormsTarget` and `BlazorTarget`. This keeps console, Web API, worker and class library untouched, and states the rule in one place.

In the SQLite branch, extend the DbSet list:

```csharp
        if (persistence == PersistenceKind.Sqlite)
        {
            // The user store is an IStore<User> like any other, so SQLite needs its table. The
            // name is appended rather than mixed into entityNames, which drives the class count,
            // the repositories and the validators -- none of which User takes part in.
            var contextEntities = customization.Login && target.HasScreens
                ? entityNames.Append(AuthCoreFiles.EntityName).ToList()
                : entityNames;

            files.Add((dataFolder, "EfStore.cs", EfStoreTemplate.Source(rootNamespace)));
            files.Add((dataFolder, "AppDbContext.cs",
                DbContextTemplate.Source(
                    projectName, contextEntities, EnvVarName(projectName), rootNamespace)));
        }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "AuthCoreEmissionTests|AppScaffolderWriteTests|AppScaffolderPersistenceTests|DbContextTemplateTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/ CodeGenerator.Tests/App/AuthCoreEmissionTests.cs
git commit -m "feat(auth): the scaffolder emits the auth core for apps with screens"
```

---

### Task 9: The WPF login window

**Files:**
- Create: `AppScaffolding/Auth/WpfLoginTemplate.cs`
- Modify: `AppScaffolding/Targets/WpfTarget.cs`, `AppScaffolding/Targets/DesktopViewModelFiles.cs`, `AppScaffolding/WpfProjectFixup.cs`
- Test: `CodeGenerator.Tests/App/WpfLoginTemplateTests.cs`

**Interfaces:**
- Consumes: Tasks 5, 6, 8.
- Produces: `WpfLoginTemplate.Xaml(LayoutContext)`, `WpfLoginTemplate.CodeBehind(string rootNamespace)`, `WpfLoginTemplate.AppCodeBehind(string rootNamespace)` — emitting `Views/LoginWindow.xaml`, `Views/LoginWindow.xaml.cs`, `App.xaml.cs`. `WpfProjectFixup.RemoveStartupUri(string projectFolder)`.

- [ ] **Step 1: Write the failing test**

```csharp
using AppScaffolding;
using AppScaffolding.Auth;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>The login must be the first window, and signing out must return to it without
/// restarting the process.</summary>
[TestClass]
public class WpfLoginTemplateTests
{
    private static IReadOnlyList<GeneratedFile> Files(AppCustomization customization)
        => new WpfTarget().FrontEndFiles("Shop", ["Order"], customization);

    private static string SourceOf(AppCustomization customization, string name)
        => Files(customization).Single(f => f.Name == name).Source;

    [TestMethod]
    public void A_default_wpf_app_emits_a_login_window()
    {
        var names = Files(AppCustomization.Default).Select(f => f.Name).ToList();

        CollectionAssert.Contains(names, "LoginWindow.xaml");
        CollectionAssert.Contains(names, "LoginWindow.xaml.cs");
        CollectionAssert.Contains(names, "App.xaml.cs");
    }

    [TestMethod]
    public void Nologin_emits_none_of_them()
    {
        var names = Files(AppCustomization.Default with { Login = false })
            .Select(f => f.Name).ToList();

        CollectionAssert.DoesNotContain(names, "LoginWindow.xaml");
        CollectionAssert.DoesNotContain(names, "App.xaml.cs");
    }

    /// <summary>The whole point: the shell must not be constructed until a sign-in succeeded.</summary>
    [TestMethod]
    public void The_entry_point_shows_the_login_before_the_shell()
    {
        var app = SourceOf(AppCustomization.Default, "App.xaml.cs");

        var login = app.IndexOf("new LoginWindow()", StringComparison.Ordinal);
        var shell = app.IndexOf("new MainWindow()", StringComparison.Ordinal);

        Assert.IsGreaterThan(-1, login, "the login window is never constructed");
        Assert.IsGreaterThan(-1, shell, "the shell is never constructed");
        Assert.IsLessThan(shell, login, "the shell must not be built before the sign-in");
    }

    /// <summary>Sign-out loops back to the login. Restarting the process instead would lose the
    /// window position and flash the taskbar.</summary>
    [TestMethod]
    public void Signing_out_returns_to_the_login()
        => StringAssert.Contains(
            SourceOf(AppCustomization.Default, "App.xaml.cs"), "Session.SignOutRequested");

    /// <summary>The password must never be a two-way bound string on the view model: WPF's
    /// PasswordBox deliberately does not expose Password as a bindable property, and a TextBox
    /// would show the password on screen.</summary>
    [TestMethod]
    public void The_password_is_entered_through_a_password_box()
    {
        StringAssert.Contains(SourceOf(AppCustomization.Default, "LoginWindow.xaml"), "PasswordBox");
        Assert.IsFalse(
            SourceOf(AppCustomization.Default, "LoginWindow.xaml")
                .Contains("Text=\"{Binding Password", StringComparison.Ordinal));
    }

    /// <summary>Enter is how anyone signs in. A login screen that needs the mouse is wrong.</summary>
    [TestMethod]
    public void Enter_signs_in()
        => StringAssert.Contains(
            SourceOf(AppCustomization.Default, "LoginWindow.xaml"), "IsDefault=\"True\"");

    [TestMethod]
    public void The_brand_panel_shows_the_company_when_there_is_one()
    {
        var branded = AppCustomization.Default with
        {
            Company = "Codis Ltd",
            TitlePattern = AppCustomization.CompanyTitlePattern,
        };

        StringAssert.Contains(SourceOf(branded, "LoginWindow.xaml"), "Codis Ltd");
    }

    /// <summary>An ampersand in a company name is XML-escaped, or the XAML does not parse.</summary>
    [TestMethod]
    public void A_company_with_an_ampersand_is_escaped()
    {
        var branded = AppCustomization.Default with
        {
            Company = "Smith & Sons",
            TitlePattern = AppCustomization.CompanyTitlePattern,
        };

        var xaml = SourceOf(branded, "LoginWindow.xaml");

        StringAssert.Contains(xaml, "&amp;");
        Assert.IsFalse(xaml.Contains("Smith & Sons", StringComparison.Ordinal));
    }

    /// <summary>Emitted WPF compiles only against the WindowsDesktop ref pack.</summary>
    [TestMethod]
    public void The_login_window_compiles_with_its_view_models()
    {
        var files = Files(AppCustomization.Default);

        EmittedDesktopSource.AssertCompilesTogether(
            files.Where(f => f.Name.EndsWith(".cs", StringComparison.Ordinal))
                .Select(f => (f.Name, f.Source))
                .Concat(AuthCoreFiles.For("Shop").Select(f => (f.Name, f.Source)))
                .ToArray());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter WpfLoginTemplateTests`
Expected: FAIL — no `LoginWindow.xaml` is emitted.

- [ ] **Step 3: Write the implementation**

Create `AppScaffolding/Auth/WpfLoginTemplate.cs`. The XAML is the split brand panel: a two-column grid, the left column filled with the accent (or a default blue when the app is unthemed) carrying the company and app name, the right column holding the form.

```csharp
using AppScaffolding.Layouts;

namespace AppScaffolding.Auth;

/// <summary>Emits the WPF login window and the entry point that shows it.</summary>
/// <remarks>
/// <c>App.xaml.cs</c> is emitted rather than left as the template's, because the sign-in has to
/// happen before any window opens and <c>StartupUri</c> cannot express that. The matching
/// <c>StartupUri</c> removal is <see cref="WpfProjectFixup.RemoveStartupUri"/>.
/// </remarks>
public static class WpfLoginTemplate
{
    /// <summary>The brand panel's fill when the app was given no accent.</summary>
    private const string DefaultBrandFill = "#1D4ED8";

    /// <summary>Source of <c>Views/LoginWindow.xaml</c>.</summary>
    public static string Xaml(LayoutContext context)
    {
        var brandFill = context.Customization.Accent ?? DefaultBrandFill;

        // The company when there is one, else the app name: the panel must never be blank, and an
        // unbranded app still has a name worth showing.
        var brandLine = context.Customization.HasBranding
            ? Escape(context.Customization.Company)
            : context.TitleAttribute;

        var subLine = context.Customization.HasBranding ? context.TitleAttribute : string.Empty;

        var windowIcon = context.IconFileName is null
            ? string.Empty
            : $"\n        Icon=\"{AppCustomization.AssetsFolder}/{context.IconFileName}\"";

        return $$"""
<Window x:Class="{{context.RootNamespace}}.Views.LoginWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="{{context.TitleAttribute}}"
        Height="420" Width="720"
        MinHeight="360" MinWidth="620"
        WindowStartupLocation="CenterScreen"{{windowIcon}}>
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="44*" MinWidth="220" />
            <ColumnDefinition Width="56*" />
        </Grid.ColumnDefinitions>

        <Border Grid.Column="0" Background="{{brandFill}}">
            <DockPanel Margin="28,26">
                <TextBlock DockPanel.Dock="Bottom"
                           Text="{{subLine}}"
                           Foreground="#CCFFFFFF"
                           FontSize="12"
                           TextWrapping="Wrap" />
                <StackPanel VerticalAlignment="Top">
                    <Border Width="40" Height="40" CornerRadius="8"
                            Background="#38FFFFFF" HorizontalAlignment="Left" Margin="0,0,0,16" />
                    <TextBlock Text="{{brandLine}}"
                               Foreground="White"
                               FontSize="22"
                               FontWeight="Bold"
                               TextWrapping="Wrap" />
                </StackPanel>
            </DockPanel>
        </Border>

        <StackPanel Grid.Column="1"
                    VerticalAlignment="Center"
                    Margin="40,0"
                    MaxWidth="280">
            <TextBlock Text="Sign in" FontSize="20" FontWeight="SemiBold" />
            <TextBlock Text="Enter your credentials" Foreground="#6B7280" Margin="0,2,0,18" />

            <!-- Collapsed until a sign-in fails, so the form does not open with a red strip. -->
            <Border Background="#FEF2F2" BorderBrush="#FECACA" BorderThickness="1"
                    CornerRadius="3" Padding="8,6" Margin="0,0,0,12"
                    Visibility="{Binding HasError, Converter={StaticResource BoolToVisibility}}">
                <TextBlock Text="{Binding Error}" Foreground="#B91C1C" TextWrapping="Wrap" />
            </Border>

            <TextBlock Text="User name" Foreground="#6B7280" FontSize="11" />
            <TextBox x:Name="UserNameBox"
                     Text="{Binding UserName, UpdateSourceTrigger=PropertyChanged}"
                     Padding="6,4" Margin="0,2,0,12" />

            <TextBlock Text="Password" Foreground="#6B7280" FontSize="11" />
            <!-- A PasswordBox, not a TextBox: PasswordBox.Password is deliberately not a
                 DependencyProperty, so the code-behind hands it over on click. Binding it would
                 mean holding the password in a bindable string. -->
            <PasswordBox x:Name="PasswordBox" Padding="6,4" Margin="0,2,0,18" />

            <Button Content="Sign in"
                    IsDefault="True"
                    Click="OnSignIn"
                    Padding="0,7"
                    Background="{{brandFill}}"
                    Foreground="White"
                    BorderThickness="0" />

            <TextBlock Text="{Binding Hint}"
                       Foreground="#9CA3AF"
                       FontSize="11"
                       TextWrapping="Wrap"
                       Margin="0,14,0,0" />
        </StackPanel>
    </Grid>
</Window>
""";
    }

    /// <summary>Source of <c>Views/LoginWindow.xaml.cs</c>.</summary>
    public static string CodeBehind(string rootNamespace) => $$"""
using System.Windows;
using {{rootNamespace}}.ViewModels;

namespace {{rootNamespace}}.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _model = new();

    public LoginWindow()
    {
        InitializeComponent();

        DataContext = _model;
        _model.Succeeded += (_, _) => { DialogResult = true; };

        Loaded += (_, _) => UserNameBox.Focus();
    }

    // The password travels from the PasswordBox to the view model here, at the moment it is used,
    // rather than living on the view model as a bindable string.
    private void OnSignIn(object sender, RoutedEventArgs e)
    {
        _model.Password = PasswordBox.Password;
        _model.SignInCommand.Execute(null);

        // Cleared whether or not it worked: a failed attempt should not leave the password on
        // screen for the next one.
        PasswordBox.Clear();
    }
}
""";

    /// <summary>Source of <c>App.xaml.cs</c> — the sign-in loop.</summary>
    public static string AppCodeBehind(string rootNamespace) => $$"""
using System.Windows;
using {{rootNamespace}}.Auth;
using {{rootNamespace}}.Views;

namespace {{rootNamespace}};

public partial class App : Application
{
    /// <summary>
    /// Signs in, runs the shell, and comes back here when it closes.
    /// </summary>
    /// <remarks>
    /// ShutdownMode is explicit because there is no StartupUri and no MainWindow until the sign-in
    /// succeeds: on the default OnLastWindowClose the application would exit the moment the login
    /// window closed, before the shell opened.
    /// </remarks>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        UserSeed.EnsureAdminSeeded();

        while (true)
        {
            if (new LoginWindow().ShowDialog() != true)
                break;

            var shell = new MainWindow();
            MainWindow = shell;
            shell.ShowDialog();

            // Closing the shell any other way means quit. Only an explicit sign-out loops.
            if (!Session.SignOutRequested)
                break;

            Session.Clear();
        }

        Shutdown();
    }
}
""";

    private static string Escape(string value)
        => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                .Replace("\"", "&quot;");
}
```

The XAML references a `BoolToVisibility` converter. Rather than emit a converter class, declare it in `Window.Resources` using the framework's built-in — add immediately after the `<Window ...>` open tag:

```xml
    <Window.Resources>
        <BooleanToVisibilityConverter x:Key="BoolToVisibility" />
    </Window.Resources>
```

In `DesktopViewModelFiles.Build`, add a `bool login` parameter (defaulting `true`) and append when set:

```csharp
        if (login)
        {
            files.Add(new("Auth", "Session.cs", SessionTemplate.Source(rootNamespace)));
            files.Add(new("ViewModels", "LoginViewModel.cs",
                LoginViewModelTemplate.Source(rootNamespace)));
        }
```

In `WpfTarget.FrontEndFiles`, pass `customization.Login` to `DesktopViewModelFiles.Build` and add:

```csharp
        if (customization.Login)
        {
            var loginContext = new LayoutContext(ns, projectName, customization, palette);

            files.Add(new("Views", "LoginWindow.xaml", WpfLoginTemplate.Xaml(loginContext)));
            files.Add(new("Views", "LoginWindow.xaml.cs", WpfLoginTemplate.CodeBehind(ns)));
            files.Add(new("", "App.xaml.cs", WpfLoginTemplate.AppCodeBehind(ns)));
        }
```

In `WpfProjectFixup`, add and call from `Apply` only when login is on — thread a `bool login` through `ITargetScaffolder.FixupProject` is too wide a change, so instead call it from `WpfTarget.FixupProject` via a new overload that the target already knows the customization for. Simplest correct shape: give `WpfProjectFixup` the method and have `AppScaffolder` call it after `FixupProject` when `customization.Login`:

```csharp
    private const string StartupUriAttribute = "StartupUri=\"Views/MainWindow.xaml\"";

    /// <summary>
    /// Removes <c>StartupUri</c> from <c>App.xaml</c> for an app with a login.
    /// </summary>
    /// <remarks>
    /// StartupUri opens a window before <c>OnStartup</c> finishes, so leaving it in place would
    /// show the shell behind the login. Best-effort and idempotent, like the repoint it undoes.
    /// </remarks>
    public static void RemoveStartupUri(string projectFolder)
    {
        var path = Path.Combine(projectFolder, "App.xaml");

        try
        {
            if (!File.Exists(path))
                return;

            var xaml = File.ReadAllText(path);

            if (!xaml.Contains(StartupUriAttribute, StringComparison.Ordinal))
                return;

            File.WriteAllText(path, xaml.Replace(StartupUriAttribute, string.Empty));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "WpfLoginTemplateTests|MainWindowTemplateTests|DesktopViewModelFilesTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/ CodeGenerator.Tests/App/WpfLoginTemplateTests.cs
git commit -m "feat(wpf): a generated app signs in before it opens"
```

---

### Task 10: The Windows Forms login form

**Files:**
- Create: `AppScaffolding/Auth/WinFormsLoginTemplate.cs`
- Modify: `AppScaffolding/WinFormsProgramTemplate.cs`, `AppScaffolding/Targets/WinFormsTarget.cs`
- Test: `CodeGenerator.Tests/App/WinFormsLoginTemplateTests.cs`

**Interfaces:**
- Consumes: Tasks 5, 6, 8.
- Produces: `WinFormsLoginTemplate.Source(LayoutContext) -> string` emitting `Views/LoginForm.cs`. `WinFormsProgramTemplate.Source(string appName, bool login)` — the existing single-argument overload stays and delegates with `login: true`.

- [ ] **Step 1: Write the failing test**

```csharp
using AppScaffolding;
using AppScaffolding.Auth;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>Windows Forms shares every view model with WPF; only the view and the entry point
/// differ, so those are what these tests pin.</summary>
[TestClass]
public class WinFormsLoginTemplateTests
{
    private static IReadOnlyList<GeneratedFile> Files(AppCustomization customization)
        => new WinFormsTarget().FrontEndFiles("Shop", ["Order"], customization);

    private static string SourceOf(AppCustomization customization, string name)
        => Files(customization).Single(f => f.Name == name).Source;

    [TestMethod]
    public void A_default_winforms_app_emits_a_login_form()
        => CollectionAssert.Contains(
            Files(AppCustomization.Default).Select(f => f.Name).ToList(), "LoginForm.cs");

    [TestMethod]
    public void Nologin_emits_no_login_form()
        => CollectionAssert.DoesNotContain(
            Files(AppCustomization.Default with { Login = false })
                .Select(f => f.Name).ToList(),
            "LoginForm.cs");

    [TestMethod]
    public void The_entry_point_runs_the_login_before_the_shell()
    {
        var program = SourceOf(AppCustomization.Default, "Program.cs");

        var login = program.IndexOf("new LoginForm()", StringComparison.Ordinal);
        var shell = program.IndexOf("new MainForm()", StringComparison.Ordinal);

        Assert.IsGreaterThan(-1, login);
        Assert.IsLessThan(shell, login);
    }

    /// <summary>Application.Run must not be given the login form: running it as the message loop
    /// would end the application when it closed.</summary>
    [TestMethod]
    public void The_message_loop_runs_the_shell_not_the_login()
    {
        var program = SourceOf(AppCustomization.Default, "Program.cs");

        StringAssert.Contains(program, "ShowDialog()");
        Assert.IsFalse(program.Contains("Application.Run(new LoginForm", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Nologin_leaves_the_entry_point_as_it_was()
    {
        var program = SourceOf(AppCustomization.Default with { Login = false }, "Program.cs");

        StringAssert.Contains(program, "Application.Run(new MainForm());");
        Assert.IsFalse(program.Contains("LoginForm", StringComparison.Ordinal));
    }

    /// <summary>The password character must be set, or the password is typed in clear.</summary>
    [TestMethod]
    public void The_password_box_masks_what_is_typed()
        => StringAssert.Contains(
            SourceOf(AppCustomization.Default, "LoginForm.cs"), "UseSystemPasswordChar = true");

    /// <summary>AcceptButton is how Enter submits a WinForms dialog.</summary>
    [TestMethod]
    public void Enter_signs_in()
        => StringAssert.Contains(
            SourceOf(AppCustomization.Default, "LoginForm.cs"), "AcceptButton");

    [TestMethod]
    public void The_login_form_compiles_with_its_view_models()
    {
        var files = Files(AppCustomization.Default);

        EmittedDesktopSource.AssertCompilesTogether(
            files.Where(f => f.Name.EndsWith(".cs", StringComparison.Ordinal))
                .Select(f => (f.Name, f.Source))
                .Concat(AuthCoreFiles.For("Shop").Select(f => (f.Name, f.Source)))
                .ToArray());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter WinFormsLoginTemplateTests`
Expected: FAIL — no `LoginForm.cs`.

- [ ] **Step 3: Write the implementation**

Create `AppScaffolding/Auth/WinFormsLoginTemplate.cs`:

```csharp
using AppScaffolding.Layouts;

namespace AppScaffolding.Auth;

/// <summary>Emits <c>Views/LoginForm.cs</c>: the Windows Forms half of the split-panel login.</summary>
/// <remarks>
/// Hand-built in code rather than through a designer file, like every other emitted form here —
/// a .Designer.cs would have to be kept in step with a .resx this generator does not write.
/// </remarks>
public static class WinFormsLoginTemplate
{
    private const string DefaultBrandFill = "29, 78, 216";

    /// <summary>Source of <c>Views/LoginForm.cs</c>.</summary>
    public static string Source(LayoutContext context)
    {
        var brandFill = HexToArgb(context.Customization.Accent) ?? DefaultBrandFill;

        var brandLine = context.Customization.HasBranding
            ? Literal(context.Customization.Company)
            : context.TitleLiteral;

        var subLine = context.Customization.HasBranding ? context.TitleLiteral : string.Empty;

        return $$"""
using System;
using System.Drawing;
using System.Windows.Forms;
using {{context.RootNamespace}}.ViewModels;

namespace {{context.RootNamespace}}.Views;

/// <summary>The sign-in dialog. Returns DialogResult.OK once the credentials were accepted.</summary>
public sealed class LoginForm : Form
{
    private readonly LoginViewModel _model = new();
    private readonly TextBox _userName = new();
    private readonly TextBox _password = new();
    private readonly Label _error = new();

    public LoginForm()
    {
        Text = "{{context.TitleLiteral}}";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(720, 420);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56F));

        layout.Controls.Add(BrandPanel(), 0, 0);
        layout.Controls.Add(FormPanel(), 1, 0);

        Controls.Add(layout);
    }

    private static Panel BrandPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb({{brandFill}}),
        };

        panel.Controls.Add(new Label
        {
            Text = "{{subLine}}",
            ForeColor = Color.FromArgb(210, 226, 255),
            AutoSize = false,
            Dock = DockStyle.Bottom,
            Height = 40,
            Padding = new Padding(28, 0, 28, 22),
        });

        panel.Controls.Add(new Label
        {
            Text = "{{brandLine}}",
            ForeColor = Color.White,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 18F, FontStyle.Bold),
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 120,
            Padding = new Padding(28, 40, 28, 0),
        });

        return panel;
    }

    private Panel FormPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(40, 60, 40, 40) };

        var heading = new Label
        {
            Text = "Sign in",
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 15F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(40, 60),
        };

        _error.ForeColor = Color.FromArgb(185, 28, 28);
        _error.AutoSize = false;
        _error.Size = new Size(280, 34);
        _error.Location = new Point(40, 96);
        _error.Visible = false;

        var userLabel = new Label { Text = "User name", AutoSize = true, Location = new Point(40, 138) };
        _userName.Location = new Point(40, 156);
        _userName.Width = 280;

        var passwordLabel = new Label { Text = "Password", AutoSize = true, Location = new Point(40, 190) };
        _password.Location = new Point(40, 208);
        _password.Width = 280;

        // Not PasswordChar: UseSystemPasswordChar takes the character the OS uses, so the dialog
        // matches every other password box on the machine.
        _password.UseSystemPasswordChar = true;

        var signIn = new Button
        {
            Text = "Sign in",
            Location = new Point(40, 250),
            Size = new Size(280, 32),
        };
        signIn.Click += OnSignIn;

        // AcceptButton is what makes Enter submit; without it the dialog needs the mouse.
        AcceptButton = signIn;

        var hint = new Label
        {
            Text = _model.Hint,
            ForeColor = Color.Gray,
            AutoSize = false,
            Size = new Size(280, 34),
            Location = new Point(40, 292),
        };

        panel.Controls.AddRange([heading, _error, userLabel, _userName, passwordLabel, _password, signIn, hint]);

        return panel;
    }

    private void OnSignIn(object? sender, EventArgs e)
    {
        _model.UserName = _userName.Text;
        _model.Password = _password.Text;
        _model.SignInCommand.Execute(null);

        _password.Clear();

        if (_model.Error is null)
        {
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        _error.Text = _model.Error;
        _error.Visible = true;
    }
}
""";
    }

    /// <summary>A "#RRGGBB" accent as the three arguments Color.FromArgb takes, or null.</summary>
    private static string? HexToArgb(string? hex)
    {
        if (hex is null || hex.Length != 7 || hex[0] != '#')
            return null;

        try
        {
            return $"{Convert.ToInt32(hex[1..3], 16)}, {Convert.ToInt32(hex[3..5], 16)}, "
                 + $"{Convert.ToInt32(hex[5..7], 16)}";
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string Literal(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\r", "\\r").Replace("\n", "\\n");
}
```

In `WinFormsProgramTemplate`, add the login overload:

```csharp
    /// <summary>Source of <c>Program.cs</c>.</summary>
    public static string Source(string appName) => Source(appName, login: true);

    /// <summary>
    /// Source of <c>Program.cs</c>. With <paramref name="login"/>, Main signs in first and returns
    /// to the login when the shell closes on a sign-out.
    /// </summary>
    public static string Source(string appName, bool login)
    {
        if (!login)
            return LegacyMain(appName);

        return $$"""
using System;
using System.Windows.Forms;
using {{appName}}.Auth;
using {{appName}}.Views;

namespace {{appName}};

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        UserSeed.EnsureAdminSeeded();

        while (true)
        {
            // ShowDialog, not Application.Run: running the login as the message loop would end the
            // application the moment it closed, before the shell could open.
            using (var login = new LoginForm())
            {
                if (login.ShowDialog() != DialogResult.OK)
                    return;
            }

            using (var shell = new MainForm())
            {
                shell.ShowDialog();
            }

            if (!Session.SignOutRequested)
                return;

            Session.Clear();
        }
    }
}
""";
    }
```

with `LegacyMain(appName)` holding the current body verbatim, so `nologin` output is byte-identical.

In `WinFormsTarget.FrontEndFiles`, pass `customization.Login` to `DesktopViewModelFiles.Build`, emit the login form when set, and call the new `Source` overload:

```csharp
        if (customization.Login)
        {
            files.Add(new("Views", "LoginForm.cs",
                WinFormsLoginTemplate.Source(new LayoutContext(ns, projectName, customization, palette))));
        }

        files.Add(new("", "Program.cs", WinFormsProgramTemplate.Source(ns, customization.Login)));
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "WinFormsLoginTemplateTests|WinFormsMainFormTemplateTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/ CodeGenerator.Tests/App/WinFormsLoginTemplateTests.cs
git commit -m "feat(winforms): a generated app signs in before it opens"
```

---

### Task 11: The Blazor login, endpoints and `Program.cs` surgery

**Files:**
- Create: `AppScaffolding/Auth/BlazorLoginTemplate.cs`
- Modify: `AppScaffolding/BlazorProjectFixup.cs`, `AppScaffolding/Targets/BlazorTarget.cs`, `AppScaffolding/BlazorShellTemplate.cs`
- Test: `CodeGenerator.Tests/App/BlazorLoginTemplateTests.cs`, `CodeGenerator.Tests/App/BlazorProjectFixupTests.cs` (extend)

**Interfaces:**
- Consumes: Tasks 6, 8.
- Produces: `BlazorLoginTemplate.LoginPage(LayoutContext)`, `BlazorLoginTemplate.Endpoints(string rootNamespace)`, and `BlazorProjectFixup.EnableAuthentication(string projectFolder)`.

**This is the highest-risk task in the branch.** It is the only one that rewrites a file `dotnet new` produced, and the failure modes — antiforgery ordering, the static web asset manifest — do not show up in any assertion over emitted strings. Task 13 is what actually proves it.

- [ ] **Step 1: Write the failing test**

```csharp
using AppScaffolding;
using AppScaffolding.Auth;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>
/// Cookie authentication is the one place this target has to touch Program.cs, and the one place
/// where "it compiles" is not evidence. These tests pin the shape; the end-to-end task proves it.
/// </summary>
[TestClass]
public class BlazorLoginTemplateTests
{
    private static IReadOnlyList<GeneratedFile> Files(AppCustomization customization)
        => new BlazorTarget().FrontEndFiles("Shop", ["Order"], customization);

    private static string SourceOf(AppCustomization customization, string name)
        => Files(customization).Single(f => f.Name == name).Source;

    [TestMethod]
    public void A_default_web_app_emits_a_login_page_and_its_endpoints()
    {
        var names = Files(AppCustomization.Default).Select(f => f.Name).ToList();

        CollectionAssert.Contains(names, "Login.razor");
        CollectionAssert.Contains(names, "AuthEndpoints.cs");
    }

    [TestMethod]
    public void Nologin_emits_neither()
    {
        var names = Files(AppCustomization.Default with { Login = false })
            .Select(f => f.Name).ToList();

        CollectionAssert.DoesNotContain(names, "Login.razor");
        CollectionAssert.DoesNotContain(names, "AuthEndpoints.cs");
    }

    /// <summary>
    /// The login page must NOT be interactive. An interactive circuit has already sent its
    /// response headers, so it cannot set a cookie — the sign-in would appear to work and the user
    /// would stay signed out.
    /// </summary>
    [TestMethod]
    public void The_login_page_is_not_interactive()
    {
        var page = SourceOf(AppCustomization.Default, "Login.razor");

        Assert.IsFalse(
            page.Contains("@rendermode", StringComparison.Ordinal),
            "An interactive circuit cannot set an authentication cookie.");
        StringAssert.Contains(page, "method=\"post\"");
        StringAssert.Contains(page, "action=\"/auth/login\"");
    }

    /// <summary>UseAntiforgery validates the token; the form must carry one or every post is
    /// rejected with a 400 that reads as a broken login.</summary>
    [TestMethod]
    public void The_login_form_carries_an_antiforgery_token()
        => StringAssert.Contains(
            SourceOf(AppCustomization.Default, "Login.razor"), "<AntiforgeryToken />");

    /// <summary>A typed URL must be refused, not merely hidden from the nav.</summary>
    [TestMethod]
    public void The_users_page_is_restricted_to_administrators()
        => StringAssert.Contains(
            SourceOf(AppCustomization.Default, "UsersPage.razor"),
            "[Authorize(Roles = \"Admin\")]");

    [TestMethod]
    public void The_endpoints_sign_in_and_out()
    {
        var endpoints = SourceOf(AppCustomization.Default, "AuthEndpoints.cs");

        StringAssert.Contains(endpoints, "/auth/login");
        StringAssert.Contains(endpoints, "/auth/logout");
        StringAssert.Contains(endpoints, "SignInAsync");
        StringAssert.Contains(endpoints, "SignOutAsync");
    }

    /// <summary>The role must be a claim, or [Authorize(Roles=...)] never matches.</summary>
    [TestMethod]
    public void The_signed_in_cookie_carries_the_role_as_a_claim()
        => StringAssert.Contains(
            SourceOf(AppCustomization.Default, "AuthEndpoints.cs"), "ClaimTypes.Role");
}
```

Extend `BlazorProjectFixupTests` with the surgery cases, using the real template `Program.cs` shape:

```csharp
    private const string TemplateProgram = """
using Shop.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
""";

    private static string Fix(string program)
    {
        var folder = Path.Combine(Path.GetTempPath(), "blz-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Program.cs"), program);

        BlazorProjectFixup.EnableAuthentication(folder);

        return File.ReadAllText(Path.Combine(folder, "Program.cs"));
    }

    [TestMethod]
    public void Authentication_services_are_registered_before_the_app_is_built()
    {
        var fixt = Fix(TemplateProgram);

        Assert.IsLessThan(
            fixt.IndexOf("var app = builder.Build();", StringComparison.Ordinal),
            fixt.IndexOf("AddAuthentication", StringComparison.Ordinal));
    }

    /// <summary>The order ASP.NET Core requires: authentication and authorization run BEFORE
    /// antiforgery. Wrong order is a runtime failure with a misleading message.</summary>
    [TestMethod]
    public void Authentication_middleware_runs_before_antiforgery()
    {
        var fixt = Fix(TemplateProgram);

        var authentication = fixt.IndexOf("app.UseAuthentication();", StringComparison.Ordinal);
        var authorization = fixt.IndexOf("app.UseAuthorization();", StringComparison.Ordinal);
        var antiforgery = fixt.IndexOf("app.UseAntiforgery();", StringComparison.Ordinal);

        Assert.IsGreaterThan(-1, authentication);
        Assert.IsLessThan(authorization, authentication);
        Assert.IsLessThan(antiforgery, authorization);
    }

    /// <summary>MapStaticAssets must survive: removing it breaks the static web asset manifest
    /// and the app serves no stylesheets.</summary>
    [TestMethod]
    public void The_static_asset_mapping_survives()
        => StringAssert.Contains(Fix(TemplateProgram), "app.MapStaticAssets();");

    [TestMethod]
    public void The_surgery_is_idempotent()
    {
        var once = Fix(TemplateProgram);
        var twice = Fix(once);

        Assert.AreEqual(once, twice);
    }

    /// <summary>A Program.cs this rewrite does not recognise is left alone, like every other
    /// fixup here.</summary>
    [TestMethod]
    public void An_unrecognised_program_is_left_alone()
    {
        const string other = "Console.WriteLine(\"not a blazor host\");";

        Assert.AreEqual(other, Fix(other));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "BlazorLoginTemplateTests|BlazorProjectFixupTests"`
Expected: FAIL — `BlazorLoginTemplate` and `EnableAuthentication` do not exist.

- [ ] **Step 3: Write the implementation**

Create `AppScaffolding/Auth/BlazorLoginTemplate.cs` emitting three files.

`Components/Pages/Login.razor` — static-rendered, posting to the endpoint:

```razor
@page "/login"
@layout EmptyLayout

<PageTitle>Sign in</PageTitle>

<div class="login-split">
    <div class="login-brand">
        <div class="login-mark"></div>
        <h1>BRAND</h1>
        <p>SUBLINE</p>
    </div>
    <div class="login-form">
        <h2>Sign in</h2>
        <p class="text-muted">Enter your credentials</p>

        @if (!string.IsNullOrEmpty(Error))
        {
            <div class="alert alert-danger py-2">@Error</div>
        }

        <form method="post" action="/auth/login">
            <AntiforgeryToken />
            <label class="form-label" for="userName">User name</label>
            <input class="form-control mb-3" id="userName" name="userName" autofocus />

            <label class="form-label" for="password">Password</label>
            <input class="form-control mb-3" id="password" name="password" type="password" />

            <button class="btn btn-primary w-100" type="submit">Sign in</button>
        </form>

        <p class="text-muted small mt-3">@Hint</p>
    </div>
</div>

@code {
    [SupplyParameterFromQuery(Name = "error")]
    private string? ErrorFlag { get; set; }

    private string? Error => ErrorFlag is null ? null : SignInResult.FailureMessage;

    private string Hint => UserSeed.IsFirstRun() ? UserSeed.FirstRunHint : string.Empty;

    protected override void OnInitialized() => UserSeed.EnsureAdminSeeded();
}
```

Substitute `BRAND` and `SUBLINE` from the `LayoutContext` exactly as the WPF template does, HTML-escaping rather than XML-escaping. Emit the `login-split` / `login-brand` / `login-form` rules into `wwwroot/app.css` by appending, or inline them in a `<style>` block at the top of the page — inline is simpler and cannot be lost by the template's own CSS bundling; prefer it.

`Auth/AuthEndpoints.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using {{rootNamespace}}.Auth;

namespace {{rootNamespace}}.Auth;

/// <summary>The two endpoints the login form and the sign-out button post to.</summary>
/// <remarks>
/// Endpoints rather than component code: an interactive circuit has already flushed its response,
/// so it cannot write an authentication cookie. This is the only place the cookie is set.
/// </remarks>
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/auth/login", async (HttpContext context, string userName, string password) =>
        {
            UserSeed.EnsureAdminSeeded();

            var result = AuthService.SignIn(userName, password);

            if (!result.Succeeded)
                return Results.Redirect("/login?error=1");

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, result.User!.UserName),
                new(ClaimTypes.Role, result.User.Role.ToString()),
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

            await context.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

            return Results.Redirect("/");
        }).DisableAntiforgery();

        app.MapPost("/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        });
    }
}
```

Note on `DisableAntiforgery()`: the form carries `<AntiforgeryToken />` so the token is present, but a minimal-API form-bound endpoint validates antiforgery through a different path than a Blazor `EditForm` and rejects the post with a bare 400. Disabling it on the endpoint and keeping the token in the form is the shape that works; **Task 13 must confirm it, and if the post succeeds with the token validated, remove the call.**

`BlazorProjectFixup.EnableAuthentication`:

```csharp
    private const string BuildMarker = "var app = builder.Build();";
    private const string AntiforgeryMarker = "app.UseAntiforgery();";
    private const string AuthenticationMarker = "app.UseAuthentication();";

    private const string Services = """
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

""";

    /// <summary>
    /// Adds cookie authentication to the template's <c>Program.cs</c>. Best-effort and idempotent.
    /// </summary>
    /// <remarks>
    /// Three insertions rather than a replacement, for the reason
    /// <see cref="ReconcileNamespace"/> gives: the template's host is correct and re-implementing
    /// it would mean drifting from it on every SDK update.
    /// <para>
    /// Middleware order is not a preference. UseAuthentication and UseAuthorization must run
    /// BEFORE UseAntiforgery, and MapStaticAssets must be left where it is — moving it breaks the
    /// static web asset manifest and the app serves no stylesheets.
    /// </para>
    /// </remarks>
    public static void EnableAuthentication(string projectFolder)
    {
        var path = Path.Combine(projectFolder, "Program.cs");

        try
        {
            if (!File.Exists(path))
                return;

            var text = File.ReadAllText(path);

            if (!text.Contains(TemplateMarker, StringComparison.Ordinal)
                || text.Contains(AuthenticationMarker, StringComparison.Ordinal))
                return;

            var buildIndex = text.IndexOf(BuildMarker, StringComparison.Ordinal);
            var antiforgeryIndex = text.IndexOf(AntiforgeryMarker, StringComparison.Ordinal);

            if (buildIndex < 0 || antiforgeryIndex < 0)
                return;

            text = text.Insert(antiforgeryIndex, AuthenticationMarker + "\napp.UseAuthorization();\n");
            text = text.Insert(buildIndex, Services);
            text = "using Microsoft.AspNetCore.Authentication.Cookies;\n" + text;

            // Mapped after the components, so a request for /auth/login is matched by the endpoint
            // rather than by the router's catch-all.
            text = text.Replace("app.Run();", "app.MapAuthEndpoints();\n\napp.Run();");

            File.WriteAllText(path, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
```

Insert at the later index first (as above), or the earlier insertion shifts the later offset.

In `BlazorTarget`, emit `Login.razor`, `UsersPage.razor` and `Auth/AuthEndpoints.cs` when `customization.Login`, add `HasScreens => true`, and call `BlazorProjectFixup.EnableAuthentication(outputFolder)` from `FixupProject`. Add `@attribute [Authorize]` to each generated entity page, and wrap the Users nav item in `BlazorShellTemplate.NavMenu` in `<AuthorizeView Roles="Admin">`. Add a sign-out form to the top row:

```razor
<form method="post" action="/auth/logout" class="ms-auto">
    <AntiforgeryToken />
    <button type="submit" class="btn btn-link nav-link">Sign out</button>
</form>
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "BlazorLoginTemplateTests|BlazorProjectFixupTests|BlazorTargetTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/ CodeGenerator.Tests/App/
git commit -m "feat(web): a generated web app signs in with a cookie"
```

---

### Task 12: The Users screen

**Files:**
- Create: `AppScaffolding/Auth/UsersViewModelTemplate.cs`
- Modify: `AppScaffolding/MainViewModelTemplate.cs`, `AppScaffolding/Targets/DesktopViewModelFiles.cs`
- Test: `CodeGenerator.Tests/App/UsersPaneTests.cs`

**Interfaces:**
- Consumes: Tasks 5, 8, 9, 10, 11.
- Produces: `UsersViewModelTemplate.Source(string rootNamespace)` emitting `ViewModels/UsersViewModel.cs` — `sealed class UsersViewModel : ObservableObject, IEntityViewModel` with the interface's members plus `string NewPassword` and `ICommand SetPasswordCommand`. `MainViewModelTemplate.Build(rootNamespace, entityNames, seed, login)`.

- [ ] **Step 1: Write the failing test**

```csharp
using AppScaffolding;
using AppScaffolding.Auth;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>
/// The Admin group belongs to the shell, which is a later branch. Until then — and permanently,
/// for anyone who names one of the four older layouts — the Users screen is an ordinary pane. An
/// app whose only account is the seeded admin otherwise has no way to add a second.
/// </summary>
[TestClass]
public class UsersPaneTests
{
    private static string MainViewModelFor(AppCustomization customization)
        => new WpfTarget().FrontEndFiles("Shop", ["Order"], customization)
            .Single(f => f.Name == "MainViewModel.cs").Source;

    [TestMethod]
    public void A_default_desktop_app_has_a_users_pane()
        => StringAssert.Contains(MainViewModelFor(AppCustomization.Default), "new UsersViewModel()");

    [TestMethod]
    public void Nologin_has_no_users_pane()
    {
        var source = MainViewModelFor(AppCustomization.Default with { Login = false });

        Assert.IsFalse(source.Contains("UsersViewModel", StringComparison.Ordinal));
    }

    /// <summary>The regression proof for the older layouts: with `nologin` the main view model is
    /// exactly what it was before this branch.</summary>
    [TestMethod]
    public void Nologin_leaves_the_main_view_model_byte_identical()
        => Assert.AreEqual(
            MainViewModelTemplate.Build("Shop", ["Order"], seed: true),
            MainViewModelFor(AppCustomization.Default with { Login = false }));

    /// <summary>The pane is added only for an administrator. A plain User seeing the account list
    /// is the role doing nothing.</summary>
    [TestMethod]
    public void The_users_pane_is_added_only_for_an_administrator()
        => StringAssert.Contains(MainViewModelFor(AppCustomization.Default), "Session.IsAdmin");

    [TestMethod]
    public void The_users_view_model_satisfies_the_pane_interface()
    {
        var source = UsersViewModelTemplate.Source("Acme");

        foreach (var member in new[]
                 { "Title", "Items", "Selected", "AddCommand", "DeleteCommand", "SaveCommand", "Status" })
        {
            StringAssert.Contains(source, member);
        }
    }

    /// <summary>A new account must not be created without a password, and the password must never
    /// be stored in clear.</summary>
    [TestMethod]
    public void Setting_a_password_goes_through_the_hasher()
    {
        var source = UsersViewModelTemplate.Source("Acme");

        StringAssert.Contains(source, "AuthService.ChangePassword");
        Assert.IsFalse(source.Contains("PasswordHash =", StringComparison.Ordinal));
    }

    [TestMethod]
    public void The_users_view_model_compiles_with_the_rest_of_the_desktop_code()
    {
        var files = new WpfTarget().FrontEndFiles("Shop", ["Order"], AppCustomization.Default);

        EmittedDesktopSource.AssertCompilesTogether(
            files.Where(f => f.Name.EndsWith(".cs", StringComparison.Ordinal))
                .Select(f => (f.Name, f.Source))
                .Concat(AuthCoreFiles.For("Shop").Select(f => (f.Name, f.Source)))
                .ToArray());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter UsersPaneTests`
Expected: FAIL — `UsersViewModelTemplate` does not exist.

- [ ] **Step 3: Write the implementation**

Create `AppScaffolding/Auth/UsersViewModelTemplate.cs` emitting a `UsersViewModel` that implements `IEntityViewModel` over `IStore<User>`: `Title => "Users"`, `Items` an `ObservableCollection<User>` loaded in the constructor, `AddCommand` appending a new active `User` with the plain role and no usable password, `DeleteCommand` removing the selected account *unless it is the signed-in one*, `SaveCommand` writing the list back, and `SetPasswordCommand` calling `AuthService.ChangePassword(Selected.Id, NewPassword)` then clearing `NewPassword`. Status reports what happened, matching `EntityViewModel<T>`'s wording.

Two rules the emitted code must enforce, each with a comment saying why:

- Deleting the signed-in account is refused — it would leave the running session pointing at a record that no longer exists.
- Deleting the last Admin is refused — it locks everyone out of account management permanently.

In `MainViewModelTemplate.Build`, add a `bool login = false` parameter and, when set, append after the entity registrations:

```csharp
        // The Users pane, for an administrator only. The Admin group of the business shell opens
        // this same view model; until then it is an ordinary pane, which is what makes the four
        // older layouts usable with a login.
        if (Session.IsAdmin)
            Entities.Add(new UsersViewModel());
```

which requires `Entities` to be built then appended to, rather than only initialised — keep the collection initialiser and add the conditional line after it, so the `nologin` output is unchanged.

In `DesktopViewModelFiles.Build`, emit `UsersViewModel.cs` when `login`, and pass `login` through to `MainViewModelTemplate.Build`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "UsersPaneTests|MainViewModelTemplateTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add AppScaffolding/ CodeGenerator.Tests/App/UsersPaneTests.cs
git commit -m "feat(auth): an administrator can manage accounts"
```

---

### Task 13: Prove it against a real generated application

**Files:**
- Modify: `docs/commands.md` (the manual's `build app` section)
- Test: manual verification, recorded in the pull request

**Interfaces:**
- Consumes: every previous task.
- Produces: evidence. No new API.

The spec names this explicitly: the Blazor `Program.cs` surgery cannot be signed off by assertions over emitted strings. Nothing in Tasks 1–12 proves a person can actually sign in.

- [ ] **Step 1: Confirm the whole suite is green in the CI configuration**

Run: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release -warnaserror --no-incremental`
Then: `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release`
Expected: build clean, all tests pass. `--no-incremental` is required or the analyzers do not rerun and `-warnaserror` proves nothing.

- [ ] **Step 2: Generate and build one app per target, under a short path**

```bash
mkdir -p /d/g
```

Generate three apps into `D:\g` — a WPF app, a WinForms app and a Blazor app — with `build app: a shop that sells books`. The scratchpad exceeds `MAX_PATH`; the tell for that failure is MSB3030 reporting a missing file that plainly exists.

Then build each: `dotnet build D:\g\Shop\Shop.csproj`, and the same for `Shop.WinForms` and `Shop.Web`.
Expected: three clean builds.

- [ ] **Step 3: Sign in to the web app — the one that cannot be unit-tested**

```bash
dotnet run --project D:\g\Shop.Web\Shop.Web.csproj
```

Then, against the URL it prints, confirm each of these by hand or with the browser-automation skill:

1. Requesting `/` while signed out redirects to `/login`.
2. The login page renders the brand panel and the form, with the first-run hint.
3. Signing in with `admin` / `admin` succeeds and lands on the home page.
4. Signing in with `admin` / `wrong` shows *User name or password not recognised.* and does **not** sign in.
5. **Refreshing the page keeps you signed in.** This is what distinguishes real cookie auth from a circuit-scoped session, and it is the single most valuable check here.
6. The stylesheets load — a broken static web asset manifest shows as an unstyled page.
7. `/users` is reachable as `admin`, and is refused for a non-Admin account created through the Users screen.
8. Sign out returns to `/login`, and the back button does not restore the signed-in page.

If check 4 fails with a bare HTTP 400 rather than the message, the antiforgery path is the cause — revisit the `DisableAntiforgery()` decision recorded in Task 11 and settle it with evidence.

- [ ] **Step 4: Sign in to the two desktop apps**

Launch `D:\g\Shop\bin\Debug\net10.0-windows\Shop.exe` and the WinForms equivalent. For each confirm:

1. The login window appears first and the shell is not visible behind it.
2. Enter submits the form.
3. A wrong password shows the message and clears the password box.
4. `admin` / `admin` opens the shell, and a Users pane is present.
5. Signing out returns to the login screen without the process restarting.
6. Closing the login window exits the application rather than leaving an invisible process running — check Task Manager, because `ShutdownMode.OnExplicitShutdown` makes this failure silent.

- [ ] **Step 5: Confirm the regression proof**

Generate a fourth app with `build app: a shop that sells books layout tabs nologin` and diff its output against one generated from `origin/main`.
Expected: **no differences at all.** Any difference is a bug in this branch, not in the test.

- [ ] **Step 6: Write the manual section and commit**

Add to `docs/commands.md` a short "Signing in to a generated app" section: the seeded `admin` / `admin` account, that the first-run hint disappears once the password is changed, that only an Admin sees the Users screen, and that `nologin` omits all of it.

```bash
git add docs/commands.md
git commit -m "docs: the manual explains signing in to a generated app"
```

---

## Self-Review

**Spec coverage.** Every requirement in the spec's Authentication section maps to a task: the user record and store (2, 8), hashing (1), the service (3), roles (2, 5, 11, 12), seeding-moves-earlier (4, 9, 10, 11), the `nologin` flag (6, 7), the Users screen without the shell (12). The spec's testing section maps to the compile-and-execute pattern used throughout and to Task 13.

**Deliberately deferred to later branches, as the spec's delivery plan says:** `EntityRole` and the classifier (branch 2), the `business` layout key and the default flip (branch 3), transaction screens (branch 4). This plan does **not** change `EffectiveLayout`, so the default layout stays `tabs` throughout branch 1.

**Two places this plan tells the implementer to look before writing:** the real signatures of `CustomizationSummary.Describe` and `BuildCommandEcho.For` in Task 7, and the real name of the class-count property on `ScaffoldResult` in Task 8. Both are single-line lookups; guessing them here would have put a wrong symbol into the plan.

**One decision left open on purpose,** with the evidence that settles it named: the `DisableAntiforgery()` call in Task 11. It is written the way that is most likely to work, and Task 13 check 4 is what decides whether it stays.

---

**Plan complete and saved to `docs/superpowers/plans/2026-08-20-professional-app-layouts-auth.md`. Two execution options:**

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints

**Which approach?**
