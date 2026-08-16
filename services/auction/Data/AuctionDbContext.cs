using BidRisk.Auction.Entities;
using Microsoft.EntityFrameworkCore;

namespace BidRisk.Auction.Data;

public sealed class AuctionDbContext(DbContextOptions<AuctionDbContext> options) : DbContext(options)
{
    public DbSet<Entities.Lot> Lots => Set<Entities.Lot>();
    public DbSet<Entities.Bid> Bids => Set<Entities.Bid>();
    public DbSet<Bidder> Bidders => Set<Bidder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Bidder>(entity =>
        {
            entity.ToTable("bidders");
            entity.HasKey(b => b.Id);
            entity.Property(b => b.Id).HasColumnName("id");
            entity.Property(b => b.Username).HasColumnName("username");
            entity.Property(b => b.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<Entities.Lot>(entity =>
        {
            entity.ToTable("lots");
            entity.HasKey(l => l.Id);
            entity.Property(l => l.Id).HasColumnName("id");
            entity.Property(l => l.Title).HasColumnName("title");
            entity.Property(l => l.ReservePrice).HasColumnName("reserve_price");
            entity.Property(l => l.CurrentPrice).HasColumnName("current_price");
            entity.Property(l => l.BidCount).HasColumnName("bid_count");
            entity.Property(l => l.ClosesAt).HasColumnName("closes_at");
            entity.Property(l => l.Closed).HasColumnName("closed");
        });

        modelBuilder.Entity<Entities.Bid>(entity =>
        {
            entity.ToTable("bids");
            entity.HasKey(b => b.Id);
            entity.Property(b => b.Id).HasColumnName("id");
            entity.Property(b => b.LotId).HasColumnName("lot_id");
            entity.Property(b => b.BidderId).HasColumnName("bidder_id");
            entity.Property(b => b.Amount).HasColumnName("amount");
            entity.Property(b => b.PlacedAt).HasColumnName("placed_at");
            entity.Property(b => b.Accepted).HasColumnName("accepted");
            entity.Property(b => b.RejectReason).HasColumnName("reject_reason");
            entity.Property(b => b.RiskScore).HasColumnName("risk_score");
            entity.Property(b => b.RiskLevel).HasColumnName("risk_level");
            entity.Property(b => b.RiskReasons).HasColumnName("risk_reasons");
            entity.Property(b => b.ModelVersion).HasColumnName("model_version");
        });
    }
}
