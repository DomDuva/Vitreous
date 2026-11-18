namespace Vitreous.CachingTests.Redis;

/// <summary>
/// A fact that needs a real Redis server. Runs when the <c>VITREOUS_TEST_REDIS</c> environment
/// variable holds a connection string (e.g. <c>localhost:6379</c>); skipped otherwise.
/// </summary>
public sealed class RedisFactAttribute : FactAttribute
{
    public static string? ConnectionString => Environment.GetEnvironmentVariable("VITREOUS_TEST_REDIS");

    public RedisFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            Skip = "Set VITREOUS_TEST_REDIS to a Redis connection string to run this test.";
    }
}
