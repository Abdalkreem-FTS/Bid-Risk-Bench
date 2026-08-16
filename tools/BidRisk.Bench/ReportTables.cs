using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BidRisk.Bench;

internal static partial class ReportTables
{
    [GeneratedRegex(@"(<!-- BENCH -->)(.*?)(<!-- /BENCH -->)", RegexOptions.Singleline)]
    private static partial Regex Marker();

    public static void Write(FileInfo results, FileInfo payloadSizes, FileInfo readme)
    {
        var payload = JsonNode.Parse(File.ReadAllText(results.FullName)) as JsonObject
            ?? throw new ArgumentException($"{results.FullName} is not an object");

        var sizes = payloadSizes.Exists
            ? JsonNode.Parse(File.ReadAllText(payloadSizes.FullName)) as JsonObject
            : null;

        var markdown = Build(new Results(payload, sizes));

        var text = readme.Exists ? File.ReadAllText(readme.FullName) : string.Empty;
        if (!Marker().IsMatch(text))
        {
            Console.WriteLine("⚠ README has no <!-- BENCH --> markers; printing instead");
            Console.WriteLine(markdown);
            return;
        }

        File.WriteAllText(
            readme.FullName,
            Marker().Replace(text, m => $"{m.Groups[1].Value}\n\n{markdown}\n\n{m.Groups[3].Value}"));

        Console.WriteLine("✓ README benchmark tables updated");
    }

    private sealed class Results(JsonObject payload, JsonObject? payloadSizes)
    {
        public JsonObject Payload { get; } = payload;

        public JsonObject Scenarios { get; } = payload["scenarios"] as JsonObject ?? [];

        public JsonArray Sizes { get; } = payloadSizes?["payloads"] as JsonArray ?? [];

        public double? Value(string scenario, string key)
        {
            if (Scenarios[scenario] is not JsonObject data ||
                data["median"] is not JsonObject median ||
                median[key] is not JsonValue value ||
                !value.TryGetValue<double>(out var number))
            {
                return null;
            }

            return number;
        }

        public bool Unstable(string scenario) =>
            Scenarios[scenario] is JsonObject data &&
            data["unstable"] is JsonValue value &&
            value.TryGetValue<bool>(out var unstable) &&
            unstable;

        public double? CallsPerQuery(string scenario)
        {
            if (Scenarios[scenario] is not JsonObject data || data["runs"] is not JsonArray runs)
            {
                return null;
            }

            var ratios = new List<double>();
            foreach (var run in runs.OfType<JsonObject>())
            {
                if (run["grpc_calls"] is not JsonObject calls ||
                    calls["total"] is not JsonValue totalValue ||
                    !totalValue.TryGetValue<double>(out var total) ||
                    total == 0)
                {
                    continue;
                }

                var count = run["count"] is JsonValue c && c.TryGetValue<double>(out var parsed) && parsed > 0
                    ? parsed
                    : 1.0;
                ratios.Add(total / count);
            }

            return ratios.Count > 0 ? Formats.Round(ratios.Average(), 1) : null;
        }
    }

    private static string Ms(double? value, int digits = 2) =>
        value is null ? "—" : Formats.Number(value.Value, digits);

    private static string Seconds(double? value, int digits = 1) =>
        value is null ? "—" : Formats.Number(value.Value / 1000.0, digits);

    private static string Flag(Results results, string scenario) =>
        results.Unstable(scenario) ? " ⚠️" : string.Empty;

    private static List<string> TransportLatency(Results results)
    {
        var rows = new List<string>
        {
            "| Transport | p50 | p95 | p99 | throughput |",
            "|---|---|---|---|---|",
        };

        foreach (var (label, scenario) in new[]
        {
            ("gRPC", "score_one_grpc"),
            ("GraphQL", "score_one_graphql"),
            ("WebSocket", "score_one_ws"),
        })
        {
            rows.Add(
                $"| {label}{Flag(results, scenario)} " +
                $"| {Ms(results.Value(scenario, "p50_ms"))} ms " +
                $"| {Ms(results.Value(scenario, "p95_ms"))} ms " +
                $"| {Ms(results.Value(scenario, "p99_ms"))} ms " +
                $"| {Ms(results.Value(scenario, "rps"), 0)} req/s |");
        }

        return rows;
    }

