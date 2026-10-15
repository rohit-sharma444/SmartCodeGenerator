namespace DataIngestion.Normalization;

/// <summary>
/// What the analyser found, plus what it could not look for or will not change.
/// <paramref name="Notes"/> exists so a check that was skipped says so, and so does a derived
/// column that is kept as stored — a table of three rows cannot be tested for functional
/// dependencies, and silence there would read as "no problems found".
/// </summary>
public sealed record NormalizationReport(
    IReadOnlyList<NormalizationFinding> Findings,
    IReadOnlyList<string> Notes);
