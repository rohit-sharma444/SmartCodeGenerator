using DomainDiscovery.Wikipedia.Dump;
using Xunit;

namespace DomainDiscovery.Tests.Wikipedia.Dump;

public sealed class StoreUpdateStatusFileTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "scg-status-" + Guid.NewGuid().ToString("N"));

    private string Path_ => System.IO.Path.Combine(_directory, "update-status.json");

    public StoreUpdateStatusFileTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { /* best effort */ }
    }

    private static StoreUpdateStatus Sample() => new(
        StoreUpdateState.Applying, 43, 6, 20260718,
        new DateTimeOffset(2026, 7, 23, 4, 11, 2, TimeSpan.Zero), null, "1.0");

    [Fact]
    public void Write_ThenRead_RoundTripsEveryField()
    {
        var file = new StoreUpdateStatusFile(Path_);
        file.Write(Sample());

        Assert.Equal(Sample(), file.Read());
    }

    [Fact]
    public void Read_MissingFile_IsUnknown()
        => Assert.Equal(StoreUpdateStatus.Unknown, new StoreUpdateStatusFile(Path_).Read());

    [Fact]
    public void Read_EmptyFile_IsUnknown()
    {
        File.WriteAllText(Path_, string.Empty);
        Assert.Equal(StoreUpdateStatus.Unknown, new StoreUpdateStatusFile(Path_).Read());
    }

    [Fact]
    public void Read_MalformedJson_IsUnknown()
    {
        File.WriteAllText(Path_, "{ not json at all");
        Assert.Equal(StoreUpdateStatus.Unknown, new StoreUpdateStatusFile(Path_).Read());
    }

    [Fact]
    public void Read_TruncatedJson_IsUnknown()
    {
        // A reader must survive catching a writer mid-flight, even though Write is atomic.
        File.WriteAllText(Path_, "{ \"state\": \"Applying\", \"percent\": 4");
        Assert.Equal(StoreUpdateStatus.Unknown, new StoreUpdateStatusFile(Path_).Read());
    }

    [Fact]
    public void Write_ToAnUnwritableLocation_DoesNotThrow()
    {
        // Status is a convenience, never a reason to fail an update.
        var file = new StoreUpdateStatusFile(
            System.IO.Path.Combine(_directory, "no-such-dir", "update-status.json"));

        file.Write(Sample());
        Assert.Equal(StoreUpdateStatus.Unknown, file.Read());
    }

    [Fact]
    public void Write_OverwritesAPreviousStatus()
    {
        var file = new StoreUpdateStatusFile(Path_);
        file.Write(Sample());
        var later = Sample() with { Percent = 91, State = StoreUpdateState.Idle };
        file.Write(later);

        Assert.Equal(later, file.Read());
    }

    [Fact]
    public void BesideStore_PutsTheStatusNextToTheDatabase()
        => Assert.Equal(
            System.IO.Path.Combine(@"D:\Dump", "update-status.json"),
            StoreUpdateStatusFile.BesideStore(@"D:\Dump\enwiki-full.db"));
}
