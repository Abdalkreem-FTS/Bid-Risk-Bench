using System.Text.Json.Nodes;
using ScottPlot;

namespace BidRisk.Bench;

internal static class Charts
{
    private static readonly Color Grpc = Color.FromHex("#4c78a8");
    private static readonly Color GraphQL = Color.FromHex("#e45756");
    private static readonly Color WebSocket = Color.FromHex("#54a24b");
    private static readonly Color Muted = Color.FromHex("#8b94a3");
    private static readonly Color Llm = Color.FromHex("#b279a2");
    private static readonly Color LlmDark = Color.FromHex("#7f5f9f");

    public static void Draw(FileInfo results, DirectoryInfo output)
    {
        var payload = JsonNode.Parse(File.ReadAllText(results.FullName)) as JsonObject
            ?? throw new ArgumentException($"{results.FullName} is not an object");

        var scenarios = payload["scenarios"] as JsonObject ?? [];
        output.Create();

        Console.WriteLine("▸ charts");

        foreach (var (name, draw) in new (string, Action<JsonObject, DirectoryInfo>)[]
        {
            ("score-one-bid", ScoreLatency),
            ("where-time-goes", WhereTheTimeGoes),
            ("serial-vs-batch", SerialVsBatch),
            ("n-plus-one", NPlusOne),
            ("streaming", Streaming),
            ("bytes", Bytes),
        })
        {
            try
            {
                draw(scenarios, output);
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException)
            {
                Console.WriteLine($"  skipped {name}: {error.Message}");
            }
        }
    }

    private static void ScoreLatency(JsonObject scenarios, DirectoryInfo output)
    {
        var transports = new (string Label, string Scenario, Color Colour)[]
        {
            ("gRPC", "score_one_grpc", Grpc),
            ("GraphQL", "score_one_graphql", GraphQL),
            ("WebSocket", "score_one_ws", WebSocket),
        };
        var percentiles = new[] { "p50_ms", "p95_ms", "p99_ms" };

        var plot = NewPlot();
        const double width = 0.26;

        for (var offset = 0; offset < transports.Length; offset++)
        {
            var (label, scenario, colour) = transports[offset];
            var bars = percentiles
                .Select((key, index) => MakeBar(
                    index + ((offset - 1) * width),
                    Median(scenarios, scenario, key) ?? 0.0,
                    width,
                    colour,
                    "0.00"))
                .ToList();

            var series = plot.Add.Bars(bars);
            series.LegendText = label;
        }

        plot.Axes.Bottom.SetTicks([0, 1, 2], ["p50", "p95", "p99"]);
        plot.YLabel("latency (ms)");
        plot.Title("Scoring one bid — 500 calls, 10 concurrent");
        plot.ShowLegend(Alignment.UpperLeft);
        Save(plot, output, "score-one-bid.png", 900, 520);
    }

    private static void WhereTheTimeGoes(JsonObject scenarios, DirectoryInfo output)
    {
        var inference = Median(scenarios, "score_one_graphql", "model_inference_p50_ms") ?? 0.0;
        var grpcScore = Median(scenarios, "score_one_grpc", "p50_ms") ?? 0.0;
        var graphqlScore = Median(scenarios, "score_one_graphql", "p50_ms") ?? 0.0;
        var llmTotal = Median(scenarios, "stream_one_ws", "total_p50_ms")
            ?? Median(scenarios, "stream_one_grpc", "total_p50_ms") ?? 0.0;
        var llmTtft = Median(scenarios, "stream_one_ws", "ttft_p50_ms")
            ?? Median(scenarios, "stream_one_grpc", "ttft_p50_ms") ?? 0.0;
        var transportGap = Math.Abs(graphqlScore - grpcScore);

        string[] labels =
        [
            "model inference\n(inside ml-service)",
            "score one bid\nend to end, gRPC",
            "gRPC vs GraphQL\ndifference",
            "LLM first token",
            "LLM full answer",
        ];
        double[] values = [inference, grpcScore, transportGap, llmTtft, llmTotal];
        Color[] colours = [Muted, Grpc, GraphQL, Llm, LlmDark];

        const double floorMs = 0.1;

        var plot = NewPlot();
        var bars = values.Select((t, index) => new Bar
            {
                Position = index,
                Value = Decades(t),
                FillColor = colours[index],
                Label = t < 1000
                    ? $"{Formats.Number(t, 1)} ms"
                    : $"{Formats.Number(t / 1000.0, 1)} s",
            })
            .ToList();

        plot.Add.Bars(bars);
        plot.Axes.Left.SetTicks(
            [0, 1, 2, 3, 4, 5],
            ["0.1", "1", "10", "100", "1k", "10k"]);
        plot.Axes.Bottom.SetTicks(
            Enumerable.Range(0, labels.Length).Select(i => (double)i).ToArray(), labels);
        plot.Axes.SetLimitsY(0, Decades(values.Max()) + 0.7);
        plot.YLabel("milliseconds (log scale)");
        plot.Title("Where the time actually goes");
        Save(plot, output, "where-time-goes.png", 1000, 560);
        return;

        static double Decades(double milliseconds) => Math.Log10(Math.Max(milliseconds, floorMs) / floorMs);
    }

