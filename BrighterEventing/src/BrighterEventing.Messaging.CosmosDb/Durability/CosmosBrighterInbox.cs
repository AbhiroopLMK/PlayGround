using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Paramore.Brighter;
using Paramore.Brighter.Observability;
using CosmosPartitionKey = Microsoft.Azure.Cosmos.PartitionKey;

namespace BrighterEventing.Messaging.CosmosDb.Durability;

internal sealed class CosmosBrighterInbox : IAmAnInboxSync, IAmAnInboxAsync
{
    private readonly Container _container;

    public CosmosBrighterInbox(Container container) => _container = container;

    public IAmABrighterTracer Tracer { private get; set; } = null!;
    public bool ContinueOnCapturedContext { get; set; }

    public void Add<T>(T command, string contextKey, RequestContext? requestContext, int timeoutInMilliseconds = -1) where T : class, IRequest
        => AddAsync(command, contextKey, requestContext, timeoutInMilliseconds).GetAwaiter().GetResult();

    public async Task AddAsync<T>(T command, string contextKey, RequestContext? requestContext, int timeoutInMilliseconds = -1, CancellationToken cancellationToken = default) where T : class, IRequest
    {
        var commandId = ResolveCommandId(command);
        var doc = new InboxDocument
        {
            id = commandId,
            ContextKey = contextKey,
            CommandId = commandId,
            CommandClrType = typeof(T).AssemblyQualifiedName ?? typeof(T).FullName ?? typeof(T).Name,
            CommandJson = JsonConvert.SerializeObject(command),
            CreatedUtc = DateTimeOffset.UtcNow
        };
        await _container.UpsertItemAsync(doc, new CosmosPartitionKey(doc.id), cancellationToken: cancellationToken).ConfigureAwait(ContinueOnCapturedContext);
    }

    public T Get<T>(string id, string contextKey, RequestContext? requestContext, int timeoutInMilliseconds = -1) where T : class, IRequest
        => GetAsync<T>(id, contextKey, requestContext, timeoutInMilliseconds).GetAwaiter().GetResult();

    public async Task<T> GetAsync<T>(string id, string contextKey, RequestContext? requestContext, int timeoutInMilliseconds = -1, CancellationToken cancellationToken = default) where T : class, IRequest
    {
        var response = await _container.ReadItemAsync<InboxDocument>(id, new CosmosPartitionKey(id), cancellationToken: cancellationToken)
            .ConfigureAwait(ContinueOnCapturedContext);
        return JsonConvert.DeserializeObject<T>(response.Resource.CommandJson)
               ?? throw new InvalidOperationException($"Inbox command '{id}' could not be deserialized.");
    }

    public bool Exists<T>(string id, string contextKey, RequestContext? requestContext, int timeoutInMilliseconds = -1) where T : class, IRequest
        => ExistsAsync<T>(id, contextKey, requestContext, timeoutInMilliseconds).GetAwaiter().GetResult();

    public async Task<bool> ExistsAsync<T>(string id, string contextKey, RequestContext? requestContext, int timeoutInMilliseconds = -1, CancellationToken cancellationToken = default) where T : class, IRequest
    {
        try
        {
            await _container.ReadItemAsync<InboxDocument>(id, new CosmosPartitionKey(id), cancellationToken: cancellationToken)
                .ConfigureAwait(ContinueOnCapturedContext);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private static string ResolveCommandId(IRequest command)
    {
        var idProp = command.GetType().GetProperty("Id");
        var raw = idProp?.GetValue(command)?.ToString();
        if (!string.IsNullOrWhiteSpace(raw)) return raw;
        throw new InvalidOperationException($"Command type '{command.GetType().Name}' must expose an Id property for inbox deduplication.");
    }

    private sealed class InboxDocument
    {
        public string id { get; set; } = string.Empty;
        public string ContextKey { get; set; } = string.Empty;
        public string CommandId { get; set; } = string.Empty;
        public string CommandClrType { get; set; } = string.Empty;
        public string CommandJson { get; set; } = string.Empty;
        public DateTimeOffset CreatedUtc { get; set; }
    }
}
