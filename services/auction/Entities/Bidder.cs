namespace BidRisk.Auction.Entities;

public sealed class Bidder
{
    public long Id { get; set; }
    public required string Username { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
