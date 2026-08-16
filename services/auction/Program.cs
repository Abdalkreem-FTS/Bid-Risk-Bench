using BidRisk;
using BidRisk.Auction;
using BidRisk.Auction.Configuration;
using BidRisk.Auction.Data;
using BidRisk.Auction.Grpc;
using BidRisk.Auction.Risk;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;

var options = AuctionOptions.FromEnvironment();

// Docker's health check runs this same binary with a flag.
if (args.Contains("--healthcheck", StringComparer.Ordinal))
{
    return await HealthProbe.RunAsync(options);
}

var migrationsPath = Environment.GetEnvironmentVariable("MIGRATIONS_PATH") is { Length: > 0 } path ? path : "db/migrations";

var builder = WebApplication.CreateBuilder(args);

// HTTP/2 only. gRPC needs it, and without TLS Kestrel would otherwise fall back to HTTP/1.1.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenAnyIP(options.GrpcPort, listen => listen.Protocols = HttpProtocols.Http2));

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddDbContext<AuctionDbContext>(db => db.UseNpgsql(options.DatabaseConnectionString));

builder.Services.AddGrpc();
builder.Services.AddGrpcHealthChecks();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

if (options.ReflectionEnabled)
{
    builder.Services.AddGrpcReflection();
}

builder.Services.AddGrpcClient<RiskService.RiskServiceClient>(client => client.Address = new Uri(options.RiskServiceAddress));

builder.Services.AddScoped<IRiskClient>(services => new RiskClient(
    services.GetRequiredService<RiskService.RiskServiceClient>(),
    options.ScoringDeadline,
    services.GetRequiredService<ILogger<RiskClient>>()));

var app = builder.Build();

await DatabaseMigrator.RunAsync(options.DatabaseConnectionString, migrationsPath, app.Logger);

app.MapGrpcService<AuctionGrpcService>();
app.MapGrpcHealthChecksService();

if (options.ReflectionEnabled)
{
    app.MapGrpcReflectionService();
}

app.Logger.LogInformation(
    "auction-service listening on :{Port} · risk service {RiskAddress} · "
        + "reject at {Threshold} · reflection {Reflection}",
    options.GrpcPort,
    options.RiskServiceAddress,
    options.RiskRejectThreshold,
    options.ReflectionEnabled ? "on" : "off");

await app.RunAsync();

return 0;
