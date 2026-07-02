using CodeCommenterPro.Models;
using System.Text;

namespace CodeCommenterPro.Services;

public class HtmlReportService
{
    public void Generate(
        CoverageReport report,
        string outputFile)
    {
        var html =
            new StringBuilder();

        html.AppendLine("<html>");
        html.AppendLine("<head>");
        html.AppendLine("<title>Coverage Report</title>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");

        html.AppendLine(
            $"<h2>Coverage : {report.Coverage}%</h2>");

        html.AppendLine(
            $"<p>Files : {report.TotalFiles}</p>");

        html.AppendLine($"<p>Classes : {report.TotalClasses} (Documented: {report.DocumentedClasses})</p>");

        html.AppendLine($"<p>Methods : {report.TotalMethods} (Documented: {report.DocumentedMethods})</p>");

        html.AppendLine($"<p>Properties : {report.TotalProperties} (Documented: {report.DocumentedProperties})</p>");

        html.AppendLine($"<p>Constructors : {report.TotalConstructors} (Documented: {report.DocumentedConstructors})</p>");

        html.AppendLine("</body>");
        html.AppendLine("</html>");

        File.WriteAllText(
            outputFile,
            html.ToString());
    }
}