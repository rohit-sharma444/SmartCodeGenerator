namespace DataIngestion;

/// <summary>The data formats the interview offers. Sql is declared here — and named at the
/// prompt — before its reader exists, so the menu the user sees is the whole menu and choosing
/// it gets a straight answer rather than a missing option.</summary>
public enum DataSourceKind
{
    Sql,
    Excel,
    Txt,
    Json,
    Csv
}
