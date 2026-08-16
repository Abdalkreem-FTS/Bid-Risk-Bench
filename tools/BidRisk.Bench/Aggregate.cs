using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BidRisk.Bench;

internal static class Aggregate
{
    private const double InstabilityThreshold = 0.10;

    private const double MinimumAbsoluteSpreadMs = 3.0;
    private const double NanosecondsPerMs = 1_000_000.0;

    public static void Write(DirectoryInfo raw, FileInfo output, int runs)
    {
        var runDirectories = raw.Exists
            ? raw.GetDirectories("run*").OrderBy(d => d.Name, StringComparer.Ordinal).ToArray()
            : [];

        var labels = runDirectories
            .SelectMany(directory => directory.GetFiles("*.tool.json"))
            .Select(file => file.Name[..^".tool.json".Length])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(label => label, StringComparer.Ordinal)
            .ToArray();

        var scenarios = new JsonObject();
        var unstable = new List<string>();

        foreach (var label in labels)
        {
            var collected = runDirectories
                .Select(directory => CollectRun(directory, label))
                .OfType<JsonObject>()
                .ToList();

            if (collected.Count == 0)
            {
                continue;
            }

            var summary = Summarise(collected);
            scenarios[label] = summary;

            if (summary["unstable"]!.GetValue<bool>())
            {
                unstable.Add(label);
            }
        }

        var payload = new JsonObject
        {
            ["generated_at"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:sszzz", Formats.Invariant),
            ["generated_by"] = "scripts/bench.sh — do not edit by hand",
            ["runs_per_scenario"] = runs,
            ["host"] = new JsonObject
            {
                ["platform"] = RuntimeInformation.OSDescription,
                ["machine"] = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
                ["dotnet"] = RuntimeInformation.FrameworkDescription,
            },
            ["instability_threshold"] = InstabilityThreshold,
            ["minimum_absolute_spread_ms"] = MinimumAbsoluteSpreadMs,
            ["unstable_scenarios"] = new JsonArray(unstable.Select(u => (JsonNode)u).ToArray()),
            ["scenarios"] = scenarios,
        };

        output.Directory?.Create();
        File.WriteAllText(output.FullName, payload.ToJsonString(Formats.Indented) + "\n");

        Console.WriteLine(
            $"✓ {output.FullName}: {scenarios.Count} scenarios × {runDirectories.Length} run(s)");
        if (unstable.Count > 0)
        {
            Console.WriteLine(
                $"⚠ unstable (>{InstabilityThreshold:P0} and >{MinimumAbsoluteSpreadMs:F0} ms " +
                "spread), do not quote: " +
                string.Join(", ", unstable));
        }
    }

    private static JsonObject? CollectRun(DirectoryInfo directory, string label)
    {
        var toolOutput = ReadJson(directory, $"{label}.tool.json");
        if (toolOutput is null)
        {
            return null;
        }

        var record =
            toolOutput.ContainsKey("latencyDistribution") ? NormaliseGhz(toolOutput)
            : toolOutput.ContainsKey("metrics") ? NormaliseK6(toolOutput)
            : NormaliseProbe(toolOutput);

        var wall = ReadJson(directory, $"{label}.wall.json");
        record["wall_ms"] = Number(wall, "wall_ms");
        record["bytes"] = NetworkDelta(
            ReadJson(directory, $"{label}.net.before.json"),
            ReadJson(directory, $"{label}.net.after.json"));
        record["resources"] = ResourceUsage(new FileInfo(
            Path.Combine(directory.FullName, $"{label}.stats.csv")));

        if (ReadJson(directory, $"{label}.calls.json") is { } calls)
        {
            record["grpc_calls"] = calls.DeepClone();
        }

        return record;
    }

    private static JsonObject NormaliseGhz(JsonObject payload)
    {
        var distribution = payload["latencyDistribution"] as JsonArray ?? [];
        var statuses = payload["statusCodeDistribution"] as JsonObject;

        return new JsonObject
        {
            ["tool"] = "ghz",
            ["count"] = (int)Number(payload, "count"),
            ["ok"] = statuses is not null ? (int)Number(statuses, "OK") : 0,
            ["p50_ms"] = GhzPercentile(distribution, 50),
            ["p95_ms"] = GhzPercentile(distribution, 95),
            ["p99_ms"] = GhzPercentile(distribution, 99),
            ["mean_ms"] = Number(payload, "average") / NanosecondsPerMs,
            ["rps"] = Number(payload, "rps"),
        };
    }

    private static double GhzPercentile(JsonArray distribution, int wanted)
    {
        return (from entry in distribution.OfType<JsonObject>()
                where (int)Number(entry, "percentage") == wanted
                select Number(entry, "latency") / NanosecondsPerMs
                ).FirstOrDefault();
    }

    private static JsonObject NormaliseK6(JsonObject payload)
    {
        var metrics = payload["metrics"] as JsonObject ?? [];
        var durationMs = Number(payload["state"] as JsonObject, "testRunDurationMs");
        var iterations = Number(
            (metrics["iterations"] as JsonObject)?["values"] as JsonObject, "count");

        var latency =
            Trend(metrics, "http_req_duration")
            ?? Trend(metrics, "score_duration")
            ?? Trend(metrics, "time_to_last_token")
            ?? new Trends(0, 0, 0, 0);

        var result = new JsonObject
        {
            ["tool"] = "k6",
            ["count"] = (int)iterations,
            ["ok"] = (int)iterations,
            ["rps"] = durationMs > 0 ? iterations / (durationMs / 1000.0) : 0.0,
            ["k6_data_sent_bytes"] =
                Number((metrics["data_sent"] as JsonObject)?["values"] as JsonObject, "count"),
            ["k6_data_received_bytes"] =
                Number((metrics["data_received"] as JsonObject)?["values"] as JsonObject, "count"),
            ["p50_ms"] = latency.P50,
            ["p95_ms"] = latency.P95,
            ["p99_ms"] = latency.P99,
            ["mean_ms"] = latency.Mean,
        };

        foreach (var (name, key) in new[]
        {
            ("time_to_first_token", "ttft"),
            ("time_to_last_token", "total"),
            ("server_time_to_first_token", "server_ttft"),
            ("model_inference_ms", "model_inference"),
        })
        {
            if (Trend(metrics, name) is not { } values)
            {
                continue;
            }

            result[$"{key}_p50_ms"] = values.P50;
            result[$"{key}_p95_ms"] = values.P95;
        }

        foreach (var counter in new[] { "tokens_received", "scores_received", "answers_completed" })
        {
            if (metrics[counter] is JsonObject metric)
            {
                result[counter] = Number(metric["values"] as JsonObject, "count");
            }
        }

        return result;
    }

    private sealed record Trends(double P50, double P95, double P99, double Mean);

    private static Trends? Trend(JsonObject metrics, string name)
    {
        if (metrics[name] is not JsonObject metric || metric["values"] is not JsonObject values)
        {
            return null;
        }

        return new Trends(
            Number(values, "med"),
            Number(values, "p(95)"),
            Number(values, "p(99)"),
            Number(values, "avg"));
    }

    private static JsonObject NormaliseProbe(JsonObject payload)
    {
        var ttft = payload["time_to_first_token_ms"] as JsonObject;
        var total = payload["total_ms"] as JsonObject;
        var server = payload["server_time_to_first_token_ms"] as JsonObject;
        var listeners = (int)Number(payload, "listeners");

        return new JsonObject
        {
            ["tool"] = "probe",
            ["count"] = listeners,
            ["ok"] = listeners,
            ["p50_ms"] = Number(total, "p50"),
            ["p95_ms"] = Number(total, "p95"),
            ["p99_ms"] = Number(total, "p99"),
            ["mean_ms"] = Number(total, "mean"),
            ["rps"] = 0.0,
            ["ttft_p50_ms"] = Number(ttft, "p50"),
            ["ttft_p95_ms"] = Number(ttft, "p95"),
            ["total_p50_ms"] = Number(total, "p50"),
            ["server_ttft_p50_ms"] = Number(server, "p50"),
            ["distinct_token_counts"] = (int)Number(payload, "distinct_token_counts"),
        };
    }

    private static JsonObject NetworkDelta(JsonObject? before, JsonObject? after)
    {
        var delta = new JsonObject();
        if (before is null || after is null)
        {
            return delta;
        }

        foreach (var (service, node) in after)
        {
            if (node is not JsonObject values)
            {
                continue;
            }

            var start = before[service] as JsonObject;
            delta[$"{service}_rx"] = (long)(Number(values, "rx") - Number(start, "rx"));
            delta[$"{service}_tx"] = (long)(Number(values, "tx") - Number(start, "tx"));
        }

        return delta;
    }

    internal static double ParseMemory(string text)
    {
        var used = text.Split('/')[0].Trim();

        foreach (var (suffix, factor) in new[]
        {
            ("GiB", 1024.0),
            ("MiB", 1.0),
            ("KiB", 1 / 1024.0),
            ("B", 1 / 1048576.0),
        })
        {
            if (used.EndsWith(suffix, StringComparison.Ordinal))
            {
                return double.TryParse(
                    used[..^suffix.Length], System.Globalization.NumberStyles.Float,
                    Formats.Invariant, out var value)
                    ? value * factor
                    : 0.0;
            }
        }

        return 0.0;
    }

    private static JsonObject ResourceUsage(FileInfo path)
    {
        var result = new JsonObject();
        if (!path.Exists)
        {
            return result;
        }

        var cpu = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        var memory = new Dictionary<string, List<double>>(StringComparer.Ordinal);

        foreach (var line in File.ReadLines(path.FullName))
        {
            var row = line.Split(',');
            if (row.Length < 3)
            {
                continue;
            }

            var name = row[0];
            const string prefix = "bid-risk-bench-";
            var service = name.StartsWith(prefix, StringComparison.Ordinal)
                ? name[prefix.Length..]
                : name;
            var lastDash = service.LastIndexOf('-');
            if (lastDash > 0)
            {
                service = service[..lastDash];
            }

            if (!double.TryParse(
                    row[1].Trim().TrimEnd('%'), System.Globalization.NumberStyles.Float,
                    Formats.Invariant, out var cpuPercent))
            {
                continue;
            }

            cpu.TryAdd(service, []);
            cpu[service].Add(cpuPercent);
            memory.TryAdd(service, []);
            memory[service].Add(ParseMemory(row[2]));
        }

        foreach (var (service, samples) in cpu)
        {
            if (samples.Count == 0)
            {
                continue;
            }

            result[service] = new JsonObject
            {
                ["cpu_pct_mean"] = Formats.Round(samples.Average(), 1),
                ["cpu_pct_max"] = Formats.Round(samples.Max(), 1),
                ["mem_mb_max"] = Formats.Round(
                    memory.TryGetValue(service, out var used) && used.Count > 0 ? used.Max() : 0.0,
                    1),
            };
        }

        return result;
    }

    private static double AbsoluteSpread(IEnumerable<double> values)
    {
        var usable = values.Where(value => value > 0).OrderBy(value => value).ToArray();
        return usable.Length < 2 ? 0.0 : usable[^1] - usable[0];
    }

    private static double Spread(IEnumerable<double> values)
    {
        var usable = values.Where(value => value > 0).OrderBy(value => value).ToArray();
        if (usable.Length < 2)
        {
            return 0.0;
        }

        var middle = StreamProbe.Median(usable);
        return middle == 0 ? 0.0 : (usable[^1] - usable[0]) / middle;
    }

    private static string StabilityMetric(List<JsonObject> runs) =>
        runs.Any(run => run.ContainsKey("ttft_p50_ms")) ? "ttft_p50_ms" : "p50_ms";

    private static JsonObject Summarise(List<JsonObject> runs)
    {
        var numericKeys = runs
            .SelectMany(run => run)
            .Where(pair => pair.Value is JsonValue value && value.TryGetValue<double>(out _))
            .Select(pair => pair.Key)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal);

        var median = new JsonObject();
        foreach (var key in numericKeys)
        {
            var values = runs
                .Where(run => run.ContainsKey(key))
                .Select(run => Number(run, key))
                .OrderBy(value => value)
                .ToArray();

            median[key] = Formats.Round(StreamProbe.Median(values), 3);
        }

        var metric = StabilityMetric(runs);
        var latencySpread = Spread(runs.Select(run => Number(run, metric)));
        var absoluteSpread = AbsoluteSpread(runs.Select(run => Number(run, metric)));

        return new JsonObject
        {
            ["runs"] = new JsonArray(runs.Select(run => run.DeepClone()).ToArray()),
            ["median"] = median,
            ["stability_metric"] = metric,
            ["p50_spread"] = Formats.Round(Spread(runs.Select(run => Number(run, "p50_ms"))), 4),
            ["stability_spread"] = Formats.Round(latencySpread, 4),
            ["absolute_spread_ms"] = Formats.Round(absoluteSpread, 3),
            ["unstable"] =
                latencySpread > InstabilityThreshold && absoluteSpread > MinimumAbsoluteSpreadMs,
        };
    }

    private static JsonObject? ReadJson(DirectoryInfo directory, string name)
    {
        var path = Path.Combine(directory.FullName, name);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static double Number(JsonObject? source, string key) =>
        source?[key] is JsonValue value && value.TryGetValue<double>(out var number) ? number : 0.0;
}
