namespace NServiceBusContrib.Persistence.Sqlite.TransactionalSession.AcceptanceTests;

using NServiceBusContrib.Persistence.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NServiceBus;
using NServiceBus.AcceptanceTesting;
using NServiceBus.Configuration.AdvancedExtensibility;
using NServiceBus.TransactionalSession;
using NUnit.Framework;
using Conventions = NServiceBus.AcceptanceTesting.Customization.Conventions;

public class When_using_outbox_send_only : NServiceBusAcceptanceTest
{
    [Test]
    public async Task Should_send_messages_via_processor_endpoint_on_commit()
    {
        var context = await Scenario.Define<Context>()
            .WithEndpoint<SendOnlyEndpoint>(s => s.When(async (_, ctx) =>
            {
                using var scope = ctx.ServiceProvider.CreateScope();
                using var transactionalSession = scope.ServiceProvider.GetRequiredService<ITransactionalSession>();
                await transactionalSession.Open(new SqliteOpenSessionOptions());

                var sendOptions = new SendOptions();
                sendOptions.SetDestination(Conventions.EndpointNamingConvention.Invoke(typeof(ReceiverEndpoint)));
                await transactionalSession.Send(new SampleMessage(), sendOptions);

                await transactionalSession.Commit(CancellationToken.None);
            }))
            .WithEndpoint<ReceiverEndpoint>()
            .WithEndpoint<ProcessorEndpoint>()
            .Done(c => c.MessageReceived)
            .Run();

        Assert.That(context.MessageReceived, Is.True);
    }

    class Context : TransactionalSessionTestContext
    {
        public bool MessageReceived { get; set; }
    }

    class SendOnlyEndpoint : EndpointConfigurationBuilder
    {
        public SendOnlyEndpoint() => EndpointSetup<DefaultServer>(c =>
        {
            var persistence = c.GetSettings().Get<PersistenceExtensions<SqlitePersistence>>();
            persistence.EnableTransactionalSession(new TransactionalSessionOptions
            {
                ProcessorEndpoint = Conventions.EndpointNamingConvention.Invoke(typeof(ProcessorEndpoint))
            });

            c.EnableOutbox();
            c.SendOnly();
        });
    }

    class ReceiverEndpoint : EndpointConfigurationBuilder
    {
        public ReceiverEndpoint() => EndpointSetup<DefaultServer>();

        class SampleHandler(Context testContext) : IHandleMessages<SampleMessage>
        {
            public Task Handle(SampleMessage message, IMessageHandlerContext context)
            {
                testContext.MessageReceived = true;
                return Task.CompletedTask;
            }
        }
    }

    class ProcessorEndpoint : EndpointConfigurationBuilder
    {
        public ProcessorEndpoint() => EndpointSetup<TransactionSessionWithOutboxEndpoint>();
    }

    class SampleMessage : ICommand { }
}
