using System.Data.Common;

namespace DataIngestion;

/// <summary>
/// What can be read off a SQL Server connection string without opening it.
/// </summary>
/// <remarks>
/// Shared by the console interview and <see cref="InlineDataSource"/>, which both have to fill in
/// <see cref="DataSourceSpec.SqlDatabase"/> — <c>BuildAppCommand.AppNameFor</c> names the
/// generated app from it when the run carried no description.
/// <para>
/// <see cref="DbConnectionStringBuilder"/> rather than splitting on <c>;</c>: it is in the BCL, it
/// handles quoting, and it accepts both spellings.
/// </para>
/// </remarks>
public static class SqlConnectionInfo
{
    /// <summary>The database <paramref name="connectionString"/> names, or null when it names
    /// none — or is not a connection string at all.</summary>
    public static string? DatabaseOf(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return null;

        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };

            foreach (var key in new[] { "initial catalog", "database" })
                if (builder.ContainsKey(key) && builder[key] is string name && name.Length > 0)
                    return name;
        }
        catch (ArgumentException)
        {
            // Not a connection string. Nothing to name the app after; AppNameFor falls back.
        }

        return null;
    }
}
