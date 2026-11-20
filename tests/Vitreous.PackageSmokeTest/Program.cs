using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Vitreous;
using Vitreous.Abstractions;

var services = new ServiceCollection();
services.AddSingleton<Log>();
services.AddVitreousHandlers();

await using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
await using var scope = root.CreateAsyncScope();
var sp = scope.ServiceProvider;

// The reflection fallback also implements ISender, so only this proves the generator ran.
var sender = sp.GetRequiredService<ISender>();
Check(sender.GetType().Name == "VitreousDispatcher", $"ISender is {sender.GetType().FullName}, so the source generator did not run");

var sum = await sender.Send(new Add(2, 3));
Check(sum.IsSuccess && sum.Value == 5, $"Add returned {sum}");

var renamed = await sender.Send(new Rename("smoke"));
Check(renamed.IsSuccess, $"Rename returned {renamed}");

var names = await sender.Send(new FindNames(2));
Check(names.IsSuccess && names.Value.SequenceEqual(["name-1", null]), $"FindNames returned {names}");

var stream = await sender.CreateStream(new Count(3));
Check(stream.IsSuccess, $"Count returned {stream}");
var items = new List<int>();
await foreach (var i in stream.Value)
    items.Add(i);
Check(items.SequenceEqual([1, 2, 3]), $"Count streamed [{string.Join(", ", items)}]");

await sp.GetRequiredService<IPublisher>().Publish(new Renamed("smoke"));
var log = sp.GetRequiredService<Log>();
Check(log.Entries.Count == 2, $"Renamed reached {log.Entries.Count} handlers, expected 2");

Console.WriteLine($"Vitreous package smoke test passed on {Environment.Version}.");
return 0;

static void Check(bool condition, string failure)
{
    if (condition)
        return;
    Console.Error.WriteLine($"FAIL: {failure}");
    Environment.Exit(1);
}

public sealed class Log
{
    public List<string> Entries { get; } = [];
}

public sealed record Add(int A, int B) : ICommand<int>;
public sealed class AddHandler : ICommandHandler<Add, int>
{
    public ValueTask<Result<int>> Handle(Add command, CancellationToken ct = default) => new(command.A + command.B);
}

public sealed record Rename(string Name) : ICommand;
public sealed class RenameHandler : ICommandHandler<Rename>
{
    public ValueTask<Result> Handle(Rename command, CancellationToken ct = default) => new(Result.Success);
}

// Nullable type argument: the generated registration must keep the annotation (CS8631 otherwise).
public sealed record FindNames(int Count) : IQuery<IReadOnlyList<string?>>;
public sealed class FindNamesHandler : IQueryHandler<FindNames, IReadOnlyList<string?>>
{
    public ValueTask<Result<IReadOnlyList<string?>>> Handle(FindNames query, CancellationToken ct = default) =>
        new(Enumerable.Range(1, query.Count).Select(static i => i % 2 == 1 ? $"name-{i}" : null).ToList());
}

public sealed record Count(int N) : IStreamQuery<int>;
public sealed class CountHandler : StreamQueryHandler<Count, int>
{
    protected override async IAsyncEnumerable<int> Stream(Count query, [EnumeratorCancellation] CancellationToken ct)
    {
        for (var i = 1; i <= query.N; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }
}

public sealed record Renamed(string Name) : INotification;
public sealed class AuditRenamed(Log log) : INotificationHandler<Renamed>
{
    public ValueTask Handle(Renamed notification, CancellationToken ct = default)
    {
        log.Entries.Add(nameof(AuditRenamed));
        return default;
    }
}
public sealed class IndexRenamed(Log log) : INotificationHandler<Renamed>
{
    public ValueTask Handle(Renamed notification, CancellationToken ct = default)
    {
        log.Entries.Add(nameof(IndexRenamed));
        return default;
    }
}
