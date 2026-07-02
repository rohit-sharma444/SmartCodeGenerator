namespace CodeGenerationEngine.Models;

public static class SynonymDictionary
{
    public static readonly Dictionary<string, string[]> Synonyms =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["list"] =
            [
                "show",
                "display",
                "provide",
                "give",
                "enumerate",
                "reveal"
            ],

            ["create"] =
            [
                "generate",
                "make",
                "build",
                "add",
                "new"
            ],

            ["delete"] =
            [
                "remove",
                "erase"
            ],

            ["restore"] =
            [
                "recover",
                "undelete"
            ],

            ["use"] =
            [
                "select",
                "switch",
                "open"
            ],

            ["analyze"] =
            [
                "inspect",
                "review",
                "check"
            ],

            ["compile"] =
            [
                "build"
            ],
            ["create"] = ["make", "generate", "build", "construct", "develop"],
            ["class"] = ["entity", "model", "dto", "business object"],
            ["service"] = ["manager", "handler", "processor"],
            ["repository"] = ["storage", "data access", "persistence"],
            ["controller"] = ["api controller", "endpoint"],
            ["project"] = ["application", "solution", "app"]
        };
}