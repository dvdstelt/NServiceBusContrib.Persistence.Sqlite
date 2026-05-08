namespace Demo.Sender;

using Demo.Messages;
using NServiceBus;

public class OrderAcceptedHandler : IHandleMessages<OrderAccepted>
{
    public Task Handle(OrderAccepted message, IMessageHandlerContext context)
    {
        Console.WriteLine($"[Sender] OrderAccepted reply received: {message.OrderId}");
        return Task.CompletedTask;
    }
}
