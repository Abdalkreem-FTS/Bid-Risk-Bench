using System.Text.Json;
using System.Text.Json.Nodes;

namespace BidRisk.Bench;

internal static class Payloads
{
    private const int Seed = 4242;
    private const int BatchSize = 500;

    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };

    private sealed record Context(
        double Amount,
        double CurrentPrice,
        double SecondsSincePreviousBid,
        int BidderBidsOnLot,
        int LotTotalBids,
        double BidderAccountAgeDays);

    public static void Write(DirectoryInfo output)
    {
        output.Create();
        var now = DateTimeOffset.UtcNow;
        var rows = Generate(BatchSize);

        Console.WriteLine("▸ benchmark payloads");

        WriteFile(output, "grpc_predict_one.json", new JsonObject
        {
            ["context"] = ToProto(rows[0], now),
        });

        WriteFile(output, "grpc_predict_many.json", new JsonArray(
            rows.Select(JsonNode (row) => new JsonObject { ["context"] = ToProto(row, now) })
                .ToArray()));

        WriteFile(output, "grpc_predict_batch.json", new JsonObject
        {
            ["contexts"] = new JsonArray(rows.Select(JsonNode (row) => ToProto(row, now)).ToArray()),
        });

        WriteFile(output, "grpc_list_lots.json", new JsonObject { ["first"] = 20 });

        WriteFile(output, "score_inputs.json", new JsonArray(
            rows.Select(JsonNode (row) => ToGraphQL(row)).ToArray()));

        Console.WriteLine($"✓ {BatchSize} bid contexts, shared by all three transports");
    }

    private static List<Context> Generate(int count)
    {
        var random = new Random(Seed);
        var rows = new List<Context>(count);

        for (var index = 0; index < count; index++)
        {
            var suspicious = index % 7 == 0;
            var currentPrice = Uniform(random, 50.0, 900.0);

            rows.Add(new Context(
                Amount: Math.Round(
                    currentPrice * (suspicious
                        ? Uniform(random, 1.3, 3.0)
                        : Uniform(random, 1.02, 1.12)),
                    2),
                CurrentPrice: Math.Round(currentPrice, 2),
                SecondsSincePreviousBid: Math.Round(
                    suspicious ? Uniform(random, 1, 30) : Uniform(random, 60, 3600), 1),
                BidderBidsOnLot: suspicious ? random.Next(5, 15) : random.Next(1, 4),
                LotTotalBids: random.Next(8, 40),
                BidderAccountAgeDays: Math.Round(
                    suspicious ? Uniform(random, 0.5, 20) : Uniform(random, 90, 2000), 2)));
        }

        return rows;
    }

    private static JsonObject ToProto(Context row, DateTimeOffset now) => new()
    {
        ["amount"] = row.Amount,
        ["current_price"] = row.CurrentPrice,
        ["bid_time"] = Rfc3339(now),
        ["previous_bid_time"] = Rfc3339(now.AddSeconds(-row.SecondsSincePreviousBid)),
        ["bidder_bids_on_lot"] = row.BidderBidsOnLot,
        ["lot_total_bids"] = row.LotTotalBids,
        ["bidder_account_created"] = Rfc3339(now.AddDays(-row.BidderAccountAgeDays)),
    };

    private static JsonObject ToGraphQL(Context row) => new()
    {
        ["amount"] = row.Amount,
        ["currentPrice"] = row.CurrentPrice,
        ["secondsSincePreviousBid"] = row.SecondsSincePreviousBid,
        ["bidderBidsOnLot"] = row.BidderBidsOnLot,
        ["lotTotalBids"] = row.LotTotalBids,
        ["bidderAccountAgeDays"] = row.BidderAccountAgeDays,
    };

    private static double Uniform(Random random, double low, double high) =>
        low + (random.NextDouble() * (high - low));

    private static string Rfc3339(DateTimeOffset moment) =>
        moment.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", Formats.Invariant);

    private static void WriteFile(DirectoryInfo output, string name, JsonNode payload)
    {
        var path = Path.Combine(output.FullName, name);
        File.WriteAllText(path, payload.ToJsonString(Compact));
        Console.WriteLine($"  {name}: {new FileInfo(path).Length:N0} bytes");
    }
}
