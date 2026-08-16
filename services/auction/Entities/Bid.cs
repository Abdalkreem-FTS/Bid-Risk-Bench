namespace BidRisk.Auction.Entities;

public sealed class Bid
{
    public long Id { get; set; }
    public long LotId { get; set; }
    public long BidderId { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset PlacedAt { get; set; }
    public bool Accepted { get; set; }
    public string? RejectReason { get; set; }
    public double? RiskScore { get; set; }
    public string? RiskLevel { get; set; }
    public string[]? RiskReasons { get; set; }
    public string? ModelVersion { get; set; }
}
