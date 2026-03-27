using BrighterEventing.Messaging.Configuration;
using BrighterEventing.Publisher.Net6;
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
            catalog =>
            {
                catalog.Map<OrderCreatedEvent>(nameof(OrderCreatedEvent), "OrderCreated")
                    .WithCloudEventsType<OrderCreatedEvent>(SampleOrderEventNames.OrderCreated);
                catalog.Map<OrderUpdatedEvent>(nameof(OrderUpdatedEvent), "OrderUpdated")
                    .WithCloudEventsType<OrderUpdatedEvent>(SampleOrderEventNames.OrderUpdated);
                catalog.Map<OrderCancelledEvent>(nameof(OrderCancelledEvent), "OrderCancelled")
                    .WithCloudEventsType<OrderCancelledEvent>(SampleOrderEventNames.OrderCancelled);
            },
            typeof(OrderCreatedEvent).Assembly);
        services.AddHostedService<PublisherHostedService>();
    })
    .Build();

await host.RunAsync();
