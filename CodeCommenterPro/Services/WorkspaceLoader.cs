using System.Reflection;
using System;
using System.Linq;

namespace CodeCommenterPro.Services;

public static class WorkspaceLoader
{
    private static object? _workspace;

    public static object? GetWorkspace()
    {
        if (_workspace is not null)
            return _workspace;

        try
        {
            // Try to load MSBuildLocator and register defaults
            var locatorAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Microsoft.Build.Locator");

            if (locatorAsm == null)
            {
                try
                {
                    locatorAsm = Assembly.Load("Microsoft.Build.Locator");
                }
                catch
                {
                    // not available
                    locatorAsm = null;
                }
            }

            if (locatorAsm != null)
            {
                var regType = locatorAsm.GetType("Microsoft.Build.Locator.MSBuildLocator");
                var registerDefaults = regType?.GetMethod("RegisterDefaults", BindingFlags.Public | BindingFlags.Static);
                registerDefaults?.Invoke(null, null);
            }

            // Try to load MSBuildWorkspace type
            var rosAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Microsoft.CodeAnalysis.Workspaces.MSBuild");

            if (rosAsm == null)
            {
                try
                {
                    rosAsm = Assembly.Load("Microsoft.CodeAnalysis.Workspaces.MSBuild");
                }
                catch
                {
                    rosAsm = null;
                }
            }

            if (rosAsm == null)
                return null;

            var wsType = rosAsm.GetType("Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace");
            if (wsType == null)
                return null;

            var createMethod = wsType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
            if (createMethod == null)
                return null;

            var ws = createMethod.Invoke(null, null);
            _workspace = ws;
            return _workspace;
        }
        catch
        {
            return null;
        }
    }
}
