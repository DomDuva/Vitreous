using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vitreous.Abstractions;
using Vitreous.DevUI;
using Vitreous.DevUI.Extensions;
using Vitreous.DevUI.Tracing;

namespace Vitreous.IntegrationTests.DevUI;

/// <summary>
/// Hosts the DevUI in a minimal app per test, so each test controls the environment,
/// options and how the UI is mounted.
/// </summary>
public sealed class DevUiHostTests
{
    // ── Fixture messages (discovered by scanning this assembly) ─────────────────

    public sealed record LoginCommand(string UserName, string Password, [Sensitive] int Pin, string RefreshToken)
        : ICommand<LoginResult>;

    public sealed record LoginResult(string AccessToken, int ExpiresIn);

    public sealed class LoginCommandHandler : ICommandHandler<LoginCommand, LoginResult>
    {
        public ValueTask<Result<LoginResult>> Handle(LoginCommand command, CancellationToken ct = default)
            => new(new LoginResult("access-token-value", 3600));
    }

    public sealed record FaultingStreamQuery : IStreamQuery<int>;

    public sealed class FaultingStreamQueryHandler : StreamQueryHandler<FaultingStreamQuery, int>
    {
        protected override async IAsyncEnumerable<int> Stream(FaultingStreamQuery query, [EnumeratorCancellation] CancellationToken ct)
        {
            yield return 1;
            await Task.Yield();
            throw new InvalidOperationException("stream broke");
        }
    }

    public sealed record FailingInternalQuery : IQuery<int>;

    public sealed class FailingInternalQueryHandler : IQueryHandler<FailingInternalQuery, int>
    {
        public ValueTask<Result<int>> Handle(FailingInternalQuery query, CancellationToken ct = default)
            => new(Error.Internal("wrapped failure", new TimeoutException("db timed out")));
    }

    // ── Host ───────────────────────────────────────────────────────────────────

    private static async Task<WebApplication> StartAsync(
        string environment,
        Action<VitreousDevUIOptions>? configure = null,
        Action<WebApplicationBuilder>? builderSetup = null,
        Action<WebApplication>? mount = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddVitreousHandlers(
            o => o.DefaultHandlerLifetime = ServiceLifetime.Singleton,
            typeof(DevUiHostTests).Assembly);
        builder.Services.AddVitreousDevUI(configure);
        builderSetup?.Invoke(builder);

        var app = builder.Build();
        if (mount is null)
            app.UseVitreousDevUI();
        else
            mount(app);

        app.MapPost("/login", async (LoginCommand command, ISender sender) =>
            (await sender.Send(command)).IsSuccess ? Results.Ok() : Results.BadRequest());

        app.MapGet("/faulting-stream", async (ISender sender) =>
        {
            var stream = await sender.CreateStream(new FaultingStreamQuery());
            try { await foreach (var _ in stream.Value) { } }
            catch (InvalidOperationException) { }
            return Results.Ok();
        });

        app.MapGet("/failing-internal", async (ISender sender) =>
            (await sender.Send(new FailingInternalQuery())).IsSuccess ? Results.Ok() : Results.Problem());

        await app.StartAsync();
        return app;
    }

