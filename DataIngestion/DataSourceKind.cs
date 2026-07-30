namespace DataIngestion;

/// <summary>The data formats the interview offers. Excel and Sql are declared here — and named
/// at the prompt — before their readers exist, so the menu the user sees is the whole menu and
/// choosing one gets a straight answer rather than a missing option.</summary>
public enum DataSourceKind
{
    Sql,
    Excel,
    Txt,
    Json,
    Csv
}
