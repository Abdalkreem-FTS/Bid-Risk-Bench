using BidRisk.Auction.Configuration;
using Grpc.Core;
using Grpc.Health.V1;
using Grpc.Net.Client;

namespace BidRisk.Auction;

internal static class HealthProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public static async Task<int> RunAsync(AuctionOptions options)
    {
        using var channel = GrpcChannel.ForAddress($"http://localhost:{options.GrpcPort}");
        var client = new Health.HealthClient(channel);

        try
        {
            var response = await client.CheckAsync(
                new HealthCheckRequest(),
                deadline: DateTime.UtcNow.Add(Timeout));

            if (response.Status == HealthCheckResponse.Types.ServingStatus.Serving)
            {
                return 0;
            }

            await Console.Error.WriteLineAsync($"unhealthy: {response.Status}");

            return 1;
        }
        catch (RpcException exception)
        {
            await Console.Error.WriteLineAsync($"unhealthy: {exception.Status.Detail}");

            return 1;
        }
    }
}
