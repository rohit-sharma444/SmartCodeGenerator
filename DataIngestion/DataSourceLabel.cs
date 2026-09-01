namespace DataIngestion;

/// <summary>
/// What to call a data source in a message to the user.
/// </summary>
/// <remarks>
/// A file is named by its file name; a database by the database. The two call sites both did
/// <c>Path.GetFileName(source.Location)</c>, which returns a connection string unchanged — so the
/// discovery and normalization lines printed the whole thing, password included. That was found
/// by running the command against a real database rather than by any test.
/// <para>
/// The database name is also simply the better label: <c>TRADING</c> says where the data came from
/// and a connection string does not.
/// </para>
/// </remarks>
public static class DataSourceLabel
{
    /// <summary>Used when a SQL connection string names no database — it has landed on master,
    /// and there is nothing else to call it that is safe to print.</summary>
    public const string UnnamedDatabase = "the database";

    public static string For(DataSourceSpec source)
        => source.Kind == DataSourceKind.Sql
            ? source.SqlDatabase
              ?? SqlConnectionInfo.DatabaseOf(source.Location)
              ?? UnnamedDatabase
            : Path.GetFileName(source.Location);
}
