using CodeGenerator.App;

var starter = new CodeGeneratorStarter();

if (args.Length > 0)
{
    // Non-interactive: treat the joined args as a single command and exit.
    var command = string.Join(' ', args);
    var handled = starter.RunOnce(command);
    return handled ? 0 : 1;
}

starter.Run();
return 0;
