namespace DataIngestion.Schema;

/// <summary>
/// One declared foreign key: the column that holds it, and the table and column it points at.
/// </summary>
/// <remarks>
/// Only a source that DECLARES its relationships fills these in — today that is SQL Server alone.
/// A file reader leaves the collection empty, and <c>DataModelBuilder</c> falls back to the
/// <c>&lt;Target&gt;Id</c> convention it has always used.
/// <para>
/// Carried on <see cref="TabularTable"/> rather than through a second reader interface because the
/// table is already the shared currency between every reader and the model builder, and a parallel
/// path would be one more thing every later reader has to choose between.
/// </para>
/// <para>
/// <paramref name="Table"/>, <paramref name="Schema"/> and <paramref name="TargetSchema"/> say
/// which table declares the key and which schema its target is in. A database's list of keys
/// covers every table, and matched by column name alone a key would be handed to every table that
/// happens to have a column of that name. Null when the source cannot say, which matches by name.
/// </para>
/// </remarks>
public sealed record TabularForeignKey(
    string Column,
    string TargetTable,
    string TargetColumn,
    string? Table = null,
    string? Schema = null,
    string? TargetSchema = null);
