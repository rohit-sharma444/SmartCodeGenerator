public sealed class DeletedClassInfo
{
    public string ClassName { get; set; } = string.Empty;

    public string OriginalPath { get; set; } = string.Empty;

    public string DeletedPath { get; set; } = string.Empty;

    /// <summary>When it was deleted, in UTC.</summary>
    public DateTime DeletedAt { get; set; }

    /// <summary>The class's other files that were deleted with it: its partials, such as
    /// <c>Customer.Validation.cs</c>.</summary>
    public List<DeletedClassPart> Parts { get; set; } = [];
}

/// <summary>One more file of a deleted class: where it was, and where its copy is kept.</summary>
public sealed class DeletedClassPart
{
    public string OriginalPath { get; set; } = string.Empty;

    public string DeletedPath { get; set; } = string.Empty;
}