// .NET 6 publisher: Cosmos DB outbox + Brighter producers (ASB or Rabbit) via BrighterMessaging.CosmosDb.

using BrighterEventing.Messaging.CosmosDb.Configuration;
using BrighterEventing.Publisher.Net6.Cosmos;
using BrighterEventing.Sample.DomainEvents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((_, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
        config.AddJsonFile("secrets.json", optional: true, reloadOnChange: false);
    })
    .ConfigureLogging((context, logging) =>
    {
        logging.ClearProviders();
        logging.AddConfiguration(context.Configuration.GetSection("Logging"));
        logging.AddConsole();
        logging.AddDebug();
    })
    .ConfigureServices((context, services) =>
    {
        services.AddBrighterEventingPublisherMessaging(
            context.Configuration,
            catalog => catalog.AddSampleOrderEvents(),
            typeof(Program).Assembly,
            SampleEventCatalog.Assembly);
        services.AddHostedService<PublisherHostedService>();
    })
    .Build();

await host.RunAsync();
