using JobSearchTracker.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<HeartbeatService>();

builder.Build().Run();
