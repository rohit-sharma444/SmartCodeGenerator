using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core;

public static class VocabularyRepository
{
    private static readonly Lazy<VocabularyStore> _store = new(CreateStore);

    public static VocabularyStore Store => _store.Value;

    private static VocabularyStore CreateStore()
    {
        var store = new VocabularyStore();

        var file = GetVocabularyFile();

        store.Load(file);

        Seed(store);

        return store;
    }

    public static void Save()
    {
        Store.Save(GetVocabularyFile());
    }

    private static string GetVocabularyFile()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, ".codegenerator");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "vocabulary.json");
    }

    private static void Seed(VocabularyStore store)
    {
        store.Learn("create", "create", TokenCategory.Action, 1);
        store.Learn("generate", "create", TokenCategory.Action, 1);
        store.Learn("build", "create", TokenCategory.Action, 1);
        store.Learn("construct", "create", TokenCategory.Action, 1);
        store.Learn("current", "current", TokenCategory.Action, 1);

        store.Learn("show", "list", TokenCategory.Action, 1);
        store.Learn("display", "list", TokenCategory.Action, 1);
        store.Learn("provide", "list", TokenCategory.Action, 1);
        store.Learn("retrieve", "list", TokenCategory.Action, 1);

        store.Learn("entity", "class", TokenCategory.Artifact, 1);
        store.Learn("model", "class", TokenCategory.Artifact, 1);
        store.Learn("dto", "class", TokenCategory.Artifact, 1);

        store.Learn("repository", "repository", TokenCategory.Artifact, 1);
        store.Learn("service", "service", TokenCategory.Artifact, 1);
        store.Learn("controller", "controller", TokenCategory.Artifact, 1);

        store.Learn("project", "project", TokenCategory.Artifact, 1);
        store.Learn("solution", "project", TokenCategory.Artifact, 1);
    }
}