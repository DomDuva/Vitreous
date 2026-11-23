# VitreousConsole.1

A command-line app on the .NET Generic Host, built with [Vitreous](https://domduva.github.io/Vitreous/). Its code is organized in **vertical slices**: each feature keeps its message, handler and verb in one file, so you read, change or delete a feature in one place.

## Layout

```text
src/VitreousConsole.1/
  Program.cs                  service registration, the scope and Ctrl+C
  Common/                     code every feature shares: Result → exit code mapping, logging behavior
  Features/Todos/             one feature: a file per command or query
    TodoCli.cs                maps the arguments to the verbs
<!--#if (UseValidation) -->
    CreateTodo.cs             command + validator + handler + verb
<!--#else -->
    CreateTodo.cs             command + handler + verb
<!--#endif -->
    GetTodo.cs                query
    ListTodos.cs              stream query (IAsyncEnumerable)
    CompleteTodo.cs           command without a result, publishes TodoCompleted
    TodoCompleted.cs          notification and its handler
    TodoStore.cs              keeps the todos in a JSON file
<!--#if (tests) -->
tests/                        CLI tests (whole command lines) and handler tests
<!--#endif -->
```

## Run it

```sh
dotnet run --project src/VitreousConsole.1 -- add Buy milk
dotnet run --project src/VitreousConsole.1 -- list
dotnet run --project src/VitreousConsole.1 -- done 1
```

Results go to stdout, errors and logs to stderr. The exit code is 0 on success, 1 when a handler returned an error, 2 for wrong arguments or invalid input, and 130 after Ctrl+C.

The todos are kept in your local application data folder. Set the `TodosFile` environment variable, or add it to `appsettings.json`, to use another file. Set `Logging__LogLevel__Default=Debug` to log every command and query.
<!--#if (tests) -->

## Test it

```sh
dotnet test
```
<!--#endif -->

## Why a scope

Handlers are scoped, like a `DbContext`, and so are `ISender` and `IPublisher`. A web app gets a scope per request. A console app creates its own, which is what `Program.cs` does before it sends anything. Resolving `ISender` from `host.Services` directly would keep everything it uses alive until the app exits, and throws in the Development environment, which validates scopes.

## Add a feature

1. Add a folder under `Features/`, or a file to an existing one.
2. Create the message and its handler in that folder, with an item template or by hand:

   ```sh
   cd src/VitreousConsole.1/Features/Orders
   dotnet new vitreous-command -n PlaceOrder --result Guid
   dotnet new vitreous-query -n GetOrder
   ```

   The handler throws until you write it, and the namespace follows the folder.
<!--#if (UseValidation) -->
   Add `--validation` for a validator.
<!--#endif -->

3. Add its verb to `TodoCli`, and call `ToExitCode()` to turn the `Result` into an exit code.

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
