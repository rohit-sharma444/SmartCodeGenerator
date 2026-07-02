namespace PROJECTBUILDER.DTO;

/// <summary>
/// It holds meta data information like class fields and their data types.
/// </summary>
public class MetaDataDTO
{
    public string Field { get; set; }

    public Type FieldType { get; set; }

    public MetaDataDTO()
    {
    }
}