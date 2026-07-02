namespace CodeCommenterPro.Models;

public class CoverageReport
{
    public int TotalFiles { get; set; }

    public int TotalClasses { get; set; }

    public int TotalMethods { get; set; }

    public int TotalProperties { get; set; }

    public int TotalConstructors { get; set; }

    public int DocumentedClasses { get; set; }

    public int DocumentedMethods { get; set; }

    public int DocumentedProperties { get; set; }

    public int DocumentedConstructors { get; set; }

    public double Coverage
    {
        get
        {
            int total =
                TotalClasses +
                TotalMethods +
                TotalProperties +
                TotalConstructors;

            int documented =
                DocumentedClasses +
                DocumentedMethods +
                DocumentedProperties +
                DocumentedConstructors;

            return total == 0
                ? 0
                : Math.Round(
                    documented * 100.0 / total,
                    2);
        }
    }
}