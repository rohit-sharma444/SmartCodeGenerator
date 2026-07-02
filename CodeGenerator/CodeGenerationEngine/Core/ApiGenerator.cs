using CodeGenerationEngine.Core;

public static class ApiGenerator
{
    public static void Generate(string root, string apiName)
    {
        ControllerGenerator.Generate(root, apiName);

        ServiceGenerator.Generate(root, apiName + "Service");
    }
}