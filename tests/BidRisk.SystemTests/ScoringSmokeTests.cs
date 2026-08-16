using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;

namespace BidRisk.SystemTests;

[Trait("Category", "Smoke")]
public sealed class ScoringSmokeTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private readonly GrpcChannel _channel = Endpoints.Ml();
    private readonly RiskService.RiskServiceClient _risk;

    public ScoringSmokeTests() => _risk = new RiskService.RiskServiceClient(_channel);

    public void Dispose() => _channel.Dispose();

    private static BidContext Context(
        double amount,
        double gapSeconds,
        int bidsOnLot,
        int totalBids,
        double accountAgeDays) => new()
        {
            Amount = amount,
            CurrentPrice = 100.0,
            BidTime = Timestamp.FromDateTimeOffset(Now),
            PreviousBidTime = Timestamp.FromDateTimeOffset(Now.AddSeconds(-gapSeconds)),
            BidderBidsOnLot = bidsOnLot,
            LotTotalBids = totalBids,
            BidderAccountCreated = Timestamp.FromDateTimeOffset(Now.AddDays(-accountAgeDays)),
        };

    private static BidContext BlatantShill() =>
        Context(amount: 320.0, gapSeconds: 3, bidsOnLot: 11, totalBids: 13, accountAgeDays: 1);

    private static BidContext OrdinaryBid() =>
        Context(amount: 105.0, gapSeconds: 600, bidsOnLot: 1, totalBids: 8, accountAgeDays: 800);

    private RiskAssessment Score(BidContext context) =>
        _risk.PredictBidRisk(
            new PredictBidRiskRequest { Context = context },
            deadline: DateTime.UtcNow.Add(Endpoints.Deadline)).Assessment;

    [Fact]
    public void PredictBidRisk_BlatantShillBid_ReturnsHighRiskWithReasonsAndTiming()
    {
        var shill = Score(BlatantShill());

        Assert.Equal(RiskLevel.High, shill.Level);
        Assert.True(shill.InferenceMs > 0.0, "inference time was not measured");
        Assert.NotEmpty(shill.Reasons);
        Assert.NotEmpty(shill.ModelVersion);
    }

    [Fact]
    public void PredictBidRisk_OrdinaryBidComparedWithShill_ScoresLower()
    {
        var shill = Score(BlatantShill());
        var ordinary = Score(OrdinaryBid());

        Assert.True(
            ordinary.Score < shill.Score,
            $"ordinary {ordinary.Score:F4} did not score below shill {shill.Score:F4}");
    }

    [Fact]
    public void PredictBatch_OneHundredContexts_ReturnsOneAssessmentEachWithTiming()
    {
        var request = new PredictBatchRequest();
        for (var index = 0; index < 50; index++)
        {
            request.Contexts.Add(OrdinaryBid());
            request.Contexts.Add(BlatantShill());
        }

        var batch = _risk.PredictBatch(request, deadline: DateTime.UtcNow.Add(Endpoints.Deadline));

        Assert.Equal(100, batch.Assessments.Count);
        Assert.True(batch.TotalInferenceMs > 0.0, "batch inference time was not measured");
    }
}