    private static void SerialVsBatch(JsonObject scenarios, DirectoryInfo output)
    {
        var transports = new (string Label, string Serial, string Batch, Color Colour)[]
        {
            ("gRPC", "score_serial_grpc", "score_batch_grpc", Grpc),
            ("GraphQL", "score_serial_graphql", "score_batch_graphql", GraphQL),
            ("WebSocket", "score_serial_ws", "score_batch_ws", WebSocket),
        };

        var labels = new List<string>();
        var serialTotals = new List<Bar>();
        var batchTotals = new List<Bar>();

        foreach (var (label, serial, batch, colour) in transports)
        {
            var serialWall = Median(scenarios, serial, "wall_ms");
            var batchP50 = Median(scenarios, batch, "p50_ms");
            if (serialWall is null || batchP50 is null)
            {
                continue;
            }

            var position = labels.Count;
            labels.Add(label);
            serialTotals.Add(MakeBar(position, serialWall.Value / 1000.0, 0.55, colour, "0.0", "s"));
            batchTotals.Add(MakeBar(position, batchP50.Value / 1000.0, 0.55, colour, "0.000", "s"));
        }

        if (labels.Count == 0)
        {
            throw new ArgumentException("no serial/batch scenarios present");
        }

        var ticks = Enumerable.Range(0, labels.Count).Select(i => (double)i).ToArray();

        var figure = SideBySide();

        var left = figure.GetPlot(0);
        Style(left);
        left.Add.Bars(serialTotals);
        left.Axes.Bottom.SetTicks(ticks, [.. labels]);
        left.YLabel("seconds for 500 bids");
        left.Title("500 bids, one call each");

        var right = figure.GetPlot(1);
        Style(right);
        right.Add.Bars(batchTotals);
        right.Axes.Bottom.SetTicks(ticks, [.. labels]);
        right.YLabel("seconds for 500 bids");
        right.Title("500 bids, one batch call");

        SaveMultiplot(figure, output, "serial-vs-batch.png", 1200, 520);
    }

    private static void NPlusOne(JsonObject scenarios, DirectoryInfo output)
    {
        if (scenarios["lots_graphql_naive"] is not JsonObject naive ||
            scenarios["lots_graphql_batched"] is not JsonObject batched)
        {
            throw new ArgumentException("N+1 scenarios not present");
        }

        var percentiles = new[] { "p50_ms", "p95_ms", "p99_ms" };
        const double width = 0.35;

        var figure = SideBySide();

        var left = figure.GetPlot(0);
        Style(left);
        var variants = new (string Label, JsonObject Data, Color Colour)[]
        {
            ("naive", naive, GraphQL),
            ("batched", batched, WebSocket),
        };

        for (var offset = 0; offset < variants.Length; offset++)
        {
            var (label, data, colour) = variants[offset];
            var median = data["median"] as JsonObject ?? [];
            var bars = percentiles
                .Select((key, index) => MakeBar(
                    index + ((offset - 0.5) * width),
                    Read(median, key),
                    width,
                    colour,
                    "0.0"))
                .ToList();

            var series = left.Add.Bars(bars);
            series.LegendText = label;
        }

        left.Axes.Bottom.SetTicks([0, 1, 2], ["p50", "p95", "p99"]);
        left.YLabel("latency (ms)");
        left.Title("lots(first: 20) — latency");
        left.ShowLegend(Alignment.UpperLeft);

        var right = figure.GetPlot(1);
        Style(right);
        right.Add.Bars(new List<Bar>
        {
            MakeBar(0, CallsPerQuery(naive), 0.5, GraphQL, "0"),
            MakeBar(1, CallsPerQuery(batched), 0.5, WebSocket, "0"),
        });
        right.Axes.Bottom.SetTicks([0, 1], ["naive", "batched"]);
        right.YLabel("gRPC calls per query");
        right.Title("lots(first: 20) — round trips");

        SaveMultiplot(figure, output, "n-plus-one.png", 1200, 520);
    }

