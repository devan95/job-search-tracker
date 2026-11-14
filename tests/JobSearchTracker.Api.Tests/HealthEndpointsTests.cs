using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JobSearchTracker.Api.Health;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JobSearchTracker.Api.Tests;

public class HealthEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private HttpClient ClientWithProbe(bool databaseReachable) =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDatabaseProbe>();
            services.AddScoped<IDatabaseProbe>(_ => new FakeProbe(databaseReachable));
        })).CreateClient();

    [Fact]
    public async Task Live_returns_200_without_touching_the_database()
    {
        using var client = ClientWithProbe(databaseReachable: false);

        var response = await client.GetAsync("/api/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_returns_200_and_database_ok_when_database_is_reachable()
    {
        using var client = ClientWithProbe(databaseReachable: true);

        var response = await client.GetAsync("/api/health/ready");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", body.GetProperty("status").GetString());
        Assert.Equal("ok", body.GetProperty("database").GetString());
    }

    [Fact]
    public async Task Ready_returns_503_and_database_unavailable_when_database_is_down()
    {
        using var client = ClientWithProbe(databaseReachable: false);

        var response = await client.GetAsync("/api/health/ready");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("degraded", body.GetProperty("status").GetString());
        Assert.Equal("unavailable", body.GetProperty("database").GetString());
    }

    [Fact]
    public async Task Ready_reports_unavailable_with_the_real_probe_when_nothing_listens_on_the_port()
    {
        // Real EF probe against a closed local port: must degrade, not throw.
        using var client = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting(
                "ConnectionStrings:Default",
                "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2"))
            .CreateClient();

        var response = await client.GetAsync("/api/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private sealed class FakeProbe(bool reachable) : IDatabaseProbe
    {
        public Task<bool> CanConnectAsync(CancellationToken cancellationToken) => Task.FromResult(reachable);
    }
}
