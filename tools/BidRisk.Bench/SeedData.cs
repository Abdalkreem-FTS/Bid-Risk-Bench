using System.Text;
using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;

namespace BidRisk.Bench;

internal static class SeedData
{
    private const int Seed = 20260813;
    private const int BidderCount = 60;
    private const int NormalLotCount = 37;

    private const int LotShill = NormalLotCount + 1;
    private const int LotSniped = NormalLotCount + 2;
    private const int LotClosed = NormalLotCount + 3;

    private const int ShillBidder = 12;

    private static readonly string[] Titles =
    [
        "Vintage typewriter",
        "Art deco table lamp",
        "First edition novel",
        "Mechanical wristwatch",
        "Cast iron skillet",
        "Wooden chess set",
        "Brass sextant",
        "Film camera",
        "Persian rug",
        "Porcelain tea service",
        "Vinyl record boxset",
        "Fountain pen",
        "Copper kettle",
        "Silver pocket watch",
        "Oil painting, unsigned",
        "Leather satchel",
        "Marble bookends",
        "Antique globe",
        "Handmade quilt",
        "Crystal decanter",
    ];

    private sealed record SeedBid(
        int LotId,
        int BidderId,
        double Amount,
        double MinutesAgo,
        double CurrentPrice,
        double? SecondsSincePreviousBid,
        int BidderBidsOnLot,
        int LotTotalBids,
        double BidderAccountAgeDays);

    public static async Task WriteAsync(FileInfo output)
    {
        var random = new Random(Seed);
        var accountAgeDays = new Dictionary<int, double>();
        var bidders = new List<string>();
        var lots = new List<string>();
        var bids = new List<SeedBid>();

        BuildBidders(random, accountAgeDays, bidders);
        BuildOrdinaryLots(random, accountAgeDays, lots, bids);
        BuildShillLot(random, accountAgeDays, lots, bids);
        BuildSnipedLot(random, accountAgeDays, lots, bids);
        BuildClosedLot(random, accountAgeDays, lots, bids);

        var (scored, modelVersion) = await ScoreAsync(bids);

        var bidRows = bids.Zip(scored).Select(pair =>
        {
            var (bid, assessment) = pair;
            var reasons = string.Join(", ", assessment.Reasons.Select(r => $"'{Escape(r)}'"));
            return
                $"({bid.LotId}, {bid.BidderId}, {Money(bid.Amount)}, " +
                $"now() - interval '{bid.MinutesAgo.ToString("F2", Formats.Invariant)} minutes', " +
                $"true, NULL, {assessment.Score.ToString("F6", Formats.Invariant)}, " +
                $"'{LevelName(assessment.Level)}', ARRAY[{reasons}], '{Escape(modelVersion)}')";
        }).ToList();

        output.Directory?.Create();
        await File.WriteAllTextAsync(output.FullName, Render(bidders, lots, bidRows));

        Console.WriteLine(
            $"✓ {output.FullName}: {bidders.Count} bidders · {lots.Count} lots · " +
            $"{bidRows.Count} bids (scored by {modelVersion})");
    }

    private static void BuildBidders(
        Random random, Dictionary<int, double> accountAgeDays, List<string> bidders)
    {
        for (var bidderId = 1; bidderId <= BidderCount; bidderId++)
        {
            var age = bidderId % 12 == 0
                ? Uniform(random, 0.5, 20.0)
                : Uniform(random, 60, 2200);

            accountAgeDays[bidderId] = age;
            bidders.Add(
                $"({bidderId}, 'bidder_{bidderId:D3}', " +
                $"now() - interval '{age.ToString("F2", Formats.Invariant)} days')");
        }
    }

