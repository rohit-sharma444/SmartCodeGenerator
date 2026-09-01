# `build app ... from sql` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Point `build app` at a SQL Server connection string, pick tables from what it finds, and get a model whose column types and relationships come from the database rather than from a guess.

**Architecture:** Two phases over one seam. `ISqlSchemaSource` is the whole of this feature's contact with ADO.NET — one thin adapter implements it over `Microsoft.Data.SqlClient`, and everything above it is tested against a fake. `SqlServerCatalog` answers "what is in here?" cheaply for the picker; `SqlServerReader` implements the existing `IDataSourceReader` for the tables that were picked. Foreign keys travel on `TabularTable` and are projected into `CandidateRelationship` by `DataModelBuilder`.

**Tech Stack:** .NET 10, `Microsoft.Data.SqlClient` (new), xunit (DataIngestion.Tests), MSTest + bUnit (CodeGenerator.Tests).

**Spec:** `docs/superpowers/specs/2026-09-01-build-app-sql-data-source-design.md`

## Global Constraints

- Target framework `net10.0`, `Nullable=enable`, `ImplicitUsings=enable` (from `Directory.Build.props`). Do not add properties there.
- **The only permitted new package is `Microsoft.Data.SqlClient`, on `DataIngestion` only.** No other dependency may be added by any task.
- Generated model types are limited to exactly five: `string`, `int`, `decimal`, `bool`, `DateTime` (`ModelEditor.SupportedTypes`). Nothing may produce a sixth.
- **Test framework differs per project.** `DataIngestion.Tests` is **xunit** (`[Fact]`, `Assert.Equal(expected, actual)`). `CodeGenerator.Tests` is **MSTest** (`[TestClass]`, `[TestMethod]`, `Assert.AreEqual`) plus bUnit. Using the wrong one will not compile.
- MSTest analyzer rule MSTEST0037 rejects `Assert.AreEqual(n, x.Count)` — use `Assert.HasCount(n, x)`.
- CI runs exactly: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-restore -warnaserror` then `dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build`. A warning is an error there.
- `docs/commands.md` and the manual's chapter-18 appendix are **generated** from `CommandDictionary`. Never hand-edit either. Regenerate both (Task 10).
- Branch is `sql-data-source`. Commit after every task.

---

### Task 1: Foreign keys can travel on a table

**Files:**
- Create: `DataIngestion/Schema/TabularForeignKey.cs`
- Modify: `DataIngestion/Schema/TabularTable.cs`
- Test: `DataIngestion.Tests/Schema/TabularForeignKeyTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `public sealed record TabularForeignKey(string Column, string TargetTable, string TargetColumn)` in namespace `DataIngestion.Schema`; `List<TabularForeignKey> ForeignKeys { get; }` on `TabularTable`.

- [ ] **Step 1: Write the failing test**

```csharp
using DataIngestion.Schema;
using Xunit;

namespace DataIngestion.Tests.Schema;

public class TabularForeignKeyTests
{
    // Every other reader leaves this empty, so the default has to be a usable empty list
    // rather than null - a reader that never heard of foreign keys must not force a null check
    // on DataModelBuilder.
    [Fact]
    public void A_table_starts_with_no_foreign_keys()
    {
        var table = new TabularTable { Name = "Orders" };

        Assert.Empty(table.ForeignKeys);
    }

    [Fact]
    public void A_foreign_key_names_its_column_and_what_it_points_at()
    {
        var table = new TabularTable { Name = "Orders" };

        table.ForeignKeys.Add(new TabularForeignKey("CustomerID", "Customers", "CustomerID"));

        var key = Assert.Single(table.ForeignKeys);
        Assert.Equal("CustomerID", key.Column);
        Assert.Equal("Customers", key.TargetTable);
        Assert.Equal("CustomerID", key.TargetColumn);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~TabularForeignKeyTests"`
Expected: FAIL to compile — `TabularForeignKey` does not exist, `TabularTable` has no `ForeignKeys`.

- [ ] **Step 3: Write minimal implementation**

`DataIngestion/Schema/TabularForeignKey.cs`:

```csharp
namespace DataIngestion.Schema;

/// <summary>
/// One declared foreign key: the column that holds it, and the table and column it points at.
/// </summary>
/// <remarks>
/// Only a source that DECLARES its relationships fills these in — today that is SQL Server alone.
/// A file reader leaves the collection empty, and <c>DataModelBuilder</c> falls back to the
/// <c>&lt;Target&gt;Id</c> convention it has always used.
/// <para>
/// Carried on <see cref="TabularTable"/> rather than through a second reader interface because
/// the table is already the shared currency between every reader and the model builder, and a
/// parallel path would be one more thing every later reader has to choose between.
/// </para>
/// </remarks>
public sealed record TabularForeignKey(string Column, string TargetTable, string TargetColumn);
```

In `DataIngestion/Schema/TabularTable.cs`, after the `Rows` property:

```csharp
    /// <summary>The foreign keys the source declared, or empty when it declares none.</summary>
    public List<TabularForeignKey> ForeignKeys { get; } = new();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~TabularForeignKeyTests"`
Expected: PASS, 2 tests.

- [ ] **Step 5: Commit**

```bash
git add DataIngestion/Schema/TabularForeignKey.cs DataIngestion/Schema/TabularTable.cs DataIngestion.Tests/Schema/TabularForeignKeyTests.cs
git commit -m "feat: a table can carry the foreign keys its source declared"
```

---

### Task 2: SQL types map to the five the generator has

**Files:**
- Create: `DataIngestion/Schema/Readers/SqlTypeMap.cs`
- Test: `DataIngestion.Tests/Schema/Readers/SqlTypeMapTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `public static class SqlTypeMap` in `DataIngestion.Schema.Readers`, with
  `public static string ToModelType(string sqlType)` and
  `public static string? LossNote(string sqlType, string columnName)`.

**Why `LossNote` exists:** the generator has five types, so `bigint` narrows to `int` and `uniqueidentifier` flattens to `string`. Both are real losses. This codebase's standing rule is that imported data must not be quietly changed, so the loss is returned as a sentence for the caller to show, not swallowed.

- [ ] **Step 1: Write the failing test**

```csharp
using DataIngestion.Schema.Readers;
using Xunit;

namespace DataIngestion.Tests.Schema.Readers;

public class SqlTypeMapTests
{
    [Theory]
    [InlineData("int")]
    [InlineData("smallint")]
    [InlineData("tinyint")]
    [InlineData("bigint")]
    public void Whole_numbers_become_int(string sqlType)
        => Assert.Equal("int", SqlTypeMap.ToModelType(sqlType));

    [Theory]
    [InlineData("decimal")]
    [InlineData("numeric")]
    [InlineData("money")]
    [InlineData("smallmoney")]
    [InlineData("float")]
    [InlineData("real")]
    public void Fractional_numbers_become_decimal(string sqlType)
        => Assert.Equal("decimal", SqlTypeMap.ToModelType(sqlType));

    [Fact]
    public void Bit_becomes_bool()
        => Assert.Equal("bool", SqlTypeMap.ToModelType("bit"));

    [Theory]
    [InlineData("date")]
    [InlineData("datetime")]
    [InlineData("datetime2")]
    [InlineData("smalldatetime")]
    [InlineData("datetimeoffset")]
    [InlineData("time")]
    public void The_date_family_becomes_DateTime(string sqlType)
        => Assert.Equal("DateTime", SqlTypeMap.ToModelType(sqlType));

    [Theory]
    [InlineData("nvarchar")]
    [InlineData("varchar")]
    [InlineData("char")]
    [InlineData("text")]
    [InlineData("uniqueidentifier")]
    [InlineData("varbinary")]
    [InlineData("sql_variant")]
    [InlineData("some_type_that_does_not_exist")]
    public void Everything_else_becomes_string(string sqlType)
        => Assert.Equal("string", SqlTypeMap.ToModelType(sqlType));

    // INFORMATION_SCHEMA reports lower case, but nothing guarantees a caller passes it through
    // untouched.
    [Fact]
    public void The_sql_type_is_matched_however_it_is_cased()
        => Assert.Equal("int", SqlTypeMap.ToModelType("BigInt"));

    [Fact]
    public void Narrowing_a_bigint_is_reported()
    {
        var note = SqlTypeMap.LossNote("bigint", "OrderId");

        Assert.NotNull(note);
        Assert.Contains("OrderId", note);
        Assert.Contains("bigint", note);
    }

    [Fact]
    public void Flattening_a_uniqueidentifier_is_reported()
    {
        var note = SqlTypeMap.LossNote("uniqueidentifier", "RowGuid");

        Assert.NotNull(note);
        Assert.Contains("RowGuid", note);
    }

    // The overwhelmingly common case. A note for every column would be noise, and noise is how a
    // warning stops being read.
    [Fact]
    public void A_type_that_maps_faithfully_says_nothing()
        => Assert.Null(SqlTypeMap.LossNote("nvarchar", "Name"));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~SqlTypeMapTests"`
Expected: FAIL to compile — `SqlTypeMap` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace DataIngestion.Schema.Readers;

/// <summary>
/// SQL Server's type for a column, as one of the five types the generator can emit.
/// </summary>
/// <remarks>
/// A table, not inference. Every other reader hands strings to <see cref="ColumnTypeInference"/>
/// and asks what they were, which is where the leading-zero and thousands-separator corruptions
/// come from. A database column has a declared type, so this path never asks the question.
/// </remarks>
public static class SqlTypeMap
{
    private static readonly Dictionary<string, string> Types =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["int"] = "int",
            ["smallint"] = "int",
            ["tinyint"] = "int",
            ["bigint"] = "int",

            ["decimal"] = "decimal",
            ["numeric"] = "decimal",
            ["money"] = "decimal",
            ["smallmoney"] = "decimal",
            ["float"] = "decimal",
            ["real"] = "decimal",

            ["bit"] = "bool",

            ["date"] = "DateTime",
            ["datetime"] = "DateTime",
            ["datetime2"] = "DateTime",
            ["smalldatetime"] = "DateTime",
            ["datetimeoffset"] = "DateTime",
            ["time"] = "DateTime",
        };

