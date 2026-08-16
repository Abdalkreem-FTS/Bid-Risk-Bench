using System.Diagnostics;
using System.Text.Json.Nodes;
using Grpc.Core;
using Grpc.Net.Client;

namespace BidRisk.Bench;

internal static class StreamProbe
{
    public const string DefaultQuestion = "Summarise the bidding activity on lot 38.";

    public static async Task RunAsync(
        int listeners,
        string question,
        string broadcastId,
        FileInfo output)
    {
        var host = Environment.GetEnvironmentVariable("ML_GRPC_HOST") is { Length: > 0 } h
            ? h
            : "ml-service";
        var port = Environment.GetEnvironmentVariable("ML_GRPC_PORT") is { Length: > 0 } p
            ? p
            : "5002";
        var target = $"http://{host}:{port}";

        if (broadcastId.Length == 0 && listeners > 1)
        {
            broadcastId = $"probe-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        }

        using var channel = GrpcChannel.ForAddress(target);
        await channel.ConnectAsync();
        var client = new RiskService.RiskServiceClient(channel);

        var wall = Stopwatch.StartNew();
        var samples = await Task.WhenAll(
            Enumerable.Range(0, listeners)
                .Select(index => ListenAsync(client, question, broadcastId, index)));
        wall.Stop();

        var timeToFirstToken = Summarise(samples.Select(s => s.TimeToFirstTokenMs));
        var total = Summarise(samples.Select(s => s.TotalMs));

        var payload = new JsonObject
        {
            ["listeners"] = listeners,
            ["broadcast_id"] = broadcastId,
            ["wall_ms"] = wall.Elapsed.TotalMilliseconds,
            ["time_to_first_token_ms"] = timeToFirstToken.ToJson(),
            ["total_ms"] = total.ToJson(),
            ["server_time_to_first_token_ms"] =
                Summarise(samples.Select(s => s.ServerTimeToFirstTokenMs)).ToJson(),
            ["tokens_per_listener"] = Summarise(samples.Select(s => (double)s.Tokens)).ToJson(),
            ["distinct_token_counts"] = samples.Select(s => s.Tokens).Distinct().Count(),
            ["samples"] = new JsonArray(samples.Select(JsonNode (s) => s.ToJson()).ToArray()),
        };

        output.Directory?.Create();
        await File.WriteAllTextAsync(output.FullName, payload.ToJsonString(Formats.Indented));

        Console.WriteLine(
            $"  {listeners,2} listener(s): " +
            $"ttft p50 {timeToFirstToken.P50:F0} ms p95 {timeToFirstToken.P95:F0} ms · " +
            $"total p50 {total.P50:F0} ms · " +
            $"{samples.Select(s => s.Tokens).Distinct().Count()} distinct answer length(s)");
    }

    private sealed record Sample(
        int Listener,
        double TimeToFirstTokenMs,
        double TotalMs,
        int Tokens,
        double ServerTimeToFirstTokenMs,
        double ServerTotalMs)
    {
        public JsonObject ToJson() => new()
        {
            ["listener"] = Listener,
            ["time_to_first_token_ms"] = TimeToFirstTokenMs,
            ["total_ms"] = TotalMs,
            ["tokens"] = Tokens,
            ["server_time_to_first_token_ms"] = ServerTimeToFirstTokenMs,
            ["server_total_ms"] = ServerTotalMs,
        };
    }

    private static async Task<Sample> ListenAsync(
        RiskService.RiskServiceClient client,
        string question,
        string broadcastId,
        int index)
    {
        var started = Stopwatch.StartNew();
        double? firstTokenAt = null;
        var tokens = 0;
        var serverTimeToFirstToken = 0.0;
        var serverTotal = 0.0;

        using var call = client.AskAboutData(new AskAboutDataRequest
        {
            Question = question,
            BroadcastId = broadcastId,
        });

        await foreach (var chunk in call.ResponseStream.ReadAllAsync())
        {
            if (chunk.PayloadCase == AskAboutDataResponse.PayloadOneofCase.Stats)
            {
                serverTimeToFirstToken = chunk.Stats.TimeToFirstTokenMs;
                serverTotal = chunk.Stats.TotalMs;
                continue;
            }

            firstTokenAt ??= started.Elapsed.TotalMilliseconds;
            tokens++;
        }

        var finished = started.Elapsed.TotalMilliseconds;
        return new Sample(
            index,
            firstTokenAt ?? finished,
            finished,
            tokens,
            serverTimeToFirstToken,
            serverTotal);
    }

    private sealed record Stats(
        double Min, double P50, double P95, double P99, double Max, double Mean)
    {
        public JsonObject ToJson() => new()
        {
            ["min"] = Min,
            ["p50"] = P50,
            ["p95"] = P95,
            ["p99"] = P99,
            ["max"] = Max,
            ["mean"] = Mean,
        };
    }

    private static Stats Summarise(IEnumerable<double> values)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        if (ordered.Length == 0)
        {
            return new Stats(0, 0, 0, 0, 0, 0);
        }

        return new Stats(
            ordered[0],
            Median(ordered),
            Percentile(ordered, 0.95),
            Percentile(ordered, 0.99),
            ordered[^1],
            ordered.Average());
    }

    internal static double Median(double[] ordered) =>
        ordered.Length == 0 ? 0.0
        : ordered.Length % 2 == 1 ? ordered[ordered.Length / 2]
        : (ordered[(ordered.Length / 2) - 1] + ordered[ordered.Length / 2]) / 2.0;

    private static double Percentile(double[] ordered, double fraction) =>
        ordered.Length == 0
            ? 0.0
            : ordered[Math.Min((int)(fraction * ordered.Length), ordered.Length - 1)];
}
