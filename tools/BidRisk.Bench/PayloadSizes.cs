using System.Text;
using System.Text.Json.Nodes;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace BidRisk.Bench;

internal static class PayloadSizes
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static readonly string[] SampleReasons =
    [
        "bid is 3.2× the current price",
        "bidder has already placed 11 bids on this lot",
        "account is 1 day old",
    ];

    public static void Write(FileInfo output)
    {
        var rows = new[]
        {
            Measure("Lot", SampleLot(), repeat: 20),
            Measure("RiskAssessment", SampleAssessment(), repeat: 500),
            Measure("BidContext", SampleContext(), repeat: 500),
        };

        output.Directory?.Create();
        var payload = new JsonObject
        {
            ["payloads"] = new JsonArray(rows.Select(row => (JsonNode)row.ToJson()).ToArray()),
        };
        File.WriteAllText(output.FullName, payload.ToJsonString(Formats.Indented) + "\n");

        Console.WriteLine("▸ payload sizes (same data, both encodings)");
        Console.WriteLine($"  {"message",-16}{"protobuf",10}{"json",10}{"ratio",8}");
        foreach (var row in rows)
        {
            Console.WriteLine(
                $"  {row.Message,-16}{row.ProtobufBytes,9} B{row.JsonCompactBytes,9} B" +
                $"{row.JsonCompactRatio.ToString("0.00", Formats.Invariant),7}×");
        }
    }

    private sealed record Row(
        string Message,
        int ProtobufBytes,
        int JsonCompactBytes,
        int JsonIndentedBytes,
        double JsonCompactRatio,
        int Repeat)
    {
        public JsonObject ToJson() => new()
        {
            ["message"] = Message,
            ["protobuf_bytes"] = ProtobufBytes,
            ["json_compact_bytes"] = JsonCompactBytes,
            ["json_indented_bytes"] = JsonIndentedBytes,
            ["json_compact_ratio"] = JsonCompactRatio,
            ["repeat"] = Repeat,
            ["protobuf_bytes_x_repeat"] = ProtobufBytes * Repeat,
            ["json_compact_bytes_x_repeat"] = JsonCompactBytes * Repeat,
        };
    }

    private static Row Measure(string name, IMessage message, int repeat)
    {
        var protobufBytes = message.CalculateSize();

        var compactFormatter = new JsonFormatter(
            JsonFormatter.Settings.Default.WithPreserveProtoFieldNames(true));
        var compact = compactFormatter.Format(message);

        var indentedFormatter = new JsonFormatter(
            JsonFormatter.Settings.Default.WithPreserveProtoFieldNames(true).WithIndentation());
        var indented = indentedFormatter.Format(message);

        var compactBytes = Encoding.UTF8.GetByteCount(compact);

        return new Row(
            name,
            protobufBytes,
            compactBytes,
            Encoding.UTF8.GetByteCount(indented),
            Formats.Round((double)compactBytes / protobufBytes, 2),
            repeat);
    }

    private static Lot SampleLot() => new()
    {
        Id = 42,
        Title = "Victorian mahogany writing desk #42",
        ReservePrice = 245.74,
        CurrentPrice = 717.08,
        BidCount = 15,
        ClosesAt = Timestamp.FromDateTimeOffset(Now.AddHours(6)),
        Closed = false,
    };

    private static RiskAssessment SampleAssessment()
    {
        var assessment = new RiskAssessment
        {
            Score = 0.9999996677308298,
            Level = RiskLevel.High,
            InferenceMs = 0.8601679983257782,
            ModelVersion = "bidrisk-20260813081745",
        };

        assessment.Reasons.AddRange(SampleReasons);

        return assessment;
    }

    private static BidContext SampleContext() => new()
    {
        Amount = 320.0,
        CurrentPrice = 100.0,
        BidTime = Timestamp.FromDateTimeOffset(Now),
        PreviousBidTime = Timestamp.FromDateTimeOffset(Now.AddSeconds(-3)),
        BidderBidsOnLot = 11,
        LotTotalBids = 13,
        BidderAccountCreated = Timestamp.FromDateTimeOffset(Now.AddDays(-1)),
    };
}