    /// <summary>
    /// The model type for <paramref name="sqlType"/>. Anything unrecognised is
    /// <c>string</c> — the safe answer, and the one that loses nothing the source held.
    /// </summary>
    public static string ToModelType(string sqlType)
        => Types.TryGetValue(sqlType ?? string.Empty, out var type) ? type : "string";

    /// <summary>
    /// A sentence naming what this column loses by being mapped, or null when it loses nothing.
    /// </summary>
    /// <remarks>
    /// Returned rather than logged so the caller decides where it appears. Only the two genuine
    /// losses are reported: a note against every column would be noise, and noise is how a
    /// warning stops being read.
    /// </remarks>
    public static string? LossNote(string sqlType, string columnName) =>
        (sqlType ?? string.Empty).ToLowerInvariant() switch
        {
            "bigint" =>
                $"{columnName} is bigint and becomes int — a value above {int.MaxValue} will not "
                + "survive.",
            "uniqueidentifier" =>
                $"{columnName} is uniqueidentifier and becomes string — correct to look at, but "
                + "no longer a Guid.",
            _ => null
        };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~SqlTypeMapTests"`
Expected: PASS, 26 tests.

- [ ] **Step 5: Commit**

```bash
git add DataIngestion/Schema/Readers/SqlTypeMap.cs DataIngestion.Tests/Schema/Readers/SqlTypeMapTests.cs
git commit -m "feat: SQL Server types map to the five the generator emits, and say what they lose"
```

---

### Task 3: A password never reaches the screen

**Files:**
- Create: `DataIngestion/ConnectionSecret.cs`
- Modify: `CodeGenerator/Web/Workflow/BuildCommandEcho.cs:33`
- Test: `DataIngestion.Tests/ConnectionSecretTests.cs`, `CodeGenerator.Tests/Web/BuildCommandEchoTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `public static class ConnectionSecret` in namespace `DataIngestion`, with `public static string Redact(string connectionString)`.

**Why:** `BuildCommandEcho.cs:33` writes `from {kind} "{source.Location}"`, and for SQL the location *is* the connection string. Unredacted, `Password=hunter2` appears on the Review page, in the console when the command finishes, and in anything the replay line is pasted into.

- [ ] **Step 1: Write the failing test**

```csharp
using DataIngestion;
using Xunit;

namespace DataIngestion.Tests;

public class ConnectionSecretTests
{
    [Fact]
    public void A_password_is_replaced_and_the_rest_is_left_alone()
    {
        var redacted = ConnectionSecret.Redact(
            "Server=.;Database=Northwind;User ID=sa;Password=hunter2");

        Assert.DoesNotContain("hunter2", redacted);
        Assert.Contains("Password=***", redacted);
        Assert.Contains("Server=.", redacted);
        Assert.Contains("Database=Northwind", redacted);
        Assert.Contains("User ID=sa", redacted);
    }

    [Theory]
    [InlineData("pwd")]
    [InlineData("PWD")]
    [InlineData("PASSWORD")]
    [InlineData("Pass Word")]
    public void Every_spelling_SqlClient_accepts_is_redacted(string key)
    {
        var redacted = ConnectionSecret.Redact($"Server=.;{key}=hunter2");

        Assert.DoesNotContain("hunter2", redacted);
    }

    // A password may legally contain a semicolon, in which case SqlClient requires it quoted.
    // Splitting naively on ';' would leave the tail of the password in the output.
    [Fact]
    public void A_password_containing_a_semicolon_is_redacted_whole()
    {
        var redacted = ConnectionSecret.Redact("Server=.;Password=\"a;b;c\";Database=X");

        Assert.DoesNotContain("a;b;c", redacted);
        Assert.Contains("Database=X", redacted);
    }

    // The overwhelmingly common case in the environments this is used in. Rewriting a string that
    // holds no secret would make the replay line differ from what the user typed for no reason.
    [Fact]
    public void A_trusted_connection_is_returned_unchanged()
    {
        const string original = "Server=.;Database=Northwind;Trusted_Connection=True";

        Assert.Equal(original, ConnectionSecret.Redact(original));
    }

