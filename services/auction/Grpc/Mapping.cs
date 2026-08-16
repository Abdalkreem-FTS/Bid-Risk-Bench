using Google.Protobuf.WellKnownTypes;

namespace BidRisk.Auction.Grpc;

internal static class Mapping
{
    public static Lot ToProto(Entities.Lot lot, DateTimeOffset now) =>
        new()
        {
            Id = lot.Id,
            Title = lot.Title,
            ReservePrice = (double)lot.ReservePrice,
            CurrentPrice = (double)lot.CurrentPrice,
            BidCount = lot.BidCount,
            ClosesAt = Timestamp.FromDateTimeOffset(lot.ClosesAt),
            Closed = lot.Closed || now >= lot.ClosesAt,
        };

    public static Bid ToProto(Entities.Bid bid)
    {
        var message = new Bid
        {
            Id = bid.Id,
            LotId = bid.LotId,
            BidderId = bid.BidderId,
            Amount = (double)bid.Amount,
            PlacedAt = Timestamp.FromDateTimeOffset(bid.PlacedAt),
            RiskScore = bid.RiskScore ?? 0.0,
            RiskLevel = ParseRiskLevel(bid.RiskLevel),
            Accepted = bid.Accepted,
            RejectReason = ParseRejectReason(bid.RejectReason),
        };

        if (bid.RiskReasons is { Length: > 0 } reasons)
        {
            message.RiskReasons.AddRange(reasons);
        }

        return message;
    }

    private static RiskLevel ParseRiskLevel(string? stored) =>
        System.Enum.TryParse<RiskLevel>(stored, ignoreCase: true, out var level)
            ? level
            : RiskLevel.Unspecified;

    private static RejectReason ParseRejectReason(string? stored) =>
        System.Enum.TryParse<RejectReason>(stored, ignoreCase: true, out var reason)
            ? reason
            : RejectReason.Unspecified;
}
