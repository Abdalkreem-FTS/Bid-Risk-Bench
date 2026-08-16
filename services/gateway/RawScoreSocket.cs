using System.Net.WebSockets;
using System.Text.Json;
using BidRisk.Gateway.GraphQL;

namespace BidRisk.Gateway;

internal static class RawScoreSocket
{
    private sealed record ScoreRequest(BidContextInput? Context, List<BidContextInput>? Contexts);

    private sealed record ScoreReply(double Score, string Level, double InferenceMs);

    private sealed record BatchReply(IReadOnlyList<ScoreReply> Scores, double TotalInferenceMs);

    private sealed record ErrorReply(string Error);

    public static async Task HandleAsync(
        HttpContext context,
        RiskService.RiskServiceClient risk,
        GrpcCallCounter counter)
    {
        using var socket = await RawSocket.AcceptAsync(context);
        if (socket is null)
        {
            return;
        }

        var cancellationToken = context.RequestAborted;
        var buffer = new byte[256 * 1024];

        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var received = await socket.ReceiveAsync(buffer, cancellationToken);
                if (received.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                var request = Parse(buffer.AsSpan(0, received.Count));
                if (request is null)
                {
                    await RawSocket.SendAsync(socket, new ErrorReply("expected {\"context\": …} or {\"contexts\": […]}"), cancellationToken);
                    continue;
                }

                await RespondAsync(socket, risk, counter, request, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {

        }
        catch (WebSocketException)
        {

        }
    }

    private static async Task RespondAsync(
        WebSocket socket,
        RiskService.RiskServiceClient risk,
        GrpcCallCounter counter,
        ScoreRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Contexts is { Count: > 0 } batch)
        {
            counter.Record("PredictBatch");

            var batchRequest = new PredictBatchRequest();
            batchRequest.Contexts.AddRange(batch.Select(item => item.ToProto()));

            var batchResponse = await risk.PredictBatchAsync(batchRequest, cancellationToken: cancellationToken);

            await RawSocket.SendAsync(
                socket,
                new BatchReply(
                    batchResponse.Assessments.Select(Reply).ToArray(),
                    batchResponse.TotalInferenceMs),
                cancellationToken);

            return;
        }

        if (request.Context is null)
        {
            await RawSocket.SendAsync(socket, new ErrorReply("no bid context given"), cancellationToken);
            return;
        }

        counter.Record("PredictBidRisk");

        var response = await risk.PredictBidRiskAsync(
            new PredictBidRiskRequest { Context = request.Context.ToProto() },
            cancellationToken: cancellationToken);

        await RawSocket.SendAsync(socket, Reply(response.Assessment), cancellationToken);
    }

    private static ScoreReply Reply(RiskAssessment assessment) =>
        new(assessment.Score, assessment.Level.ToString(), assessment.InferenceMs);

    private static ScoreRequest? Parse(ReadOnlySpan<byte> payload)
    {
        try
        {
            return JsonSerializer.Deserialize<ScoreRequest>(payload, RawSocket.Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
