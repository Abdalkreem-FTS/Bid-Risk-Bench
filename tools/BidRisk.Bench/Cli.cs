namespace BidRisk.Bench;

internal static class Cli
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            await Console.Error.WriteLineAsync(
                "usage: BidRisk.Bench <payloads|sizes|probe|aggregate|tables|charts|seed> [options]");
            return 2;
        }

        var options = Parse(args.Skip(1));

        try
        {
            switch (args[0])
            {
                case "payloads":
                    Payloads.Write(Directory(options, "--out"));
                    return 0;

                case "sizes":
                    PayloadSizes.Write(File(options, "--out"));
                    return 0;

                case "probe":
                    await StreamProbe.RunAsync(
                        listeners: Int(options, "--listeners", 1),
                        question: Text(options, "--question", StreamProbe.DefaultQuestion),
                        broadcastId: Text(options, "--broadcast-id", ""),
                        output: File(options, "--out"));
                    return 0;

                case "aggregate":
                    Aggregate.Write(
                        raw: Directory(options, "--raw"),
                        output: File(options, "--out"),
                        runs: Int(options, "--runs", 3));
                    return 0;

                case "tables":
                    ReportTables.Write(
                        results: File(options, "--results"),
                        payloadSizes: File(options, "--payload-sizes"),
                        readme: File(options, "--readme"));
                    return 0;

                case "charts":
                    Charts.Draw(
                        results: File(options, "--results"),
                        output: Directory(options, "--out"));
                    return 0;

                case "seed":
                    await SeedData.WriteAsync(File(options, "--out"));
                    return 0;

                default:
                    await Console.Error.WriteLineAsync($"unknown verb '{args[0]}'");
                    return 2;
            }
        }
        catch (ArgumentException error)
        {
            await Console.Error.WriteLineAsync(error.Message);
            return 2;
        }
    }

    private static Dictionary<string, string> Parse(IEnumerable<string> args)
    {
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        string? pending = null;

        foreach (var token in args)
        {
            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                if (pending is not null)
                {
                    parsed[pending] = string.Empty;
                }

                pending = token;
            }
            else if (pending is not null)
            {
                parsed[pending] = token;
                pending = null;
            }
        }

        if (pending is not null)
        {
            parsed[pending] = string.Empty;
        }

        return parsed;
    }

    private static string Required(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) && value.Length > 0
            ? value
            : throw new ArgumentException($"{name} is required");

    private static FileInfo File(Dictionary<string, string> options, string name) =>
        new(Required(options, name));

    private static DirectoryInfo Directory(Dictionary<string, string> options, string name) =>
        new(Required(options, name));

    private static string Text(Dictionary<string, string> options, string name, string fallback) =>
        options.TryGetValue(name, out var value) && value.Length > 0 ? value : fallback;

    private static int Int(Dictionary<string, string> options, string name, int fallback) =>
        options.TryGetValue(name, out var value) && int.TryParse(value, out var parsed)
            ? parsed
            : fallback;
}