    private static void Streaming(JsonObject scenarios, DirectoryInfo output)
    {
        var groups = new (string Title, (string Label, string Scenario, Color Colour)[] Transports)[]
        {
            ("one listener", [
                ("gRPC", "stream_one_grpc", Grpc),
                ("GraphQL", "stream_one_graphql", GraphQL),
                ("WebSocket", "stream_one_ws", WebSocket),
            ]),
            ("50 listeners", [
                ("gRPC", "fanout_50_grpc", Grpc),
                ("GraphQL", "fanout_50_graphql", GraphQL),
                ("WebSocket", "fanout_50_ws", WebSocket),
            ]),
        };

        var figure = SideBySide();
        const double width = 0.38;
        var drewAnything = false;

        for (var index = 0; index < groups.Length; index++)
        {
            var (title, transports) = groups[index];
            var plot = figure.GetPlot(index);
            Style(plot);

            var labels = new List<string>();
            var ttftBars = new List<Bar>();
            var totalBars = new List<Bar>();

            foreach (var (label, scenario, colour) in transports)
            {
                var ttft = Median(scenarios, scenario, "ttft_p50_ms");
                var total = Median(scenarios, scenario, "total_p50_ms")
                    ?? Median(scenarios, scenario, "p50_ms");
                if (ttft is null && total is null)
                {
                    continue;
                }

                var position = labels.Count;
                labels.Add(label);
                ttftBars.Add(MakeBar(
                    position - (width / 2), (ttft ?? 0.0) / 1000.0, width, colour, "0.0"));
                totalBars.Add(MakeBar(
                    position + (width / 2), (total ?? 0.0) / 1000.0, width,
                    colour.WithAlpha(0.45), "0.0"));
            }

            if (labels.Count == 0)
            {
                continue;
            }

            drewAnything = true;
            var first = plot.Add.Bars(ttftBars);
            first.LegendText = "first token";
            var second = plot.Add.Bars(totalBars);
            second.LegendText = "full answer";

            plot.Axes.Bottom.SetTicks(
                Enumerable.Range(0, labels.Count).Select(i => (double)i).ToArray(), [.. labels]);
            plot.Title($"Streaming — {title}");
            plot.YLabel("seconds");

            if (index == 0)
            {
                plot.ShowLegend(Alignment.UpperLeft);
            }
        }

        if (!drewAnything)
        {
            throw new ArgumentException("no streaming scenarios present");
        }

        SaveMultiplot(figure, output, "streaming.png", 1200, 520);
    }

