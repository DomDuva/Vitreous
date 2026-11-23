using Vitreous;
using Vitreous.Abstractions;
#if (UseCaching)
using Vitreous.Caching.Abstractions;
#endif

namespace VitreousWorker._1.Features.Reminders;

#if (UseCaching)
// ICacheableQuery: the caching behavior returns the stored result for CacheKey and only runs the
// handler on a miss. A worker asks for the same few recipients over and over.
public sealed record GetRecipientQuery(string Name) : ICacheableQuery<RecipientDto>
{
    public string CacheKey => $"recipients:{Name}";
    public TimeSpan? AbsoluteExpiration => TimeSpan.FromMinutes(5);
    public TimeSpan? SlidingExpiration => null;
}
#else
public sealed record GetRecipientQuery(string Name) : IQuery<RecipientDto>;
#endif

public sealed record RecipientDto(string Name, string Email);

public sealed class GetRecipientHandler(RecipientDirectory directory) : IQueryHandler<GetRecipientQuery, RecipientDto>
{
    public ValueTask<Result<RecipientDto>> Handle(GetRecipientQuery query, CancellationToken ct = default) =>
        new(directory.Find(query.Name) is { } recipient ? recipient : Error.NotFound($"There is no recipient named '{query.Name}'."));
}

/// <summary>Stands in for where you keep your users: a database, or another service's API.</summary>
public sealed class RecipientDirectory
{
    private readonly Dictionary<string, RecipientDto> _recipients = new()
    {
        ["ada"]   = new("Ada", "ada@example.com"),
        ["grace"] = new("Grace", "grace@example.com"),
    };

    public RecipientDto? Find(string name) => _recipients.GetValueOrDefault(name);
}
