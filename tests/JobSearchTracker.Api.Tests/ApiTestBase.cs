using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace JobSearchTracker.Api.Tests;

/// <summary>
/// Runs the real API (WebApplicationFactory) against its own migrated PostgreSQL database, created per test and dropped
/// afterwards. Like the other database tests, these fail (never skip) when PostgreSQL is not reachable.
/// Assertions read the JSON wire format directly so that field names and error codes are part of what is tested.
/// </summary>
[Trait("Category", "Database")]
public abstract class ApiTestBase : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private TestDatabase _db = null!;
    private WebApplicationFactory<Program>? _root;
    private WebApplicationFactory<Program>? _factory;

    protected HttpClient Client { get; private set; } = null!;

    /// <summary>Connection string of this test's own database (for tests that need a second, direct connection).</summary>
    protected string ConnectionString => _db.ConnectionString;

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateMigratedAsync();
        _root = new WebApplicationFactory<Program>();
        _factory = _root.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:Default", _db.ConnectionString));
        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_root is not null)
        {
            await _root.DisposeAsync();
        }

        if (_db is not null)
        {
            await _db.DisposeAsync();
        }
    }

    protected sealed record Reply(HttpStatusCode Status, JsonElement Body, string? Location, string? ContentType)
    {
        public Guid Id => Body.GetProperty("id").GetGuid();
        public string Str(string property) => Body.GetProperty(property).GetString()!;
        public string[] Strings(string property) => Body.GetProperty(property).EnumerateArray().Select(e => e.GetString()!).ToArray();
        public int Count => Body.GetArrayLength();
        public JsonElement this[int index] => Body[index];
    }

    protected async Task<Reply> SendAsync(HttpMethod method, string url, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        return await ReadAsync(await Client.SendAsync(request));
    }

    protected Task<Reply> GetAsync(string url) => SendAsync(HttpMethod.Get, url);
    protected Task<Reply> PostAsync(string url, object? body = null) => SendAsync(HttpMethod.Post, url, body ?? new { });
    protected Task<Reply> PutAsync(string url, object? body = null) => SendAsync(HttpMethod.Put, url, body);
    protected Task<Reply> DeleteAsync(string url) => SendAsync(HttpMethod.Delete, url);

    protected async Task<Reply> SendRawJsonAsync(HttpMethod method, string url, string rawJson)
    {
        using var request = new HttpRequestMessage(method, url)
        {
            Content = new StringContent(rawJson, System.Text.Encoding.UTF8, "application/json"),
        };
        return await ReadAsync(await Client.SendAsync(request));
    }

    private static async Task<Reply> ReadAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        var body = string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
        return new Reply(response.StatusCode, body, response.Headers.Location?.ToString(), response.Content.Headers.ContentType?.MediaType);
    }

    protected static void AssertProblem(Reply reply, HttpStatusCode status, string code)
    {
        Assert.Equal(status, reply.Status);
        Assert.Contains("json", reply.ContentType);
        Assert.Equal(code, reply.Body.GetProperty("code").GetString());
        Assert.Equal((int)status, reply.Body.GetProperty("status").GetInt32());
    }

    protected static void AssertInvalid(Reply reply, string field)
    {
        AssertProblem(reply, HttpStatusCode.BadRequest, "validation");
        Assert.True(reply.Body.GetProperty("errors").TryGetProperty(field, out var messages), $"No validation error for '{field}'.");
        Assert.True(messages.GetArrayLength() > 0);
    }

    protected async Task<Guid> CreateCandidateAsync(string name = "Candidate One")
    {
        var reply = await PostAsync("/api/candidates", new { name });
        Assert.Equal(HttpStatusCode.Created, reply.Status);
        return reply.Id;
    }

    protected async Task<Guid> CreateEmployerAsync(string name)
    {
        var reply = await PostAsync("/api/employers", new { name });
        Assert.Equal(HttpStatusCode.Created, reply.Status);
        return reply.Id;
    }

    protected async Task<Guid> CreateListAsync(string name, string? locationLabel = null, string[]? defaultWorkModes = null)
    {
        var reply = await PostAsync("/api/company-lists", new { name, locationLabel, defaultWorkModes });
        Assert.Equal(HttpStatusCode.Created, reply.Status);
        return reply.Id;
    }

    protected async Task<Guid> CreateProfileAsync(Guid candidateId, string name, params Guid[] companyListIds)
    {
        var reply = await PostAsync("/api/search-profiles", new { candidateId, name, companyListIds });
        Assert.Equal(HttpStatusCode.Created, reply.Status);
        return reply.Id;
    }

    protected async Task AddEmployerToListAsync(Guid listId, Guid employerId)
    {
        var reply = await PutAsync($"/api/company-lists/{listId}/employers/{employerId}");
        Assert.Equal(HttpStatusCode.NoContent, reply.Status);
    }
}