    private static void Bytes(JsonObject scenarios, DirectoryInfo output)
    {
        var entries = new (string Label, string Scenario, string Service, Color Colour)[]
        {
            ("gRPC\n(protobuf)", "score_one_grpc", "ml-service", Grpc),
            ("GraphQL\n(JSON)", "score_one_graphql", "gateway", GraphQL),
            ("WebSocket\n(JSON)", "score_one_ws", "gateway", WebSocket),
        };

        var labels = new List<string>();
        var bars = new List<Bar>();

        foreach (var (label, scenario, service, colour) in entries)
        {
            if (scenarios[scenario] is not JsonObject data || data["runs"] is not JsonArray runs)
            {
                continue;
            }

            var totals = new List<double>();
            foreach (var run in runs.OfType<JsonObject>())
            {
                var bytes = run["bytes"] as JsonObject ?? [];
                var received = Read(bytes, $"{service}_rx");
                var sent = Read(bytes, $"{service}_tx");
                var count = run["count"] is JsonValue c && c.TryGetValue<double>(out var parsed) && parsed > 0
                    ? parsed
                    : 1.0;

                if (received != 0 || sent != 0)
                {
                    totals.Add((received + sent) / count);
                }
            }

            if (totals.Count == 0)
            {
                continue;
            }

            bars.Add(MakeBar(labels.Count, totals.Average(), 0.5, colour, "0", " B"));
            labels.Add(label);
        }

        if (labels.Count == 0)
        {
            throw new ArgumentException("no interface byte counts recorded");
        }

        var plot = NewPlot();
        plot.Add.Bars(bars);
        plot.Axes.Bottom.SetTicks(
            Enumerable.Range(0, labels.Count).Select(i => (double)i).ToArray(), [.. labels]);
        plot.YLabel("bytes on the wire per scored bid");
        plot.Title("Message size, measured at the interface");
        Save(plot, output, "bytes.png", 820, 520);
    }

    private static Plot NewPlot()
    {
        var plot = new Plot();
        Style(plot);
        return plot;
    }

    private static Multiplot SideBySide()
    {
        var figure = new Multiplot();
        figure.AddPlots(2);
        figure.Layout = new ScottPlot.MultiplotLayouts.Grid(rows: 1, columns: 2);
        return figure;
    }

    private static void Style(Plot plot)
    {
        plot.FigureBackground.Color = Colors.White;
        plot.DataBackground.Color = Colors.White;

        plot.Axes.Top.FrameLineStyle.IsVisible = false;
        plot.Axes.Right.FrameLineStyle.IsVisible = false;

        plot.Grid.MajorLineColor = Colors.Black.WithAlpha(0.12);
        plot.Grid.XAxisStyle.IsVisible = false;

        plot.Axes.Margins(bottom: 0.0, top: 0.20);
    }

    private static Bar MakeBar(
        double position,
        double value,
        double width,
        Color colour,
        string format,
        string suffix = "")
    {
        return new Bar
        {
            Position = position,
            Value = value,
            Size = width,
            FillColor = colour,
            Label = value.ToString(format, Formats.Invariant) + suffix,
        };
    }

    private static void Save(Plot plot, DirectoryInfo output, string name, int width, int height)
    {
        plot.Axes.AutoScale();
        var path = Path.Combine(output.FullName, name);
        plot.SavePng(path, width, height);
        Console.WriteLine($"  {name}");
    }

    private static void SaveMultiplot(
        Multiplot figure, DirectoryInfo output, string name, int width, int height)
    {
        var path = Path.Combine(output.FullName, name);
        figure.SavePng(path, width, height);
        Console.WriteLine($"  {name}");
    }

    private static double? Median(JsonObject scenarios, string scenario, string key)
    {
        if (scenarios[scenario] is not JsonObject data ||
            data["median"] is not JsonObject median ||
            median[key] is not JsonValue value ||
            !value.TryGetValue<double>(out var number))
        {
            return null;
        }

        return number;
    }

    private static double CallsPerQuery(JsonObject data)
    {
        if (data["runs"] is not JsonArray runs)
        {
            return 0.0;
        }

        var ratios = new List<double>();
        foreach (var run in runs.OfType<JsonObject>())
        {
            var total = run["grpc_calls"] is JsonObject calls ? Read(calls, "total") : 0.0;
            if (total == 0)
            {
                continue;
            }

            var count = run["count"] is JsonValue c && c.TryGetValue<double>(out var parsed) && parsed > 0
                ? parsed
                : 1.0;
            ratios.Add(total / count);
        }

        return ratios.Count > 0 ? Formats.Round(ratios.Average(), 1) : 0.0;
    }

    private static double Read(JsonObject source, string key) =>
        source[key] is JsonValue value && value.TryGetValue<double>(out var number) ? number : 0.0;
}
