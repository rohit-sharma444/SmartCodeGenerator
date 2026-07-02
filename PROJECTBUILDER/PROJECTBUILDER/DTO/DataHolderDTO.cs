namespace PROJECTBUILDER.DTO;
/// <summary>
/// Represents Data Holder DTO.
/// </summary>

public class DataHolderDTO
{
    public List<MetaDataDTO> Metadata { get; set; }
    public (Type, List<object>) BusinessData { get; set; }
    public List<ValidatorDTO> Validators { get; set; }
}