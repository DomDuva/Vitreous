# VitreousWorker.1

A background service built with [Vitreous](https://domduva.github.io/Vitreous/). A `BackgroundService` looks for work on a timer and sends a command for each piece of it. The code is organized in **vertical slices**: each feature keeps its message and handler in one file, so you read, change or delete a feature in one place.

## Layout

```text
src/VitreousWorker.1/
  Program.cs                  service registration, the pipeline behaviors and sample data
  Common/                     code every feature shares: the logging behavior
  Features/Reminders/         one feature: a file per command or query, plus its worker
    ReminderWorker.cs         BackgroundService: the timer, and a scope per unit of work
    GetDueReminders.cs        query
<!--#if (UseCaching) -->
    GetRecipient.cs           cached query
<!--#else -->
    GetRecipient.cs           query
<!--#endif -->
<!--#if (UseValidation) -->
    SendReminder.cs           command + validator + handler, publishes ReminderSent
<!--#else -->
    SendReminder.cs           command + handler, publishes ReminderSent
<!--#endif -->
    ReminderSent.cs           notification and its handler
    ReminderStore.cs          in-memory storage
<!--#if (tests) -->
tests/                        worker tests (with a fake clock) and handler tests
<!--#endif -->
```

## Run it

```sh
dotnet run --project src/VitreousWorker.1
```

It sends three sample reminders within the first seconds, then keeps polling until you press Ctrl+C. `Reminders:Interval` in `appsettings.json` sets how often it looks for due reminders.
<!--#if (caching == "redis") -->

Queries are cached in Redis. Start one locally first with `docker compose up -d`. The connection string is in `appsettings.Development.json`. When Redis is down, queries still run, without the cache.
<!--#endif -->
<!--#if (tests) -->

## Test it

```sh
dotnet test
```
<!--#endif -->

## How the worker runs

- **A scope per unit of work.** A hosted service is a singleton, while handlers, `ISender` and `IPublisher` are scoped, like a `DbContext`. The worker takes an `IServiceScopeFactory` and opens a scope for each query and each command it sends. Injecting `ISender` into the worker's constructor fails at startup in Development.
- **A failure doesn't stop the loop.** A failed `Result` is logged by `LoggingBehavior`, and the reminder is tried again on the next tick. An unexpected exception is logged by the worker, because one that leaves `ExecuteAsync` stops the host.
- **Shutdown is graceful.** On Ctrl+C or SIGTERM, the handler that is running gets a cancelled token, and nothing new starts.
- **The clock is a service.** `TimeProvider` drives the timer and the handlers, so the tests move time forward instead of waiting.

## Add a feature

1. Add a folder under `Features/`, or a file to an existing one.
2. Create the message and its handler in that folder, with an item template or by hand:

   ```sh
   cd src/VitreousWorker.1/Features/Orders
   dotnet new vitreous-command -n PlaceOrder --result Guid
   dotnet new vitreous-query -n GetOrder
   ```

   The handler throws until you write it, and the namespace follows the folder.
<!--#if (UseValidation) -->
   Add `--validation` for a validator.
<!--#endif -->

3. Send it from a worker, in a scope of its own.

The source generator registers new handlers at build time, so there is nothing to add to `Program.cs`, except `AddHostedService` for a new worker.

## Learn more

- [Project templates](https://domduva.github.io/Vitreous/guides/templates/): every option of this template
- [Your first feature](https://domduva.github.io/Vitreous/getting-started/first-feature/)
- [Commands, queries and notifications](https://domduva.github.io/Vitreous/concepts/messages/)
- [Results and errors](https://domduva.github.io/Vitreous/concepts/results-and-errors/)
- [Pipeline behaviors](https://domduva.github.io/Vitreous/concepts/pipeline-behaviors/)
<!--#if (UseValidation) -->
- [Validation](https://domduva.github.io/Vitreous/guides/validation/)
<!--#endif -->
<!--#if (UseCaching) -->
- [Caching](https://domduva.github.io/Vitreous/guides/caching/)
<!--#endif -->
