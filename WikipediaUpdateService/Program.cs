using DomainDiscovery.Wikipedia.Dump;
using WikipediaUpdateService;

var builder = Host.CreateApplicationBuilder(args);

// Lets the same executable run under services.msc and, for diagnosis, straight from a console.
builder.Services.AddWindowsService(options =>
    options.ServiceName = "SmartCodeGenWikipediaUpdater");

builder.Services.Configure<ServiceOptions>(
    builder.Configuration.GetSection(ServiceOptions.SectionName));

builder.Services.AddSingleton<IUpdateLog, EventLogUpdateLog>();
builder.Services.AddHostedService<UpdateWorker>();

await builder.Build().RunAsync();
