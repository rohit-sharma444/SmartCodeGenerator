using NaturalLanguageEngine.Core.Prompts;
using NaturalLanguageEngine.Models.Semantics;
using Xunit;

namespace NaturalLanguageEngine.Tests.Core;

public class PromptStoreTests : IDisposable
{
    private readonly string _tempDir;

    public PromptStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NaturalLanguageEngineTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string PathFor(string fileName) => Path.Combine(_tempDir, fileName);

    [Fact]
    public void Load_ReturnsEmptyList_WhenFileDoesNotExist()
    {
        var store = new PromptStore();

        var records = store.Load(PathFor("missing.json"));

        Assert.Empty(records);
    }

    [Fact]
    public void Append_CreatesFile_WithSingleRecord()
    {
        var store = new PromptStore();
        var path = PathFor("prompts.json");

        store.Append(path, new PromptRecord
        {
            Prompt = "Sarah booked a flight to London tomorrow.",
            Timestamp = DateTimeOffset.UtcNow,
            Result = new SemanticResult { Confidence = 0.9 }
        });

        Assert.True(File.Exists(path));
        var records = store.Load(path);
        var record = Assert.Single(records);
        Assert.Equal("Sarah booked a flight to London tomorrow.", record.Prompt);
    }

    [Fact]
    public void Append_AddsToExistingRecords_RatherThanOverwriting()
    {
        var store = new PromptStore();
        var path = PathFor("prompts.json");

        store.Append(path, new PromptRecord { Prompt = "first sentence" });
        store.Append(path, new PromptRecord { Prompt = "second sentence" });

        var records = store.Load(path);

        Assert.Equal(2, records.Count);
        Assert.Equal("first sentence", records[0].Prompt);
        Assert.Equal("second sentence", records[1].Prompt);
    }

    [Fact]
    public void Append_CreatesDirectory_WhenItDoesNotExist()
    {
        var store = new PromptStore();
        var path = PathFor(Path.Combine("nested", "dir", "prompts.json"));

        store.Append(path, new PromptRecord { Prompt = "hello" });

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Append_PreservesEntitiesOnPreviouslySavedRecords_AcrossMultipleAppends()
    {
        // Regression test: SemanticResult.Entities/Relationships/Diagnostics
        // are read-only collection properties. Without
        // PreferredObjectCreationHandling.Populate, System.Text.Json
        // silently drops their contents when Load() re-deserializes a
        // record that was already saved - so a second Append() call would
        // otherwise wipe out the entities saved by the first one.
        var store = new PromptStore();
        var path = PathFor("prompts.json");

        var firstResult = new SemanticResult { Confidence = 0.9 };
        firstResult.Entities.Add(new Entity { Text = "Sarah", Type = EntityType.Person });
        firstResult.Relationships.Add(new Relationship
        {
            Source = new Entity { Text = "Sarah", Type = EntityType.Person },
            Target = new Entity { Text = "booked", Type = EntityType.Action },
            Type = RelationshipType.Subject
        });

        store.Append(path, new PromptRecord { Prompt = "first", Result = firstResult });
        store.Append(path, new PromptRecord { Prompt = "second", Result = new SemanticResult() });

        var records = store.Load(path);

        Assert.Equal(2, records.Count);
        var firstRecord = records[0];
        Assert.NotNull(firstRecord.Result);
        var firstEntity = Assert.Single(firstRecord.Result!.Entities);
        Assert.Equal("Sarah", firstEntity.Text);
        var firstRelationship = Assert.Single(firstRecord.Result!.Relationships);
        Assert.Equal(RelationshipType.Subject, firstRelationship.Type);
    }

    [Fact]
    public void Save_Overwrites_ExistingFile()
    {
        var store = new PromptStore();
        var path = PathFor("prompts.json");

        store.Append(path, new PromptRecord { Prompt = "will be overwritten" });
        store.Save(path, new List<PromptRecord>
        {
            new() { Prompt = "only this one remains" }
        });

        var records = store.Load(path);
        var record = Assert.Single(records);
        Assert.Equal("only this one remains", record.Prompt);
    }
}