    private static List<string> PayloadSizeTable(Results results)
    {
        var rows = new List<string>
        {
            "| Message | Protobuf | JSON | JSON is |",
            "|---|---|---|---|",
        };
        rows.AddRange(results.Sizes.OfType<JsonObject>()
            .Select(entry => $"| `{entry["message"]}` | {entry["protobuf_bytes"]} B " + $"| {entry["json_compact_bytes"]} B | {entry["json_compact_ratio"]}× larger |"));

        return rows;
    }

    private static List<string> SerialVsBatch(Results results)
    {
        var rows = new List<string>
        {
            "| Transport | 500 one at a time | 500 in one call | Ratio |",
            "|---|---|---|---|",
        };

        foreach (var (label, serial, batch) in new[]
        {
            ("gRPC", "score_serial_grpc", "score_batch_grpc"),
            ("GraphQL", "score_serial_graphql", "score_batch_graphql"),
            ("WebSocket", "score_serial_ws", "score_batch_ws"),
        })
        {
            var serialMs = results.Value(serial, "wall_ms");
            var batchMs = results.Value(batch, "p50_ms");
            var ratio = serialMs is > 0 && batchMs is > 0
                ? $"{Formats.Number(serialMs.Value / batchMs.Value, 0)}× faster"
                : "—";

            rows.Add($"| {label} | {Seconds(serialMs)} s | {Ms(batchMs)} ms | {ratio} |");
        }

        return rows;
    }

    private static List<string> NPlusOne(Results results)
    {
        var rows = new List<string>
        {
            "| `GATEWAY_BATCHING` | gRPC calls per query | p50 | p95 | p99 | throughput |",
            "|---|---|---|---|---|---|",
        };

        foreach (var (label, scenario) in new[]
        {
            ("`off` (naive)", "lots_graphql_naive"),
            ("`on` (DataLoader)", "lots_graphql_batched"),
        })
        {
            var calls = results.CallsPerQuery(scenario);
            rows.Add(
                $"| {label}{Flag(results, scenario)} " +
                $"| {(calls is null ? "—" : calls.Value.ToString("F0", Formats.Invariant))} " +
                $"| {Ms(results.Value(scenario, "p50_ms"))} ms " +
                $"| {Ms(results.Value(scenario, "p95_ms"))} ms " +
                $"| {Ms(results.Value(scenario, "p99_ms"))} ms " +
                $"| {Ms(results.Value(scenario, "rps"), 0)} req/s |");
        }

        rows.Add(
            $"| direct gRPC `ListLots` | 1 | {Ms(results.Value("lots_grpc", "p50_ms"))} ms " +
            $"| {Ms(results.Value("lots_grpc", "p95_ms"))} ms " +
            $"| {Ms(results.Value("lots_grpc", "p99_ms"))} ms " +
            $"| {Ms(results.Value("lots_grpc", "rps"), 0)} req/s |");

        return rows;
    }

    private static List<string> Streaming(Results results)
    {
        var rows = new List<string>
        {
            "| Scenario | Transport | Time to first token | Full answer |",
            "|---|---|---|---|",
        };

        var groups = new (string Label, (string Transport, string Scenario)[] Entries)[]
        {
            ("one listener", [
                ("gRPC", "stream_one_grpc"),
                ("GraphQL subscription", "stream_one_graphql"),
                ("raw WebSocket", "stream_one_ws"),
            ]),
            ("50 listeners", [
                ("gRPC", "fanout_50_grpc"),
                ("GraphQL subscription", "fanout_50_graphql"),
                ("raw WebSocket", "fanout_50_ws"),
            ]),
        };

        foreach (var (scenarioLabel, entries) in groups)
        {
            foreach (var (label, scenario) in entries)
            {
                var ttft = results.Value(scenario, "ttft_p50_ms");
                var total = results.Value(scenario, "total_p50_ms")
                    ?? results.Value(scenario, "p50_ms");

                rows.Add(
                    $"| {scenarioLabel} | {label}{Flag(results, scenario)} " +
                    $"| {Seconds(ttft)} s | {Seconds(total)} s |");
            }
        }

        return rows;
    }

