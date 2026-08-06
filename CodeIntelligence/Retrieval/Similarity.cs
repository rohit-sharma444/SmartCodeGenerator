namespace CodeIntelligence.Retrieval;

public static class Similarity
{
    /// <summary>Cosine similarity. Mismatched lengths score 0 rather than throwing:
    /// a dimension mismatch means a stale index, which the caller reports as
    /// "reindex required" instead of crashing the REPL.</summary>
    public static double Cosine(float[] a, float[] b)
    {
        if (a.Length == 0 || a.Length != b.Length)
            return 0.0;

        double dot = 0, normA = 0, normB = 0;

        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0 || normB == 0)
            return 0.0;

        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    /// <summary>Returns a unit-length copy. A zero vector is returned unchanged.</summary>
    public static float[] Normalize(float[] vector)
    {
        double sum = 0;
        foreach (var value in vector)
            sum += value * value;

        var magnitude = Math.Sqrt(sum);
        if (magnitude == 0)
            return vector;

        var result = new float[vector.Length];
        for (var i = 0; i < vector.Length; i++)
            result[i] = (float)(vector[i] / magnitude);

        return result;
    }
}
