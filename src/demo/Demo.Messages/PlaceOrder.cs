namespace Demo.Messages;

using NServiceBus;

public class PlaceOrder : ICommand
{
    public string OrderId { get; init; } = "";
    public decimal Amount { get; init; }
}
