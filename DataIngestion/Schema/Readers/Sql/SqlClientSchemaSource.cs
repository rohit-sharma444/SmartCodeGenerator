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
                    CASE WHEN pk.COLUMN_NAME IS NULL THEN 0 ELSE 1 END AS IS_PK,
                    c.COLUMN_DEFAULT, cc.definition AS COMPUTED_AS
            FROM    INFORMATION_SCHEMA.COLUMNS c
            LEFT JOIN sys.computed_columns cc
                      ON cc.object_id = OBJECT_ID(QUOTENAME(c.TABLE_SCHEMA) + '.' + QUOTENAME(c.TABLE_NAME))
                     AND cc.name = c.COLUMN_NAME
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
                Convert.ToInt32(row["IS_PK"], CultureInfo.InvariantCulture) == 1,
                row["COMPUTED_AS"] as string,
                row["COLUMN_DEFAULT"] as string));
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

    public IReadOnlyList<SqlConstraintInfo> Constraints(string connectionString, string schema, string table)
    {
        // Unique indexes include the primary key's and every UNIQUE constraint's, which SQL Server
        // implements as one. A filtered index is unique only where its filter holds, so it comes
        // back as its own kind, for the reader to say it was not carried. A disabled index or
        // check enforces nothing, and is left out.
        const string sql = """
            SELECT  i.name AS [Name],
                    CASE WHEN i.is_primary_key = 1 THEN 'P' WHEN i.has_filter = 1 THEN 'F' ELSE 'U' END AS [Kind],
                    c.name AS [ColumnName],
                    CAST(i.filter_definition AS nvarchar(max)) AS [Definition],
                    ic.key_ordinal AS [Ordinal]
            FROM    sys.indexes i
            JOIN    sys.index_columns ic
                      ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
            JOIN    sys.columns c
                      ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE   i.object_id = OBJECT_ID(QUOTENAME(@schema) + '.' + QUOTENAME(@table))
              AND   i.is_unique = 1 AND i.is_disabled = 0
            UNION ALL
            SELECT  k.name, 'C', c.name, k.definition, 0
            FROM    sys.check_constraints k
            LEFT JOIN sys.columns c
                      ON c.object_id = k.parent_object_id AND c.column_id = k.parent_column_id
            WHERE   k.parent_object_id = OBJECT_ID(QUOTENAME(@schema) + '.' + QUOTENAME(@table))
              AND   k.is_disabled = 0
            ORDER BY [Name], [Ordinal];
            """;

        var constraints = new List<SqlConstraintInfo>();

        foreach (var group in Query(connectionString, sql, ("@schema", schema), ("@table", table))
                     .GroupBy(row => (Name: (string)row["Name"], Kind: (string)row["Kind"])))
        {
            var kind = group.Key.Kind switch
            {
                "P" => SqlConstraintKind.PrimaryKey,
                "U" => SqlConstraintKind.Unique,
                "F" => SqlConstraintKind.FilteredUnique,
                _ => SqlConstraintKind.Check
            };

            constraints.Add(new SqlConstraintInfo(
                group.Key.Name,
                kind,
                [.. group.Select(row => row["ColumnName"] as string).OfType<string>()],
                group.First()["Definition"] as string));
        }

        return constraints;
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
                row[i] = reader.IsDBNull(i) ? null : SqlValueText.Render(reader.GetValue(i));

            rows.Add(row);
        }

        return rows;
    }

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
