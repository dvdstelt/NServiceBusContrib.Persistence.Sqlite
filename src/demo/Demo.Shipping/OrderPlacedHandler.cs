namespace Demo.Shipping;

using Demo.Messages;
using NServiceBus;

public class OrderPlacedHandler : IHandleMessages<OrderPlaced>
{
    public async Task Handle(OrderPlaced message, IMessageHandlerContext context)
    {
        Console.WriteLine($"[Shipping] OrderPlaced received: {message.OrderId}");
        await Task.Delay(500);
        await context.Publish(new OrderShipped { OrderId = message.OrderId });
        Console.WriteLine($"[Shipping] OrderShipped published: {message.OrderId}");
    }
}
