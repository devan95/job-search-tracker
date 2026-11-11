using Microsoft.AspNetCore.Mvc.Testing;

namespace JobTracker.Tests;

public class SmokeTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Live_endpoint_returns_ok()
    {
        var res = await factory.CreateClient().GetAsync("/api/health/live");
        Assert.Equal(System.Net.HttpStatusCode.OK, res.StatusCode);
    }
}
