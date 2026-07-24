using System.Runtime.Versioning;
using System.ServiceProcess;

namespace CodeGenerator.App;

/// <summary>Whether the updater service is there, and whether it is running.</summary>
public enum UpdaterServiceState
{
    /// <summary>No such service. Nothing is updating the store — the install hint is warranted.</summary>
    NotInstalled,

    Running,

    Stopped,

    /// <summary>Not Windows, or the query was refused. Claim nothing.</summary>
    Unknown,
}

/// <summary>
/// Asks Windows whether the updater service exists. Isolated in its own file because it is the only
/// Windows-only API the CLI touches.
/// </summary>
public static class UpdaterServicePresence
{
    public const string ServiceName = "SmartCodeGenWikipediaUpdater";

    public static UpdaterServiceState Detect()
    {
        if (!OperatingSystem.IsWindows())
            return UpdaterServiceState.Unknown;

        return DetectOnWindows();
    }

    [SupportedOSPlatform("windows")]
    private static UpdaterServiceState DetectOnWindows()
    {
        try
        {
            using var controller = new ServiceController(ServiceName);

            return controller.Status == ServiceControllerStatus.Running
                ? UpdaterServiceState.Running
                : UpdaterServiceState.Stopped;
        }
        catch (InvalidOperationException)
        {
            // ServiceController throws this — not a "not found" exception — when the name is absent.
            return UpdaterServiceState.NotInstalled;
        }
        catch
        {
            // Refused, or a machine that will not answer. Claim nothing.
            return UpdaterServiceState.Unknown;
        }
    }
}
