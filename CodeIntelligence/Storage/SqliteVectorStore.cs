using CodeGenerationEngine.Models;
using CodeIntelligence.Abstractions;
using Microsoft.Data.Sqlite;

namespace CodeIntelligence.Storage;

/// <summary>SQLite-backed vector store. ReplaceAll is a single transaction, so a
/// crash or a rejected row leaves the previous index intact and serving — a killed
/// rebuild must never leave the store unusable.</summary>
public sealed class SqliteVectorStore : IVectorStore
{
    private readonly string _databasePath;

    public SqliteVectorStore(string databasePath)
    {
        _databasePath = databasePath;
        var folder = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        VectorSchema.EnsureCreated(connection);
        return connection;
    }

    public IndexStatus GetStatus(string currentModelId)
    {
        using var connection = Open();

        var indexedModel = ReadMeta(connection, VectorSchema.ModelIdKey);
        var dimension = int.TryParse(ReadMeta(connection, VectorSchema.DimensionKey), out var d) ? d : 0;
        var built = DateTime.TryParse(ReadMeta(connection, VectorSchema.BuiltUtcKey), out var b)
            ? b : (DateTime?)null;

        using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM embedding WHERE model_id = $m";
        count.Parameters.AddWithValue("$m", currentModelId);
        var documents = Convert.ToInt32(count.ExecuteScalar() ?? 0);

        var reindex = documents == 0 ||
                      !string.Equals(indexedModel, currentModelId, StringComparison.Ordinal);

        return new IndexStatus(documents, indexedModel, dimension, reindex, built);
    }

    public IReadOnlyList<StoredVector> LoadAll(string modelId)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT d.id, d.text, d.intent, e.vector
            FROM   embedding e JOIN corpus_document d ON d.id = e.document_id
            WHERE  e.model_id = $m
            ORDER  BY d.id
            """;
        cmd.Parameters.AddWithValue("$m", modelId);

        var results = new List<StoredVector>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var intent = Enum.TryParse<IntentType>(reader.GetString(2), out var parsed)
                ? parsed : IntentType.Unknown;

            results.Add(new StoredVector(
                reader.GetInt64(0), reader.GetString(1), intent, FromBlob((byte[])reader[3])));
        }

        return results;
    }

    public void ReplaceAll(
        string modelId, int dimension, IReadOnlyList<(CorpusDocument Doc, float[] Vector)> rows)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        using (var wipe = connection.CreateCommand())
        {
            wipe.CommandText = "DELETE FROM embedding; DELETE FROM corpus_document;";
            wipe.ExecuteNonQuery();
        }

        foreach (var (doc, vector) in rows)
        {
            if (vector.Length != dimension)
                throw new InvalidOperationException(
                    $"Vector for '{doc.Text}' has length {vector.Length}, expected {dimension}.");

            using var insertDoc = connection.CreateCommand();
            insertDoc.CommandText = """
                INSERT INTO corpus_document(text, intent, source_kind, source_ref, content_hash)
                VALUES($t, $i, $k, $r, $h);
                SELECT last_insert_rowid();
                """;
            insertDoc.Parameters.AddWithValue("$t", doc.Text);
            insertDoc.Parameters.AddWithValue("$i", doc.Intent.ToString());
            insertDoc.Parameters.AddWithValue("$k", doc.SourceKind.ToString());
            insertDoc.Parameters.AddWithValue("$r", doc.SourceRef);
            insertDoc.Parameters.AddWithValue("$h", doc.ContentHash);
            var id = Convert.ToInt64(insertDoc.ExecuteScalar()!);

            using var insertVec = connection.CreateCommand();
            insertVec.CommandText =
                "INSERT INTO embedding(document_id, model_id, dim, vector) VALUES($d, $m, $n, $v)";
            insertVec.Parameters.AddWithValue("$d", id);
            insertVec.Parameters.AddWithValue("$m", modelId);
            insertVec.Parameters.AddWithValue("$n", dimension);
            insertVec.Parameters.AddWithValue("$v", ToBlob(vector));
            insertVec.ExecuteNonQuery();
        }

        WriteMeta(connection, VectorSchema.ModelIdKey, modelId);
        WriteMeta(connection, VectorSchema.DimensionKey, dimension.ToString());
        WriteMeta(connection, VectorSchema.BuiltUtcKey, DateTime.UtcNow.ToString("o"));

        transaction.Commit();
    }

    private static string? ReadMeta(SqliteConnection connection, string key)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM index_meta WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    private static void WriteMeta(SqliteConnection connection, string key, string value)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO index_meta(key, value) VALUES($k, $v)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """;
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    private static byte[] ToBlob(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] FromBlob(byte[] bytes)
    {
        var vector = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        return vector;
    }
}
