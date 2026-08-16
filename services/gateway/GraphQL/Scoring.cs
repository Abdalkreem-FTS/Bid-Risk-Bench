using Google.Protobuf.WellKnownTypes;

// Imported explicitly rather than relying on HotChocolate's implicit global usings.
using HotChocolate;
using HotChocolate.Types;

namespace BidRisk.Gateway.GraphQL;

public sealed record BidContextInput(
    double Amount,
    double CurrentPrice,
    double SecondsSincePreviousBid,
    int BidderBidsOnLot,
    int LotTotalBids,
    double BidderAccountAgeDays);

public sealed record RiskAssessmentResult(
    double Score,
    RiskLevel Level,
    IReadOnlyList<string> Reasons,
    double InferenceMs,
    string ModelVersion);

public sealed record BatchScoreResult(
    IReadOnlyList<RiskAssessmentResult> Assessments,
    double TotalInferenceMs);

[ExtendObjectType<Query>]
public sealed class ScoringQueries
{
    public async Task<RiskAssessmentResult> ScoreBid(
        BidContextInput input,
        RiskService.RiskServiceClient risk,
        GrpcCallCounter counter,
        CancellationToken cancellationToken)
    {
        counter.Record("PredictBidRisk");

        var response = await risk.PredictBidRiskAsync(
            new PredictBidRiskRequest { Context = input.ToProto() },
            cancellationToken: cancellationToken);

        return response.Assessment.ToResult();
    }

    public async Task<BatchScoreResult> ScoreBids(
        IReadOnlyList<BidContextInput> inputs,
        RiskService.RiskServiceClient risk,
        GrpcCallCounter counter,
        CancellationToken cancellationToken)
    {
        counter.Record("PredictBatch");

        var request = new PredictBatchRequest();
        request.Contexts.AddRange(inputs.Select(input => input.ToProto()));

        var response = await risk.PredictBatchAsync(request, cancellationToken: cancellationToken);

        return new BatchScoreResult(
            response.Assessments.Select(assessment => assessment.ToResult()).ToArray(),
            response.TotalInferenceMs);
    }
}

internal static class ScoringConvert
{
    public static BidContext ToProto(this BidContextInput input)
    {
        var now = DateTimeOffset.UtcNow;
        var context = new BidContext
        {
            Amount = input.Amount,
            CurrentPrice = input.CurrentPrice,
            BidTime = Timestamp.FromDateTimeOffset(now),
            BidderBidsOnLot = input.BidderBidsOnLot,
            LotTotalBids = input.LotTotalBids,
            BidderAccountCreated =
                Timestamp.FromDateTimeOffset(now.AddDays(-input.BidderAccountAgeDays)),
        };

        if (input.SecondsSincePreviousBid >= 0)
        {
            context.PreviousBidTime = Timestamp.FromDateTimeOffset(now.AddSeconds(-input.SecondsSincePreviousBid));
        }

        return context;
    }

    public static RiskAssessmentResult ToResult(this RiskAssessment assessment) =>
        new(
            assessment.Score,
            assessment.Level.ToGraphQL(),
            assessment.Reasons.ToArray(),
            assessment.InferenceMs,
            assessment.ModelVersion);
}
