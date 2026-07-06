namespace CodeGenerationEngine.Core;

public static class InterfaceGenerator
{
    public static void Generate(string root, string interfaceName)
    {
        var code = $@"namespace Generated.Interfaces;

        public interface {interfaceName}
        {{
        }}
        ";

        Directory.CreateDirectory(Path.Combine(root, "Interfaces"));

        File.WriteAllText(Path.Combine(root, "Interfaces", interfaceName + ".cs"), code);
    }
}