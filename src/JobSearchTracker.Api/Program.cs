using JobSearchTracker.Api.Data;
using JobSearchTracker.Api.Health;
using JobSearchTracker.Api.Management;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// The connection string (including the password) comes from environment
// variables or user secrets. It is never committed. See docs/development.md.
// It is read lazily so configuration overrides (e.g. in tests) are honoured.
builder.Services.AddDbContext<AppDbContext>((services, options) =>
{
    var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
    options.UseNpgsql(connectionString);
});
builder.Services.AddScoped<IDatabaseProbe, EfDatabaseProbe>();

// Errors (including malformed request bodies and unexpected failures) are returned as RFC 7807 problem documents.
builder.Services.AddProblemDetails();
// A malformed request is always a 400 problem document, also in Development (where binding failures would otherwise throw).
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

var api = app.MapGroup("/api");
api.MapHealthEndpoints();
api.MapCandidateEndpoints();
api.MapEmployerEndpoints();
api.MapCompanyListEndpoints();
api.MapSearchProfileEndpoints();

app.Run();

// Exposed so integration tests can use WebApplicationFactory<Program>.
public partial class Program;
