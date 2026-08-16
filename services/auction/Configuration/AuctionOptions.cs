namespace BidRisk.Auction.Configuration;

public sealed record AuctionOptions
{
    public required int GrpcPort { get; init; }
    public required string DatabaseConnectionString { get; init; }
    public required string RiskServiceAddress { get; init; }
    public required double RiskRejectThreshold { get; init; }
    public required TimeSpan ScoringDeadline { get; init; }
    public required bool ReflectionEnabled { get; init; }
    public required int DefaultPageSize { get; init; }

    public static AuctionOptions FromEnvironment()
    {
        var host = Env("POSTGRES_HOST", "postgres");
        var port = Env("POSTGRES_PORT", "5432");
        var database = Env("POSTGRES_DB", "bidrisk");
        var user = Env("POSTGRES_USER", "bidrisk");
        var password = Env("POSTGRES_PASSWORD", "bidrisk");

        return new AuctionOptions
        {
            GrpcPort = EnvInt("AUCTION_GRPC_PORT", 5001),
            DatabaseConnectionString =
                $"Host={host};Port={port};Database={database};Username={user};Password={password}"
                + $";Maximum Pool Size={EnvInt("POSTGRES_MAX_POOL", 20)}",
            RiskServiceAddress = $"http://{Env("ML_GRPC_HOST", "ml-service")}:{EnvInt("ML_GRPC_PORT", 5002)}",
            RiskRejectThreshold = EnvDouble("RISK_REJECT_THRESHOLD", 0.70),
            ScoringDeadline = TimeSpan.FromMilliseconds(EnvInt("SCORING_DEADLINE_MS", 300)),
            ReflectionEnabled = EnvBool("GRPC_REFLECTION", true),
            DefaultPageSize = EnvInt("AUCTION_DEFAULT_PAGE_SIZE", 20),
        };
    }

    private static string Env(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;

    private static int EnvInt(string name, int fallback) =>
        int.TryParse(Env(name, string.Empty), out var value) ? value : fallback;

    private static double EnvDouble(string name, double fallback) =>
        double.TryParse(
            Env(name, string.Empty),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : fallback;

    private static bool EnvBool(string name, bool fallback) =>
        Env(name, string.Empty) switch
        {
            "1" or "true" or "True" or "TRUE" or "yes" or "on" => true,
            "0" or "false" or "False" or "FALSE" or "no" or "off" => false,
            _ => fallback,
        };
}
