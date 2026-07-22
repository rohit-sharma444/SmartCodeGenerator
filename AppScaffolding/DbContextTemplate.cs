namespace AppScaffolding;

/// <summary>Emits <c>Data/AppDbContext.cs</c>: a SQLite context with one table per entity. Maps
/// scalar columns only — navigation and collection properties are ignored — so storage stays flat
/// and non-relational, matching JsonStore (a JSON store enforces no foreign keys either). Ids are
/// client-assigned, and the database is created on first use.</summary>
public static class DbContextTemplate
{
    /// <summary>Source of <c>Data/AppDbContext.cs</c> in <paramref name="projectName"/>.Data with a
    /// DbSet per entity in <paramref name="entityNames"/>. <paramref name="envVarName"/> names an
    /// environment variable holding a full SQLite connection string that overrides the default.</summary>
    public static string Source(
        string projectName, IReadOnlyList<string> entityNames, string envVarName)
    {
        var dbSets = string.Join("\n",
            entityNames.Select(n => $"    public DbSet<{n}> {n} => Set<{n}>();"));

        return $$"""
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using {{projectName}}.Models;

namespace {{projectName}}.Data;

/// <summary>SQLite database context. One table per entity, scalar columns only (navigation
/// properties are ignored to match JsonStore's flat storage). Client-assigned int Ids. Creates the
/// database file on first use.</summary>
public sealed class AppDbContext : DbContext
{
{{dbSets}}

    /// <summary>Opens the context and ensures the database and schema exist.</summary>
    public AppDbContext()
    {
        Database.EnsureCreated();
    }

    /// <inheritdoc />
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSqlite(ConnectionString());

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Snapshot the entity types: Ignore() below mutates the model, so iterating the live
        // collection could throw.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            var clr = entityType.ClrType;
            var entity = modelBuilder.Entity(clr);

            // Ids come from the store (NextId), never the database.
            entity.Property("Id").ValueGeneratedNever();

            // Map plain columns only; drop entity refs and collections so an unset foreign key is
            // just a 0 (as in JSON), never a constraint that fails an insert.
            foreach (var prop in clr.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (!IsScalar(prop.PropertyType))
                    entity.Ignore(prop.Name);
        }

        base.OnModelCreating(modelBuilder);
    }

    private static bool IsScalar(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive
            || type.IsEnum
            || type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(Guid);
    }

    private static string ConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("{{envVarName}}");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        return "Data Source=" + Path.Combine(AppContext.BaseDirectory, "{{projectName}}.db");
    }
}
""";
    }
}
