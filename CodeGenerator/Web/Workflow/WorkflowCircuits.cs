using Microsoft.AspNetCore.Components.Server.Circuits;

namespace CodeGenerator.Web.Workflow;

/// <summary>
/// Tells <see cref="CircuitWatch"/> when a tab arrives and when it goes.
/// </summary>
/// <remarks>
/// Scoped, because Blazor creates one of these per circuit; the watch it reports to is the
/// singleton, which is what makes the count meaningful across tabs.
/// <para>
/// The connection pair rather than the circuit pair, deliberately.
/// <see cref="OnCircuitClosedAsync"/> does not run when a tab is closed — it runs once the
/// disconnected circuit's retention period expires, three minutes later by default, which is far
/// too long to leave a console blocked. <see cref="OnConnectionDownAsync"/> runs immediately, and
/// the grace period the host applies is what tells a closed tab from a refresh.
/// </para>
/// </remarks>
internal sealed class WorkflowCircuits(CircuitWatch watch) : CircuitHandler
{
    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        watch.Opened(DateTimeOffset.UtcNow);

        return Task.CompletedTask;
    }

    public override Task OnConnectionDownAsync(
        Circuit circuit, CancellationToken cancellationToken)
    {
        watch.Closed(DateTimeOffset.UtcNow);

        return Task.CompletedTask;
    }
}
