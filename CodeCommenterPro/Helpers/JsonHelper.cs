namespace CodeCommenterPro.Helpers;

using System.Text.Json;

public static class JsonHelper
{
    public static T Load<T>(
        string filePath)
        where T : new()
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new T();
            }

            string json =
                File.ReadAllText(
                    filePath);

            if (string.IsNullOrWhiteSpace(json))
            {
                return new T();
            }

            return JsonSerializer.Deserialize<T>(
                       json)
                   ?? new T();
        }
        catch
        {
            return new T();
        }
    }

    public static void Save<T>(
        string filePath,
        T data)
    {
        string json =
            JsonSerializer.Serialize(
                data,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

        File.WriteAllText(
            filePath,
            json);
    }
}