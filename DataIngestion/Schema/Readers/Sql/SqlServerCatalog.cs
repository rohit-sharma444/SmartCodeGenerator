namespace DataIngestion.Schema.Readers.Sql;

/// <summary>
/// What is in this database — the question the Data stage asks before the user has picked
/// anything.
/// </summary>
/// <remarks>
/// Separate from <see cref="SqlServerReader"/> because it answers a different question at a
/// different moment and has a different cost. It runs while somebody is waiting to see a list, so
/// it reads no data: a picker that takes thirty seconds to appear reads as a hang.
/// </remarks>
public sealed class SqlServerCatalog(ISqlSchemaSource source)
{
    /// <summary>The base tables the connection can see.</summary>
    /// <exception cref="InvalidDataException">The database holds no base tables.</exception>
    public IReadOnlyList<SqlTableInfo> List(string connectionString)
    {
        var tables = source.Tables(connectionString);

        if (tables.Count == 0)
            throw new InvalidDataException(
                "That connection worked, but the database has no tables to read. Check you named "
                + "the right one — a connection string with no Initial Catalog lands on master.");

        return tables;
    }
}
