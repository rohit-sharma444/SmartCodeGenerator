using Xunit;

namespace DomainDiscovery.Tests.Wikipedia.Dump;

/// <summary>
/// Serializes every test class that owns a SQLite store on disk.
/// </summary>
/// <remarks>
/// Each of those classes calls <c>SqliteConnection.ClearAllPools()</c> in its Dispose, because
/// Windows will not delete the database file while a pooled connection still holds it open. That
/// call is process-wide, and xunit runs test classes in parallel by default: one class's teardown
/// could close a connection another class was mid-read on. The read then failed, the never-throw
/// contract turned the SqliteException into an empty result, and a content assertion failed —
/// intermittently, in roughly one full-suite run in five.
/// <para>
/// Sharing one collection makes these classes run one after another, so no Dispose can ever land
/// inside another class's read.
/// </para>
/// </remarks>
[CollectionDefinition(Name)]
public sealed class SqliteStoreCollection
{
    public const string Name = "SQLite store";
}
