using System.Globalization;
using Microsoft.Data.SqlClient;

namespace DataIngestion.Schema.Readers.Sql;

/// <summary>
/// <see cref="ISqlSchemaSource"/> over <c>Microsoft.Data.SqlClient</c>.
/// </summary>
/// <remarks>
/// Deliberately the only file in this feature that references SqlClient, and deliberately thin:
/// it is the one piece CI cannot run, so there is as little of it as possible and no decision in
/// it worth testing. Anything that looks like a rule belongs above the seam, where a fake can
/// reach it.
/// </remarks>
public sealed class SqlClientSchemaSource : ISqlSchemaSource
{
    public IReadOnlyList<SqlTableInfo> Tables(string connectionString)
    {
        // The row count is a correlated sub-select rather than a join, so a login without
        // VIEW DATABASE STATE gets NULL for it instead of losing the row. Losing the count must
        // not lose the table from the picker.
        const string sql = """
            SELECT  s.name AS [SchemaName],
                    t.name AS [TableName],
                    (SELECT COUNT(*) FROM sys.columns c
                      WHERE c.object_id = t.object_id) AS [ColumnCount],
                    (SELECT SUM(p.row_count) FROM sys.dm_db_partition_stats p
                      WHERE p.object_id = t.object_id AND p.index_id IN (0, 1)) AS [RowCount]
            FROM    sys.tables t
            JOIN    sys.schemas s ON s.schema_id = t.schema_id
            WHERE   t.is_ms_shipped = 0
            ORDER BY s.name, t.name;
            """;

        var tables = new List<SqlTableInfo>();

        foreach (var row in Query(connectionString, sql))
        {
            tables.Add(new SqlTableInfo(
                (string)row["SchemaName"],
                (string)row["TableName"],
                Convert.ToInt32(row["ColumnCount"], CultureInfo.InvariantCulture),
                row["RowCount"] is DBNull
                    ? null
                    : Convert.ToInt64(row["RowCount"], CultureInfo.InvariantCulture)));
        }

        return tables;
    }

    public IReadOnlyList<SqlColumnInfo> Columns(
        string connectionString, string schema, string table)
    {
        // IS_PK marks the column of a single-column primary key. A composite key marks nothing:
        // the model numbers its rows by one Id, and half a key is not one.
        const string sql = """
            SELECT  c.COLUMN_NAME, c.DATA_TYPE, c.IS_NULLABLE,
                    c.CHARACTER_MAXIMUM_LENGTH, c.NUMERIC_PRECISION, c.NUMERIC_SCALE,
                    CASE WHEN pk.COLUMN_NAME IS NULL THEN 0 ELSE 1 END AS IS_PK
            FROM    INFORMATION_SCHEMA.COLUMNS c
            LEFT JOIN (
                        SELECT   k.TABLE_SCHEMA, k.TABLE_NAME, MIN(k.COLUMN_NAME) AS COLUMN_NAME
                        FROM     INFORMATION_SCHEMA.TABLE_CONSTRAINTS t
                        JOIN     INFORMATION_SCHEMA.KEY_COLUMN_USAGE k
                                   ON k.CONSTRAINT_SCHEMA = t.CONSTRAINT_SCHEMA
                                  AND k.CONSTRAINT_NAME = t.CONSTRAINT_NAME
                        WHERE    t.CONSTRAINT_TYPE = 'PRIMARY KEY'
                        GROUP BY k.TABLE_SCHEMA, k.TABLE_NAME
                        HAVING   COUNT(*) = 1
                      ) pk
                      ON pk.TABLE_SCHEMA = c.TABLE_SCHEMA
                     AND pk.TABLE_NAME = c.TABLE_NAME
                     AND pk.COLUMN_NAME = c.COLUMN_NAME
            WHERE   c.TABLE_SCHEMA = @schema AND c.TABLE_NAME = @table
            ORDER BY c.ORDINAL_POSITION;
            """;

        var columns = new List<SqlColumnInfo>();

        foreach (var row in Query(connectionString, sql, ("@schema", schema), ("@table", table)))
        {
            columns.Add(new SqlColumnInfo(
                (string)row["COLUMN_NAME"],
                (string)row["DATA_TYPE"],
                string.Equals(
                    (string)row["IS_NULLABLE"], "YES", StringComparison.OrdinalIgnoreCase),
                AsInt(row["CHARACTER_MAXIMUM_LENGTH"]),
                AsInt(row["NUMERIC_PRECISION"]),
                AsInt(row["NUMERIC_SCALE"]),
                Convert.ToInt32(row["IS_PK"], CultureInfo.InvariantCulture) == 1));
        }

        return columns;
    }

