namespace CodeGenerator.BuildFix;

/// <summary>Proposes a fix for an error no rule knows. Never throws; null means "no proposal".</summary>
internal interface IBuildFixAi
{
    /// <summary>Whether the model can be asked at all (a quick probe).</summary>
    bool IsAvailable();

    /// <summary>
    /// The corrected text for exactly <paramref name="excerpt"/> (the lines around the error), or
    /// null. The model never chooses a file or a line range: the caller owns both. The result is
    /// untrusted model output that has passed only the shape checks; the caller still checks the
    /// edit (it parses, the error count does not rise) before keeping it.
    /// </summary>
    string? Propose(BuildDiagnostic d, string excerpt);

    /// <summary>
    /// The same, but the wait ends when <paramref name="ct"/> is cancelled. A model that ignores the
    /// token still works: the loop checks it between asks.
    /// </summary>
    string? Propose(BuildDiagnostic d, string excerpt, CancellationToken ct) => Propose(d, excerpt);

    /// <summary>
    /// True when the latest <c>Propose</c> returned null because the wait ran out. The loop then
    /// stops asking for the rest of the run: every further ask would wait just as long.
    /// </summary>
    bool LastTimedOut => false;
}
