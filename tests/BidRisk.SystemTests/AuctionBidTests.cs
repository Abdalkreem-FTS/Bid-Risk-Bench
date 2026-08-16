using Grpc.Core;
using Grpc.Net.Client;

namespace BidRisk.SystemTests;

[Trait("Category", "Integration")]
public sealed class AuctionBidTests : IDisposable
{
    private const long LotOrdinary = 1;
    private const long LotClosed = 40;
    private const long LotReserved = 39;
    private const long Bidder = 3;
    private const long YoungBidder = 12;

    private static readonly RejectReason[] BelowSomething = [RejectReason.BelowReserve, RejectReason.BelowCurrentPrice];

    private readonly GrpcChannel _channel = Endpoints.Auction();
    private readonly AuctionService.AuctionServiceClient _auction;

    public AuctionBidTests() => _auction = new AuctionService.AuctionServiceClient(_channel);

    public void Dispose() => _channel.Dispose();

    private Lot GetLot(long lotId) =>
        _auction.GetLot(new GetLotRequest { LotId = lotId }, deadline: Deadline()).Lot;

    private PlaceBidResponse Place(long lotId, double amount, long bidder = Bidder) =>
        _auction.PlaceBid(
            new PlaceBidRequest { LotId = lotId, BidderId = bidder, Amount = amount },
            deadline: Deadline());

    private static DateTime Deadline() => DateTime.UtcNow.Add(Endpoints.Deadline);

    [Fact]
    public void ListLots_FixedDatasetLoaded_ReturnsAtLeastFortyLots()
    {
        var lots = _auction.ListLots(new ListLotsRequest { First = 50 }, deadline: Deadline()).Lots;
        Assert.True(lots.Count >= 40, "the fixed dataset is not loaded — run `make seed`");
    }

    [Fact]
    public void PlaceBid_ValidBidAboveCurrentPrice_IsAcceptedScoredAndStored()
    {
        var before = GetLot(LotOrdinary);
        var amount = Math.Round(before.CurrentPrice * 1.05, 2);

        var response = Place(LotOrdinary, amount);

        Assert.Equal(PlaceBidResponse.OutcomeOneofCase.Accepted, response.OutcomeCase);
        var accepted = response.Accepted;

        Assert.InRange(accepted.Risk.Score, 0.0, 1.0);
        Assert.True(accepted.Risk.InferenceMs > 0.0, "inference time was not measured");
        Assert.NotEmpty(accepted.Risk.ModelVersion);
        Assert.NotEmpty(accepted.Risk.Reasons);

        var after = GetLot(LotOrdinary);
        Assert.Equal(amount, after.CurrentPrice, 2);
        Assert.Equal(before.BidCount + 1, after.BidCount);

        var history = _auction.GetBidHistory(
            new GetBidHistoryRequest { LotId = LotOrdinary, Limit = 1 }, deadline: Deadline()).Bids;

        Assert.Equal(accepted.Bid.Id, history[0].Id);
        Assert.True(history[0].Accepted);
        Assert.Equal(accepted.Risk.Score, history[0].RiskScore, 6);
    }

    [Fact]
    public void PlaceBid_AmountBelowCurrentPrice_RejectsAndLeavesPriceUnchanged()
    {
        var lot = GetLot(LotOrdinary);

        var response = Place(LotOrdinary, Math.Round(lot.CurrentPrice * 0.9, 2));

        Assert.Equal(PlaceBidResponse.OutcomeOneofCase.Rejected, response.OutcomeCase);
        Assert.Equal(RejectReason.BelowCurrentPrice, response.Rejected.Reason);
        Assert.Equal(lot.CurrentPrice, GetLot(LotOrdinary).CurrentPrice, 2);
    }

    [Fact]
    public void PlaceBid_AmountEqualsCurrentPrice_RejectsAsBelowCurrentPrice()
    {
        var lot = GetLot(LotOrdinary);
        var response = Place(LotOrdinary, lot.CurrentPrice);
        Assert.Equal(RejectReason.BelowCurrentPrice, response.Rejected.Reason);
    }

    [Fact]
    public void PlaceBid_LotIsClosed_RejectsAsLotClosed()
    {
        var lot = GetLot(LotClosed);
        Assert.True(lot.Closed, "seed lot 40 is supposed to be closed");

        var response = Place(LotClosed, lot.CurrentPrice * 2);

        Assert.Equal(PlaceBidResponse.OutcomeOneofCase.Rejected, response.OutcomeCase);
        Assert.Equal(RejectReason.LotClosed, response.Rejected.Reason);
    }

    [Fact]
    public void PlaceBid_WildlyInflatedBidFromYoungAccount_RejectsAsHighRiskWithReasons()
    {
        var lot = GetLot(LotOrdinary);

        var response = Place(LotOrdinary, Math.Round(lot.CurrentPrice * 8, 2), bidder: YoungBidder);

        Assert.Equal(PlaceBidResponse.OutcomeOneofCase.Rejected, response.OutcomeCase);
        Assert.Equal(RejectReason.HighRisk, response.Rejected.Reason);
        Assert.True(response.Rejected.Risk.Score >= 0.70);
        Assert.NotEmpty(response.Rejected.Risk.Reasons);
        Assert.Equal(lot.CurrentPrice, GetLot(LotOrdinary).CurrentPrice, 2);
    }

    [Fact]
    public void PlaceBid_AmountBelowReserve_RejectsAndLeavesPriceUnchanged()
    {
        var lot = GetLot(LotReserved);
        var response = Place(LotReserved, 0.01);

        Assert.Equal(PlaceBidResponse.OutcomeOneofCase.Rejected, response.OutcomeCase);
        Assert.Contains(response.Rejected.Reason, BelowSomething);
        Assert.Equal(lot.CurrentPrice, GetLot(LotReserved).CurrentPrice, 2);
    }

    [Fact]
    public void PlaceBid_LotDoesNotExist_ThrowsNotFoundRatherThanRejecting()
    {
        var error = Assert.Throws<RpcException>(() => Place(999_999, 100.0));
        Assert.Equal(StatusCode.NotFound, error.StatusCode);
    }

    [Fact]
    public void PlaceBid_NegativeAmount_ThrowsInvalidArgument()
    {
        var error = Assert.Throws<RpcException>(() => Place(LotOrdinary, -5.0));
        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
    }
}
