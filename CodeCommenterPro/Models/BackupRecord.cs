namespace CodeCommenterPro.Models;

public class BackupRecord
{
    public string OriginalFile { get; set; }
        = string.Empty;

    public string BackupFile { get; set; }
        = string.Empty;

    public DateTime CreatedOn { get; set; }
}