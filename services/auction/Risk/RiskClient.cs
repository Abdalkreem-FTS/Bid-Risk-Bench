using BidRisk.Contracts.Results;
using Grpc.Core;

namespace BidRisk.Auction.Risk;

public interface IRiskClient
{
    Task<Result<RiskAssessment>> ScoreAsync(BidContext context, CancellationToken cancellationToken);
}

public sealed class RiskClient(
    RiskService.RiskServiceClient client,
    TimeSpan deadline,
    ILogger<RiskClient> logger) : IRiskClient
{
    public async Task<Result<RiskAssessment>> ScoreAsync(
        BidContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.PredictBidRiskAsync(
                new PredictBidRiskRequest { Context = context },
                deadline: DateTime.UtcNow.Add(deadline),
                cancellationToken: cancellationToken);

            return response.Assessment;
        }
        // If scoring is down we refuse the bid instead of accepting it unscored.
        catch (RpcException exception) when (
            exception.StatusCode is StatusCode.DeadlineExceeded or StatusCode.Unavailable)
        {
            logger.LogWarning(
                "scoring unavailable ({Status}) — refusing the bid rather than accepting it unscored",
                exception.StatusCode);
            return exception.StatusCode == StatusCode.DeadlineExceeded
                ? Error.Failure("Risk.DeadlineExceeded", $"scoring did not answer within {deadline.TotalMilliseconds:F0} ms")
                : Error.Failure("Risk.Unavailable", "the risk service is not reachable");
        }
    }
}
