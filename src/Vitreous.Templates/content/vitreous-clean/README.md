# VitreousClean.1

<!--#if (HostApi) -->
An ASP.NET Core minimal API built with [Vitreous](https://domduva.github.io/Vitreous/), in the layers of Clean Architecture.
<!--#elseif (HostConsole) -->
A command-line app built with [Vitreous](https://domduva.github.io/Vitreous/), in the layers of Clean Architecture.
<!--#else -->
A background service built with [Vitreous](https://domduva.github.io/Vitreous/), in the layers of Clean Architecture.
<!--#endif -->
The business rules are in the middle and know nothing about the database or the app around them.

## Layout

| Project | What is in it |
|---|---|
| `src/VitreousClean.1.Domain` | The business rules: the `Order` aggregate, the `Money` value object and the domain events |
<!--#if (UseValidation) -->
| `src/VitreousClean.1.Application` | The use cases, a file per message with its validator and handler: `PlaceOrder`, `ShipOrder`, `GetOrder`, `ListOrders`. `EmailCustomer` handles the domain events. `Abstractions/` is what the use cases need from outside |
<!--#else -->
| `src/VitreousClean.1.Application` | The use cases, a file per message with its handler: `PlaceOrder`, `ShipOrder`, `GetOrder`, `ListOrders`. `EmailCustomer` handles the domain events. `Abstractions/` is what the use cases need from outside |
<!--#endif -->
| `src/VitreousClean.1.Infrastructure` | Implements those abstractions: EF Core with SQLite, and email |
<!--#if (HostApi) -->
| `src/VitreousClean.1.Api` | The host: endpoints, and the mapping from `Result` to HTTP |
<!--#elseif (HostConsole) -->
| `src/VitreousClean.1.Cli` | The host: verbs, and the mapping from `Result` to exit codes |
<!--#else -->
| `src/VitreousClean.1.Worker` | The host: a timer, and a scope per unit of work |
<!--#endif -->
<!--#if (tests) -->
| `tests/` | A project for each layer, one for the host, and one for the dependency rule |
<!--#endif -->

## The dependency rule

Dependencies point inwards: HOSTPROJECT → Infrastructure → Application → Domain. The domain uses nothing but `Vitreous`, and only for its abstractions: business rules return a `Result`, and domain events are notifications. Application uses EF Core's `DbSet`, but no database provider.
<!--#if (tests) -->

`tests/VitreousClean.1.Architecture.Tests` checks this. It fails as soon as a layer uses something it shouldn't, and lists what each layer may use.
<!--#endif -->

## Run it

<!--#if (HostApi) -->
```sh
dotnet run --project src/VitreousClean.1.Api
```

<!--#if (UseDevUI) -->
Then open `/scalar/v1` for the API reference and `/vitreous` for the developer UI, which shows a live trace of every command, query and domain event.
<!--#else -->
Then open `/scalar/v1` for the API reference.
<!--#endif -->
<!--#elseif (HostConsole) -->
```sh
dotnet run --project src/VitreousClean.1.Cli -- place ada@example.com Keyboard 2 49.90
dotnet run --project src/VitreousClean.1.Cli -- list
dotnet run --project src/VitreousClean.1.Cli -- ship <id>
```

Results go to stdout, errors and logs to stderr. The exit code is 0 on success, 1 when a handler returned an error, 2 for wrong arguments or invalid input, and 130 after Ctrl+C. Set `Logging__LogLevel__Default=Information` to log every command, query and email.
<!--#else -->
```sh
dotnet run --project src/VitreousClean.1.Worker
```

In Development it places two sample orders, ships them, and keeps polling until you press Ctrl+C. `Shipping:Interval` in `appsettings.json` sets how often it looks for placed orders. An order that fails stays placed and is tried again on the next tick.
<!--#endif -->
<!--#if (caching == "redis" && HostConsole) -->

Queries are cached in Redis. Start one locally first with `docker compose up -d`. The connection string is in `appsettings.json`. When Redis is down, queries still run, without the cache.
<!--#elseif (caching == "redis") -->

Queries are cached in Redis. Start one locally first with `docker compose up -d`. The connection string is in `appsettings.Development.json`. When Redis is down, queries still run, without the cache.
<!--#endif -->

## The database

<!--#if (HostConsole) -->
The orders are in a SQLite file in your local application data folder. Set the `ConnectionStrings__Database` environment variable to use another one.
<!--#else -->
The orders are in a SQLite file, `src/VitreousClean.1.HOSTPROJECT/VitreousClean.1.db`. Its connection string is in `appsettings.json`.
<!--#endif -->
The app creates it on the first run with `EnsureCreated`, which never changes a database that exists: after changing the model, delete the file. Switch to [EF Core migrations](https://learn.microsoft.com/ef/core/managing-schemas/migrations/) before you have data to keep.

There are no repositories. Handlers use EF Core through `IAppDbContext`: a `DbContext` already is a unit of work, and each `DbSet` a repository. `AppDbContext.SaveChangesAsync` saves, then publishes the domain events of the saved entities.
<!--#if (tests) -->

## Test it

```sh
dotnet test
```
<!--#endif -->

## Add a use case

1. Put the business rule in the domain, as a method of the aggregate that returns a `Result`.
2. Create the message and its handler in `src/VitreousClean.1.Application`, with an item template or by hand:

   ```sh
   dotnet new vitreous-command -n CancelOrder
   dotnet new vitreous-query -n GetInvoice
   ```

<!--#if (HostApi) -->
3. Map its endpoint in `src/VitreousClean.1.Api`, and call `ToHttpResult()` to turn the `Result` into a response.
<!--#elseif (HostConsole) -->
3. Add its verb to `OrderCli` in `src/VitreousClean.1.Cli`, and call `ToExitCode()` to turn the `Result` into an exit code.
<!--#else -->
3. Send it from a worker in `src/VitreousClean.1.Worker`, in a scope of its own.
<!--#endif -->

The source generator is installed on the host only. It finds the handlers in every layer at build time, so there is nothing to register.

## Learn more

- [Project templates](https://domduva.github.io/Vitreous/guides/templates/): every option of this template
- [Clean Architecture and DDD](https://domduva.github.io/Vitreous/guides/clean-architecture-ddd/)
- [Commands, queries and notifications](https://domduva.github.io/Vitreous/concepts/messages/)
- [Results and errors](https://domduva.github.io/Vitreous/concepts/results-and-errors/)
- [Pipeline behaviors](https://domduva.github.io/Vitreous/concepts/pipeline-behaviors/)
<!--#if (UseValidation) -->
- [Validation](https://domduva.github.io/Vitreous/guides/validation/)
<!--#endif -->
<!--#if (UseCaching) -->
- [Caching](https://domduva.github.io/Vitreous/guides/caching/)
<!--#endif -->
<!--#if (UseDevUI) -->
- [Developer UI](https://domduva.github.io/Vitreous/guides/developer-ui/)
<!--#endif -->
