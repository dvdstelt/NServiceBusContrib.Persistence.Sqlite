namespace Demo.Messages;

using NServiceBus;

public class OrderShipped : IEvent
{
    public string OrderId { get; init; } = "";
}
