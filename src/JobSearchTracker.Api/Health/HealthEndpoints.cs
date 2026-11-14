namespace JobSearchTracker.Api.Health;

public static class HealthEndpoints
{
    // Database states are reported honestly: "ok" only when a connection
    // actually succeeded, "unavailable" when it failed.
    public const string DatabaseOk = "ok";
    public const string DatabaseUnavailable = "unavailable";

    public static RouteGroupBuilder MapHealthEndpoints(this RouteGroupBuilder group)
    {
        // Liveness: the process is up. Does not touch dependencies.
        group.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));

        // Readiness: 200 when the database is reachable, 503 (with a body) when not.
        group.MapGet("/health/ready", async (IDatabaseProbe probe, CancellationToken ct) =>
        {
            var reachable = await probe.CanConnectAsync(ct);
            return reachable
                ? Results.Json(new { status = "ok", database = DatabaseOk })
                : Results.Json(
                    new { status = "degraded", database = DatabaseUnavailable },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        return group;
    }
}