    private static void BuildOrdinaryLots(
        Random random,
        Dictionary<int, double> accountAgeDays,
        List<string> lots,
        List<SeedBid> bids)
    {
        for (var lotId = 1; lotId <= NormalLotCount; lotId++)
        {
            var title = Titles[(lotId - 1) % Titles.Length];
            var reserve = Math.Round(Uniform(random, 20, 400), 2);
            var closesInHours = Math.Round(Uniform(random, 2, 240), 2);

            var price = reserve;
            var bidTotal = 0;
            var perBidder = new Dictionary<int, int>();
            var minutesAgo = Uniform(random, 600, 4000);

            var count = random.Next(3, 30);
            for (var index = 0; index < count; index++)
            {
                var bidderId = random.Next(1, BidderCount + 1);
                double? previous = bidTotal > 0 ? minutesAgo : null;
                minutesAgo = Math.Max(minutesAgo - Uniform(random, 5, 300), 1.0);
                var amount = Math.Round(price * Uniform(random, 1.02, 1.12), 2);

                perBidder[bidderId] = perBidder.GetValueOrDefault(bidderId) + 1;
                bidTotal++;

                bids.Add(Bid(
                    lotId, bidderId, amount, minutesAgo, price, previous,
                    perBidder[bidderId], bidTotal, accountAgeDays));

                price = amount;
            }

            lots.Add(
                $"({lotId}, '{Escape(title)} #{lotId}', {Money(reserve)}, {Money(price)}, " +
                $"{bidTotal}, now() + interval '{closesInHours.ToString("F2", Formats.Invariant)} hours', false)");
        }
    }

    private static void BuildShillLot(
        Random random,
        Dictionary<int, double> accountAgeDays,
        List<string> lots,
        List<SeedBid> bids)
    {
        var price = 50.0;
        var bidTotal = 0;
        var minutesAgo = 900.0;

        for (var index = 0; index < 14; index++)
        {
            var bidderId = index % 4 != 3 ? ShillBidder : random.Next(1, BidderCount + 1);
            double? previous = bidTotal > 0 ? minutesAgo : null;
            minutesAgo = Math.Max(minutesAgo - Uniform(random, 0.3, 3.0), 1.0);
            var amount = Math.Round(price * (bidderId == ShillBidder ? 1.45 : 1.05), 2);
            bidTotal++;

            var shillCount = bids.Count(b => b.LotId == LotShill && b.BidderId == bidderId) + 1;
            bids.Add(Bid(
                LotShill, bidderId, amount, minutesAgo, price, previous,
                shillCount, bidTotal, accountAgeDays));

            price = amount;
        }

        lots.Add(
            $"({LotShill}, 'Rare stamp album (heavily contested) #{LotShill}', 50.00, " +
            $"{Money(price)}, {bidTotal}, now() + interval '6 hours', false)");
    }

    private static void BuildSnipedLot(
        Random random,
        Dictionary<int, double> accountAgeDays,
        List<string> lots,
        List<SeedBid> bids)
    {
        var price = 120.0;
        var bidTotal = 0;
        var minutesAgo = 2000.0;

        for (var index = 0; index < 5; index++)
        {
            var bidderId = random.Next(1, BidderCount + 1);
            double? previous = bidTotal > 0 ? minutesAgo : null;
            minutesAgo = Math.Max(minutesAgo - Uniform(random, 100, 400), 5.0);
            var amount = Math.Round(price * 1.05, 2);
            bidTotal++;

            bids.Add(Bid(
                LotSniped, bidderId, amount, minutesAgo, price, previous,
                1, bidTotal, accountAgeDays));

            price = amount;
        }

        bidTotal++;
        var snipe = Math.Round(price * 2.4, 2);
        bids.Add(Bid(
            LotSniped, 24, snipe, minutesAgo - 0.05, price, minutesAgo,
            1, bidTotal, accountAgeDays));
        price = snipe;

        lots.Add(
            $"({LotSniped}, 'Signed football shirt #{LotSniped}', 120.00, {Money(price)}, " +
            $"{bidTotal}, now() + interval '3 hours', false)");
    }

    private static void BuildClosedLot(
        Random random,
        Dictionary<int, double> accountAgeDays,
        List<string> lots,
        List<SeedBid> bids)
    {
        var price = 80.0;
        var bidTotal = 0;
        var minutesAgo = 800.0;

        for (var index = 0; index < 6; index++)
        {
            var bidderId = random.Next(1, BidderCount + 1);
            double? previous = bidTotal > 0 ? minutesAgo : null;
            minutesAgo = Math.Max(minutesAgo - Uniform(random, 20, 120), 130.0);
            var amount = Math.Round(price * 1.22, 2);
            bidTotal++;

            bids.Add(Bid(
                LotClosed, bidderId, amount, minutesAgo, price, previous,
                1, bidTotal, accountAgeDays));

            price = amount;
        }

        lots.Add(
            $"({LotClosed}, 'Ended: brass telescope #{LotClosed}', 80.00, {Money(price)}, " +
            $"{bidTotal}, now() - interval '2 hours', true)");
    }