    private static List<string> Resources(Results results)
    {
        if (results.Scenarios["score_one_grpc"] is not JsonObject data ||
            data["runs"] is not JsonArray runs)
        {
            return [];
        }

        var combined = new Dictionary<string, (double Cpu, double Memory)>(StringComparer.Ordinal);
        foreach (var run in runs.OfType<JsonObject>())
        {
            if (run["resources"] is not JsonObject resources)
            {
                continue;
            }

            foreach (var (service, node) in resources)
            {
                if (node is not JsonObject usage)
                {
                    continue;
                }

                combined.TryGetValue(service, out var current);
                combined[service] = (
                    Math.Max(current.Cpu, Read(usage, "cpu_pct_max")),
                    Math.Max(current.Memory, Read(usage, "mem_mb_max")));
            }
        }

        var caps = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["gateway"] = "1 CPU · 512 MB",
            ["auction-service"] = "1 CPU · 512 MB",
            ["ml-service"] = "1 CPU · 512 MB",
            ["ollama"] = "2 CPU · 4 GB",
            ["postgres"] = "1 CPU · 512 MB",
        };

        var rows = new List<string>
        {
            "| Container | Peak CPU | Peak memory | Cap |",
            "|---|---|---|---|",
        };

        foreach (var service in new[] { "gateway", "auction-service", "ml-service", "ollama", "postgres" })
        {
            if (!combined.TryGetValue(service, out var usage))
            {
                continue;
            }

            rows.Add(
                $"| `{service}` | {usage.Cpu.ToString("F0", Formats.Invariant)}% " +
                $"| {Formats.Number(usage.Memory, 0)} MB | {caps[service]} |");
        }

        return rows;

        static double Read(JsonObject source, string key) =>
            source[key] is JsonValue value && value.TryGetValue<double>(out var number) ? number : 0.0;
    }

    private static string Build(Results results)
    {
        var host = results.Payload["host"] as JsonObject;
        var threshold = results.Payload["instability_threshold"] is JsonValue t &&
            t.TryGetValue<double>(out var parsed) ? parsed : 0.10;

        var lines = new List<string>
        {
            $"<sub>Generated by `make bench` on {results.Payload["generated_at"]} · " +
            $"{results.Payload["runs_per_scenario"]} runs per scenario · " +
            $"{host?["platform"]} · do not edit by hand.</sub>",
            "",
            "#### Scoring one bid — the same work over three transports",
            "",
        };

        lines.AddRange(TransportLatency(results));
        lines.AddRange(["", "#### Message size, same data both ways", ""]);
        lines.AddRange(PayloadSizeTable(results));
        lines.AddRange(["", "#### 500 bids: one at a time versus one batch call", ""]);
        lines.AddRange(SerialVsBatch(results));
        lines.AddRange(["", "#### `lots(first: 20)` before and after the N+1 fix", ""]);
        lines.AddRange(NPlusOne(results));
        lines.AddRange(["", "#### Streaming an LLM answer", ""]);
        lines.AddRange(Streaming(results));
        lines.AddRange(["", "#### Resource use during the heaviest scoring run", ""]);
        lines.AddRange(Resources(results));

        if (results.Payload["unstable_scenarios"] is not JsonArray { Count: > 0 } unstable)
        {
            return string.Join("\n", lines);
        }

        var names = string.Join(", ", unstable.Select(name => $"`{name}`"));
        lines.AddRange([
            "",
            $"> ⚠️ These scenarios moved by more than {threshold:P0} between runs and are " +
            $"marked in the tables above: {names}. They are reported for completeness but " +
            "should be read as indicative only.",
        ]);

        return string.Join("\n", lines);
    }
}
