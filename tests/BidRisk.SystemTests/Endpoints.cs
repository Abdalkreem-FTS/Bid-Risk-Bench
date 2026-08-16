using Grpc.Net.Client;

// These tests share one running stack and lot 1's price moves as they run, so they must not
// run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace BidRisk.SystemTests;

internal static class Endpoints
{
    public static TimeSpan Deadline => TimeSpan.FromSeconds(15);

    public static TimeSpan StreamDeadline => TimeSpan.FromSeconds(300);

    private static string AuctionAddress =>
        $"http://{Env("AUCTION_GRPC_HOST", "auction-service")}:{Env("AUCTION_GRPC_PORT", "5001")}";

    private static string MlAddress =>
        $"http://{Env("ML_GRPC_HOST", "ml-service")}:{Env("ML_GRPC_PORT", "5002")}";

    public static string GatewayHttp =>
        $"http://{Env("GATEWAY_HOST", "gateway")}:{Env("GATEWAY_HTTP_PORT", "8080")}";

    public static string GatewayWebSocket =>
        $"ws://{Env("GATEWAY_HOST", "gateway")}:{Env("GATEWAY_HTTP_PORT", "8080")}";

    public static GrpcChannel Auction() => GrpcChannel.ForAddress(AuctionAddress);

    public static GrpcChannel Ml() => GrpcChannel.ForAddress(MlAddress);

    private static string Env(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;
}
