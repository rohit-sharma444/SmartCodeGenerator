namespace CodeGenerationEngine.Core;

public static class ControllerGenerator
{
    public static void Generate(string root, string controllerName)
    {
        Directory.CreateDirectory(Path.Combine(root, "Controllers"));

        var code =
        $@"using Microsoft.AspNetCore.Mvc;

        [ApiController]
        [Route(""api/[controller]"")]
        public class {controllerName}Controller
            : ControllerBase
        {{
            [HttpGet]
            public IActionResult Get()
            {{
                return Ok();
            }}
        }}
        ";

        File.WriteAllText(Path.Combine(root, "Controllers", controllerName + "Controller.cs"), code);
    }
}