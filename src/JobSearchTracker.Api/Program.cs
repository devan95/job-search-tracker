using JobSearchTracker.Api.Data;
using JobSearchTracker.Api.Health;
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

var app = builder.Build();

var api = app.MapGroup("/api");
api.MapHealthEndpoints();

app.Run();

// Exposed so integration tests can use WebApplicationFactory<Program>.
public partial class Program;
