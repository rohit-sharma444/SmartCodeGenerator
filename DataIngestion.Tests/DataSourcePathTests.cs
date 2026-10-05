using DataIngestion.Schema.Readers;
using Xunit;

namespace DataIngestion.Tests;

/// <summary>
/// A data file named relative is found in the workspace, not wherever the process was started.
/// </summary>
/// <remarks>
/// <c>build app from csv books.csv</c> piped with PROJECTPATH set said "No file at books.csv"
/// whenever the console was started from any other folder: the readers opened the bare name
/// against the process's current directory.
/// </remarks>
public sealed class DataSourcePathTests : IDisposable
{
    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "dsp" + Guid.NewGuid().ToString("N")[..8]);

    public DataSourcePathTests()
    {
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void A_relative_file_resolves_against_the_workspace()
    {
        var books = Path.Combine(_workspace, "books.csv");
        File.WriteAllText(books, "Title,Pages\nDune,412\n");

        Assert.Equal(books, DataSourcePath.Resolve("books.csv", _workspace));
    }

    [Fact]
    public void A_relative_file_in_a_subfolder_resolves_against_the_workspace()
    {
        var data = Path.Combine(_workspace, "data");
        Directory.CreateDirectory(data);
        var books = Path.Combine(data, "books.csv");
        File.WriteAllText(books, "Title\nDune\n");

        Assert.Equal(books, DataSourcePath.Resolve(Path.Combine("data", "books.csv"), _workspace));
    }

    [Fact]
    public void An_absolute_path_is_kept_as_typed()
    {
        var elsewhere = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");

        Assert.Equal(elsewhere, DataSourcePath.Resolve(elsewhere, _workspace));
    }

    /// <summary>A missing file is reported at the workspace path, the one the user meant.</summary>
    [Fact]
    public void A_file_found_nowhere_is_named_at_the_workspace()
    {
        var name = "missing-" + Guid.NewGuid().ToString("N") + ".csv";

        Assert.Equal(Path.Combine(_workspace, name), DataSourcePath.Resolve(name, _workspace));
    }

    /// <summary>The current directory still counts when the workspace has no such file, so a
    /// path that worked before this change keeps working.</summary>
    [Fact]
    public void A_file_only_in_the_current_directory_is_still_found()
    {
        var name = "cwd-" + Guid.NewGuid().ToString("N") + ".csv";
        var inCwd = Path.GetFullPath(name);
        File.WriteAllText(inCwd, "Title\nDune\n");

        try
        {
            Assert.Equal(inCwd, DataSourcePath.Resolve(name, _workspace));
        }
        finally
        {
            File.Delete(inCwd);
        }
    }

    [Fact]
    public void A_sql_connection_string_is_never_treated_as_a_path()
    {
        var spec = new DataSourceSpec(
            DataSourceKind.Sql, "Server=.;Database=Shop;Trusted_Connection=True", "Shop");

        Assert.Same(spec, DataSourcePath.Resolve(spec, _workspace));
    }

    [Fact]
    public void A_file_spec_keeps_its_kind_and_gains_the_workspace_path()
    {
        var books = Path.Combine(_workspace, "books.csv");
        File.WriteAllText(books, "Title\nDune\n");

        var resolved = DataSourcePath.Resolve(new DataSourceSpec(DataSourceKind.Csv, "books.csv"), _workspace);

        Assert.Equal(new DataSourceSpec(DataSourceKind.Csv, books), resolved);
    }

    [Fact]
    public void The_interview_finds_a_relative_file_in_the_workspace()
    {
        var books = Path.Combine(_workspace, "books.csv");
        File.WriteAllText(books, "Title\nDune\n");
        var writer = new StringWriter();

        var outcome = DataSourceInterview.Resolve(
            new StringReader("y\ncsv\nbooks.csv\n"), writer, workspaceRoot: _workspace);

        Assert.Equal(InterviewDecision.Source, outcome.Decision);
        Assert.Equal(books, outcome.Source!.Location);
    }
}
