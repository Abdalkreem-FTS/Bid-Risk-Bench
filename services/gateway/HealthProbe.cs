using BidRisk.Gateway.Configuration;

namespace BidRisk.Gateway;

internal static class HealthProbe
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(3)
    };

    public static async Task<int> RunAsync(GatewayOptions options)
    {
        try
        {
            var response = await Client.GetAsync(new Uri($"http://localhost:{options.HttpPort}/health"));
            if (response.IsSuccessStatusCode)
            {
                return 0;
            }

            await Console.Error.WriteLineAsync($"unhealthy: HTTP {(int)response.StatusCode}");

            return 1;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            await Console.Error.WriteLineAsync($"unhealthy: {exception.Message}");

            return 1;
        }
    }
}
