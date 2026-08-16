using BidRisk;
using BidRisk.Gateway;
using BidRisk.Gateway.Configuration;
using BidRisk.Gateway.GraphQL;
using BidRisk.Gateway.Http;

var options = GatewayOptions.FromEnvironment();

if (args.Contains("--healthcheck", StringComparer.Ordinal))
{
    return await HealthProbe.RunAsync(options);
}

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenAnyIP(options.HttpPort));

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<GrpcCallCounter>();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddGrpcClient<AuctionService.AuctionServiceClient>(client => client.Address = new Uri(options.AuctionAddress));
builder.Services.AddGrpcClient<RiskService.RiskServiceClient>(client => client.Address = new Uri(options.RiskAddress));

// Both resolvers ship in the image. The env var picks one, so the benchmark's before and after
// differ by a restart and nothing else.
if (options.BatchingEnabled)
{
    builder.Services.AddScoped<ILotRiskResolver, BatchedLotRiskResolver>();
}
else
{
    builder.Services.AddScoped<ILotRiskResolver, NaiveLotRiskResolver>();
}

builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>()
    .AddSubscriptionType<Subscription>()
    .AddTypeExtension<LotResolvers>()
    .AddTypeExtension<ScoringQueries>()
    .AddInMemorySubscriptions()
    .AddDataLoader<LotRiskDataLoader>();

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseExceptionHandler();

app.UseMiddleware<RequestLogContextMiddleware>();

app.UseWebSockets();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGraphQL();

app.Map("/ws", async (HttpContext context, RiskService.RiskServiceClient risk, ILoggerFactory loggers) =>
    await RawAnswerSocket.HandleAsync(context, risk, loggers.CreateLogger("raw-ws")));

app.Map("/ws/score", async (HttpContext context, RiskService.RiskServiceClient risk, GrpcCallCounter counter) =>
    await RawScoreSocket.HandleAsync(context, risk, counter));

app.MapHealthChecks("/health");

app.MapGet("/debug/grpc-calls", (GrpcCallCounter counter) => Results.Ok(counter.Snapshot()));
app.MapPost("/debug/grpc-calls/reset", (GrpcCallCounter counter) =>
{
    counter.Reset();
    return Results.NoContent();
});

app.Logger.LogInformation(
    "gateway listening on :{Port} · batching {Batching} · auction {Auction} · risk {Risk}",
    options.HttpPort,
    options.BatchingEnabled ? "on" : "off",
    options.AuctionAddress,
    options.RiskAddress);

await app.RunAsync();

return 0;
