namespace CodeGenerator.BuildFix;

/// <summary>One narrow, safe fix for one kind of build error.</summary>
internal interface IBuildFixRule
{
    string Name { get; }

    /// <summary>
    /// Fixes <paramref name="d"/> when this rule knows how and it is safe; returns true only when it
    /// changed a file. Returns false — and changes nothing — for everything else.
    /// </summary>
    bool Fix(BuildDiagnostic d, FixSession session);
}
