namespace CodeGenerationEngine.Core;

public static class ServiceGenerator
{
    public static void Generate(string root, string serviceName)
    {
        var code = $@"namespace Generated.Services;
        public class {serviceName}
        {{
        }}
        ";

        Directory.CreateDirectory(Path.Combine(root, "Services"));

        File.WriteAllText(Path.Combine(root, "Services", serviceName + ".cs"), code);
    }
}