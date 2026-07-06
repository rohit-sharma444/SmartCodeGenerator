using System.Text.Json;
using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Core;

public static class IntentRepository
{
    private static readonly object LockObject = new();

    private static readonly string FilePath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "intents.json");

    private static List<IntentRule>? _cache;

    public static void Initialize()
    {
        lock (LockObject)
        {
            if (!File.Exists(FilePath))
            {
                _cache = GetDefaultRules();
                Save(_cache);
                return;
            }

            LoadCache();

            foreach (var rule in GetDefaultRules())
            {
                AddOrUpdate(rule);
            }

            Save(_cache);
        }
    }

    public static IReadOnlyList<IntentRule> GetAll()
    {
        lock (LockObject)
        {
            if (_cache == null)
            {
                LoadCache();
            }

            return _cache!;
        }
    }

    public static IntentRule? Find(IntentType intent)
    {
        return GetAll().FirstOrDefault(x => x.Intent == intent);
    }

    public static bool Exists(IntentType intent)
    {
        return Find(intent) != null;
    }

    public static void AddOrUpdate(IntentRule rule)
    {
        lock (LockObject)
        {
            _cache ??= [];

            var existing = _cache.FirstOrDefault(x => x.Intent == rule.Intent);

            if (existing == null)
            {
                _cache.Add(rule);
                return;
            }

            existing.Priority = rule.Priority;

            existing.Actions = existing.Actions
                .Union(rule.Actions, StringComparer.OrdinalIgnoreCase)
                .ToList();

            existing.Targets = existing.Targets
                .Union(rule.Targets, StringComparer.OrdinalIgnoreCase)
                .ToList();

            existing.Keywords = existing.Keywords
                .Union(rule.Keywords, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public static bool Remove(IntentType intent)
    {
        lock (LockObject)
        {
            if (_cache == null)
            {
                LoadCache();
            }

            var removed =
                _cache!.RemoveAll(x => x.Intent == intent);

            if (removed > 0)
            {
                SaveInternal();
            }

            return removed > 0;
        }
    }

    public static void Reload()
    {
        lock (LockObject)
        {
            _cache = null;
            LoadCache();
        }
    }

    public static void Save(IEnumerable<IntentRule> rules)
    {
        lock (LockObject)
        {
            _cache = rules.ToList();
            SaveInternal();
        }
    }

    private static void LoadCache()
    {
        EnsureFileExists();
        var json = File.ReadAllText(FilePath);
        _cache = JsonSerializer.Deserialize<List<IntentRule>>(json) ?? [];
    }

    private static void SaveInternal()
    {
        var json = JsonSerializer.Serialize(_cache,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

        File.WriteAllText(FilePath, json);
    }

    private static void EnsureFileExists()
    {
        if (File.Exists(FilePath))
            return;

        var defaults = GetDefaultRules();

        var json = JsonSerializer.Serialize(defaults,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

        File.WriteAllText(FilePath, json);
    }

    private static List<IntentRule> GetDefaultRules()
    {
        return
        [
            // =====================================================
            // HELP
            // =====================================================

            new()
        {
            Intent = IntentType.Help,
            Priority = 100,
            Actions = ["help"],
            Targets = ["help"]
        },

        // =====================================================
        // PROJECTS
        // =====================================================

        new()
        {
            Intent = IntentType.ListProjects,
            Priority = 90,
            Actions = ["list", "show", "display", "view"],
            Targets = ["project", "projects"]
        },

        new()
        {
            Intent = IntentType.UseProject,
            Priority = 100,
            Actions = ["use", "select", "switch", "open", "activate"],
            Targets = ["project"]
        },

        new()
        {
            Intent = IntentType.CurrentProject,
            Priority = 100,
            Actions = ["current", "active", "selected"],
            Targets = ["project"]
        },

        new()
        {
            Intent = IntentType.CreateProject,
            Priority = 100,
            Actions = ["create", "new", "generate"],
            Targets =
            [
                "project",
                "console project",
                "webapi project",
                "class library",
                "worker service",
                "blazor project",
                "wpf project"
            ]
        },

        // =====================================================
        // CLASSES
        // =====================================================

        new()
        {
            Intent = IntentType.CreateClass,
            Priority = 100,
            Actions = ["create", "add", "generate"],
            Targets = ["class"]
        },

        new()
        {
            Intent = IntentType.ClassInherit,
            Priority = 90,
            Actions = ["inherit", "extend", "derive"],
            Targets = ["class"]
        },

        new()
        {
            Intent = IntentType.ShowClass,
            Priority = 90,
            Actions = ["show", "view", "display", "open"],
            Targets = ["class"]
        },

        new()
        {
            Intent = IntentType.ListClasses,
            Priority = 90,
            Actions = ["list", "show", "display"],
            Targets = ["classes"]
        },

        new()
        {
            Intent = IntentType.ListClassesInProject,
            Priority = 95,
            Actions = ["list", "show", "display"],
            Targets = ["classes in"]
        },

        new()
        {
            Intent = IntentType.DeleteClass,
            Priority = 100,
            Actions = ["delete", "remove"],
            Targets = ["class"]
        },

        new()
        {
            Intent = IntentType.RestoreClass,
            Priority = 100,
            Actions = ["restore", "recover"],
            Targets = ["class"]
        },

        new()
        {
            Intent = IntentType.RestoreLastClass,
            Priority = 100,
            Actions = ["restore"],
            Targets = ["last class"]
        },

        new()
        {
            Intent = IntentType.ListDeletedClasses,
            Priority = 90,
            Actions = ["list", "show"],
            Targets = ["deleted classes"]
        },

        new()
        {
            Intent = IntentType.RenameClass,
            Priority = 100,
            Actions = ["rename"],
            Targets = ["class"]
        },

        // =====================================================
        // PROPERTIES
        // =====================================================

        new()
        {
            Intent = IntentType.CreateProperty,
            Priority = 100,
            Actions = ["add", "create"],
            Targets = ["property"]
        },

        new()
        {
            Intent = IntentType.ListProperties,
            Priority = 90,
            Actions = ["list", "show"],
            Targets = ["properties"]
        },

        new()
        {
            Intent = IntentType.DeleteProperty,
            Priority = 100,
            Actions = ["delete", "remove"],
            Targets = ["property"]
        },

        new()
        {
            Intent = IntentType.RenameProperty,
            Priority = 100,
            Actions = ["rename"],
            Targets = ["property"]
        },

        // =====================================================
        // METHODS
        // =====================================================

        new()
        {
            Intent = IntentType.CreateMethod,
            Priority = 90,
            Actions = ["add", "create", "generate"],
            Targets = ["method"]
        },

        new()
        {
            Intent = IntentType.ListMethods,
            Priority = 90,
            Actions = ["list", "show"],
            Targets = ["methods"]
        },

        // =====================================================
        // ARCHITECTURE
        // =====================================================

        new()
        {
            Intent = IntentType.CreateService,
            Priority = 100,
            Actions = ["create", "generate"],
            Targets = ["service"]
        },

        new()
        {
            Intent = IntentType.CreateRepository,
            Priority = 100,
            Actions = ["create", "generate"],
            Targets = ["repository"]
        },

        new()
        {
            Intent = IntentType.CreateInterface,
            Priority = 100,
            Actions = ["create", "generate"],
            Targets = ["interface"]
        },

        new()
        {
            Intent = IntentType.CreateController,
            Priority = 100,
            Actions = ["create", "generate"],
            Targets = ["controller"]
        },

        new()
        {
            Intent = IntentType.CreateApi,
            Priority = 100,
            Actions = ["create", "generate"],
            Targets = ["api", "endpoint"]
        },

        // =====================================================
        // ANALYSIS
        // =====================================================

        new()
        {
            Intent = IntentType.AnalyzeProject,
            Priority = 90,
            Actions = ["analyze"],
            Targets = ["project"]
        },

        new()
        {
            Intent = IntentType.AnalyzeClass,
            Priority = 90,
            Actions = ["analyze"],
            Targets = ["class"]
        },

        // =====================================================
        // VALIDATION
        // =====================================================

        new()
        {
            Intent = IntentType.ValidateProject,
            Priority = 90,
            Actions = ["validate"],
            Targets = ["project"]
        },

        // =====================================================
        // DOCUMENTATION
        // =====================================================

        new()
        {
            Intent = IntentType.GenerateDocumentation,
            Priority = 90,
            Actions = ["generate"],
            Targets =
            [
                "documentation",
                "design document",
                "project report"
            ]
        },

        new()
        {
            Intent = IntentType.GenerateDesignDocument,
            Priority = 90,
            Actions = ["generate"],
            Targets =
            [
                "design document",
                "architecture document",
                "technical document"            ]
        }

        ];
    }
}
