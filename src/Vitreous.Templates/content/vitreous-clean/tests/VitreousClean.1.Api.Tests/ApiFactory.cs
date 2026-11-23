using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace VitreousClean._1.Api.Tests;

/// <summary>Runs the whole app in memory, with a database of its own that is deleted afterwards.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory();

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting(
            "ConnectionStrings:Database",
            $"Data Source={Path.Combine(_directory.FullName, "test.db")};Pooling=False");

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        _directory.Delete(recursive: true);
    }
}
