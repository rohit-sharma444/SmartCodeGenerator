using System.Reflection;
using AppScaffolding;
using AppScaffolding.Auth;
using CodeGenerationEngine.Core;
using Microsoft.CodeAnalysis;

namespace CodeGenerator.Tests.App;

/// <summary>Which of the three acting-user shapes a generated app has.</summary>
internal enum EmittedLogin
{
    /// <summary>No login at all: the console, webapi, worker and classlib targets.</summary>
    None,

    /// <summary>A login and a desktop Session: WinForms and WPF.</summary>
    Session,

    /// <summary>A login and no Session: Blazor.</summary>
    Sessionless,
}

/// <summary>
/// The real emitted Data and Validation folders, compiled into one assembly and loaded, so a test
/// can RUN <c>Repository&lt;T&gt;</c> rather than read it.
/// </summary>
/// <remarks>
/// The model is named afresh for every instance because the real JsonStore writes
/// <c>&lt;Type&gt;.json</c> beside the test binary; a unique name keeps one test's rows out of
/// another's, and <see cref="Dispose"/> deletes the file.
/// </remarks>
internal sealed class EmittedDataLayer : IDisposable
{
    private EmittedDataLayer(Assembly assembly, string modelName)
    {
        Assembly = assembly;
        ModelName = modelName;
    }

    public Assembly Assembly { get; }

    public string ModelName { get; }

    public Type Model => Assembly.GetType("Acme.Models." + ModelName)!;

    /// <summary>Compiles the data layer of an app with <paramref name="login"/>, plus
    /// <paramref name="extra"/> sources (a generated test file, say) in the same assembly.</summary>
    public static EmittedDataLayer Compile(
        EmittedLogin login, bool validation = true, params (string Path, string Code)[] extra)
    {
        var modelName = "Probe" + Guid.NewGuid().ToString("N")[..8];
        var auth = login != EmittedLogin.None;
        var sessionAuth = login == EmittedLogin.Session;

        var files = new List<(string Path, string Code)>
        {
            ($"Models/{modelName}.cs", $$"""
                using System;

                namespace Acme.Models;

                public sealed class {{modelName}}
                {
                    public int Id { get; set; }

                    public string Name { get; set; } = string.Empty;

                    public DateTime CreatedOn { get; set; }

                    public DateTime ModifiedOn { get; set; }
                }
                """),
            ("Validation/ValidationContext.cs", ValidationContextTemplate.Source("Acme")),
            ("Validation/IEntityLookup.cs", ValidatorTemplate.EntityLookupInterface("Acme")),
            ("Validation/Permissions.cs", PermissionsTemplate.Source("Acme", Array.Empty<string>(), auth)),
            ("Validation/ActingUser.cs", ActingUserTemplate.Source("Acme", sessionAuth, auth)),
            ("Data/IStore.cs", StoreSeamTemplate.Source("Acme")),
            ("Data/SaveResult.cs", SaveResultTemplate.Source("Acme")),
            ("Data/StoreFactory.cs", StoreFactoryTemplate.Source("Acme", PersistenceKind.Json)),
            ("Data/JsonStore.cs", JsonStoreTemplate.Source("Acme.Data", sessionAuth)),
            ("Data/Repository.cs", RepositoryTemplate.Base("Acme", validation, logging: false, sessionAuth)),
            ($"Data/{modelName}Repository.cs", RepositoryTemplate.Typed("Acme", modelName)),
        };

        if (validation)
        {
            files.Add(("Validation/IValidator.cs", ValidatorTemplate.ValidatorInterface("Acme")));
            files.Add(("Validation/StoreEntityLookup.cs", ValidatorTemplate.StoreEntityLookup("Acme")));
            files.Add(("Validation/ValidatorFactory.cs",
                ValidatorTemplate.Factory("Acme", Array.Empty<string>())));
        }

        if (auth)
        {
            files.Add(("Models/User.cs", AuthModelTemplates.User("Acme")));
            files.Add(("Auth/AppRole.cs", AuthModelTemplates.Role("Acme")));
        }

        if (sessionAuth)
            files.Add(("Auth/Session.cs", SessionTemplate.Source("Acme")));

        files.AddRange(extra);

        var result = Compiler.CompileSources(files);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();

        Assert.IsEmpty(errors, "Compilation errors:\n" + string.Join("\n", errors));

        return new EmittedDataLayer(Assembly.Load(result.AssemblyBytes!), modelName);
    }

    /// <summary>A fresh typed repository over the model.</summary>
    public object NewRepository()
        => Activator.CreateInstance(Assembly.GetType($"Acme.Data.{ModelName}Repository")!)!;

    /// <summary>A new model instance with a name, so it is not blank.</summary>
    public object NewItem(string name = "Test")
    {
        var item = Activator.CreateInstance(Model)!;
        Model.GetProperty("Name")!.SetValue(item, name);
        return item;
    }

    /// <summary>Calls <paramref name="method"/> on <paramref name="repository"/> and returns its
    /// SaveResult's Success, Errors and Item.</summary>
    public static (bool Success, IReadOnlyList<string> Errors, object? Item) Call(
        object repository, string method, object argument)
    {
        var result = repository.GetType().GetMethod(method)!.Invoke(repository, new[] { argument })!;
        var type = result.GetType();

        return (
            (bool)type.GetProperty("Success")!.GetValue(result)!,
            (IReadOnlyList<string>)type.GetProperty("Errors")!.GetValue(result)!,
            type.GetProperty("Item")!.GetValue(result));
    }

    /// <summary>A static member of a type in the emitted assembly, public or not.</summary>
    public object? Invoke(string typeName, string method, params object?[] arguments)
        => Assembly.GetType(typeName)!
            .GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, arguments);

    public void Dispose()
    {
        var path = Path.Combine(AppContext.BaseDirectory, ModelName + ".json");

        try { File.Delete(path); }
        catch (IOException) { }
    }
}
