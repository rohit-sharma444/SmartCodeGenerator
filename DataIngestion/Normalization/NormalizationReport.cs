namespace DataIngestion.Normalization;

/// <summary>
/// What the analyser found, plus what it could not look for. <paramref name="Notes"/> exists so a
/// check that was skipped says so — a table of three rows cannot be tested for functional
/// dependencies, and silence there would read as "no problems found".
/// </summary>
public sealed record NormalizationReport(
    IReadOnlyList<NormalizationFinding> Findings,
    IReadOnlyList<string> Notes);
