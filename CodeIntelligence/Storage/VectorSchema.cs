using Microsoft.Data.Sqlite;

namespace CodeIntelligence.Storage;

/// <summary>DDL for the AI index. Vectors are stored as raw little-endian float32
/// BLOBs; at a few thousand rows a brute-force scan is microseconds, so there is no
/// ANN index to maintain.</summary>
internal static class VectorSchema
{
    internal const string ModelIdKey = "model_id";
    internal const string DimensionKey = "dimension";
    internal const string BuiltUtcKey = "built_utc";

    internal static void EnsureCreated(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS corpus_document(
              id          INTEGER PRIMARY KEY AUTOINCREMENT,
              text        TEXT    NOT NULL,
              intent      TEXT    NOT NULL,
              source_kind TEXT    NOT NULL,
              source_ref  TEXT    NOT NULL,
              content_hash TEXT   NOT NULL);

            CREATE TABLE IF NOT EXISTS embedding(
              document_id INTEGER PRIMARY KEY REFERENCES corpus_document(id),
              model_id    TEXT    NOT NULL,
              dim         INTEGER NOT NULL,
              vector      BLOB    NOT NULL);

            CREATE TABLE IF NOT EXISTS index_meta(
              key   TEXT PRIMARY KEY,
              value TEXT NOT NULL);

            CREATE TABLE IF NOT EXISTS ai_decision(
              id           INTEGER PRIMARY KEY AUTOINCREMENT,
              created_utc  TEXT    NOT NULL,
              input        TEXT    NOT NULL,
              intent       TEXT    NOT NULL,
              confidence   REAL    NOT NULL,
              evidence_ids TEXT    NOT NULL,
              accepted     INTEGER);
            """;
        cmd.ExecuteNonQuery();
    }
}
