using CodeGenerator.App;

// First, before anything prints: the tool emits an em dash in dozens of places and the arrows
// the picker's hint depends on, none of which a legacy code page can render. Best-effort — a
// host that refuses leaves the previous encoding and everything still works.
ConsoleEncoding.UseUtf8();

// Answered before the starter is constructed, deliberately: its constructor resolves the
// workspace, scans for projects and loads rules, printing as it goes. A script reading
// `CodeGenerator --version` would otherwise have to strip that noise off the front.
switch (StartupFlags.Parse(args))
{
    case StartupFlag.Version:
        Console.WriteLine(StartupFlags.VersionText());
        return 0;

    case StartupFlag.Help:
        Console.WriteLine(StartupFlags.HelpText());
        return 0;
}

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