    private static async Task<JsonElement[]> GetTracesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement[]>("/vitreous/api/traces"))!;

    private static LoginCommand Login() => new("ada", "hunter2", 1234, "refresh-token-value");

    // ── Activation ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Production_ByDefault_ServesNothingAndTracesNothing()
    {
        await using var app = await StartAsync(Environments.Production);
        var client = app.GetTestClient();

        (await client.GetAsync("/vitreous")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/vitreous/api/traces")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await client.PostAsJsonAsync("/login", Login())).EnsureSuccessStatusCode();

        app.Services.GetRequiredService<TraceStore>().Count.Should().Be(0);
        app.Services.GetRequiredService<DevUIState>().IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Production_WhenEnabled_IsActive()
    {
        await using var app = await StartAsync(Environments.Production, o => o.Enabled = true);
        var client = app.GetTestClient();

        (await client.GetAsync("/vitreous")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/login", Login())).EnsureSuccessStatusCode();

        (await GetTracesAsync(client)).Should().Contain(t => t.GetProperty("messageType").GetString() == "LoginCommand");
    }

    [Fact]
    public async Task Development_WhenDisabled_ServesNothing()
    {
        await using var app = await StartAsync(Environments.Development, o => o.Enabled = false);

        (await app.GetTestClient().GetAsync("/vitreous")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── MapVitreousDevUI ───────────────────────────────────────────────────────

    [Fact]
    public async Task MapVitreousDevUI_ServesTheUiTracesAndConfig()
    {
        await using var app = await StartAsync(
            Environments.Development,
            o => o.TraceBufferCapacity = 123,
            mount: a => a.MapVitreousDevUI());
        var client = app.GetTestClient();

        var page = await client.GetAsync("/vitreous");
        page.StatusCode.Should().Be(HttpStatusCode.OK);
        page.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await client.GetAsync("/vitreous/")).StatusCode.Should().Be(HttpStatusCode.OK);

        var config = await client.GetFromJsonAsync<JsonElement>("/vitreous/api/config");
        config.GetProperty("traceBufferCapacity").GetInt32().Should().Be(123);

        (await client.PostAsJsonAsync("/login", Login())).EnsureSuccessStatusCode();
        (await GetTracesAsync(client)).Should().NotBeEmpty();
    }

    [Fact]
    public async Task MapVitreousDevUI_RequireAuthorization_RejectsAnonymousRequests()
    {
        await using var app = await StartAsync(
            Environments.Development,
            builderSetup: b =>
            {
                b.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, AnonymousOnlyHandler>("Test", null);
                b.Services.AddAuthorization();
            },
            mount: a =>
            {
                a.UseAuthentication();
                a.UseAuthorization();
                a.MapVitreousDevUI().RequireAuthorization();
            });
        var client = app.GetTestClient();

        foreach (var path in new[] { "/vitreous", "/vitreous/api/traces", "/vitreous/api/config", "/vitreous/events" })
            (await client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, path);
    }

    [Fact]
    public async Task MapVitreousDevUI_InProduction_MapsNothing()
    {
        await using var app = await StartAsync(Environments.Production, mount: a => a.MapVitreousDevUI());

        (await app.GetTestClient().GetAsync("/vitreous")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UiPage_SendsAStrictContentSecurityPolicy_MatchingItsInlineBlocks()
    {
        await using var app = await StartAsync(Environments.Development, mount: a => a.MapVitreousDevUI());

        var response = await app.GetTestClient().GetAsync("/vitreous");
        var html = await response.Content.ReadAsStringAsync();
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();

        csp.Should().Contain("default-src 'none'").And.Contain("frame-ancestors 'none'").And.NotContain("unsafe-inline");
        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");

        // The hashes must match the page's actual inline blocks, or the browser blocks them.
        foreach (var tag in new[] { "script", "style" })
        {
            var block = System.Text.RegularExpressions.Regex.Match(html, $"<{tag}>(.*?)</{tag}>", System.Text.RegularExpressions.RegexOptions.Singleline);
            var hash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(block.Groups[1].Value)));
            csp.Should().Contain($"'sha256-{hash}'", tag);
        }

        html.Should().NotContain("style=\"", "inline style attributes would be blocked by the CSP");
    }

    // ── Captured data ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SensitiveValues_AreMaskedInInputAndOutput()
    {
        await using var app = await StartAsync(Environments.Development);
        var client = app.GetTestClient();

        (await client.PostAsJsonAsync("/login", Login())).EnsureSuccessStatusCode();

        var trace = (await GetTracesAsync(client)).Single(t => t.GetProperty("messageType").GetString() == "LoginCommand");
        using var input  = JsonDocument.Parse(trace.GetProperty("serializedInput").GetString()!);
        using var output = JsonDocument.Parse(trace.GetProperty("serializedOutput").GetString()!);

        input.RootElement.GetProperty("userName").GetString().Should().Be("ada");
        input.RootElement.GetProperty("password").GetString().Should().Be("***");
        input.RootElement.GetProperty("pin").GetString().Should().Be("***", "[Sensitive] on a positional parameter masks it, whatever its type");
        input.RootElement.GetProperty("refreshToken").GetString().Should().Be("***");
        output.RootElement.GetProperty("accessToken").GetString().Should().Be("***");
        output.RootElement.GetProperty("expiresIn").GetInt32().Should().Be(3600);
        trace.GetRawText().Should().NotContain("hunter2").And.NotContain("access-token-value").And.NotContain("refresh-token-value");
    }

    [Fact]
    public async Task StreamThatFaultsMidIteration_IsReportedAsFailed_WithExceptionDetails()
    {
        await using var app = await StartAsync(Environments.Development);
        var client = app.GetTestClient();

        (await client.GetAsync("/faulting-stream")).EnsureSuccessStatusCode();

        var closed = (await GetTracesAsync(client)).Single(t =>
            t.GetProperty("messageType").GetString() == "FaultingStreamQuery" &&
            t.GetProperty("streamPhase").GetString() == "closed");

        closed.GetProperty("isSuccess").GetBoolean().Should().BeFalse();
        closed.GetProperty("itemCount").GetInt32().Should().Be(1);
        closed.GetProperty("error").GetProperty("exceptionType").GetString().Should().Be("System.InvalidOperationException");
        closed.GetProperty("error").GetProperty("stackTrace").GetString().Should().Contain("stream broke");
    }

    [Fact]
    public async Task FailureWrappingAnException_CarriesTheExceptionDetails()
    {
        await using var app = await StartAsync(Environments.Development);
        var client = app.GetTestClient();

        await client.GetAsync("/failing-internal");

        var error = (await GetTracesAsync(client))
            .Single(t => t.GetProperty("messageType").GetString() == "FailingInternalQuery")
            .GetProperty("error");

        error.GetProperty("code").GetString().Should().Be("INTERNAL");
        error.GetProperty("exceptionType").GetString().Should().Be("System.TimeoutException");
        error.GetProperty("stackTrace").GetString().Should().Contain("db timed out");
    }

    // ── Live events ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Events_IdleConnection_ReceivesHeartbeats()
    {
        await using var app = await StartAsync(Environments.Development, o => o.SseHeartbeatInterval = TimeSpan.FromMilliseconds(100));
        var client = app.GetTestClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        using var response = await client.GetAsync("/vitreous/events", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cts.Token));
        string? line;
        do
        {
            line = await reader.ReadLineAsync(cts.Token);
        } while (line is not null && line != ": ping");

        line.Should().Be(": ping");
    }

    [Fact]
    public async Task Events_DeliverLiveTraces()
    {
        await using var app = await StartAsync(Environments.Development);
        var client = app.GetTestClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        using var response = await client.GetAsync("/vitreous/events", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cts.Token));

        (await client.PostAsJsonAsync("/login", Login(), cts.Token)).EnsureSuccessStatusCode();

        string? line;
        do
        {
            line = await reader.ReadLineAsync(cts.Token);
        } while (line is not null && !(line.StartsWith("data: ", StringComparison.Ordinal) && line.Contains("LoginCommand")));

        line.Should().NotBeNull();
        line.Should().NotContain("hunter2");
    }

    /// <summary>Authentication handler that never authenticates, so authorization challenges with 401.</summary>
    private sealed class AnonymousOnlyHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.NoResult());
    }
}
