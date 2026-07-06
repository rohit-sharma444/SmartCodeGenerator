namespace CodeGenerationEngine.Models;

/// <summary>
/// One learned association between a command signature and an intent, with the
/// success/failure tally that drives its confidence.
/// </summary>
public sealed class LearnedIntentEntry
{
    public string Signature { get; set; } = string.Empty;

    public IntentType Intent { get; set; }

    public int SuccessCount { get; set; }

    public int FailureCount { get; set; }

    public DateTime LastUsedUtc { get; set; }

    /// <summary>
    /// Success ratio, damped by how few observations we have so a single
    /// success does not immediately read as certainty. Clamped to [0, 1].
    /// </summary>
    public double Confidence
    {
        get
        {
            var total = SuccessCount + FailureCount;
            if (total == 0)
                return 0d;

            var ratio = (double)SuccessCount / total;
            var observationWeight = total / (total + 2.0);
            return Math.Clamp(ratio * observationWeight, 0d, 1d);
        }
    }
}
