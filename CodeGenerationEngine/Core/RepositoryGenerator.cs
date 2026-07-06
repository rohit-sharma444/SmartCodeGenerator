namespace CodeGenerationEngine.Core;

public static class RepositoryGenerator
{
    public static void Generate(string root, string repositoryName)
    {
        Directory.CreateDirectory(Path.Combine(root, "Repositories"));

        var interfaceCode =
        $@"public interface I{repositoryName}
        {{
        }}
        ";

        var classCode =
        $@"public class {repositoryName}
            : I{repositoryName}
        {{
        }}
        ";

        File.WriteAllText(Path.Combine(root, "Repositories", $"I{repositoryName}.cs"), interfaceCode);

        File.WriteAllText(Path.Combine(root, "Repositories", $"{repositoryName}.cs"), classCode);
    }
}