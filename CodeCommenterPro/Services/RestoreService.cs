namespace CodeCommenterPro.Services;

using System.Text.Json;
using CodeCommenterPro.Models;

public class RestoreService
{
    private const string BackupFolderName =
        ".CodeCommenterBackups";


    public void Restore(
    string rootFolder)
    {
        string backupFolder =
            Path.Combine(
                rootFolder,
                BackupFolderName);

        string manifestFile =
            Path.Combine(
                backupFolder,
                "backup_manifest.json");

        if (!File.Exists(manifestFile))
        {
            Console.WriteLine(
                "Backup manifest not found.");

            return;
        }

        List<BackupRecord> records;

        try
        {
            string json =
                File.ReadAllText(
                    manifestFile);

            if (string.IsNullOrWhiteSpace(json))
            {
                Console.WriteLine(
                    "Backup manifest is empty.");

                return;
            }

            records =
                JsonSerializer.Deserialize<
                    List<BackupRecord>>(
                    json)
                ?? new List<BackupRecord>();
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"Invalid manifest: {ex.Message}");

            return;
        }

        var latestRecords =
            records
            .GroupBy(x => x.OriginalFile)
            .Select(g =>
                g.OrderByDescending(
                    x => x.CreatedOn)
                 .First())
            .ToList();

        foreach (var record in latestRecords)
        {
            try
            {
                if (!File.Exists(record.BackupFile))
                {
                    continue;
                }

                File.Copy(
                    record.BackupFile,
                    record.OriginalFile,
                    true);

                Console.WriteLine(
                    $"Restored: {record.OriginalFile}");
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"Failed: {record.OriginalFile}");

                Console.WriteLine(
                    ex.Message);
            }
        }
    }


}