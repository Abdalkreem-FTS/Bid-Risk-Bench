using System.Runtime.CompilerServices;
using Grpc.Core;

// Imported explicitly rather than relying on HotChocolate's implicit global usings.
using HotChocolate;
using HotChocolate.Types;

namespace BidRisk.Gateway.GraphQL;

public sealed class Subscription
{
    [Subscribe(With = nameof(StreamAnswer))]
    public AnswerChunk AskAboutData(string question, [EventMessage] AnswerChunk chunk, string? broadcastId = null) => chunk;

    public async IAsyncEnumerable<AnswerChunk> StreamAnswer(
        string question,
        RiskService.RiskServiceClient risk,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        string? broadcastId = null)
    {
        using var call = risk.AskAboutData(
            new AskAboutDataRequest
            {
                Question = question,
                BroadcastId = broadcastId ?? string.Empty,
            },
            cancellationToken: cancellationToken);

        await foreach (var chunk in call.ResponseStream.ReadAllAsync(cancellationToken))
        {
            if (chunk.PayloadCase == AskAboutDataResponse.PayloadOneofCase.Token)
            {
                yield return new AnswerChunk(chunk.Token, Done: false);
            }
            else if (chunk.PayloadCase == AskAboutDataResponse.PayloadOneofCase.Stats)
            {
                yield return new AnswerChunk(
                    Token: null,
                    Done: true,
                    TimeToFirstTokenMs: chunk.Stats.TimeToFirstTokenMs,
                    TotalMs: chunk.Stats.TotalMs,
                    TokenCount: chunk.Stats.TokenCount);
            }
        }
    }
}
