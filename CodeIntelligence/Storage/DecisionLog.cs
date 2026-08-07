using System.Globalization;
using CodeIntelligence.Abstractions;
using Microsoft.Data.Sqlite;

namespace CodeIntelligence.Storage;

public sealed record LoggedDecision(
    DateTime CreatedUtc, string Input, string Intent, double Confidence,
    string EvidenceIds, bool? Accepted);

/// <summary>Append-only audit of every AI resolution and the user's answer. This is
/// both the traceability record behind `ai why` and the labelled data a later phase
/// needs to calibrate an auto-execute threshold from measurement instead of a guess.</summary>
public sealed class DecisionLog
{
    private readonly string _databasePath;

    public DecisionLog(string databasePath) => _databasePath = databasePath;

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        VectorSchema.EnsureCreated(connection);
        return connection;
    }

    public void Append(string input, AiResolution resolution, bool? accepted)
    {
        try
        {
            using var connection = Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO ai_decision(created_utc, input, intent, confidence, evidence_ids, accepted)
                VALUES($c, $i, $n, $f, $e, $a)
                """;
            cmd.Parameters.AddWithValue("$c", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$i", input);
            cmd.Parameters.AddWithValue("$n", resolution.Intent.ToString());
            cmd.Parameters.AddWithValue("$f", resolution.Confidence);
            cmd.Parameters.AddWithValue("$e", string.Join(',', resolution.Evidence.Select(x => x.DocumentId)));
            cmd.Parameters.AddWithValue("$a", accepted.HasValue ? accepted.Value ? 1 : 0 : DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        catch (Exception)
        {
            // Audit is best-effort; losing a row must never fail the command.
        }
    }

    public LoggedDecision? Last()
    {
        try
        {
            using var connection = Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT created_utc, input, intent, confidence, evidence_ids, accepted
                FROM ai_decision ORDER BY id DESC LIMIT 1
                """;

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return null;

            // RoundtripKind is load-bearing, exactly as in SqliteVectorStore.BuiltUtc:
            // the value was written with "o", and a bare Parse hands it back as
            // DateTimeKind.Local, silently shifting a field the record calls CreatedUtc
            // by the machine's offset.
            var createdUtc = DateTime.Parse(
                reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

            return new LoggedDecision(
                createdUtc,
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDouble(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5) == 1);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public int Count()
    {
        try
        {
            using var connection = Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM ai_decision";
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
