using System.Text.Json;

namespace CodeGenerationEngine.Helper;

/// <summary>
/// Helper class to read properties from a file.
/// Supports CSV and JSON formats with intelligent type detection.
/// </summary>
public static class FileReader
{
    public static List<string> ReadFile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        string extension = Path.GetExtension(filePath).ToLower();

        return extension switch
        {
            ".csv" => ReadCsv(filePath),
            ".json" => ReadJson(filePath),
            _ => throw new NotSupportedException($"Unsupported file type: {extension}. Supported: .csv, .json")
        };
    }

    /// <summary>
    /// Reads CSV file and returns property definitions.
    /// 
    /// Supports two formats:
    /// 1. Simple: Name,Age,Salary (types auto-inferred from column names)
    /// 2. Typed: Name:string,Age:int,Salary:decimal (explicit types)
    /// </summary>
    private static List<string> ReadCsv(string filePath)
    {
        var result = new List<string>();
        var lines = File.ReadAllLines(filePath);

        if (lines.Length == 0)
            return result;

        var firstLine = lines[0];

        // Check if first line has type declarations (format: "Name:string,Age:int")
        bool hasTypeInfo = firstLine.Contains(':');

        if (hasTypeInfo)
        {
            // Parse header with explicit type info
            result.AddRange(ParseTypedHeader(firstLine));
        }
        else
        {
            // Parse simple header (just column names)
            result.AddRange(ParseSimpleHeader(firstLine));
        }

        // Optional: Could validate data rows here for type consistency
        // For now, we just return the column definitions

        return result;
    }

    /// <summary>
    /// Parses CSV header with explicit type declarations.
    /// Format: "Name:string,Age:int,Salary:decimal"
    /// </summary>
    private static List<string> ParseTypedHeader(string headerLine)
    {
        var result = new List<string>();
        var columns = headerLine.Split(',');

        foreach (var column in columns)
        {
            var trimmed = column.Trim();

            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            // Check if this column has type info
            if (trimmed.Contains(':'))
            {
                var parts = trimmed.Split(':', 2);  // Split on first colon only
                if (parts.Length == 2)
                {
                    var columnName = parts[0].Trim();
                    var columnType = parts[1].Trim();

                    // Validate and normalize the type
                    if (!string.IsNullOrWhiteSpace(columnName) && !string.IsNullOrWhiteSpace(columnType))
                    {
                        // Add as "ColumnName:Type" format
                        result.Add($"{columnName}:{columnType}");
                    }
                }
            }
            else
            {
                // No type specified - will be inferred later
                result.Add(trimmed);
            }
        }

        if (result.Count == 0)
        {
            throw new InvalidOperationException("CSV header is empty or invalid. " +
                "Format: ColumnName:type or ColumnName (for auto-detection)");
        }

        return result;
    }

    /// <summary>
    /// Parses simple CSV header without type declarations.
    /// Format: "Name,Age,Salary"
    /// Types will be inferred from column names.
    /// </summary>
    private static List<string> ParseSimpleHeader(string headerLine)
    {
        var result = new List<string>();
        var columns = headerLine.Split(',');

        foreach (var column in columns)
        {
            var trimmed = column.Trim();

            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                result.Add(trimmed);
            }
        }

        if (result.Count == 0)
        {
            throw new InvalidOperationException("CSV header is empty.");
        }

        return result;
    }

    /// <summary>
    /// Reads JSON file and extracts all values as a flat list.
    /// </summary>
    private static List<string> ReadJson(string filePath)
    {
        try
        {
            string json = File.ReadAllText(filePath);
            using JsonDocument document = JsonDocument.Parse(json);
            List<string> result = [];
            ExtractValues(document.RootElement, result);
            return result;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid JSON file format: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Recursively extracts values from JSON document.
    /// </summary>
    private static void ExtractValues(JsonElement element, List<string> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    ExtractValues(property.Value, result);
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    ExtractValues(item, result);
                }
                break;

            default:
                string value = element.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    result.Add(value);
                }
                break;
        }
    }
}
