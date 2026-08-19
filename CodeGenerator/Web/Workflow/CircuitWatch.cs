namespace CodeGenerator.Web.Workflow;

/// <summary>
/// How many browser tabs are driving the workflow, and how long it has been since the last one
/// left.
/// </summary>
/// <remarks>
/// The host polls this to decide when a closed tab means the workflow is over. Two rules, and
/// they pull in opposite directions:
/// <para>
/// A workflow nobody has opened yet is not abandoned. The URL is printed whether or not a browser
/// could be launched, and someone pasting it a minute later must find the page still there — so
/// the count starting at zero is not enough on its own, and <see cref="_everOpened"/> is what
/// separates "not yet" from "no longer".
/// </para>
/// <para>
/// A refresh is not a closed tab either. Blazor drops the circuit and raises a new one a moment
/// later, which reaches here as a close followed by an open; the grace period the host applies is
/// what covers the gap between them.
/// </para>
/// <para>
/// Every instant is passed in rather than read from a clock here, so the rules are assertable
/// without waiting for real seconds to pass.
/// </para>
/// </remarks>
internal sealed class CircuitWatch
{
    private readonly Lock _gate = new();

    private int _open;

    private bool _everOpened;

    /// <summary>When the last tab left. Null while one is still connected, and while none has
    /// ever arrived.</summary>
    private DateTimeOffset? _emptySince;

    public void Opened(DateTimeOffset now)
    {
        lock (_gate)
        {
            _open++;
            _everOpened = true;
            _emptySince = null;
        }
    }

    public void Closed(DateTimeOffset now)
    {
        lock (_gate)
        {
            // Floored rather than allowed to go negative: a close without a matching open would
            // otherwise make the count permanently wrong, and the count is what keeps a second
            // tab's departure from ending a workflow the first one is still driving.
            if (_open > 0)
                _open--;

            if (_open == 0)
                _emptySince = now;
        }
    }

    /// <summary>
    /// Whether every tab has been gone for longer than <paramref name="grace"/>.
    /// </summary>
    public bool ShouldAbandon(TimeSpan grace, DateTimeOffset now)
    {
        lock (_gate)
            return _everOpened && _open == 0 && _emptySince is { } since && now - since > grace;
    }
}
