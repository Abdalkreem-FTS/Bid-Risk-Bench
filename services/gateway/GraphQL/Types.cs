namespace BidRisk.Gateway.GraphQL;

public enum RiskLevel
{
    Unknown,
    Low,
    Medium,
    High,
}

public enum RejectReason
{
    Unknown,
    BelowReserve,
    BelowCurrentPrice,
    LotClosed,
    HighRisk,
    ScoringUnavailable,
}

public sealed record Lot(
    long Id,
    string Title,
    double ReservePrice,
    double CurrentPrice,
    int BidCount,
    DateTimeOffset ClosesAt,
    bool Closed);

public sealed record PlaceBidResult(
    bool Accepted,
    RejectReason? RejectReason,
    string? Message,
    double? RiskScore,
    RiskLevel? RiskLevel,
    IReadOnlyList<string> Reasons,
    long? BidId);

public sealed record AnswerChunk(
    string? Token,
    bool Done,
    double? TimeToFirstTokenMs = null,
    double? TotalMs = null,
    int? TokenCount = null);

internal static class Convert
{
    public static Lot ToGraphQL(this BidRisk.Lot lot) =>
        new(
            lot.Id,
            lot.Title,
            lot.ReservePrice,
            lot.CurrentPrice,
            lot.BidCount,
            lot.ClosesAt.ToDateTimeOffset(),
            lot.Closed);

    public static RiskLevel ToGraphQL(this BidRisk.RiskLevel level) => level switch
    {
        BidRisk.RiskLevel.Low => RiskLevel.Low,
        BidRisk.RiskLevel.Medium => RiskLevel.Medium,
        BidRisk.RiskLevel.High => RiskLevel.High,
        _ => RiskLevel.Unknown,
    };

    public static RejectReason ToGraphQL(this BidRisk.RejectReason reason) => reason switch
    {
        BidRisk.RejectReason.BelowReserve => RejectReason.BelowReserve,
        BidRisk.RejectReason.BelowCurrentPrice => RejectReason.BelowCurrentPrice,
        BidRisk.RejectReason.LotClosed => RejectReason.LotClosed,
        BidRisk.RejectReason.HighRisk => RejectReason.HighRisk,
        BidRisk.RejectReason.ScoringUnavailable => RejectReason.ScoringUnavailable,
        _ => RejectReason.Unknown,
    };

    public static PlaceBidResult ToGraphQL(this PlaceBidResponse response) =>
        response.OutcomeCase switch
        {
            PlaceBidResponse.OutcomeOneofCase.Accepted => new PlaceBidResult(
                Accepted: true,
                RejectReason: null,
                Message: null,
                RiskScore: response.Accepted.Risk?.Score,
                RiskLevel: response.Accepted.Risk?.Level.ToGraphQL(),
                Reasons: response.Accepted.Risk?.Reasons.ToArray() ?? [],
                BidId: response.Accepted.Bid?.Id),
            PlaceBidResponse.OutcomeOneofCase.Rejected => new PlaceBidResult(
                Accepted: false,
                RejectReason: response.Rejected.Reason.ToGraphQL(),
                Message: response.Rejected.Message,
                RiskScore: response.Rejected.Risk?.Score,
                RiskLevel: response.Rejected.Risk?.Level.ToGraphQL(),
                Reasons: response.Rejected.Risk?.Reasons.ToArray() ?? [],
                BidId: null),
            _ => new PlaceBidResult(false, RejectReason.Unknown, "no outcome", null, null, [], null),
        };
}
