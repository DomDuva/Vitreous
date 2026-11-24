# VitreousApi.1

An ASP.NET Core minimal API built with [Vitreous](https://domduva.github.io/Vitreous/). Its code is organized in **vertical slices**: each feature keeps its message, handler and endpoint in one file, so you read, change or delete a feature in one place.

## Layout

```text
src/VitreousApi.1/
  Program.cs                  service registration and the pipeline behaviors
  Common/                     code every feature shares: Result → HTTP mapping, logging behavior
  Features/Todos/             one feature: a file per command or query, plus its endpoints
<!--#if (UseValidation) -->
    CreateTodo.cs             command + validator + handler + endpoint
<!--#else -->
    CreateTodo.cs             command + handler + endpoint
<!--#endif -->
<!--#if (UseCaching) -->
    GetTodo.cs                cached query
<!--#else -->
    GetTodo.cs                query
<!--#endif -->
    ListTodos.cs              stream query (IAsyncEnumerable)
    CompleteTodo.cs           command without a result, publishes TodoCompleted
    TodoCompleted.cs          notification and its handler
    TodoStore.cs              in-memory storage
<!--#if (aot) -->
  Common/AppJsonContext.cs    every JSON type, for the source-generated serializer
<!--#endif -->
<!--#if (tests) -->
tests/                        endpoint tests (WebApplicationFactory) and handler tests
<!--#endif -->
```

## Run it

```sh
dotnet run --project src/VitreousApi.1
```

<!--#if (UseOpenApi && UseDevUI) -->
Then open `/scalar/v1` for the API reference and `/vitreous` for the developer UI, which shows a live trace of every command and query.
<!--#elseif (UseOpenApi) -->
Then open `/scalar/v1` for the API reference.
<!--#endif -->
<!--#if (caching == "redis") -->

Queries are cached in Redis. Start one locally first with `docker compose up -d`. The connection string is in `appsettings.Development.json`. When Redis is down, queries still run, without the cache.
<!--#endif -->
<!--#if (tests) -->

## Test it

```sh
dotnet test
```
<!--#endif -->
<!--#if (aot) -->

## Publish with Native AOT

```sh
dotnet publish src/VitreousApi.1 -c Release -r linux-x64
```

The output is one native executable under `src/VitreousApi.1/bin/Release/net10.0/linux-x64/publish/`. Trim and AOT warnings fail the build. Every type an endpoint reads or writes must be listed in `Common/AppJsonContext.cs`.
<!--#endif -->

## Add a feature

1. Add a folder under `Features/`, or a file to an existing one.
2. Create the message and its handler in that folder, with an item template or by hand:

   ```sh
   cd src/VitreousApi.1/Features/Orders
   dotnet new vitreous-command -n PlaceOrder --result Guid
   dotnet new vitreous-query -n GetOrder
   ```

   The handler throws until you write it, and the namespace follows the folder.
<!--#if (UseValidation) -->
   Add `--validation` for a validator.
<!--#endif -->

3. Map its endpoint, and call `ToHttpResult()` to turn the `Result` into a response.

The source generator registers new handlers at build time, so there is nothing to add to `Program.cs`.

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
<!--#if (UseDevUI) -->
- [Developer UI](https://domduva.github.io/Vitreous/guides/developer-ui/)
<!--#endif -->
<!--#if (aot) -->
- [Native AOT](https://domduva.github.io/Vitreous/guides/native-aot/)
<!--#endif -->
