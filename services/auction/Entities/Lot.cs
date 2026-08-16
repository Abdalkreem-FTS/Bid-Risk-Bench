namespace BidRisk.Auction.Entities;

public sealed class Lot
{
    public long Id { get; set; }
    public required string Title { get; set; }
    public decimal ReservePrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public int BidCount { get; set; }
    public DateTimeOffset ClosesAt { get; set; }
    public bool Closed { get; set; }
}
