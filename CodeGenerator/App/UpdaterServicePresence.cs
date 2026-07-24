using System.ComponentModel;
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
        catch (InvalidOperationException ex) when (ex.InnerException is Win32Exception
            { NativeErrorCode: 1060 }) // ERROR_SERVICE_DOES_NOT_EXIST
        {
            // ServiceController wraps EVERY SCM failure in InvalidOperationException — access
            // denied, the services database locked, RPC unavailable — not only "service does not
            // exist". Only the genuine not-found case (Win32 1060) means NotInstalled; anything
            // else refused by the SCM falls through to the general catch below, which claims
            // nothing, per this enum's own doc.
            return UpdaterServiceState.NotInstalled;
        }
        catch
        {
            // Refused, or a machine that will not answer. Claim nothing.
            return UpdaterServiceState.Unknown;
        }
    }
}
