using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Paramore.Brighter;
using Paramore.Brighter.Observability;
using CosmosPartitionKey = Microsoft.Azure.Cosmos.PartitionKey;

namespace BrighterEventing.Messaging.CosmosDb.Durability;

internal sealed class CosmosBrighterOutbox :
    IAmAnOutboxSync<Message, object>,
    IAmAnOutboxAsync<Message, object>
{
    private readonly Container _container;

    public CosmosBrighterOutbox(Container container) => _container = container;

    public IAmABrighterTracer? Tracer { private get; set; }
    public bool ContinueOnCapturedContext { get; set; }

    public void Add(Message message, RequestContext requestContext, int outBoxTimeout = -1, IAmABoxTransactionProvider<object>? transactionProvider = null)
        => AddAsync(message, requestContext, outBoxTimeout, transactionProvider).GetAwaiter().GetResult();

    public void Add(IEnumerable<Message> messages, RequestContext? requestContext, int outBoxTimeout = -1, IAmABoxTransactionProvider<object>? transactionProvider = null)
    {
        foreach (var message in messages) Add(message, requestContext ?? new RequestContext(), outBoxTimeout, transactionProvider);
    }

    public async Task AddAsync(Message message, RequestContext requestContext, int outBoxTimeout = -1, IAmABoxTransactionProvider<object>? transactionProvider = null, CancellationToken cancellationToken = default)
    {
        var id = (string)message.Header.MessageId;
        var doc = new OutboxDocument
        {
            id = id,
            MessageId = id,
            Topic = message.Header.Topic,
            MessageJson = JsonSerializer.Serialize(message, BrighterMessageCosmosJson.Options),
            Dispatched = false,
            CreatedUtc = DateTimeOffset.UtcNow,
            DispatchedUtc = null
        };
        await _container.UpsertItemAsync(doc, new CosmosPartitionKey(id), cancellationToken: cancellationToken).ConfigureAwait(ContinueOnCapturedContext);
    }

    public async Task AddAsync(IEnumerable<Message> messages, RequestContext? requestContext, int outBoxTimeout = -1, IAmABoxTransactionProvider<object>? transactionProvider = null, CancellationToken cancellationToken = default)
    {
        foreach (var message in messages)
            await AddAsync(message, requestContext ?? new RequestContext(), outBoxTimeout, transactionProvider, cancellationToken).ConfigureAwait(ContinueOnCapturedContext);
    }

    public void Delete(Id[] messageIds, RequestContext? requestContext, Dictionary<string, object>? args = null)
        => DeleteAsync(messageIds, requestContext ?? new RequestContext(), args).GetAwaiter().GetResult();

    public async Task DeleteAsync(Id[] messageIds, RequestContext requestContext, Dictionary<string, object>? args = null, CancellationToken cancellationToken = default)
    {
        foreach (var messageId in messageIds.Select(id => (string)id))
        {
            await _container.DeleteItemAsync<OutboxDocument>(messageId, new CosmosPartitionKey(messageId), cancellationToken: cancellationToken)
                .ConfigureAwait(ContinueOnCapturedContext);
        }
    }

    public IEnumerable<Message> DispatchedMessages(TimeSpan dispatchedSince, RequestContext requestContext, int pageSize = 100, int pageNumber = 1, int outBoxTimeout = -1, Dictionary<string, object>? args = null)
        => DispatchedMessagesAsync(dispatchedSince, requestContext, pageSize, pageNumber, outBoxTimeout, args).GetAwaiter().GetResult();

    public async Task<IEnumerable<Message>> DispatchedMessagesAsync(TimeSpan dispatchedSince, RequestContext requestContext, int pageSize = 100, int pageNumber = 1, int outBoxTimeout = -1, Dictionary<string, object>? args = null, CancellationToken cancellationToken = default)
    {
        var from = DateTimeOffset.UtcNow - dispatchedSince;
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.Dispatched = true AND c.DispatchedUtc >= @from ORDER BY c.DispatchedUtc OFFSET @offset LIMIT @limit")
            .WithParameter("@from", from)
            .WithParameter("@offset", Math.Max(0, pageNumber - 1) * pageSize)
            .WithParameter("@limit", pageSize);
        return await QueryMessagesAsync(query, cancellationToken).ConfigureAwait(ContinueOnCapturedContext);
    }

    public Message Get(Id messageId, RequestContext requestContext, int outBoxTimeout = -1, Dictionary<string, object>? args = null)
        => GetAsync(messageId, requestContext, outBoxTimeout, args).GetAwaiter().GetResult();

    public async Task<Message> GetAsync(Id messageId, RequestContext requestContext, int outBoxTimeout = -1, Dictionary<string, object>? args = null, CancellationToken cancellationToken = default)
    {
        var id = (string)messageId;
        var response = await _container.ReadItemAsync<OutboxDocument>(id, new CosmosPartitionKey(id), cancellationToken: cancellationToken)
            .ConfigureAwait(ContinueOnCapturedContext);
        var message = JsonSerializer.Deserialize<Message>(response.Resource.MessageJson, BrighterMessageCosmosJson.Options)
                      ?? throw new InvalidOperationException($"Outbox message '{id}' could not be deserialized.");
        CosmosDeserializedMessageNormalizer.NormalizeAfterDeserialize(message);
        return message;
    }

    public IEnumerable<Message> Get(IEnumerable<Id> messageIds, RequestContext requestContext, int outBoxTimeout = -1, Dictionary<string, object>? args = null)
        => GetAsync(messageIds, requestContext, outBoxTimeout, args).GetAwaiter().GetResult();

    public async Task<IEnumerable<Message>> GetAsync(IEnumerable<Id> messageIds, RequestContext requestContext, int outBoxTimeout = -1, Dictionary<string, object>? args = null, CancellationToken cancellationToken = default)
    {
        var list = new List<Message>();
        foreach (var messageId in messageIds)
        {
            list.Add(await GetAsync(messageId, requestContext, outBoxTimeout, args, cancellationToken).ConfigureAwait(ContinueOnCapturedContext));
        }

        return list;
    }

    public void MarkDispatched(Id id, RequestContext requestContext, DateTimeOffset? dispatchedAt = null, Dictionary<string, object>? args = null)
        => MarkDispatchedAsync(id, requestContext, dispatchedAt, args).GetAwaiter().GetResult();

    public async Task MarkDispatchedAsync(Id id, RequestContext requestContext, DateTimeOffset? dispatchedAt = null, Dictionary<string, object>? args = null, CancellationToken cancellationToken = default)
    {
        var key = (string)id;
        var response = await _container.ReadItemAsync<OutboxDocument>(key, new CosmosPartitionKey(key), cancellationToken: cancellationToken)
            .ConfigureAwait(ContinueOnCapturedContext);
        var doc = response.Resource;
        doc.Dispatched = true;
        doc.DispatchedUtc = dispatchedAt ?? DateTimeOffset.UtcNow;
        await _container.ReplaceItemAsync(doc, key, new CosmosPartitionKey(key), cancellationToken: cancellationToken)
            .ConfigureAwait(ContinueOnCapturedContext);
    }

    public IEnumerable<Message> OutstandingMessages(TimeSpan dispatchedSince, RequestContext? requestContext, int pageSize = 100, int pageNumber = 1, IEnumerable<RoutingKey>? trippedTopics = null, Dictionary<string, object>? args = null)
        => OutstandingMessagesAsync(dispatchedSince, requestContext ?? new RequestContext(), pageSize, pageNumber, trippedTopics, args).GetAwaiter().GetResult();

    public async Task<IEnumerable<Message>> OutstandingMessagesAsync(TimeSpan dispatchedSince, RequestContext requestContext, int pageSize = 100, int pageNumber = 1, IEnumerable<RoutingKey>? trippedTopics = null, Dictionary<string, object>? args = null, CancellationToken cancellationToken = default)
    {
        var upperBound = DateTimeOffset.UtcNow - dispatchedSince;
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.Dispatched = false AND c.CreatedUtc <= @upperBound ORDER BY c.CreatedUtc OFFSET @offset LIMIT @limit")
            .WithParameter("@upperBound", upperBound)
            .WithParameter("@offset", Math.Max(0, pageNumber - 1) * pageSize)
            .WithParameter("@limit", pageSize);
        return await QueryMessagesAsync(query, cancellationToken).ConfigureAwait(ContinueOnCapturedContext);
    }

    public int GetOutstandingMessageCount(TimeSpan dispatchedSince, RequestContext? requestContext, int maxCount = 100, Dictionary<string, object>? args = null)
        => GetOutstandingMessageCountAsync(dispatchedSince, requestContext, maxCount, args).GetAwaiter().GetResult();

    public async Task<int> GetOutstandingMessageCountAsync(TimeSpan dispatchedSince, RequestContext? requestContext, int maxCount = 100, Dictionary<string, object>? args = null, CancellationToken cancellationToken = default)
    {
        var upperBound = DateTimeOffset.UtcNow - dispatchedSince;
        var query = new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.Dispatched = false AND c.CreatedUtc <= @upperBound")
            .WithParameter("@upperBound", upperBound);
        using var it = _container.GetItemQueryIterator<int>(query, requestOptions: new QueryRequestOptions { MaxItemCount = 1 });
        if (!it.HasMoreResults) return 0;
        var page = await it.ReadNextAsync(cancellationToken).ConfigureAwait(ContinueOnCapturedContext);
        return Math.Min(page.FirstOrDefault(), maxCount);
    }

    public async Task MarkDispatchedAsync(IEnumerable<Id> ids, RequestContext requestContext, DateTimeOffset? dispatchedAt = null, Dictionary<string, object>? args = null, CancellationToken cancellationToken = default)
    {
        foreach (var id in ids)
            await MarkDispatchedAsync(id, requestContext, dispatchedAt, args, cancellationToken).ConfigureAwait(ContinueOnCapturedContext);
    }

    private async Task<IEnumerable<Message>> QueryMessagesAsync(QueryDefinition query, CancellationToken cancellationToken)
    {
        var result = new List<Message>();
        using var it = _container.GetItemQueryIterator<OutboxDocument>(query);
        while (it.HasMoreResults)
        {
            var page = await it.ReadNextAsync(cancellationToken).ConfigureAwait(ContinueOnCapturedContext);
            foreach (var item in page)
            {
                var message = JsonSerializer.Deserialize<Message>(item.MessageJson, BrighterMessageCosmosJson.Options);
                if (message != null)
                {
                    CosmosDeserializedMessageNormalizer.NormalizeAfterDeserialize(message);
                    result.Add(message);
                }
            }
        }

        return result;
    }

    private sealed class OutboxDocument
    {
        public string id { get; set; } = string.Empty;
        public string MessageId { get; set; } = string.Empty;
        public string Topic { get; set; } = string.Empty;
        public string MessageJson { get; set; } = string.Empty;
        public bool Dispatched { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }
        public DateTimeOffset? DispatchedUtc { get; set; }
    }
}
