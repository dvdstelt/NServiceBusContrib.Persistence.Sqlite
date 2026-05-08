# Demo

End-to-end demo of the SQLite persister using `LearningTransport`. Demonstrates pub/sub, send & reply, a saga, and `ITransactionalSession` driven from a console.

## Topology

```
Demo.Sender ──PlaceOrder──▶ Demo.Sales (OrderSaga)
Demo.Sales ──reply OrderAccepted──▶ Demo.Sender
Demo.Sales ──publish OrderPlaced──▶ Demo.Shipping
Demo.Shipping ──publish OrderShipped──▶ Demo.Sales (saga completes)

Demo.TxClient ──atomic SQLite write + send PlaceOrder──▶ Demo.Sales (via outbox handoff)
```

`Demo.TxClient` is a **send-only** endpoint. It opens an `ITransactionalSession`, inserts a row into a custom `DemoOrderAudit` table, and sends `PlaceOrder` — all in one atomic commit. Because it uses `ProcessorEndpoint = "Demo.Sales"`, the outbox record lives in the Sales database and Sales actually dispatches the message.

Replies to `Demo.TxClient`'s sends are discarded (a send-only endpoint has no input queue).

## Storage

Every endpoint writes under `%TEMP%/nservicebuscontrib-sqlite-demo/`:

- `transport/` - LearningTransport message files, shared by all endpoints.
- `demo-sales.db` - shared by `Demo.Sales` and `Demo.TxClient`.
- `demo-shipping.db`, `demo-sender.db` - one per endpoint.

Tables (Outbox, Saga, Subscription) are created on startup because every endpoint calls `EnableInstallers()`. The audit table `DemoOrderAudit` is created by `Demo.TxClient` on launch.

To reset the demo, delete the `%TEMP%/nservicebuscontrib-sqlite-demo/` folder and re-run.

## Run order

Open four terminals from the repo root and run, in this order:

```sh
dotnet run --project src/demo/Demo.Sales
dotnet run --project src/demo/Demo.Shipping
dotnet run --project src/demo/Demo.Sender
```

Then in `Demo.Sender`, press Enter to send a `PlaceOrder`. You should see:

- `[Sender]` log the send and the `OrderAccepted` reply.
- `[Sales]` log the saga start and saga completion.
- `[Shipping]` log `OrderPlaced` received and `OrderShipped` published.

For the transactional-session demo, in a fourth terminal:

```sh
dotnet run --project src/demo/Demo.TxClient
```

Press Enter; `Demo.TxClient` writes a row to `DemoOrderAudit` and sends `PlaceOrder` atomically. Inspect the table with:

```sh
sqlite3 "$TMPDIR/nservicebuscontrib-sqlite-demo/demo-sales.db" "SELECT * FROM DemoOrderAudit;"
```

## Notes on subscriptions

LearningTransport supports native pub/sub, so subscribers register at startup automatically. The first time `Demo.Shipping` runs while `Demo.Sales` is publishing, it may miss the very first event - that's expected for any pub/sub system. Either start subscribers first, or send a second message.
