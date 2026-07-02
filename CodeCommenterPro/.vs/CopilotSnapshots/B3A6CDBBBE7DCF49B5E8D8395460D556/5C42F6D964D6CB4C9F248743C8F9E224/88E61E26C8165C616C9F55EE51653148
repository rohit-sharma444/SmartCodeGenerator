namespace CodeCommenterPro.Roslyn;

using Microsoft.CodeAnalysis;

public static class BusinessRuleAnalyzer
{
    public static string? Analyze(SyntaxNode node)
    {
        string text = node.ToString();

        // Consult JSON-configured rules first
        try
        {
            var rules = CodeCommenterPro.Helpers.RuleLoader.Load();
            foreach (var br in rules.BusinessRules)
            {
                if (string.IsNullOrWhiteSpace(br.Match))
                    continue;

                if (System.Text.RegularExpressions.Regex.IsMatch(text, br.Match))
                    return br.Description;
            }
        }
        catch
        {
            // ignore errors and fall back to heuristics
        }

        // Fallback heuristics
        if (text.Contains("throw") && text.Contains("null"))
            return "Validate required input before processing.";

        if (text.Contains("Role") && text.Contains("Admin"))
            return "Allow operation only for administrators.";

        if (text.Contains("Quantity") && text.Contains("< 0"))
            return "Prevent inventory quantity from becoming negative.";

        if (text.Contains("Discount"))
            return "Apply discount calculation according to business rules.";

        if (text.Contains("AnalysisCode"))
            return "Maintain Sage analysis code mapping.";

        if (text.Contains("StockItem"))
            return "Process inventory item information.";

        if (text.Contains("Attachment"))
            return "Manage document attachments associated with the record.";

        return null;
    }
}