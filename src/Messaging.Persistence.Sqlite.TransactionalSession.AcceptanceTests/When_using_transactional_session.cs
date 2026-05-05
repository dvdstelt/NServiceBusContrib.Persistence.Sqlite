namespace Messaging.Persistence.Sqlite.TransactionalSession.AcceptanceTests;

using Messaging.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NServiceBus;
using NServiceBus.AcceptanceTesting;
using NServiceBus.TransactionalSession;
using NUnit.Framework;

public class When_using_transactional_session : NServiceBusAcceptanceTest
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task Should_send_message_and_persist_row_when_committed(bool withOutbox)
    {
        var context = await Scenario.Define<Context>()
            .WithEndpoint<AnEndpoint>(s => s.When(async (_, ctx) =>
            {
                using var scope = ctx.ServiceProvider.CreateScope();
                using var transactionalSession = scope.ServiceProvider.GetRequiredService<ITransactionalSession>();

                await transactionalSession.Open();
                ctx.SessionId = transactionalSession.SessionId;

                var sqlite = transactionalSession.SynchronizedStorageSession.SqliteSession();
                await InsertSampleRow(sqlite, transactionalSession.SessionId);

                await transactionalSession.SendLocal(new SampleMessage());
                await transactionalSession.Commit();
            }))
            .Done(c => c.MessageReceived)
            .Run();

        Assert.That(await SampleRowExists(context.SessionId!), Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Should_resolve_typed_storage_session_from_di_and_persist_row(bool withOutbox)
    {
        var context = await Scenario.Define<Context>()
            .WithEndpoint<AnEndpoint>(s => s.When(async (_, ctx) =>
            {
                using var scope = ctx.ServiceProvider.CreateScope();
                using var transactionalSession = scope.ServiceProvider.GetRequiredService<ITransactionalSession>();

                await transactionalSession.Open();
                ctx.SessionId = transactionalSession.SessionId;

                var sqlite = scope.ServiceProvider.GetRequiredService<ISqliteStorageSession>();
                await InsertSampleRow(sqlite, transactionalSession.SessionId);

                await transactionalSession.SendLocal(new SampleMessage());
                await transactionalSession.Commit();
            }))
            .Done(c => c.MessageReceived)
            .Run();

        Assert.That(await SampleRowExists(context.SessionId!), Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Should_not_send_or_persist_when_session_is_not_committed(bool withOutbox)
    {
        var context = await Scenario.Define<Context>()
            .WithEndpoint<AnEndpoint>(s => s.When(async (statelessSession, ctx) =>
            {
                using (var scope = ctx.ServiceProvider.CreateScope())
                {
                    using var transactionalSession = scope.ServiceProvider.GetRequiredService<ITransactionalSession>();
                    await transactionalSession.Open(new SqliteOpenSessionOptions());
                    ctx.SessionId = transactionalSession.SessionId;

                    await transactionalSession.SendLocal(new SampleMessage());

                    var sqlite = transactionalSession.SynchronizedStorageSession.SqliteSession();
                    await InsertSampleRow(sqlite, transactionalSession.SessionId);
                    // Deliberately not Commit().
                }

                // Send an immediate-dispatch message so the test can finish without waiting on
                // the deliberately uncommitted SampleMessage.
                await statelessSession.SendLocal(new CompleteTestMessage());
            }))
            .Done(c => c.CompleteMessageReceived)
            .Run();

        Assert.That(context.CompleteMessageReceived, Is.True);
        Assert.That(context.MessageReceived, Is.False);
        Assert.That(await SampleRowExists(context.SessionId!), Is.False);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Should_send_immediate_dispatch_messages_without_committing(bool withOutbox)
    {
        var context = await Scenario.Define<Context>()
            .WithEndpoint<AnEndpoint>(s => s.When(async (_, ctx) =>
            {
                using var scope = ctx.ServiceProvider.CreateScope();
                using var transactionalSession = scope.ServiceProvider.GetRequiredService<ITransactionalSession>();
                await transactionalSession.Open(new SqliteOpenSessionOptions());

                var sendOptions = new SendOptions();
                sendOptions.RequireImmediateDispatch();
                sendOptions.RouteToThisEndpoint();
                await transactionalSession.Send(new SampleMessage(), sendOptions);
                // Deliberately not Commit().
            }))
            .Done(c => c.MessageReceived)
            .Run();

        Assert.That(context.MessageReceived, Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Should_allow_storage_writes_without_outgoing_messages(bool withOutbox)
    {
        var context = await Scenario.Define<Context>()
            .WithEndpoint<AnEndpoint>(s => s.When(async (statelessSession, ctx) =>
            {
                using (var scope = ctx.ServiceProvider.CreateScope())
                {
                    using var transactionalSession = scope.ServiceProvider.GetRequiredService<ITransactionalSession>();
                    await transactionalSession.Open();
                    ctx.SessionId = transactionalSession.SessionId;

                    var sqlite = transactionalSession.SynchronizedStorageSession.SqliteSession();
                    await InsertSampleRow(sqlite, transactionalSession.SessionId);

                    await transactionalSession.Commit();
                }

                await statelessSession.SendLocal(new CompleteTestMessage());
            }))
            .Done(c => c.CompleteMessageReceived)
            .Run();

        Assert.That(await SampleRowExists(context.SessionId!), Is.True);
    }

    static async Task InsertSampleRow(ISqliteStorageSession sqliteSession, string id)
    {
        await using var insert = sqliteSession.Connection.CreateCommand();
        insert.Transaction = sqliteSession.Transaction;
        insert.CommandText = $"INSERT INTO {SetupFixture.SampleTableName} (Id) VALUES ($id);";
        insert.Parameters.AddWithValue("$id", id);
        await insert.ExecuteNonQueryAsync();
    }

    static async Task<bool> SampleRowExists(string id)
    {
        await using var connection = new SqliteConnection(SetupFixture.ConnectionString);
        await connection.OpenAsync();
        await using var query = connection.CreateCommand();
        query.CommandText = $"SELECT COUNT(*) FROM {SetupFixture.SampleTableName} WHERE Id = $id;";
        query.Parameters.AddWithValue("$id", id);
        return (long)(await query.ExecuteScalarAsync())! > 0;
    }

    class Context : TransactionalSessionTestContext
    {
        public bool MessageReceived { get; set; }
        public bool CompleteMessageReceived { get; set; }
        public string? SessionId { get; set; }
    }

    class AnEndpoint : EndpointConfigurationBuilder
    {
        public AnEndpoint()
        {
            var withOutbox = (bool)TestContext.CurrentContext.Test.Arguments[0]!;
            if (withOutbox)
            {
                EndpointSetup<TransactionSessionWithOutboxEndpoint>();
            }
            else
            {
                EndpointSetup<TransactionSessionDefaultServer>();
            }
        }

        class SampleHandler(Context testContext) : IHandleMessages<SampleMessage>
        {
            public Task Handle(SampleMessage message, IMessageHandlerContext context)
            {
                testContext.MessageReceived = true;
                return Task.CompletedTask;
            }
        }

        class CompleteTestMessageHandler(Context testContext) : IHandleMessages<CompleteTestMessage>
        {
            public Task Handle(CompleteTestMessage message, IMessageHandlerContext context)
            {
                testContext.CompleteMessageReceived = true;
                return Task.CompletedTask;
            }
        }
    }

    class SampleMessage : ICommand { }
    class CompleteTestMessage : ICommand { }
}