    private static SeedBid Bid(
        int lotId,
        int bidderId,
        double amount,
        double minutesAgo,
        double currentPrice,
        double? previousMinutesAgo,
        int bidderBids,
        int lotBids,
        Dictionary<int, double> accountAgeDays) =>
        new(
            lotId,
            bidderId,
            amount,
            minutesAgo,
            currentPrice,
            (previousMinutesAgo - minutesAgo) * 60.0,
            bidderBids,
            lotBids,
            accountAgeDays[bidderId]);

    // Every seeded bid is scored by the real ml-service, not by a second copy of the logic here.
    private static async Task<(IReadOnlyList<RiskAssessment> Assessments, string ModelVersion)>
        ScoreAsync(List<SeedBid> bids)
    {
        var host = Environment.GetEnvironmentVariable("ML_GRPC_HOST") is { Length: > 0 } h
            ? h
            : "ml-service";
        var port = Environment.GetEnvironmentVariable("ML_GRPC_PORT") is { Length: > 0 } p
            ? p
            : "5002";

        using var channel = GrpcChannel.ForAddress($"http://{host}:{port}");
        var client = new RiskService.RiskServiceClient(channel);

        var now = DateTimeOffset.UtcNow;
        var request = new PredictBatchRequest();
        request.Contexts.AddRange(bids.Select(bid => ToContext(bid, now)));

        var response = await client.PredictBatchAsync(request);

        if (response.Assessments.Count != bids.Count)
        {
            throw new InvalidOperationException(
                $"ml-service scored {response.Assessments.Count} of {bids.Count} bids");
        }

        var modelVersion = response.Assessments.Count > 0
            ? response.Assessments[0].ModelVersion
            : "unknown";

        return (response.Assessments, modelVersion);
    }

    private static BidContext ToContext(SeedBid bid, DateTimeOffset now)
    {
        var placedAt = now.AddMinutes(-bid.MinutesAgo);
        var context = new BidContext
        {
            Amount = bid.Amount,
            CurrentPrice = bid.CurrentPrice,
            BidTime = Timestamp.FromDateTimeOffset(placedAt),
            BidderBidsOnLot = bid.BidderBidsOnLot,
            LotTotalBids = bid.LotTotalBids,
            BidderAccountCreated =
                Timestamp.FromDateTimeOffset(now.AddDays(-bid.BidderAccountAgeDays)),
        };

        if (bid.SecondsSincePreviousBid is { } seconds)
        {
            context.PreviousBidTime = Timestamp.FromDateTimeOffset(placedAt.AddSeconds(-seconds));
        }

        return context;
    }

    private static string Render(
        List<string> bidders, List<string> lots, List<string> bids)
    {
        var sql = new StringBuilder();

        sql.AppendLine("BEGIN;");
        sql.AppendLine();
        sql.AppendLine("TRUNCATE bids, lots, bidders RESTART IDENTITY CASCADE;");
        sql.AppendLine();
        sql.AppendLine("INSERT INTO bidders (id, username, created_at) VALUES");
        sql.AppendLine(string.Join(",\n", bidders) + ";");
        sql.AppendLine();
        sql.AppendLine("INSERT INTO lots (id, title, reserve_price, current_price, bid_count,");
        sql.AppendLine("                  closes_at, closed) VALUES");
        sql.AppendLine(string.Join(",\n", lots) + ";");
        sql.AppendLine();
        sql.AppendLine("INSERT INTO bids (lot_id, bidder_id, amount, placed_at, accepted, reject_reason,");
        sql.AppendLine("                  risk_score, risk_level, risk_reasons, model_version) VALUES");
        sql.AppendLine(string.Join(",\n", bids) + ";");
        sql.AppendLine();
        sql.AppendLine("SELECT setval('bidders_id_seq', (SELECT max(id) FROM bidders));");
        sql.AppendLine("SELECT setval('lots_id_seq', (SELECT max(id) FROM lots));");
        sql.AppendLine();
        sql.AppendLine("COMMIT;");

        return sql.ToString();
    }

    private static string LevelName(RiskLevel level) => level switch
    {
        RiskLevel.Low => "LOW",
        RiskLevel.Medium => "MEDIUM",
        RiskLevel.High => "HIGH",
        _ => "UNSPECIFIED",
    };

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static double Uniform(Random random, double low, double high) =>
        low + (random.NextDouble() * (high - low));

    private static string Money(double value) => value.ToString("F2", Formats.Invariant);
}
