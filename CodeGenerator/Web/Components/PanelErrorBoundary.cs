using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace CodeGenerator.Web.Components;

/// <summary>
/// Catches whatever a stage panel throws, and hands it back instead of letting it end the run.
/// </summary>
/// <remarks>
/// There was no boundary at all. An exception out of a component handler tears down the Blazor
/// circuit, and this page carries no <c>blazor-error-ui</c> element for the framework's own bar to
/// render into, so the page just stopped responding — after which the host counted the tab as gone
/// and the console reported the workflow closed before it finished. Three stages read files and
/// start processes, and their failures cannot all be named in advance: the specific catches say
/// what went wrong where it is worth saying, and this is the floor under the rest.
/// <para>
/// Deriving from <see cref="ErrorBoundary"/> rather than reimplementing it keeps the framework's
/// own rendering — child content normally, error content once something has thrown — and overrides
/// only what happens in between.
/// </para>
/// </remarks>
public sealed class PanelErrorBoundary : ErrorBoundary
{
    /// <summary>Raised with whatever the panel threw. The page turns it into a failed stage.</summary>
    [Parameter]
    public EventCallback<Exception> OnError { get; set; }

    protected override Task OnErrorAsync(Exception exception) => OnError.InvokeAsync(exception);
}