    /// <summary>
    /// One INFORMATION_SCHEMA numeric, or null. The columns are DBNull for types they do not
    /// apply to, and their declared types vary — NUMERIC_PRECISION is tinyint, and
    /// CHARACTER_MAXIMUM_LENGTH is int — so an unboxing cast to int would throw on real schemas.
    /// </summary>
    private static int? AsInt(object? value)
        => value is null || value is DBNull ? null : Convert.ToInt32(value);

    public IReadOnlyList<TabularForeignKey> ForeignKeys(string connectionString)
    {
        // Single-column keys only. A composite key produces one row per column, and the model has
        // no way to express the pair — half of it would be a relationship that is wrong rather
        // than incomplete. The HAVING COUNT(*) = 1 is what excludes them.
        //
        // The declaring table and both schemas come back too: without them a key is matched to
        // every table that has a column of the same name.
        const string sql = """
            SELECT  parentSchema.name AS [ParentSchema],
                    parentTable.name  AS [ParentTable],
                    parent.name       AS [ColumnName],
                    targetSchema.name AS [TargetSchema],
                    target.name       AS [TargetTable],
                    targetCol.name    AS [TargetColumn]
            FROM    sys.foreign_key_columns fkc
            JOIN    sys.columns parent
                      ON parent.object_id = fkc.parent_object_id
                     AND parent.column_id = fkc.parent_column_id
            JOIN    sys.tables parentTable
                      ON parentTable.object_id = fkc.parent_object_id
            JOIN    sys.schemas parentSchema
                      ON parentSchema.schema_id = parentTable.schema_id
            JOIN    sys.tables target
                      ON target.object_id = fkc.referenced_object_id
            JOIN    sys.schemas targetSchema
                      ON targetSchema.schema_id = target.schema_id
            JOIN    sys.columns targetCol
                      ON targetCol.object_id = fkc.referenced_object_id
                     AND targetCol.column_id = fkc.referenced_column_id
            WHERE   fkc.constraint_object_id IN (
                        SELECT constraint_object_id
                        FROM   sys.foreign_key_columns
                        GROUP BY constraint_object_id
                        HAVING COUNT(*) = 1);
            """;

        var keys = new List<TabularForeignKey>();

        foreach (var row in Query(connectionString, sql))
        {
            keys.Add(new TabularForeignKey(
                (string)row["ColumnName"],
                (string)row["TargetTable"],
                (string)row["TargetColumn"],
                (string)row["ParentTable"],
                (string)row["ParentSchema"],
                (string)row["TargetSchema"]));
        }

        return keys;
    }

    public IReadOnlyList<string?[]> Rows(string connectionString, string schema, string table, int limit)
    {
        // The only place a name is interpolated rather than parameterised, because a table name
        // cannot be a parameter. Both parts are bracket-escaped for that reason: these names come
        // from sys.tables today, and this keeps it safe if a caller ever passes something else.
        //
        // TOP, because a picked table can be enormous -- one on the machine this was built on
        // holds 47 million rows, and every one was once read into memory to seed 1,000.
        var sql = $"SELECT TOP (@limit) * FROM [{Escape(schema)}].[{Escape(table)}];";

        var rows = new List<string?[]>();

        using var connection = new SqlConnection(connectionString);
        connection.Open();

        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@limit", limit);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var row = new string?[reader.FieldCount];

            for (var i = 0; i < reader.FieldCount; i++)
                row[i] = reader.IsDBNull(i) ? null : Render(reader.GetValue(i));

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// A value as the string <see cref="TabularTable.Rows"/> holds.
    /// </summary>
    /// <remarks>
    /// Invariant throughout, and round-trip formats for the types where a locale would otherwise
    /// change the meaning. This is the opposite direction of travel from the file readers: the
    /// type is known and the string is derived from it, rather than the string being read and the
    /// type guessed.
    /// </remarks>
    private static string Render(object value) => value switch
    {
        DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset offset => offset.ToString("O", CultureInfo.InvariantCulture),

        // Words rather than 0/1, matching what ExcelSheetReader emits for the same reason:
        // ColumnTypeInference recognises true/false, and a bool that reads back as an int is a
        // column that silently changes type.
        bool flag => flag ? "true" : "false",

        byte[] bytes => Convert.ToBase64String(bytes),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    /// <summary>Doubles a closing bracket, so a bracket inside an identifier cannot end it.</summary>
    private static string Escape(string name) => name.Replace("]", "]]");

    private static List<Dictionary<string, object>> Query(
        string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        var rows = new List<Dictionary<string, object>>();

        using var connection = new SqlConnection(connectionString);
        connection.Open();

        using var command = new SqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.GetValue(i);

            rows.Add(row);
        }

        return rows;
    }
}
