namespace AssetDashboard.Domain;

/// <summary>Cost and pricing assumptions for selling repossessed equipment.</summary>
public static class RemarketingRules
{
    /// <summary>Storage cost per day in the yard, by class (bulky machinery costs more to store).</summary>
    public static decimal DailyStorageRate(AssetClass assetClass) => assetClass switch
    {
        AssetClass.Construction => 45m,
        AssetClass.Agriculture => 35m,
        AssetClass.Transportation => 30m,
        AssetClass.MaterialHandling => 15m,
        AssetClass.CleanTech => 20m,
        AssetClass.Healthcare => 25m,
        AssetClass.Technology => 8m,
        _ => 20m,
    };

    /// <summary>Seller-side fee as a share of the sale price, by channel.</summary>
    public static decimal SellingFeeRate(RemarketingChannel channel) => channel switch
    {
        RemarketingChannel.LiveAuction => 0.08m,
        RemarketingChannel.OnlineAuction => 0.05m,
        RemarketingChannel.PrivateSale => 0.02m,
        RemarketingChannel.VendorBuyBack => 0m,
        _ => 0.05m,
    };

    public static bool IsAuction(RemarketingChannel channel) =>
        channel is RemarketingChannel.LiveAuction or RemarketingChannel.OnlineAuction;

    /// <summary>Sets the cost breakdown for a sale on <paramref name="soldOn"/> and their total.</summary>
    public static void ApplySaleCosts(RemarketingCase c, decimal salePrice, DateOnly soldOn)
    {
        var days = Math.Max(0, soldOn.DayNumber - c.RepossessedOn.DayNumber);
        c.StorageCost = Math.Round(days * c.DailyStorageRate, 2);
        c.SellingFees = Math.Round(salePrice * SellingFeeRate(c.Channel), 2);
        c.RecoveryCosts = c.TransportCost + c.StorageCost + c.RefurbishmentCost + c.SellingFees;
    }

    /// <summary>Hammer price relative to the forced sale value: more bidders, higher price.</summary>
    public static decimal HammerFactor(int bids, double noise) => (decimal)Math.Clamp(0.75 + 0.03 * Math.Min(bids, 15) + noise, 0.5, 1.45);
}
