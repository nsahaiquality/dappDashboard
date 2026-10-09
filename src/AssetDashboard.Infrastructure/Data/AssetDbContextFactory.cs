using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AssetDashboard.Infrastructure.Data;

/// <summary>Used by <c>dotnet ef</c> at design time (creating migrations) without starting the API.</summary>
public class AssetDbContextFactory : IDesignTimeDbContextFactory<AssetDbContext>
{
    public AssetDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AssetDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=assetdashboard;Username=assetdash;Password=assetdash")
            .Options;
        return new AssetDbContext(options);
    }
}