    [Fact]
    public void Something_that_is_not_a_connection_string_is_returned_unchanged()
    {
        const string original = @"D:\data\orders.csv";

        Assert.Equal(original, ConnectionSecret.Redact(original));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~ConnectionSecretTests"`
Expected: FAIL to compile — `ConnectionSecret` does not exist.

- [ ] **Step 3: Write minimal implementation**

Note: this uses `System.Data.Common.DbConnectionStringBuilder`, which is in the BCL — **no package reference is needed for this task.**

```csharp
using System.Data.Common;

namespace DataIngestion;

/// <summary>
/// Removes the password from a connection string that is about to be shown to someone.
/// </summary>
/// <remarks>
/// <c>DataSourceSpec.Location</c> is a file path for every other kind and a connection string for
/// SQL, and the location is echoed — on the review page, in the console sign-off, and in the
/// replay line a user is invited to copy. Without this, an ordinary SQL-auth connection string
/// puts its password in all three.
/// <para>
/// Parsed with <see cref="DbConnectionStringBuilder"/> rather than split on <c>;</c>: a password
/// may contain a semicolon when quoted, and a naive split leaves its tail in the output. The
/// builder is in the BCL, so this costs no package reference and works for a caller who has
/// nothing to do with SQL Server.
/// </para>
/// </remarks>
public static class ConnectionSecret
{
    public const string Mask = "***";

    private static readonly string[] Keys = ["password", "pwd", "pass word"];

    /// <summary>
    /// <paramref name="connectionString"/> with any password masked, or the input unchanged when
    /// it holds none — or is not a connection string at all.
    /// </summary>
    public static string Redact(string connectionString)
    {
        var text = connectionString ?? string.Empty;

        if (text.Length == 0)
            return text;

        DbConnectionStringBuilder builder;

        try
        {
            builder = new DbConnectionStringBuilder { ConnectionString = text };
        }
        catch (ArgumentException)
        {
            // Not a connection string. A file path is the normal case here, and returning it
            // unchanged is the only correct answer.
            return text;
        }

        var redacted = false;

        foreach (var key in Keys)
        {
            if (!builder.ContainsKey(key))
                continue;

            builder[key] = Mask;
            redacted = true;
        }

        // Unchanged rather than round-tripped when there was no secret: the builder re-orders and
        // re-quotes, and a replay line that does not match what the user typed invites the
        // question of what else was changed.
        return redacted ? builder.ConnectionString : text;
    }
}
```

In `CodeGenerator/Web/Workflow/BuildCommandEcho.cs`, replace line 33:

```csharp
        if (session.Source is { } source)
            line += $" from {source.Kind.ToString().ToLowerInvariant()} "
                  + $"\"{DataIngestion.ConnectionSecret.Redact(source.Location)}\"";
```

- [ ] **Step 4: Add the echo test**

Append to `CodeGenerator.Tests/Web/BuildCommandEchoTests.cs` (MSTest — note the different framework):

```csharp
    /// <summary>
    /// The echo prints the location, and for SQL the location IS the connection string. This is
    /// shown on the review page and printed to the console when the command finishes.
    /// </summary>
    [TestMethod]
    public void A_sql_password_is_not_echoed()
    {
        var session = new BuildSession(
            "a shop", Path.GetTempPath(), AppCustomization.Default,
            new DataSourceSpec(
                DataSourceKind.Sql,
                "Server=.;Database=Northwind;User ID=sa;Password=hunter2",
                SqlDatabase: "Northwind"));

        var echo = string.Join(Environment.NewLine, BuildCommandEcho.For(session));

        StringAssert.Contains(echo, "Password=***");
        Assert.IsFalse(echo.Contains("hunter2"), "the password reached the echo");
    }
```

- [ ] **Step 5: Run both test sets to verify they pass**

Run:
```
dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~ConnectionSecretTests"
dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~BuildCommandEchoTests"
```
Expected: PASS both.

- [ ] **Step 6: Commit**

```bash
git add DataIngestion/ConnectionSecret.cs DataIngestion.Tests/ConnectionSecretTests.cs CodeGenerator/Web/Workflow/BuildCommandEcho.cs CodeGenerator.Tests/Web/BuildCommandEchoTests.cs
git commit -m "feat: a connection string's password never reaches the echo"
```

---

### Task 4: Declared foreign keys become relationships

**Files:**
- Modify: `DataIngestion/DataModelBuilder.cs` (add a pass; `AddRelationships` at line 313 stays)
- Test: `DataIngestion.Tests/DataModelBuilderForeignKeyTests.cs`

**Interfaces:**
- Consumes: `TabularForeignKey` (Task 1).
- Produces: no new public API. `DataModelBuilder.Build` gains behaviour.

**The three traps here — read before writing code:**

1. **Entity names are normalised.** `Build` does `NameNormalizer.ToUserTypedEntityName(table.Name)`, so a foreign key naming table `Order Details` must be normalised the *same way* before it can match an entity. Comparing raw SQL names against normalised entity names silently matches nothing.
2. **The convention pass already exists.** `AddRelationships` (line 313) emits a relationship for every `int` field ending in `Id` whose target is a known entity. A SQL foreign key on `CustomerID` will *also* be caught by it. Without de-duplication the model gets the same relationship twice, and `DomainModelMapper.AddNavigationProperties` would then try to add the same navigation property twice.
3. **A target that was not picked must be dropped.** Same rule the convention pass applies, and the same rule `DomainModelMapper` applies on its side: a relationship to a class that will not exist does not compile.

- [ ] **Step 1: Write the failing test**

```csharp
using DataIngestion;
using DataIngestion.Schema;
using Xunit;

namespace DataIngestion.Tests;

public class DataModelBuilderForeignKeyTests
{
    private static TabularTable Table(string name, params string[] columns)
    {
        var table = new TabularTable { Name = name };

        foreach (var column in columns)
            table.Columns.Add(new TabularColumn { Name = column, Type = "int" });

        return table;
    }

    [Fact]
    public void A_declared_foreign_key_becomes_a_relationship()
    {
        var customers = Table("Customers", "CustomerID");
        var orders = Table("Orders", "OrderID", "CustomerID");
        orders.ForeignKeys.Add(new TabularForeignKey("CustomerID", "Customers", "CustomerID"));

        var model = DataModelBuilder.Build([customers, orders]);

        var order = model.Entities.Single(e => e.Name == "Orders");
        Assert.Contains(order.Relationships, r => r.Target == "Customers");
    }

    /// <summary>
    /// The name trap. Entity names go through NameNormalizer, so a foreign key naming the raw SQL
    /// table has to be normalised the same way or it matches nothing and the relationship is
    /// silently lost.
    /// </summary>
    [Fact]
    public void A_foreign_key_to_a_table_whose_name_was_normalised_still_resolves()
    {
        var details = Table("Order Details", "OrderID");
        var orders = Table("Orders", "OrderID");
        details.ForeignKeys.Add(new TabularForeignKey("OrderID", "Orders", "OrderID"));

        var model = DataModelBuilder.Build([orders, details]);

        var entity = model.Entities.Single(e => e.Name != "Orders");
        Assert.Contains(entity.Relationships, r => r.Target == "Orders");
    }

    /// <summary>
    /// The duplication trap. AddRelationships already emits a relationship for any int field
    /// ending in Id, so a foreign key on CustomerID is seen twice. Twice means
    /// DomainModelMapper.AddNavigationProperties tries to write the same navigation property
    /// twice.
    /// </summary>
    [Fact]
    public void A_foreign_key_the_convention_would_also_find_is_recorded_once()
    {
        var customer = Table("Customer", "CustomerId");
        var order = Table("Order", "OrderId", "CustomerId");
        order.ForeignKeys.Add(new TabularForeignKey("CustomerId", "Customer", "CustomerId"));

        var model = DataModelBuilder.Build([customer, order]);

        var entity = model.Entities.Single(e => e.Name == "Order");
        Assert.Single(entity.Relationships.Where(r => r.Target == "Customer"));
    }

    [Fact]
    public void A_foreign_key_to_a_table_that_was_not_picked_is_dropped()
    {
        var orders = Table("Orders", "OrderID", "ShipperID");
        orders.ForeignKeys.Add(new TabularForeignKey("ShipperID", "Shippers", "ShipperID"));

        var model = DataModelBuilder.Build([orders]);

        var order = model.Entities.Single();
        Assert.DoesNotContain(order.Relationships, r => r.Target == "Shippers");
    }

    [Fact]
    public void A_self_referencing_foreign_key_is_dropped()
    {
        var employees = Table("Employees", "EmployeeID", "ReportsTo");
        employees.ForeignKeys.Add(new TabularForeignKey("ReportsTo", "Employees", "EmployeeID"));

        var model = DataModelBuilder.Build([employees]);

        var entity = model.Entities.Single();
        Assert.DoesNotContain(entity.Relationships, r => r.Target == entity.Name);
    }

    // A file reader declares nothing, and must keep behaving exactly as it did.
    [Fact]
    public void A_table_with_no_declared_keys_still_gets_the_convention_relationships()
    {
        var customer = Table("Customer", "CustomerId");
        var order = Table("Order", "OrderId", "CustomerId");

        var model = DataModelBuilder.Build([customer, order]);

        var entity = model.Entities.Single(e => e.Name == "Order");
        Assert.Contains(entity.Relationships, r => r.Target == "Customer");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~DataModelBuilderForeignKeyTests"`
Expected: FAIL — the four declared-key tests fail (no relationship produced, or produced twice). The last test passes already; that is correct, it is the regression guard.

- [ ] **Step 3: Write minimal implementation**

In `DataModelBuilder.Build`, the existing call at line 85 is:

```csharp
        AddRelationships(model, entityNames);
```

Change it to run the declared pass **first**, so the convention pass can see what is already there:

```csharp
        AddDeclaredRelationships(model, tables, entityNames);
        AddRelationships(model, entityNames);
```

Add the new method beside `AddRelationships`:

```csharp
    /// <summary>
    /// Turns the foreign keys a source DECLARED into relationships, before the
    /// <c>&lt;Target&gt;Id</c> convention gets a look at the same columns.
    /// </summary>
    /// <remarks>
    /// First, deliberately. These are read from the constraint the database enforces, where
    /// <see cref="AddRelationships"/> is inferring from a column name — so when both would fire on
    /// the same column, the declared one is the one that should be there, and running it first
    /// makes the convention pass's duplicate check into a no-op rather than a race.
    /// <para>
    /// A foreign key's target is a RAW table name and entity names have been through
    /// <c>NameNormalizer</c>, so it is normalised the same way before being matched. Comparing the
    /// two directly matches nothing, and loses every relationship without a word.
    /// </para>
    /// </remarks>
    private static void AddDeclaredRelationships(
        CandidateDomainModel model,
        IReadOnlyList<TabularTable> tables,
        HashSet<string> entityNames)
    {
        foreach (var table in tables)
        {
            if (table.ForeignKeys.Count == 0)
                continue;

            var entityName = NameNormalizer.ToUserTypedEntityName(table.Name);
            var entity = model.Entities
                .FirstOrDefault(e => string.Equals(e.Name, entityName, StringComparison.OrdinalIgnoreCase));

            if (entity is null)
                continue;

            foreach (var key in table.ForeignKeys)
            {
                var target = NameNormalizer.ToUserTypedEntityName(key.TargetTable);

                if (target.Length == 0
                    || !entityNames.Contains(target)
                    || string.Equals(target, entity.Name, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (entity.Relationships.Any(
                        r => string.Equals(r.Target, target, StringComparison.OrdinalIgnoreCase)))
                    continue;

                entity.Relationships.Add(new CandidateRelationship
                {
                    Target = target,
                    Role = ReferencesRole
                });
            }
        }
    }
```

Then in the existing `AddRelationships`, add the same duplicate guard immediately before its `entity.Relationships.Add(...)`:

```csharp
                if (entity.Relationships.Any(
                        r => string.Equals(r.Target, target, StringComparison.OrdinalIgnoreCase)))
                    continue;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~DataModelBuilder"`
Expected: PASS — the new file's 6 tests, and every pre-existing `DataModelBuilder` test still green.

- [ ] **Step 5: Commit**

```bash
git add DataIngestion/DataModelBuilder.cs DataIngestion.Tests/DataModelBuilderForeignKeyTests.cs
git commit -m "feat: a declared foreign key becomes a relationship, and only once"
```

---

### Task 5: The seam, and the catalog above it

**Files:**
- Create: `DataIngestion/Schema/Readers/Sql/ISqlSchemaSource.cs`
- Create: `DataIngestion/Schema/Readers/Sql/SqlServerCatalog.cs`
- Test: `DataIngestion.Tests/Schema/Readers/Sql/FakeSqlSchemaSource.cs`, `DataIngestion.Tests/Schema/Readers/Sql/SqlServerCatalogTests.cs`

**Interfaces:**
- Consumes: `TabularForeignKey` (Task 1).
- Produces, all in namespace `DataIngestion.Schema.Readers.Sql`:
  - `public sealed record SqlTableInfo(string Schema, string Name, int Columns, long? Rows)`
  - `public sealed record SqlColumnInfo(string Name, string SqlType, bool Nullable)`
  - `public interface ISqlSchemaSource` with `Tables`, `Columns`, `ForeignKeys`, `Rows` (signatures below)
  - `public sealed class SqlServerCatalog(ISqlSchemaSource source)` with `IReadOnlyList<SqlTableInfo> List(string connectionString)`

- [ ] **Step 1: Write the failing test**

`FakeSqlSchemaSource.cs`:

```csharp
using DataIngestion.Schema;
using DataIngestion.Schema.Readers.Sql;

namespace DataIngestion.Tests.Schema.Readers.Sql;

/// <summary>
/// A database, as far as anything above the seam can tell. Everything of substance in this
/// feature is tested against this; only the SqlClient adapter is not, and that is stated in the
/// spec rather than papered over.
/// </summary>
internal sealed class FakeSqlSchemaSource : ISqlSchemaSource
{
    public List<SqlTableInfo> TableList { get; } = [];
    public Dictionary<string, List<SqlColumnInfo>> ColumnsByTable { get; } = [];
    public Dictionary<string, List<string?[]>> RowsByTable { get; } = [];
    public List<TabularForeignKey> Keys { get; } = [];

    /// <summary>Every table whose rows were actually fetched, in order, so a test can assert
    /// that nothing was read before it was asked for.</summary>
    public List<string> RowsFetched { get; } = [];

    public IReadOnlyList<SqlTableInfo> Tables(string connectionString) => TableList;

    public IReadOnlyList<SqlColumnInfo> Columns(string connectionString, string schema, string table)
        => ColumnsByTable.TryGetValue(table, out var columns) ? columns : [];

    public IReadOnlyList<TabularForeignKey> ForeignKeys(string connectionString) => Keys;

    public IReadOnlyList<string?[]> Rows(string connectionString, string schema, string table)
    {
        RowsFetched.Add(table);
        return RowsByTable.TryGetValue(table, out var rows) ? rows : [];
    }
}
```

`SqlServerCatalogTests.cs`:

```csharp
using DataIngestion.Schema.Readers.Sql;
using Xunit;

namespace DataIngestion.Tests.Schema.Readers.Sql;

public class SqlServerCatalogTests
{
    private const string Connection = "Server=.;Database=Northwind;Trusted_Connection=True";

    [Fact]
    public void Lists_what_the_database_holds()
    {
        var source = new FakeSqlSchemaSource();
        source.TableList.Add(new SqlTableInfo("dbo", "Customers", 11, 91));
        source.TableList.Add(new SqlTableInfo("dbo", "Orders", 14, 830));

        var listed = new SqlServerCatalog(source).List(Connection);

        Assert.Equal(2, listed.Count);
        Assert.Equal("Customers", listed[0].Name);
        Assert.Equal(91, listed[0].Rows);
    }

    /// <summary>
    /// The whole point of the phase: it runs before the user has chosen anything, so it must not
    /// touch a single row.
    /// </summary>
    [Fact]
    public void Reads_no_data_at_all()
    {
        var source = new FakeSqlSchemaSource();
        source.TableList.Add(new SqlTableInfo("dbo", "Orders", 14, 830));

        new SqlServerCatalog(source).List(Connection);

        Assert.Empty(source.RowsFetched);
    }

    [Fact]
    public void A_database_with_no_tables_is_refused_with_a_reason()
    {
        var source = new FakeSqlSchemaSource();

        var error = Assert.Throws<InvalidDataException>(
            () => new SqlServerCatalog(source).List(Connection));

        Assert.Contains("no tables", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A low-privilege login can read tables without holding VIEW DATABASE STATE, which is what
    /// sys.dm_db_partition_stats needs. Losing the count must not lose the picker.
    /// </summary>
    [Fact]
    public void A_table_whose_row_count_could_not_be_read_is_still_listed()
    {
        var source = new FakeSqlSchemaSource();
        source.TableList.Add(new SqlTableInfo("dbo", "Orders", 14, null));

        var listed = new SqlServerCatalog(source).List(Connection);

        Assert.Single(listed);
        Assert.Null(listed[0].Rows);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~SqlServerCatalogTests"`
Expected: FAIL to compile — none of these types exist.

- [ ] **Step 3: Write minimal implementation**

`ISqlSchemaSource.cs`:

```csharp
namespace DataIngestion.Schema.Readers.Sql;

/// <summary>One table as the picker needs it. <paramref name="Rows"/> is null when the row count
/// could not be read — see <see cref="ISqlSchemaSource"/>.</summary>
public sealed record SqlTableInfo(string Schema, string Name, int Columns, long? Rows);

/// <summary>One column, with SQL Server's own type name for it.</summary>
public sealed record SqlColumnInfo(string Name, string SqlType, bool Nullable);

/// <summary>
/// The whole of this feature's contact with ADO.NET.
/// </summary>
/// <remarks>
/// One thin adapter implements this over <c>Microsoft.Data.SqlClient</c>; everything above it is
/// tested against a fake. CI has no SQL Server, and a mock that asserts the query text matches a
/// string would be a test that passes while proving nothing — so the seam is drawn here, kept
/// narrow enough that the adapter can be read, and the adapter is covered by an opt-in
/// integration test instead.
/// </remarks>
public interface ISqlSchemaSource
{
    /// <summary>Base tables only, with column counts and near-exact row counts. Reads no data.</summary>
    IReadOnlyList<SqlTableInfo> Tables(string connectionString);

    IReadOnlyList<SqlColumnInfo> Columns(string connectionString, string schema, string table);

    /// <summary>Every declared foreign key in the database, in the shape the model builder wants.</summary>
    IReadOnlyList<TabularForeignKey> ForeignKeys(string connectionString);

    /// <summary>Every row of one table, each value already rendered invariantly to a string.</summary>
    IReadOnlyList<string?[]> Rows(string connectionString, string schema, string table);
}
```

`SqlServerCatalog.cs`:

```csharp
namespace DataIngestion.Schema.Readers.Sql;

/// <summary>
/// What is in this database — the question the Data stage asks before the user has picked
/// anything.
/// </summary>
/// <remarks>
/// Separate from <see cref="SqlServerReader"/> because it answers a different question at a
/// different moment and has a different cost. It runs while somebody is waiting to see a list, so
/// it reads no data: a picker that takes thirty seconds to appear reads as a hang.
/// </remarks>
public sealed class SqlServerCatalog(ISqlSchemaSource source)
{
    /// <summary>The base tables the connection can see.</summary>
    /// <exception cref="InvalidDataException">The database holds no base tables.</exception>
    public IReadOnlyList<SqlTableInfo> List(string connectionString)
    {
        var tables = source.Tables(connectionString);

        if (tables.Count == 0)
            throw new InvalidDataException(
                "That connection worked, but the database has no tables to read. Check you named "
                + "the right database — a connection with no Initial Catalog lands on master.");

        return tables;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~SqlServerCatalogTests"`
Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add DataIngestion/Schema/Readers/Sql/ DataIngestion.Tests/Schema/Readers/Sql/
git commit -m "feat: list a database's tables without reading a row"
```

---

### Task 6: The reader

**Files:**
- Create: `DataIngestion/Schema/Readers/Sql/SqlServerReader.cs`
- Modify: `DataIngestion/Schema/Readers/DataSourceReaders.cs` (the `TryCreate` switch and `NotSupportedMessage`)
- Test: `DataIngestion.Tests/Schema/Readers/Sql/SqlServerReaderTests.cs`

**Interfaces:**
- Consumes: `ISqlSchemaSource`, `SqlTableInfo`, `SqlColumnInfo` (Task 5); `SqlTypeMap` (Task 2); `TabularForeignKey` (Task 1).
- Produces: `public sealed class SqlServerReader(ISqlSchemaSource source) : IDataSourceReader` with `IReadOnlyList<TabularTable> Read(DataSourceSpec spec)`, and `public IReadOnlyList<string> LossNotes { get; }`.

- [ ] **Step 1: Write the failing test**

```csharp
using DataIngestion;
using DataIngestion.Schema;
using DataIngestion.Schema.Readers.Sql;
using Xunit;

namespace DataIngestion.Tests.Schema.Readers.Sql;

public class SqlServerReaderTests
{
    private const string Connection = "Server=.;Database=Northwind;Trusted_Connection=True";

    private static FakeSqlSchemaSource Northwind()
    {
        var source = new FakeSqlSchemaSource();

        source.TableList.Add(new SqlTableInfo("dbo", "Customers", 2, 2));
        source.TableList.Add(new SqlTableInfo("dbo", "Orders", 2, 1));

        source.ColumnsByTable["Customers"] =
        [
            new SqlColumnInfo("CustomerID", "int", false),
            new SqlColumnInfo("CompanyName", "nvarchar", false),
        ];
        source.ColumnsByTable["Orders"] =
        [
            new SqlColumnInfo("OrderID", "int", false),
            new SqlColumnInfo("OrderDate", "datetime", true),
        ];

        source.RowsByTable["Customers"] = [["1", "Alfreds"], ["2", "Ana"]];
        source.RowsByTable["Orders"] = [["10248", "1996-07-04"]];

        return source;
    }

    private static DataSourceSpec Spec(params string[] tables)
        => new(DataSourceKind.Sql, Connection, "Northwind", tables);

    [Fact]
    public void Reads_only_the_tables_that_were_named()
    {
        var source = Northwind();

        var tables = new SqlServerReader(source).Read(Spec("Customers"));

        Assert.Single(tables);
        Assert.Equal("Customers", tables[0].Name);
        Assert.Equal(["Customers"], source.RowsFetched);
    }

    [Fact]
    public void Takes_the_column_type_from_the_database_rather_than_inferring_it()
    {
        var tables = new SqlServerReader(Northwind()).Read(Spec("Orders"));

        var orders = tables.Single();
        Assert.Equal("int", orders.Columns.Single(c => c.Name == "OrderID").Type);
        Assert.Equal("DateTime", orders.Columns.Single(c => c.Name == "OrderDate").Type);
    }

    [Fact]
    public void Brings_every_row()
    {
        var tables = new SqlServerReader(Northwind()).Read(Spec("Customers"));

        Assert.Equal(2, tables.Single().Rows.Count);
    }

    [Fact]
    public void Carries_the_foreign_keys_that_point_between_the_tables_read()
    {
        var source = Northwind();
        source.Keys.Add(new TabularForeignKey("CustomerID", "Customers", "CustomerID"));
        source.ColumnsByTable["Orders"] =
        [
            new SqlColumnInfo("OrderID", "int", false),
            new SqlColumnInfo("CustomerID", "int", false),
        ];
        source.RowsByTable["Orders"] = [["10248", "1"]];

        var tables = new SqlServerReader(source)
            .Read(Spec("Customers", "Orders"));

        var orders = tables.Single(t => t.Name == "Orders");
        Assert.Contains(orders.ForeignKeys, k => k.TargetTable == "Customers");
    }

    [Fact]
    public void A_named_table_that_is_not_there_is_refused_by_name()
    {
        var error = Assert.Throws<InvalidDataException>(
            () => new SqlServerReader(Northwind()).Read(Spec("Suppliers")));

        Assert.Contains("Suppliers", error.Message);
    }

    /// <summary>Naming nothing is what a piped run does, and it can only mean everything.</summary>
    [Fact]
    public void Naming_no_tables_reads_them_all()
    {
        var tables = new SqlServerReader(Northwind())
            .Read(new DataSourceSpec(DataSourceKind.Sql, Connection, "Northwind"));

        Assert.Equal(2, tables.Count);
    }

    [Fact]
    public void A_lossy_column_type_is_reported_rather_than_hidden()
    {
        var source = Northwind();
        source.ColumnsByTable["Orders"] = [new SqlColumnInfo("OrderID", "bigint", false)];
        source.RowsByTable["Orders"] = [["10248"]];

        var reader = new SqlServerReader(source);
        reader.Read(Spec("Orders"));

        Assert.Contains(reader.LossNotes, note => note.Contains("OrderID"));
    }

    /// <summary>The label is what the user is shown the source called it.</summary>
    [Fact]
    public void A_table_is_labelled_with_its_two_part_name()
    {
        var tables = new SqlServerReader(Northwind()).Read(Spec("Customers"));

        Assert.Equal("dbo.Customers", tables.Single().Label);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~SqlServerReaderTests"`
Expected: FAIL to compile — `SqlServerReader` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace DataIngestion.Schema.Readers.Sql;

/// <summary>
/// Reads the tables the user picked out of a SQL Server database.
/// </summary>
/// <remarks>
/// The one reader that never calls <see cref="ColumnTypeInference"/>. Every other reader produces
/// strings and then asks what they were, which is where the leading-zero and thousands-separator
/// corruptions come from; a database column has a declared type, so the question is never put.
/// </remarks>
public sealed class SqlServerReader(ISqlSchemaSource source) : IDataSourceReader
{
    private readonly List<string> _lossNotes = [];

    /// <summary>What the five-type target set cost this read, or empty. See
    /// <see cref="SqlTypeMap.LossNote"/>.</summary>
    public IReadOnlyList<string> LossNotes => _lossNotes;

    public IReadOnlyList<TabularTable> Read(DataSourceSpec spec)
    {
        _lossNotes.Clear();

        var available = source.Tables(spec.Location);
        var wanted = Wanted(spec, available);

        var keys = source.ForeignKeys(spec.Location);
        var read = new List<TabularTable>();

        foreach (var info in wanted)
        {
            var table = new TabularTable
            {
                Name = info.Name,
                SourceLabel = $"{info.Schema}.{info.Name}"
            };

            foreach (var column in source.Columns(spec.Location, info.Schema, info.Name))
            {
                table.Columns.Add(new TabularColumn
                {
                    Name = column.Name,
                    Type = SqlTypeMap.ToModelType(column.SqlType)
                });

                var note = SqlTypeMap.LossNote(column.SqlType, column.Name);

                if (note is not null)
                    _lossNotes.Add(note);
            }

            table.Rows.AddRange(source.Rows(spec.Location, info.Schema, info.Name));

            // Only the keys that point between tables being read. One pointing at a table the
            // user did not pick would become a navigation property typed as a class that is
            // never generated, which does not compile — DataModelBuilder drops it too, and
            // dropping it here as well keeps the table honest about what it is carrying.
            foreach (var key in keys)
            {
                if (key.Column.Length > 0
                    && table.Columns.Any(
                        c => string.Equals(c.Name, key.Column, StringComparison.OrdinalIgnoreCase))
                    && wanted.Any(
                        t => string.Equals(t.Name, key.TargetTable, StringComparison.OrdinalIgnoreCase)))
                {
                    table.ForeignKeys.Add(key);
                }
            }

            read.Add(table);
        }

        return read;
    }

    /// <summary>
    /// The tables to read: the ones named, or all of them when none were.
    /// </summary>
    /// <remarks>
    /// Naming nothing can only mean everything — it is what a piped run does, where there is
    /// nobody to ask. A name that is not there is refused rather than skipped: silently producing
    /// a smaller model than was asked for is the failure this whole codebase keeps guarding
    /// against.
    /// </remarks>
    private static List<SqlTableInfo> Wanted(
        DataSourceSpec spec, IReadOnlyList<SqlTableInfo> available)
    {
        if (spec.SqlTables is not { Count: > 0 } names)
            return [.. available];

        var wanted = new List<SqlTableInfo>();

        foreach (var name in names)
        {
            var match = available.FirstOrDefault(
                t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)
                  || string.Equals($"{t.Schema}.{t.Name}", name, StringComparison.OrdinalIgnoreCase));

            if (match is null)
                throw new InvalidDataException(
                    $"The database has no table called {name}. It may have been dropped or "
                    + "renamed since the list was read.");

            wanted.Add(match);
        }

        return wanted;
    }
}
```

In `DataSourceReaders.TryCreate`, add before `default:`:

```csharp
            case DataSourceKind.Sql:
                reader = new Sql.SqlServerReader(new Sql.SqlClientSchemaSource());
                return true;
```

**Note:** `SqlClientSchemaSource` is written in Task 7. Until then this will not compile, so **do this switch edit in Task 7, not here.** In this task, leave `DataSourceReaders` alone.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~SqlServerReaderTests"`
Expected: PASS, 8 tests.

- [ ] **Step 5: Commit**

```bash
git add DataIngestion/Schema/Readers/Sql/SqlServerReader.cs DataIngestion.Tests/Schema/Readers/Sql/SqlServerReaderTests.cs
git commit -m "feat: read the picked tables, with the database's own types and keys"
```

---

### Task 7: The adapter, and the package

**Files:**
- Modify: `DataIngestion/DataIngestion.csproj`
- Create: `DataIngestion/Schema/Readers/Sql/SqlClientSchemaSource.cs`
- Modify: `DataIngestion/Schema/Readers/DataSourceReaders.cs`
- Test: `DataIngestion.Tests/Schema/Readers/Sql/SqlServerIntegrationTests.cs`

**Interfaces:**
- Consumes: `ISqlSchemaSource` (Task 5), `SqlServerReader` (Task 6).
- Produces: `public sealed class SqlClientSchemaSource : ISqlSchemaSource`.

**This is the one task whose deliverable CI cannot verify.** Everything above the seam is already covered; this is the adapter. Its integration test is skipped unless `SMARTCODEGEN_SQL_TEST_CONNECTION` is set.

- [ ] **Step 1: Add the package reference**

In `DataIngestion/DataIngestion.csproj`, inside the existing `<ItemGroup>` that holds package references (create one if there is none):

```xml
    <PackageReference Include="Microsoft.Data.SqlClient" Version="6.0.2" />
```

Run: `dotnet restore DataIngestion/DataIngestion.csproj`
Expected: restores without error.

- [ ] **Step 2: Write the integration test (skipped without a server)**

A plain `[Fact]` that returns early. `Xunit.SkippableFact` would express the skip properly but
it is a package, and the only permitted new package is `Microsoft.Data.SqlClient`.

```csharp
using DataIngestion.Schema.Readers.Sql;
using Xunit;

namespace DataIngestion.Tests.Schema.Readers.Sql;

/// <summary>
/// The adapter against a real server. Everything above <see cref="ISqlSchemaSource"/> is covered
/// by unit tests over a fake; this is the only thing that proves the SQL itself runs.
/// </summary>
/// <remarks>
/// Does nothing unless SMARTCODEGEN_SQL_TEST_CONNECTION names a database. CI has no SQL Server,
/// and a test that mocked the driver would assert that a query string equals a query string.
/// </remarks>
public class SqlServerIntegrationTests
{
    private const string Variable = "SMARTCODEGEN_SQL_TEST_CONNECTION";

    private static string? Connection => Environment.GetEnvironmentVariable(Variable);

    [Fact]
    public void Lists_tables_columns_and_keys_from_a_real_database()
    {
        if (string.IsNullOrWhiteSpace(Connection))
            return;   // no server configured — see the class remarks

        var source = new SqlClientSchemaSource();

        var tables = source.Tables(Connection);
        Assert.NotEmpty(tables);

        var first = tables[0];
        Assert.NotEmpty(source.Columns(Connection, first.Schema, first.Name));

        // Not asserted non-empty: a database may legitimately declare no foreign keys. That the
        // query runs at all is the thing being proved.
        _ = source.ForeignKeys(Connection);

        // Reading the first table proves the SELECT and the value rendering, whatever it holds.
        _ = source.Rows(Connection, first.Schema, first.Name);
    }
}
```

- [ ] **Step 3: Write the adapter**

```csharp
using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace DataIngestion.Schema.Readers.Sql;

/// <summary>
/// <see cref="ISqlSchemaSource"/> over <c>Microsoft.Data.SqlClient</c>.
/// </summary>
/// <remarks>
/// Deliberately the only file in this feature that references SqlClient, and deliberately thin:
/// it is the one piece CI cannot run, so there is as little of it as possible and no decision in
/// it worth testing. Anything that looks like a rule belongs above the seam.
/// </remarks>
public sealed class SqlClientSchemaSource : ISqlSchemaSource
{
    public IReadOnlyList<SqlTableInfo> Tables(string connectionString)
    {
        // LEFT JOIN on the row count: sys.dm_db_partition_stats needs VIEW DATABASE STATE, which
        // a read-only login may not hold. Losing the count must not lose the table.
        const string sql = """
            SELECT  s.name  AS [Schema],
                    t.name  AS [Table],
                    (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id) AS [Columns],
                    (SELECT SUM(p.row_count) FROM sys.dm_db_partition_stats p
                      WHERE p.object_id = t.object_id AND p.index_id IN (0, 1))          AS [Rows]
            FROM    sys.tables t
            JOIN    sys.schemas s ON s.schema_id = t.schema_id
            WHERE   t.is_ms_shipped = 0
            ORDER BY s.name, t.name;
            """;

        var tables = new List<SqlTableInfo>();

        foreach (var row in Query(connectionString, sql))
        {
            tables.Add(new SqlTableInfo(
                (string)row["Schema"],
                (string)row["Table"],
                Convert.ToInt32(row["Columns"], CultureInfo.InvariantCulture),
                row["Rows"] is DBNull ? null : Convert.ToInt64(row["Rows"], CultureInfo.InvariantCulture)));
        }

        return tables;
    }

    public IReadOnlyList<SqlColumnInfo> Columns(
        string connectionString, string schema, string table)
    {
        const string sql = """
            SELECT  COLUMN_NAME, DATA_TYPE, IS_NULLABLE
            FROM    INFORMATION_SCHEMA.COLUMNS
            WHERE   TABLE_SCHEMA = @schema AND TABLE_NAME = @table
            ORDER BY ORDINAL_POSITION;
            """;

        var columns = new List<SqlColumnInfo>();

        foreach (var row in Query(
                     connectionString, sql, ("@schema", schema), ("@table", table)))
        {
            columns.Add(new SqlColumnInfo(
                (string)row["COLUMN_NAME"],
                (string)row["DATA_TYPE"],
                string.Equals((string)row["IS_NULLABLE"], "YES", StringComparison.OrdinalIgnoreCase)));
        }

        return columns;
    }

    public IReadOnlyList<TabularForeignKey> ForeignKeys(string connectionString)
    {
        // Single-column keys only. A composite key produces one row per column here, and the
        // model has no way to express the pair — see the spec's Risks.
        const string sql = """
            SELECT  parent.name    AS [Column],
                    target.name    AS [TargetTable],
                    targetCol.name AS [TargetColumn]
            FROM    sys.foreign_key_columns fkc
            JOIN    sys.columns parent
                      ON parent.object_id = fkc.parent_object_id
                     AND parent.column_id = fkc.parent_column_id
            JOIN    sys.tables target
                      ON target.object_id = fkc.referenced_object_id
            JOIN    sys.columns targetCol
                      ON targetCol.object_id = fkc.referenced_object_id
                     AND targetCol.column_id = fkc.referenced_column_id
            WHERE   fkc.constraint_object_id IN (
                        SELECT constraint_object_id
                        FROM   sys.foreign_key_columns
                        GROUP BY constraint_object_id
                        HAVING COUNT(*) = 1);
            """;

        var keys = new List<TabularForeignKey>();

        foreach (var row in Query(connectionString, sql))
        {
            keys.Add(new TabularForeignKey(
                (string)row["Column"],
                (string)row["TargetTable"],
                (string)row["TargetColumn"]));
        }

        return keys;
    }

    public IReadOnlyList<string?[]> Rows(string connectionString, string schema, string table)
    {
        // The only place a name is interpolated rather than parameterised, because a table name
        // cannot be a parameter. QUOTENAME-equivalent bracket escaping is applied for that
        // reason: the names come from sys.tables rather than from user input, and this keeps it
        // that way even if a caller ever passes something else.
        var sql = $"SELECT * FROM [{Escape(schema)}].[{Escape(table)}];";

        var rows = new List<string?[]>();

        using var connection = new SqlConnection(connectionString);
        connection.Open();

        using var command = new SqlCommand(sql, connection);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var row = new string?[reader.FieldCount];

            for (var i = 0; i < reader.FieldCount; i++)
                row[i] = reader.IsDBNull(i) ? null : Render(reader.GetValue(i));

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// A value as the string <c>TabularTable.Rows</c> holds.
    /// </summary>
    /// <remarks>
    /// Invariant throughout, and round-trip formats for the two types where a locale would
    /// otherwise change the meaning. This is the opposite direction of travel from the file
    /// readers — the type is known and the string is derived from it, rather than the string
    /// being read and the type guessed.
    /// </remarks>
    private static string Render(object value) => value switch
    {
        DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset offset => offset.ToString("O", CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        byte[] bytes => Convert.ToBase64String(bytes),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static string Escape(string name) => name.Replace("]", "]]");

    private static List<Dictionary<string, object>> Query(
        string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        var rows = new List<Dictionary<string, object>>();

        using var connection = new SqlConnection(connectionString);
        connection.Open();

        using var command = new SqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.GetValue(i);

            rows.Add(row);
        }

        return rows;
    }
}
```

- [ ] **Step 4: Wire the reader into the registry**

In `DataSourceReaders.TryCreate`, before `default:`:

```csharp
            case DataSourceKind.Sql:
                reader = new Sql.SqlServerReader(new Sql.SqlClientSchemaSource());
                return true;
```

And update `NotSupportedMessage` — with every kind now readable it is unreachable, but it must not claim otherwise:

```csharp
    /// <summary>What to tell a user who picked a kind this build cannot read.</summary>
    /// <remarks>
    /// Every declared kind has a reader as of the SQL Server branch, so nothing reaches this
    /// today. It stays because <see cref="TryCreate"/> is the one place that knows, and a new
    /// kind added to <see cref="DataSourceKind"/> before its reader needs something to say.
    /// </remarks>
    public static string NotSupportedMessage(DataSourceKind kind)
        => $"{kind} is not in this build yet. Working formats: "
         + string.Join(", ", Keys.Where(k => TryParseKind(k, out var parsed) && TryCreate(parsed, out _)));
```

- [ ] **Step 5: Verify the whole solution still builds as CI builds it**

Run: `dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror`
Expected: `Build succeeded.` with no warnings. A `Microsoft.Data.SqlClient` version conflict surfaces here.

- [ ] **Step 6: Run the full DataIngestion suite**

Run: `dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj`
Expected: PASS, all tests. The integration test returns early without a connection string.

- [ ] **Step 7: Commit**

```bash
git add DataIngestion/DataIngestion.csproj DataIngestion/Schema/Readers/Sql/SqlClientSchemaSource.cs DataIngestion/Schema/Readers/DataSourceReaders.cs DataIngestion.Tests/Schema/Readers/Sql/SqlServerIntegrationTests.cs
git commit -m "feat: the SqlClient adapter, and sql becomes a format this build can read"
```

---

### Task 8: `tables A,B,C` on the command line

**Files:**
- Modify: `DataIngestion/InlineDataSource.cs`
- Test: `DataIngestion.Tests/InlineDataSourceSqlTests.cs`

**Interfaces:**
- Consumes: `DataSourceSpec.SqlTables`.
- Produces: no new API. `InlineDataSource.Parse` populates `SqlTables` and `SqlDatabase`.

**The ordering trap:** `Parse` currently takes the location as *the rest of the line* (`text[locationStart..]`), because Windows paths contain spaces. So `tables A,B,C` must be stripped **before** the location is taken, exactly as `normalize` already is — otherwise the connection string swallows it. Strip `tables` first, then `normalize`, then unquote.

- [ ] **Step 1: Write the failing test**

```csharp
using DataIngestion;
using Xunit;

namespace DataIngestion.Tests;

public class InlineDataSourceSqlTests
{
    [Fact]
    public void Tables_are_taken_off_the_line_and_not_left_in_the_connection_string()
    {
        var request = InlineDataSource.Parse(
            "order tracking from sql \"Server=.;Database=Northwind\" tables Customers,Orders");

        Assert.NotNull(request.Source);
        Assert.Equal("Server=.;Database=Northwind", request.Source!.Location);
        Assert.Equal(["Customers", "Orders"], request.Source.SqlTables);
        Assert.Equal("order tracking", request.Description);
    }

    [Fact]
    public void Naming_no_tables_leaves_the_list_empty()
    {
        var request = InlineDataSource.Parse(
            "order tracking from sql \"Server=.;Database=Northwind\"");

        Assert.Null(request.Source!.SqlTables);
        Assert.Equal("Server=.;Database=Northwind", request.Source.Location);
    }

    [Fact]
    public void A_quoted_table_name_may_contain_a_space()
    {
        var request = InlineDataSource.Parse(
            "from sql \"Server=.;Database=Northwind\" tables \"Order Details,Orders\"");

        Assert.Equal(["Order Details", "Orders"], request.Source!.SqlTables);
    }

    [Fact]
    public void Normalize_still_works_alongside_tables()
    {
        var request = InlineDataSource.Parse(
            "from sql \"Server=.;Database=X\" tables Orders normalize");

        Assert.True(request.Normalize);
        Assert.Equal(["Orders"], request.Source!.SqlTables);
        Assert.Equal("Server=.;Database=X", request.Source.Location);
    }

    /// <summary>The database names the generated app when no description was given —
    /// BuildAppCommand.AppNameFor already reads this member.</summary>
    [Fact]
    public void The_database_name_is_taken_from_the_connection_string()
    {
        var request = InlineDataSource.Parse(
            "from sql \"Server=.;Initial Catalog=Northwind;Trusted_Connection=True\"");

        Assert.Equal("Northwind", request.Source!.SqlDatabase);
    }

    [Fact]
    public void Database_is_accepted_as_well_as_initial_catalog()
    {
        var request = InlineDataSource.Parse("from sql \"Server=.;Database=Shop\"");

        Assert.Equal("Shop", request.Source!.SqlDatabase);
    }

    /// <summary>A path is not a connection string, and must not acquire SQL members.</summary>
    [Fact]
    public void A_csv_is_untouched_by_any_of_this()
    {
        var request = InlineDataSource.Parse(@"orders from csv D:\data\orders.csv");

        Assert.Null(request.Source!.SqlTables);
        Assert.Null(request.Source.SqlDatabase);
        Assert.Equal(@"D:\data\orders.csv", request.Source.Location);
    }

    // "tables" is an ordinary English word and a path may legitimately contain it.
    [Fact]
    public void A_path_containing_the_word_tables_is_not_split_on_it()
    {
        var request = InlineDataSource.Parse(@"from csv D:\my tables\orders.csv");

        Assert.Equal(@"D:\my tables\orders.csv", request.Source!.Location);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~InlineDataSourceSqlTests"`
Expected: FAIL — `SqlTables` and `SqlDatabase` are null; the location still contains `tables ...`.

- [ ] **Step 3: Write minimal implementation**

Add the keyword constant beside `NormalizeKeyword`:

```csharp
    /// <summary>Names the SQL tables to read, comma-joined. Only meaningful for the sql kind.</summary>
    public const string TablesKeyword = "tables";
```

In `Parse`, **immediately after** `var location = text[locationStart..].Trim();` and **before** the `normalize` block:

```csharp
            // Before normalize and before the location is settled, because the location is the
            // REST OF THE LINE — anything left behind here is swallowed by it. Only for sql: a
            // path may legitimately contain the word "tables".
            IReadOnlyList<string>? sqlTables = null;

            if (kind == DataSourceKind.Sql)
                (location, sqlTables) = SplitTables(location);
```

Change the two `return new InlineBuildRequest(...)` for the matched branch to:

```csharp
            return new InlineBuildRequest(
                description,
                new DataSourceSpec(kind, location, DatabaseOf(kind, location), sqlTables),
                normalize);
```

Add the two helpers:

```csharp
    /// <summary>
    /// Splits a trailing <c>tables A,B,C</c> off a location.
    /// </summary>
    /// <remarks>
    /// Searched from the right and only outside the quoted connection string, so a
    /// <c>Password</c> containing the word cannot be mistaken for the keyword. Returns the
    /// location unchanged when there is no such tail.
    /// </remarks>
    private static (string Location, IReadOnlyList<string>? Tables) SplitTables(string location)
    {
        var marker = location.LastIndexOf(
            " " + TablesKeyword + " ", StringComparison.OrdinalIgnoreCase);

        if (marker < 0)
            return (location, null);

        var names = Unquote(location[(marker + TablesKeyword.Length + 2)..].Trim());

        var split = names
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return split.Length == 0
            ? (location, null)
            : (location[..marker].Trim(), split);
    }

    /// <summary>
    /// The database a SQL connection string names, for the app-naming rule that already reads it.
    /// </summary>
    /// <remarks>
    /// <c>DbConnectionStringBuilder</c> rather than splitting on <c>;</c> — it is in the BCL, it
    /// handles quoting, and it accepts both spellings SqlClient does.
    /// </remarks>
    private static string? DatabaseOf(DataSourceKind kind, string location)
    {
        if (kind != DataSourceKind.Sql)
            return null;

        try
        {
            var builder = new System.Data.Common.DbConnectionStringBuilder
            {
                ConnectionString = location
            };

            foreach (var key in new[] { "initial catalog", "database" })
                if (builder.ContainsKey(key) && builder[key] is string name && name.Length > 0)
                    return name;
        }
        catch (ArgumentException)
        {
            // Not a connection string. Nothing to name the app after; AppNameFor falls back.
        }

        return null;
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DataIngestion.Tests/DataIngestion.Tests.csproj --filter "FullyQualifiedName~InlineDataSource"`
Expected: PASS — the new 8 tests, and every pre-existing `InlineDataSource` test still green.

- [ ] **Step 5: Commit**

```bash
git add DataIngestion/InlineDataSource.cs DataIngestion.Tests/InlineDataSourceSqlTests.cs
git commit -m "feat: tables A,B,C names what a piped sql run should read"
```

---

### Task 9: Picking the tables on the Data stage

**Files:**
- Modify: `CodeGenerator/Web/Components/Stages/DataStage.razor`
- Modify: `CodeGenerator/wwwroot/workflow.css`
- Test: `CodeGenerator.Tests/Web/DataStageSqlTests.cs`

**Interfaces:**
- Consumes: `SqlServerCatalog`, `SqlTableInfo` (Task 5); `DataSourceSpec.SqlTables`.
- Produces: no new API.

**Note on the format drop-down:** `DataStage.razor:28` already renders every key with `disabled="@(!Readable(key))"`. Once Task 7 lands, `sql` becomes selectable with no change here.

**bUnit constraint:** the stage must take its catalog through a parameter or an injected service so a test can supply a fake. Do **not** construct `SqlClientSchemaSource` inside the component — a bUnit test would then try to open a socket.

- [ ] **Step 1: Write the failing test**

```csharp
using AppScaffolding;
using AppScaffolding.Targets;
using Bunit;
using CodeGenerator.Web.Components.Stages;
using CodeGenerator.Web.Workflow;
using DataIngestion;
using DataIngestion.Schema.Readers.Sql;
using Microsoft.Extensions.DependencyInjection;

namespace CodeGenerator.Tests.Web;

/// <summary>
/// The SQL branch of the data stage: connect, see what is there, pick, then read. Nothing is read
/// until the pick is made, which is the whole reason the catalog is a separate phase.
/// </summary>
[TestClass]
public class DataStageSqlTests
{
    private const string Connection = "Server=.;Database=Northwind;Trusted_Connection=True";

    [TestMethod]
    public void Connecting_lists_the_tables_with_their_counts()
    {
        using var context = new BunitContext();
        var rendered = RenderWithTables(context, out _);

        rendered.Find("input.connection").Change(Connection);
        rendered.Find("button.connect").Click();

        StringAssert.Contains(rendered.Markup, "Customers");
        StringAssert.Contains(rendered.Markup, "91");
        Assert.HasCount(2, rendered.FindAll("input.table-pick"));
    }

    [TestMethod]
    public void Nothing_is_read_until_the_tables_are_chosen()
    {
        using var context = new BunitContext();
        var rendered = RenderWithTables(context, out var source);

        rendered.Find("input.connection").Change(Connection);
        rendered.Find("button.connect").Click();

        Assert.IsEmpty(source.RowsFetched);
    }

    [TestMethod]
    public void Ticking_a_table_records_it_on_the_source()
    {
        using var context = new BunitContext();
        var rendered = RenderWithTables(context, out _, out var session);

        rendered.Find("input.connection").Change(Connection);
        rendered.Find("button.connect").Click();
        rendered.FindAll("input.table-pick")[0].Change(true);
        rendered.Find("button.read-tables").Click();

        Assert.IsNotNull(session.Source);
        CollectionAssert.Contains(session.Source!.SqlTables!.ToList(), "Customers");
    }

    /// <summary>A failure says why and leaves the stage usable, rather than ending the run.</summary>
    [TestMethod]
    public void A_connection_that_fails_says_so_and_stays_clickable()
    {
        using var context = new BunitContext();
        var source = new ThrowingSqlSchemaSource("Login failed for user 'sa'.");
        var rendered = Render(context, source, out _);

        rendered.Find("input.connection").Change(Connection);
        rendered.Find("button.connect").Click();

        StringAssert.Contains(rendered.Find(".failure").TextContent, "Login failed");
        Assert.IsNotEmpty(rendered.FindAll("button.connect"));
    }

    [TestMethod]
    public void A_table_whose_row_count_is_unknown_is_still_offered()
    {
        using var context = new BunitContext();
        var source = new FakeSqlSchemaSource();
        source.TableList.Add(new SqlTableInfo("dbo", "Orders", 14, null));

        var rendered = Render(context, source, out _);

        rendered.Find("input.connection").Change(Connection);
        rendered.Find("button.connect").Click();

        Assert.HasCount(1, rendered.FindAll("input.table-pick"));
    }

    private static IRenderedComponent<DataStage> RenderWithTables(
        BunitContext context, out FakeSqlSchemaSource source)
        => RenderWithTables(context, out source, out _);

    private static IRenderedComponent<DataStage> RenderWithTables(
        BunitContext context, out FakeSqlSchemaSource source, out BuildSession session)
    {
        source = new FakeSqlSchemaSource();
        source.TableList.Add(new SqlTableInfo("dbo", "Customers", 11, 91));
        source.TableList.Add(new SqlTableInfo("dbo", "Orders", 14, 830));

        return Render(context, source, out session);
    }

    private static IRenderedComponent<DataStage> Render(
        BunitContext context, ISqlSchemaSource source, out BuildSession session)
    {
        context.Services.AddSingleton(source);

        session = NewSession();
        var captured = session;

        var rendered = context.Render<DataStage>(p => p
            .Add(c => c.Session, captured)
            .Add(c => c.Navigation, new StageNavigation())
            .Add(c => c.OnAdvance, () => { }));

        rendered.Find("input[value=file]").Change(true);
        rendered.Find("select.answer").Change("sql");

        return rendered;
    }

    private static BuildSession NewSession()
    {
        var session = new BuildSession("a shop", Path.GetTempPath(), AppCustomization.Default);
        session.SetTarget(new WpfTarget());
        session.Advance();
        return session;
    }

    /// <summary>A server that refuses, so the failure path can be asserted.</summary>
    private sealed class ThrowingSqlSchemaSource(string message) : ISqlSchemaSource
    {
        public IReadOnlyList<SqlTableInfo> Tables(string connectionString)
            => throw new InvalidOperationException(message);

        public IReadOnlyList<SqlColumnInfo> Columns(string c, string s, string t) => [];
        public IReadOnlyList<DataIngestion.Schema.TabularForeignKey> ForeignKeys(string c) => [];
        public IReadOnlyList<string?[]> Rows(string c, string s, string t) => [];
    }
}
```

**`FakeSqlSchemaSource` lives in `DataIngestion.Tests` and cannot be referenced from `CodeGenerator.Tests`.** Copy it into `CodeGenerator.Tests/Web/FakeSqlSchemaSource.cs` as an `internal sealed class` in namespace `CodeGenerator.Tests.Web`. Duplicating a five-line test double across two projects is cheaper than a shared test-utility project, and this codebase has no such project.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~DataStageSqlTests"`
Expected: FAIL — no `input.connection`, no `button.connect`.

- [ ] **Step 3: Write minimal implementation**

In `DataStage.razor`, add to the `@code` block:

```csharp
    /// <summary>
    /// Injected rather than constructed so a test can supply a database. Constructing
    /// SqlClientSchemaSource here would make every bUnit render of this panel try to open a
    /// socket.
    /// </summary>
    [Inject] public ISqlSchemaSource SchemaSource { get; set; } = default!;

    private string _connection = string.Empty;
    private IReadOnlyList<SqlTableInfo> _tables = [];
    private readonly HashSet<string> _picked = new(StringComparer.OrdinalIgnoreCase);

    private bool IsSql => string.Equals(_format, "sql", StringComparison.OrdinalIgnoreCase);

    private void Connect()
    {
        _error = null;
        _tables = [];
        _picked.Clear();

        try
        {
            _tables = new SqlServerCatalog(SchemaSource).List(_connection);
        }
        catch (Exception ex)
        {
            // Whatever the server said. A driver's own message names the real problem — a bad
            // password, an unreachable host — far better than anything invented here could.
            _error = ex.Message;
        }
    }

    private void Pick(string name, bool on)
    {
        if (on)
            _picked.Add(name);
        else
            _picked.Remove(name);
    }

    private void ReadPicked()
    {
        Session.SetSource(new DataSourceSpec(
            DataSourceKind.Sql, _connection, DatabaseLabel(), [.. _picked]));
    }

    private string? DatabaseLabel()
    {
        try
        {
            var builder = new System.Data.Common.DbConnectionStringBuilder
            {
                ConnectionString = _connection
            };

            foreach (var key in new[] { "initial catalog", "database" })
                if (builder.ContainsKey(key) && builder[key] is string name && name.Length > 0)
                    return name;
        }
        catch (ArgumentException) { }

        return null;
    }

    private static string Count(long? rows)
        => rows is null ? "—" : rows.Value.ToString("N0");
```

And in the markup, inside the existing `@if (_hasFile)` block, replace the path box and drop zone with a branch. Keep the file markup exactly as it is in the `else`:

```razor
        @if (IsSql)
        {
            <label>Connection
                <input class="connection answer" value="@_connection"
                       @onchange="e => _connection = e.Value?.ToString() ?? string.Empty" />
            </label>

            <button type="button" class="connect" @onclick="Connect">connect</button>

            @if (_tables.Count > 0)
            {
                @* Counts before the choice, not after. All rows of what is ticked come across,
                   so the number beside each table is what makes that an informed decision. *@
                <table class="tables">
                    <tbody>
                    @foreach (var table in _tables)
                    {
                        <tr>
                            <td>
                                <input type="checkbox" class="table-pick"
                                       checked="@_picked.Contains(table.Name)"
                                       @onchange="e => Pick(table.Name, (bool?)e.Value ?? false)" />
                            </td>
                            <td>@($"{table.Schema}.{table.Name}")</td>
                            <td class="cols">@table.Columns cols</td>
                            <td class="rows">@Count(table.Rows) rows</td>
                        </tr>
                    }
                    </tbody>
                </table>

                <button type="button" class="read-tables" disabled="@(_picked.Count == 0)"
                        @onclick="ReadPicked">
                    read @_picked.Count of @_tables.Count
                </button>
            }
        }
        else
        {
            @* The existing markup, moved into this branch UNCHANGED: the `<label>Path` box and
               its `input.path`, the `@if (_peek is not null)` paragraph, the `<InputFile
               OnChange="Upload">` drop zone and its hint. Do not reword or restyle any of
               it — this task adds a branch, it does not touch the file path. *@
        }
```

Register the service where the workflow host builds its services — in `CodeGenerator/Web/BuildAppWebHost.cs`, in the `TryStart` service callback beside `AddSingleton(session)`:

```csharp
        s => { s.AddSingleton(session); s.AddSingleton<ISqlSchemaSource, SqlClientSchemaSource>(); }
```

Add to `workflow.css`:

```css
/* The table picker. Counts are right-aligned because they are read as a column of magnitudes -
   which table is the big one - rather than as labels. */
.tables { border-collapse: collapse; margin: 0.5rem 0; font-size: 0.9em; }
.tables td { padding: 0.15rem 0.5rem 0.15rem 0; }
.tables .cols, .tables .rows { text-align: right; color: var(--pending); }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~DataStageSqlTests"`
Expected: PASS, 5 tests.

- [ ] **Step 5: Look at it in a browser**

Not optional, and not replaceable by the tests above. Four consecutive branches in this repo have shipped a defect that compiled, passed its tests, and was visible only in a running page.

Write a throwaway MSTest in `CodeGenerator.Tests/Web/` that builds a `BuildSession`, advances it to `BuildStage.Data`, starts `BuildAppWebHost`, writes `host.Url` to a temp file and sleeps ~200s. Run it in the background, then drive the URL with patchright:

```
node <scratch>/drive.mjs "<url>"
```

importing patchright from
`file:///C:/Users/admin/.vscode/extensions/danielsanmedium.dscodegpt-*/standalone/node_modules/patchright/index.mjs`
with `channel: 'chromium'`. The browser-automation skill's `--script` flag is broken on Windows — it never converts the path to a `file://` URL.

Check: the picker's columns line up, the counts are legible, a long table name does not break the row, and the console is clean. **Delete the throwaway test afterwards**, and kill its `testhost` — it locks the test DLL against the next build.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerator/Web/Components/Stages/DataStage.razor CodeGenerator/Web/BuildAppWebHost.cs CodeGenerator/wwwroot/workflow.css CodeGenerator.Tests/Web/DataStageSqlTests.cs CodeGenerator.Tests/Web/FakeSqlSchemaSource.cs
git commit -m "feat: pick the tables to read on the data stage"
```

---

### Task 10: The documentation

**Files:**
- Modify: `CodeGenerationEngine/Core/CommandDictionary.cs`
- Regenerate: `docs/commands.md`
- Modify: `docs/user-manual-source.html`
- Regenerate: `docs/SmartCodeGenerator-User-Manual.docx`

**Interfaces:** none.

**Both doc artefacts are generated from `CommandDictionary` and neither may be hand-edited.** `CommandReferenceDocTests` guards `commands.md`; `ManualAppendixDocTests` guards the manual's chapter-18 appendix. Forgetting the second one is what turned a green suite red on the previous branch.

- [ ] **Step 1: Update the catalog entry**

In `CommandDictionary.cs`, find the entry whose `Command` is
`"build app: <description> from <format> <location>"`. Its description says
`Formats: csv, txt, json, excel (sql is not in this build yet).` Replace that sentence with:

```
Formats: csv, txt, json, excel, sql. For sql the location is a SQL Server connection string, and
the tables are chosen interactively on the Data stage - the list shows each table's columns and
row count before you pick, because every row of what you pick is read. A piped run names them
with 'tables A,B,C', or names none and reads them all. Column types and relationships come from
the database rather than being inferred, and any password in the connection string is masked
wherever the run is echoed back to you.
```

Keep the existing example row and add a second entry for the SQL form:

```csharp
        new()
        {
            Category = "Build App",
            Command = "build app: <description> from sql \"<connection>\" [tables <A,B>]",
            Description = "Builds the model from a SQL Server database. The location is a "
                        + "connection string; interactively the Data stage lists the tables with "
                        + "their column and row counts and you tick the ones you want, and "
                        + "nothing is read until you do. 'tables A,B,C' names them for a piped "
                        + "run, and naming none reads them all. Column types come from the "
                        + "database instead of being inferred from the text, and declared foreign "
                        + "keys become relationships rather than being guessed from column names. "
                        + "A bigint column becomes int and a uniqueidentifier becomes string; "
                        + "both are reported. Any password is masked wherever the run is echoed.",
            Example = "build app: order tracking from sql \"Server=.;Database=Northwind;Trusted_Connection=True\" tables Customers,Orders"
        },
```

- [ ] **Step 2: Regenerate `commands.md`**

Run: `UPDATE_COMMAND_DOCS=1 dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~CommandReferenceDocTests"`
Expected: PASS, and `git diff docs/commands.md` shows the two changed rows.

Note the filter: `CommandDictionaryDataTests` is a different class and regenerates nothing.

- [ ] **Step 3: Write the manual section**

In `docs/user-manual-source.html`, in the chapter that covers data sources, add a section after the existing format list:

```html
<h3>Building from a SQL Server database</h3>

<p>The location for <code>sql</code> is a connection string rather than a path. Nothing is read
   when you give it: the tool lists what the database holds and waits.</p>

<pre>Northwind on .\SQLEXPRESS — 13 tables

  [x] dbo.Customers        11 cols      91 rows
  [x] dbo.Orders           14 cols     830 rows
  [ ] dbo.__EFMigrations…   2 cols       1 row</pre>

<p>The counts are there because <strong>every row of every table you tick is read</strong>. There
   is no cap, deliberately — a silent truncation would be one more way for imported data to differ
   from what you believe it is — so the number beside each table is what makes ticking it an
   informed choice. A row count shown as <code>—</code> means the login could not read it, which
   needs the <code>VIEW DATABASE STATE</code> permission; the table can still be read.</p>

<p>Two things come across that a CSV export would have thrown away. <strong>Column types</strong>
   are the database's own, so none of the guessing described above happens on this path at all.
   <strong>Foreign keys</strong> become relationships — read from the constraint that enforces
   them, rather than inferred from a column being called <code>CustomerId</code>.</p>

<p>The generator has five types, so two mappings lose something and say so: a <code>bigint</code>
   becomes an <code>int</code>, and a <code>uniqueidentifier</code> becomes a <code>string</code>.
   Composite foreign keys are skipped rather than half-emitted.</p>

<p>For a scripted run, name the tables on the line. Naming none reads all of them:</p>

<pre>build app: order tracking from sql "Server=.;Database=Northwind;Trusted_Connection=True" tables Customers,Orders</pre>

<div class="note"><p>A password in the connection string is masked wherever the run is echoed back
   — on the review page, in the console sign-off, and in the replay line — and the connection
   string is never written to <code>.codegen/</code>.</p></div>
```

- [ ] **Step 4: Regenerate the appendix, then the document**

Run in this order:
```
UPDATE_MANUAL_APPENDIX=1 dotnet test CodeGenerator.Tests/CodeGenerator.Tests.csproj --filter "FullyQualifiedName~ManualAppendixDocTests"
powershell -File tools/build-user-manual.ps1
powershell -File tools/verify-user-manual.ps1
```
Expected: the appendix test passes and rewrites the generated block; the build reports a page count; verify reports `All checks passed` with TOC links equal to heading bookmarks.

If `verify-user-manual.ps1` fails on `$PSScriptRoot` being empty, invoke it with an absolute path via `&` in PowerShell rather than `-File`.

- [ ] **Step 5: Run the full suite as CI runs it**

Run:
```
dotnet build SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-incremental -warnaserror
dotnet test SmartCodeGenerator/SmartCodeGenerator.slnx -c Release --no-build
```
Expected: `Build succeeded.` and every project green.

When reading the output, grep for `"^  Failed "` as well as `Failed!` — the summary line matches `Failed!` and the per-test line does not, so a filter on `Failed!` alone loses the name of what broke.

- [ ] **Step 6: Commit**

```bash
git add CodeGenerationEngine/Core/CommandDictionary.cs docs/commands.md docs/user-manual-source.html docs/SmartCodeGenerator-User-Manual.docx
git commit -m "docs: sql is a format this build reads"
```

---

## Self-review notes

**Spec coverage.** Package reference → 7. `SqlServerCatalog` → 5. `SqlServerReader` → 6. `TabularForeignKey` and its projection → 1, 4. Data stage branch → 9. `tables A,B,C` → 8. Redaction → 3. Catalog and manual → 10. `SqlTypeMap` and its two lossy cases → 2. Every spec section has a task.

**Not covered by any task, deliberately, and both are in the spec's Risks:** composite foreign keys are filtered out in the adapter's SQL (Task 7) rather than detected and reported — a first pass that drops them is what the spec calls for, and reporting them would need a channel that does not exist yet. And there is no guard against ticking a 40M-row table; the counts in the picker are the mitigation the spec chose.

**Ordering.** Tasks 1–6 and 8 need no database and no package. Task 7 introduces the dependency, and is the point at which `sql` becomes selectable. Task 9 needs 5 and 7. Task 10 is last because `commands.md` and the appendix must be regenerated after the catalog's final wording.
