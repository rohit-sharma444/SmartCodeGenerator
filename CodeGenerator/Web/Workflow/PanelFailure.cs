namespace CodeGenerator.Web.Workflow;

/// <summary>
/// Turns whatever a panel threw into something the workflow can show.
/// </summary>
/// <remarks>
/// Separate from the boundary that catches it so the wording can be asserted without a renderer,
/// and so the page has one line to write rather than a decision to make.
/// </remarks>
internal static class PanelFailure
{
    /// <summary>
    /// Records the failure against the stage that was open.
    /// </summary>
    /// <remarks>
    /// Nothing happens to a workflow that has already ended. <see cref="BuildSession.Fail"/>
    /// refuses one — rightly — and this is the one place where a thrown exception would have
    /// nothing left to catch it.
    /// </remarks>
    public static void Report(BuildSession session, Exception exception)
    {
        if (session.Phase != SessionPhase.Running)
            return;

        session.Fail(Explain(exception));
    }

    /// <summary>
    /// What to put on screen. The message is included rather than swallowed: a path or a file name
    /// in it is usually the whole answer, and "something went wrong" sends the user to a log they
    /// do not have.
    /// </summary>
    public static string Explain(Exception exception)
        => exception switch
        {
            PathTooLongException
                => "A path here was too long for Windows. A shorter workspace folder — "
                   + @"C:\ws rather than a deep Documents path — is the usual fix.",

            UnauthorizedAccessException
                => $"Access was denied: {exception.Message}",

            IOException
                => $"A file could not be read or written: {exception.Message}",

            _ => $"This step could not finish: {exception.Message}",
        };
}
