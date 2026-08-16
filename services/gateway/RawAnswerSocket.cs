using System.Net.WebSockets;
using System.Text.Json;
using Grpc.Core;

namespace BidRisk.Gateway;

internal static class RawAnswerSocket
{
    private sealed record AskMessage(string? Question, string? BroadcastId);

    private sealed record TokenFrame(string Token);

    private sealed record DoneFrame(
        bool Done,
        double TimeToFirstTokenMs,
        double TotalMs,
        int TokenCount);

    private sealed record ErrorFrame(string Error);

    public static async Task HandleAsync(
        HttpContext context,
        RiskService.RiskServiceClient risk,
        ILogger logger)
    {
        using var socket = await RawSocket.AcceptAsync(context);
        if (socket is null)
        {
            return;
        }

        var cancellationToken = context.RequestAborted;

        var ask = await ReceiveAskAsync(socket, cancellationToken);
        if (ask?.Question is not { Length: > 0 } question)
        {
            await RawSocket.SendAsync(socket, new ErrorFrame("send {\"question\": \"…\"} first"), cancellationToken);
            await socket.CloseAsync(WebSocketCloseStatus.InvalidPayloadData, "no question", cancellationToken);

            return;
        }

        try
        {
            using var call = risk.AskAboutData(
                new AskAboutDataRequest
                {
                    Question = question,
                    BroadcastId = ask.BroadcastId ?? string.Empty,
                },
                cancellationToken: cancellationToken);

            await foreach (var chunk in call.ResponseStream.ReadAllAsync(cancellationToken))
            {
                if (chunk.PayloadCase == AskAboutDataResponse.PayloadOneofCase.Token)
                {
                    await RawSocket.SendAsync(
                        socket, new TokenFrame(chunk.Token), cancellationToken);
                }
                else if (chunk.PayloadCase == AskAboutDataResponse.PayloadOneofCase.Stats)
                {
                    await RawSocket.SendAsync(
                        socket,
                        new DoneFrame(
                            true,
                            chunk.Stats.TimeToFirstTokenMs,
                            chunk.Stats.TotalMs,
                            chunk.Stats.TokenCount),
                        cancellationToken);
                }
            }

            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", cancellationToken);
        }
        catch (OperationCanceledException)
        {

            logger.LogWarning("raw websocket closed early: {Reason}", "client disconnected");
        }
        catch (RpcException exception)
        {
            logger.LogWarning("raw websocket closed early: {Reason}", exception.Status.Detail);
            if (socket.State == WebSocketState.Open)
            {
                await RawSocket.SendAsync(socket, new ErrorFrame(exception.Status.Detail), CancellationToken.None);
                await socket.CloseAsync(WebSocketCloseStatus.InternalServerError, "upstream failed", CancellationToken.None);
            }
        }
    }

    private static async Task<AskMessage?> ReceiveAskAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4 * 1024];
        var received = await socket.ReceiveAsync(buffer, cancellationToken);
        if (received.MessageType != WebSocketMessageType.Text || received.Count == 0)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AskMessage>(buffer.AsSpan(0, received.Count), RawSocket.Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
