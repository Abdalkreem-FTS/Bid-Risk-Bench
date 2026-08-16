using BidRisk.Gateway.Configuration;

// Imported explicitly rather than relying on HotChocolate's implicit global usings.
using HotChocolate;
using HotChocolate.Types;

using Error = BidRisk.Contracts.Results.Error;

namespace BidRisk.Gateway.GraphQL;

public sealed class Query
{
    public async Task<Lot?> GetLot(
        long id,
        AuctionService.AuctionServiceClient auction,
        GrpcCallCounter counter,
        CancellationToken cancellationToken)
    {
        var lot = await FetchLotAsync(id, auction, counter, cancellationToken);

        // An unknown lot is null, not an error. Asking about something that does not exist is
        // a normal thing for a client to do.
        return lot.Match<Lot?>(
            found => found.ToGraphQL(),
            _ => null);
    }

    private static async Task<Contracts.Results.Result<BidRisk.Lot>> FetchLotAsync(
        long id,
        AuctionService.AuctionServiceClient auction,
        GrpcCallCounter counter,
        CancellationToken cancellationToken)
    {
        counter.Record("GetLot");
        try
        {
            var response = await auction.GetLotAsync(new GetLotRequest { LotId = id }, cancellationToken: cancellationToken);
            return response.Lot;
        }
        catch (Grpc.Core.RpcException exception)when (exception.StatusCode == Grpc.Core.StatusCode.NotFound)
        {
            return Error.NotFound("Lot.NotFound", exception.Status.Detail);
        }
    }

    public async Task<IReadOnlyList<Lot>> GetLots(
        AuctionService.AuctionServiceClient auction,
        GatewayOptions options,
        GrpcCallCounter counter,
        CancellationToken cancellationToken,
        int first = 0)
    {
        counter.Record("ListLots");

        var response = await auction.ListLotsAsync(
            new ListLotsRequest { First = first > 0 ? first : options.DefaultPageSize },
            cancellationToken: cancellationToken);

        return response.Lots.Select(lot => lot.ToGraphQL()).ToArray();
    }

    public async Task<IReadOnlyList<Bid>> GetBids(
        long lotId,
        AuctionService.AuctionServiceClient auction,
        GrpcCallCounter counter,
        CancellationToken cancellationToken,
        int limit = 20)
    {
        counter.Record("GetBidHistory");
        var response = await auction.GetBidHistoryAsync(
            new GetBidHistoryRequest { LotId = lotId, Limit = limit },
            cancellationToken: cancellationToken);

        return response.Bids
            .Select(bid => new Bid(
                bid.Id,
                bid.LotId,
                bid.BidderId,
                bid.Amount,
                bid.PlacedAt.ToDateTimeOffset(),
                bid.Accepted,
                bid.RiskScore,
                bid.RiskLevel.ToGraphQL(),
                bid.RiskReasons.ToArray()))
            .ToArray();
    }
}

public sealed record Bid(
    long Id,
    long LotId,
    long BidderId,
    double Amount,
    DateTimeOffset PlacedAt,
    bool Accepted,
    double RiskScore,
    RiskLevel RiskLevel,
    IReadOnlyList<string> RiskReasons);

// riskLevel is not on the Lot that auction-service returns. It comes from ml-service, and
// GraphQL resolves it once per lot. That is where the N+1 comes from.
[ExtendObjectType<Lot>]
public sealed class LotResolvers
{
    public Task<RiskLevel> GetRiskLevel(
        [Parent] Lot lot,
        ILotRiskResolver resolver,
        CancellationToken cancellationToken) => resolver.ResolveAsync(lot.Id, cancellationToken);
}
