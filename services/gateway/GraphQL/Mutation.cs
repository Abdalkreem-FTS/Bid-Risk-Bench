namespace BidRisk.Gateway.GraphQL;

public sealed class Mutation
{
    public async Task<PlaceBidResult> PlaceBid(
        long lotId,
        long bidderId,
        double amount,
        AuctionService.AuctionServiceClient auction,
        GrpcCallCounter counter,
        CancellationToken cancellationToken)
    {
        counter.Record("PlaceBid");

        var response = await auction.PlaceBidAsync(
            new PlaceBidRequest { LotId = lotId, BidderId = bidderId, Amount = amount },
            cancellationToken: cancellationToken);

        return response.ToGraphQL();
    }
}
