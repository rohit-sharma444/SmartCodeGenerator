namespace DocumentIndexer;

using DocumentFormat.OpenXml.Packaging;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using UglyToad.PdfPig;

public static class DocumentParser
{
    public static string ExtractText(string filePath)
    {
        string extension =
            Path.GetExtension(filePath).ToLower();

        return extension switch
        {
            ".txt" => ExtractTxt(filePath),
            ".pdf" => ExtractPdf(filePath),
            ".docx" => ExtractDocx(filePath),
            ".xls" => ExtractExcel(filePath),
            ".xlsx" => ExtractExcel(filePath),
            _ => ""
        };
    }

    private static string ExtractTxt(string filePath)
        => File.ReadAllText(filePath);

    private static string ExtractPdf(string filePath)
    {
        using var document = PdfDocument.Open(filePath);

        var text = new System.Text.StringBuilder();

        foreach (var page in document.GetPages())
        {
            text.AppendLine(page.Text);
        }

        return text.ToString();
    }

    private static string ExtractDocx(string filePath)
    {
        using var document =
            WordprocessingDocument.Open(filePath, false);

        return document.MainDocumentPart?
            .Document.Body?
            .InnerText ?? "";
    }

    private static string ExtractExcel(string filePath)
    {
        using var stream = File.OpenRead(filePath);

        IWorkbook workbook =
            filePath.EndsWith(".xls")
            ? new HSSFWorkbook(stream)
            : new XSSFWorkbook(stream);

        var sb = new System.Text.StringBuilder();

        for (int i = 0; i < workbook.NumberOfSheets; i++)
        {
            var sheet = workbook.GetSheetAt(i);

            for (int row = 0;
                 row <= sheet.LastRowNum;
                 row++)
            {
                var currentRow = sheet.GetRow(row);

                if (currentRow == null)
                    continue;

                foreach (var cell in currentRow.Cells)
                {
                    sb.Append(cell.ToString());
                    sb.Append(' ');
                }
            }
        }

        return sb.ToString();
    }
}