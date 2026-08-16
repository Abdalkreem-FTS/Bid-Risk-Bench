using BidRisk.Contracts.Results;

namespace BidRisk.Auction.Domain;

public static class BidErrors
{
    public static Error LotClosed =>
        Error.Conflict("Bid.LotClosed", "this lot has closed");

    public static Error BelowReserve =>
        Error.Conflict("Bid.BelowReserve", "the bid is below the reserve price");

    public static Error BelowCurrentPrice =>
        Error.Conflict("Bid.BelowCurrentPrice", "the bid does not beat the current price");

    public static Error HighRisk =>
        Error.Conflict("Bid.HighRisk", "the bid was scored as high risk");

    public static Error ScoringUnavailable =>
        Error.Conflict(
            "Bid.ScoringUnavailable",
            "the risk service could not score this bid, and unscored bids are not accepted");

    public static Error InvalidAmount =>
        Error.Validation(
            "Bid.InvalidAmount",
            $"amount must be a positive number no greater than {BidRules.MaxAmount:N2}");

    public static Error LotNotFound(long lotId) =>
        Error.NotFound("Lot.NotFound", $"lot {lotId} does not exist");

    public static Error BidderNotFound(long bidderId) =>
        Error.NotFound("Bidder.NotFound", $"bidder {bidderId} does not exist");
}
