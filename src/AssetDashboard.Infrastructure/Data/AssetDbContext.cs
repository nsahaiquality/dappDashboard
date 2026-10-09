using AssetDashboard.Domain;
using Microsoft.EntityFrameworkCore;

namespace AssetDashboard.Infrastructure.Data;

public class AssetDbContext(DbContextOptions<AssetDbContext> options) : DbContext(options)
{
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<AssetValuation> AssetValuations => Set<AssetValuation>();
    public DbSet<RemarketingCase> RemarketingCases => Set<RemarketingCase>();
    public DbSet<PortfolioHistoryPoint> PortfolioHistory => Set<PortfolioHistoryPoint>();
    public DbSet<RefinancingRequest> RefinancingRequests => Set<RefinancingRequest>();
    public DbSet<Auction> Auctions => Set<Auction>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Money columns: numeric(18,2). Enums stored as readable strings.
        builder.Properties<decimal>().HavePrecision(18, 2);
        builder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Vendor>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Country).HasMaxLength(2);
            e.Property(x => x.Rating).HasMaxLength(1);
        });

        b.Entity<Customer>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Country).HasMaxLength(2);
            e.Property(x => x.City).HasMaxLength(100);
        });

        b.Entity<Contract>(e =>
        {
            e.Property(x => x.ContractNumber).HasMaxLength(32);
            e.HasIndex(x => x.ContractNumber).IsUnique();
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.InterestRate).HasPrecision(6, 3);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.AssetClass);
            e.HasIndex(x => x.DaysPastDue);
            e.HasIndex(x => x.MaturityDate);
            e.HasOne(x => x.Vendor).WithMany(x => x.Contracts).HasForeignKey(x => x.VendorId);
            e.HasOne(x => x.Customer).WithMany(x => x.Contracts).HasForeignKey(x => x.CustomerId);
        });

        b.Entity<Asset>(e =>
        {
            e.Property(x => x.SerialNumber).HasMaxLength(40);
            e.HasIndex(x => x.SerialNumber).IsUnique();
            e.Property(x => x.Category).HasMaxLength(80);
            e.Property(x => x.Manufacturer).HasMaxLength(80);
            e.Property(x => x.Model).HasMaxLength(80);
            e.Property(x => x.Country).HasMaxLength(2);
            e.Property(x => x.City).HasMaxLength(100);
            e.HasIndex(x => new { x.Status, x.AssetClass });
            e.HasOne(x => x.Contract).WithMany(x => x.Assets).HasForeignKey(x => x.ContractId);
        });

        b.Entity<AssetValuation>(e =>
        {
            e.HasIndex(x => new { x.AssetId, x.ValuedAt });
            e.HasOne(x => x.Asset).WithMany(x => x.Valuations).HasForeignKey(x => x.AssetId);
        });

        b.Entity<RemarketingCase>(e =>
        {
            e.HasIndex(x => x.AssetId).IsUnique();
            e.HasIndex(x => x.Status);
            e.HasOne(x => x.Asset).WithOne(x => x.RemarketingCase).HasForeignKey<RemarketingCase>(x => x.AssetId);
            e.HasOne(x => x.Auction).WithMany(x => x.Lots).HasForeignKey(x => x.AuctionId);
            e.Property(x => x.Yard).HasMaxLength(100);
        });

        b.Entity<Auction>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.Location).HasMaxLength(100);
            e.HasIndex(x => new { x.Status, x.Date });
        });

        b.Entity<RefinancingRequest>(e =>
        {
            e.Property(x => x.CurrentRate).HasPrecision(6, 3);
            e.Property(x => x.ProposedRate).HasPrecision(6, 3);
            e.Property(x => x.LtvAtRequest).HasPrecision(9, 4);
            e.Property(x => x.ForcedSaleCoverAtRequest).HasPrecision(9, 4);
            e.Property(x => x.DecisionNote).HasMaxLength(300);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.RequestedAt);
            e.HasOne(x => x.Contract).WithMany(x => x.RefinancingRequests).HasForeignKey(x => x.ContractId);
        });

        b.Entity<PortfolioHistoryPoint>(e =>
        {
            // One row per day for the total (null class) and per class; NULLS NOT DISTINCT makes the total unique too.
            e.Property(x => x.AssetClass).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(x => new { x.Date, x.AssetClass }).IsUnique().AreNullsDistinct(false);
        });
    }
}
