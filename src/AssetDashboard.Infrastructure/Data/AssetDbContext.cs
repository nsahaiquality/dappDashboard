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
        });
    }
}
