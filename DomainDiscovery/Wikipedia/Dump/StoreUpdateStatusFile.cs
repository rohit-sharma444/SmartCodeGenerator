using System.Text.Json;
using System.Text.Json.Serialization;

namespace DomainDiscovery.Wikipedia.Dump;

/// <summary>
/// Publishes and reads <see cref="StoreUpdateStatus"/> as a small JSON file beside the store.
/// </summary>
/// <remarks>
/// <b>Neither direction may throw.</b> A write happens on the update's own thread, ultimately from
/// a progress callback, so a failing write must never disturb the apply. A read happens in the CLI
/// at startup, where an unreadable status must not stop the prompt appearing.
/// <para>
/// Writes are atomic — a temporary file in the same directory, then an overwriting move — so a
/// reader can never observe a half-written document. The tolerant read is belt-and-braces: it also
/// covers a file left behind by an older or newer version of this record.
/// </para>
/// </remarks>
public sealed class StoreUpdateStatusFile
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public StoreUpdateStatusFile(string path) => _path = path;

    /// <summary>The conventional status location for a given store: alongside the database.</summary>
    public static string BesideStore(string databasePath)
        => Path.Combine(Path.GetDirectoryName(databasePath) ?? ".", "update-status.json");

    public void Write(StoreUpdateStatus status)
    {
        try
        {
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(status, Json));
            File.Move(temporary, _path, overwrite: true);
        }
        catch
        {
            // Status is a convenience; the store's own dump date is the truth.
        }
    }

    public StoreUpdateStatus Read()
    {
        try
        {
            if (!File.Exists(_path))
                return StoreUpdateStatus.Unknown;

            return JsonSerializer.Deserialize<StoreUpdateStatus>(File.ReadAllText(_path), Json)
                ?? StoreUpdateStatus.Unknown;
        }
        catch
        {
            return StoreUpdateStatus.Unknown;
        }
    }
}
