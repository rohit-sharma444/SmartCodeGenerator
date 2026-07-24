using DomainDiscovery.Wikipedia.Dump;
using WikipediaUpdateService;

// The Windows Service Control Manager starts this process with its working directory set to
// %SystemRoot%\System32, not the executable's own folder — so Host.CreateApplicationBuilder's
// default content root (Directory.GetCurrentDirectory()) would miss the appsettings.json published
// beside the exe once this runs as an installed service, and every Updater setting would silently
// revert to ServiceOptions.ApplyDefaults()'s hardcoded fallbacks. Pinning the content root to
// AppContext.BaseDirectory unconditionally (not just when running as a service) keeps console mode
// and service mode reading the same file, which matters for diagnosing the difference.
var builder = Host.CreateApplicationBuilder(
    new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });

// Lets the same executable run under services.msc and, for diagnosis, straight from a console.
builder.Services.AddWindowsService(options =>
    options.ServiceName = "SmartCodeGenWikipediaUpdater");

builder.Services.Configure<ServiceOptions>(
    builder.Configuration.GetSection(ServiceOptions.SectionName));

builder.Services.AddSingleton<IUpdateLog, EventLogUpdateLog>();
builder.Services.AddHostedService<UpdateWorker>();

await builder.Build().RunAsync();
