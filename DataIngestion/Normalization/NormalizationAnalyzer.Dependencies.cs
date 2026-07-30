using DataIngestion.Schema;

namespace DataIngestion.Normalization;

public static partial class NormalizationAnalyzer
{
    private static IEnumerable<NormalizationFinding> FunctionalDependencies(
        TabularTable table, List<string> notes)
        => [];

    private static IEnumerable<NormalizationFinding> DerivedColumns(TabularTable table)
        => [];
}
