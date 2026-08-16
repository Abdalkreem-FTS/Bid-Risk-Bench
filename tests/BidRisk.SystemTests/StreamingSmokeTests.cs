using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Grpc.Core;

namespace BidRisk.SystemTests;

[Trait("Category", "SmokeLlm")]
public sealed class StreamingSmokeTests
{
    private const string Question = "Summarise the bidding activity on lot 38.";

    [Fact]
    public async Task AskAboutData_QuestionOverGrpc_StreamsTokensAndReportsTimings()
    {
        using var channel = Endpoints.Ml();
        var risk = new RiskService.RiskServiceClient(channel);

        var started = Stopwatch.StartNew();
        double? firstTokenAt = null;
        var text = new StringBuilder();
        StreamStats? stats = null;

        using var call = risk.AskAboutData(
            new AskAboutDataRequest { Question = Question },
            deadline: DateTime.UtcNow.Add(Endpoints.StreamDeadline));

        await foreach (var chunk in call.ResponseStream.ReadAllAsync())
        {
            if (chunk.PayloadCase == AskAboutDataResponse.PayloadOneofCase.Stats)
            {
                stats = chunk.Stats;
                continue;
            }

            firstTokenAt ??= started.Elapsed.TotalMilliseconds;
            text.Append(chunk.Token);
        }

        Assert.NotNull(stats);
        Assert.True(text.Length > 20, $"the stream produced almost nothing: '{text}'");
        Assert.True(stats.TokenCount > 0, "no tokens were counted");
        Assert.NotNull(firstTokenAt);

        Assert.True(stats.TimeToFirstTokenMs <= stats.TotalMs, $"first token {stats.TimeToFirstTokenMs} ms after total {stats.TotalMs} ms");
    }

    [Fact]
    public async Task AskAboutData_SubscriptionAndRawSocketShareABroadcastId_BothReceiveIdenticalText()
    {
        var broadcastId = $"smoke-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        var subscription = SubscriptionAsync(broadcastId);
        var rawSocket = RawSocketAsync(broadcastId);
        await Task.WhenAll(subscription, rawSocket);

        var fromSubscription = await subscription;
        var fromRawSocket = await rawSocket;

        Assert.True(fromSubscription.Length > 20, "the subscription produced almost nothing");
        Assert.True(fromRawSocket.Length > 20, "the raw socket produced almost nothing");
        Assert.Equal(fromRawSocket, fromSubscription);
    }

    private static async Task<string> SubscriptionAsync(string broadcastId)
    {
        using var socket = new ClientWebSocket();
        socket.Options.AddSubProtocol("graphql-transport-ws");
        using var timeout = new CancellationTokenSource(Endpoints.StreamDeadline);

        await socket.ConnectAsync(
            new Uri($"{Endpoints.GatewayWebSocket}/graphql"), timeout.Token);

        await SendAsync(socket, new JsonObject { ["type"] = "connection_init" }, timeout.Token);

        var ack = await ReceiveAsync(socket, timeout.Token);
        Assert.Equal("connection_ack", ack?["type"]?.GetValue<string>());

        await SendAsync(socket, new JsonObject
        {
            ["id"] = "1",
            ["type"] = "subscribe",
            ["payload"] = new JsonObject
            {
                ["query"] = """
                    subscription Ask($q: String!, $b: String) {
                      askAboutData(question: $q, broadcastId: $b) {
                        token done timeToFirstTokenMs totalMs tokenCount } }
                    """,
                ["variables"] = new JsonObject
                {
                    ["q"] = Question,
                    ["b"] = broadcastId,
                },
            },
        }, timeout.Token);

        var text = new StringBuilder();
        while (true)
        {
            var message = await ReceiveAsync(socket, timeout.Token);
            if (message is null)
            {
                break;
            }

            var type = message["type"]?.GetValue<string>();
            if (type == "error")
            {
                Assert.Fail($"subscription error: {message["payload"]}");
            }

            if (type == "complete")
            {
                break;
            }

            if (type != "next")
            {
                continue;
            }

            var chunk = message["payload"]?["data"]?["askAboutData"];
            if (chunk?["done"]?.GetValue<bool>() == true)
            {
                break;
            }

            if (chunk?["token"]?.GetValue<string>() is { Length: > 0 } token)
            {
                text.Append(token);
            }
        }

        return text.ToString();
    }

    private static async Task<string> RawSocketAsync(string broadcastId)
    {
        using var socket = new ClientWebSocket();
        using var timeout = new CancellationTokenSource(Endpoints.StreamDeadline);

        await socket.ConnectAsync(new Uri($"{Endpoints.GatewayWebSocket}/ws"), timeout.Token);

        await SendAsync(socket, new JsonObject
        {
            ["question"] = Question,
            ["broadcastId"] = broadcastId,
        }, timeout.Token);

        var text = new StringBuilder();
        while (true)
        {
            var frame = await ReceiveAsync(socket, timeout.Token);
            if (frame is null)
            {
                break;
            }

            if (frame["error"] is not null)
            {
                Assert.Fail($"raw socket error: {frame["error"]}");
            }

            if (frame["done"]?.GetValue<bool>() == true)
            {
                break;
            }

            if (frame["token"]?.GetValue<string>() is { Length: > 0 } token)
            {
                text.Append(token);
            }
        }

        return text.ToString();
    }

    private static async Task SendAsync(
        ClientWebSocket socket, JsonNode message, CancellationToken cancellationToken) =>
        await socket.SendAsync(
            Encoding.UTF8.GetBytes(message.ToJsonString()),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);

    private static async Task<JsonNode?> ReceiveAsync(
        ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var payload = new List<byte>();

        while (true)
        {
            var received = await socket.ReceiveAsync(buffer, cancellationToken);
            if (received.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            payload.AddRange(buffer.AsSpan(0, received.Count).ToArray());
            if (received.EndOfMessage)
            {
                break;
            }
        }

        return payload.Count == 0
            ? null
            : JsonNode.Parse(Encoding.UTF8.GetString([.. payload]));
    }
}
