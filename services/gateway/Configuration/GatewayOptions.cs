namespace BidRisk.Gateway.Configuration;

public sealed record GatewayOptions
{
    public required int HttpPort { get; init; }
    public required string AuctionAddress { get; init; }
    public required string RiskAddress { get; init; }
    public required bool BatchingEnabled { get; init; }
    public required int DefaultPageSize { get; init; }

    public static GatewayOptions FromEnvironment() =>
        new()
        {
            HttpPort = EnvInt("GATEWAY_HTTP_PORT", 8080),
            AuctionAddress = $"http://{Env("AUCTION_GRPC_HOST", "auction-service")}:{EnvInt("AUCTION_GRPC_PORT", 5001)}",
            RiskAddress = $"http://{Env("ML_GRPC_HOST", "ml-service")}:{EnvInt("ML_GRPC_PORT", 5002)}",
            BatchingEnabled = Env("GATEWAY_BATCHING", "off").Equals("on", StringComparison.OrdinalIgnoreCase),
            DefaultPageSize = EnvInt("GATEWAY_DEFAULT_PAGE_SIZE", 20)
        };

    private static string Env(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;

    private static int EnvInt(string name, int fallback) =>
        int.TryParse(Env(name, string.Empty), out var value) ? value : fallback;
}
