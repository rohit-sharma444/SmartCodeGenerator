public sealed class DeletedClassInfo
{
    public string ClassName { get; set; } = string.Empty;

    public string OriginalPath { get; set; } = string.Empty;

    public string DeletedPath { get; set; } = string.Empty;

    public DateTime DeletedAt { get; set; }
}