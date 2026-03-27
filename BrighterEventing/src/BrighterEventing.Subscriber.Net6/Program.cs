// Minimal .NET 6 subscriber: optional Cosmos inbox via BrighterMessaging.CosmosDb; ASB/Rabbit from appsettings.

using BrighterEventing.Messaging.Configuration;
using BrighterEventing.Subscriber.Net6.Configuration;
using BrighterEventing.Sample.DomainEvents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Paramore.Brighter.ServiceActivator.Extensions.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((_, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
        config.AddJsonFile("secrets.json", optional: true, reloadOnChange: false);
    })
    .ConfigureLogging((context, logging) =>
    {
        // CreateDefaultBuilder registers Windows Event Log on Windows; it throws when Event Log is unavailable (e.g. restricted/sandboxed).
        logging.ClearProviders();
        logging.AddConfiguration(context.Configuration.GetSection("Logging"));
        logging.AddConsole();
        logging.AddDebug();
    })
    .ConfigureServices((context, services) =>
    {
        services.Configure<TestingOptions>(context.Configuration.GetSection(TestingOptions.SectionName));
        services.AddBrighterEventingSubscriberMessaging(
            context.Configuration,
            catalog => catalog.AddSampleOrderEvents(),
            typeof(OrderCreatedEvent).Assembly);
        services.AddHostedService<ServiceActivatorHostedService>();
        services.Configure<HostOptions>(opts => opts.ShutdownTimeout = TimeSpan.FromSeconds(30));
    })
    .Build();

await host.RunAsync();
