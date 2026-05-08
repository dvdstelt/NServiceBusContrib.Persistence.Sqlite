namespace Demo.Sales;

using Demo.Messages;
using NServiceBus;

public class OrderSaga :
    Saga<OrderSaga.OrderSagaData>,
    IAmStartedByMessages<PlaceOrder>,
    IHandleMessages<OrderShipped>
{
    public class OrderSagaData : ContainSagaData
    {
        public string OrderId { get; set; } = "";
        public decimal Amount { get; set; }
        public bool Shipped { get; set; }
    }

    protected override void ConfigureHowToFindSaga(SagaPropertyMapper<OrderSagaData> mapper)
    {
        mapper.MapSaga(s => s.OrderId)
            .ToMessage<PlaceOrder>(m => m.OrderId)
            .ToMessage<OrderShipped>(m => m.OrderId);
    }

    public async Task Handle(PlaceOrder message, IMessageHandlerContext context)
    {
        Data.Amount = message.Amount;
        Console.WriteLine($"[Sales] PlaceOrder received: {message.OrderId} ({message.Amount:C}) - saga started");

        await context.Reply(new OrderAccepted { OrderId = message.OrderId });
        await context.Publish(new OrderPlaced { OrderId = message.OrderId });
    }

    public Task Handle(OrderShipped message, IMessageHandlerContext context)
    {
        Data.Shipped = true;
        Console.WriteLine($"[Sales] OrderShipped received: {message.OrderId} - saga complete");
        MarkAsComplete();
        return Task.CompletedTask;
    }
}
