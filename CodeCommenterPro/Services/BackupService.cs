namespace CodeCommenterPro.Services;

using CodeCommenterPro.Models;
using System.Text.Json;

public class BackupService
{
    private const string BackupFolderName =
        ".CodeCommenterBackups";

    public void CreateBackup(
        string rootFolder,
        string filePath)
    {
        string rel = Path.GetRelativePath(rootFolder, filePath);
        string dest = Path.Combine(rootFolder, "backups", rel + ".bak");
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(filePath, dest, true);
    }


    private static void SaveManifest(
    string backupFolder,
    string originalFile,
    string backupFile)
    {
        string manifestFile =
            Path.Combine(
                backupFolder,
                "backup_manifest.json");

        List<BackupRecord> records =
            LoadManifest(manifestFile);

        records.Add(
            new BackupRecord
            {
                OriginalFile = originalFile,
                BackupFile = backupFile,
                CreatedOn = DateTime.Now
            });

        string json =
            JsonSerializer.Serialize(
                records,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

        File.WriteAllText(
            manifestFile,
            json);
    }


    private static List<BackupRecord>
    LoadManifest(
    string manifestFile)
    {
        try
        {
            if (!File.Exists(manifestFile))
            {
                return new List<BackupRecord>();
            }

            string json =
                File.ReadAllText(
                    manifestFile);

            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<BackupRecord>();
            }

            return JsonSerializer.Deserialize<
                       List<BackupRecord>>(
                       json)
                   ?? new List<BackupRecord>();
        }
        catch
        {
            return new List<BackupRecord>();
        }
    }

}