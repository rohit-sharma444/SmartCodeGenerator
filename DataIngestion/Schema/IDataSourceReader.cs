namespace DataIngestion.Schema;

/// <summary>
/// The seam every format implements. One method, one return shape — adding Excel or SQL Server
/// is one more implementation of this and no change anywhere downstream.
/// </summary>
public interface IDataSourceReader
{
    /// <summary>Reads <paramref name="spec"/> into one or more tables.</summary>
    /// <exception cref="FileNotFoundException">The location does not exist.</exception>
    /// <exception cref="InvalidDataException">
    /// The source exists but carries nothing a domain model can be built from. The message says
    /// what was wrong in terms the user can act on.
    /// </exception>
    IReadOnlyList<TabularTable> Read(DataSourceSpec spec);
}
