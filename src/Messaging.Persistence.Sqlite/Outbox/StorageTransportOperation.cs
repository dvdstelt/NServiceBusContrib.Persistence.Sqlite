namespace Messaging.Persistence.Sqlite.Outbox;

using NServiceBus.Outbox;

sealed class StorageTransportOperation
{
    public string MessageId { get; set; } = "";
    public Dictionary<string, string> Options { get; set; } = [];
    public byte[] Body { get; set; } = [];
    public Dictionary<string, string> Headers { get; set; } = [];

    public static StorageTransportOperation From(TransportOperation op) =>
        new()
        {
            MessageId = op.MessageId,
            Options = op.Options is null ? [] : new Dictionary<string, string>(op.Options),
            Body = op.Body.ToArray(),
            Headers = op.Headers is null ? [] : new Dictionary<string, string>(op.Headers)
        };

    public TransportOperation ToTransportOperation() =>
        new(MessageId, new NServiceBus.Transport.DispatchProperties(Options), Body, Headers);
}
