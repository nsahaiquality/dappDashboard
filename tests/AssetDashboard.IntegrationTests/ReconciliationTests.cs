using AssetDashboard.Domain;
using AssetDashboard.Infrastructure.Dashboard;
using AssetDashboard.Infrastructure.History;
using AssetDashboard.Infrastructure.Refinancing;
using AssetDashboard.Infrastructure.Remarketing;
using AssetDashboard.Infrastructure.Sources;
using AssetDashboard.Infrastructure.Vendors;

namespace AssetDashboard.IntegrationTests;

/// <summary>
/// "What you see is what is in the database": every key figure the API returns is recomputed with
/// hand-written SQL, independent of the application's LINQ queries, and must match exactly.
/// Rolling time windows allow a difference of one row (the clock moves between the two queries).
/// </summary>
[Collection(ApiCollection.Name)]
public class ReconciliationTests(ApiFixture api)
{
    private readonly Sql sql = new(api.ConnectionString);

    /// <summary>One filter, as query string and as SQL predicates on contracts (c, cu) and assets (a, c).</summary>
    public sealed record FilterCase(string Name, string Query, string ContractWhere, string AssetWhere)
    {
        public override string ToString() => Name;
    }

    private static FilterCase Case(string name, string query, string? cls = null, string? country = null, int? vendor = null,
        string? product = null, bool underwater = false)
    {
        var contract = Sql.ContractFilter(cls, country, vendor, product, underwater);
        // Assets follow the asset's own class and country, and the contract's vendor, product and underwater state.
        var asset = Sql.ContractFilter(null, null, vendor, product, underwater)
                    + (cls is null ? "" : $" AND a.\"AssetClass\" = '{cls}'")
                    + (country is null ? "" : $" AND a.\"Country\" = '{country}'");
        return new FilterCase(name, query, contract, asset);
    }

    public static TheoryData<FilterCase> Filters => new()
    {
        Case("no filter", ""),
        Case("asset class", "assetClass=Healthcare", cls: "Healthcare"),
        Case("country", "country=NL", country: "NL"),
        Case("product", "productType=Loan", product: "Loan"),
        Case("underwater", "underwaterOnly=true", underwater: true),
        Case("combined", "assetClass=Construction&country=DE&productType=FinanceLease", "Construction", "DE", product: "FinanceLease"),
    };

    private const string OpenContracts = """
        FROM "Contracts" c JOIN "Customers" cu ON cu."Id" = c."CustomerId"
        WHERE c."Status" <> 'Closed' AND
        """;

    private const string ActiveAssets = """
        FROM "Assets" a JOIN "Contracts" c ON c."Id" = a."ContractId"
        WHERE a."Status" <> 'Sold' AND
        """;

    [Theory]
    [MemberData(nameof(Filters))]
    public async Task Snapshot_headline_figures_match_sql(FilterCase f)
    {
        var s = (await api.GetAsync<PortfolioSnapshot>($"/api/dashboard/snapshot?{f.Query}"))!;

        var contracts = (await sql.RowsAsync($"""
            SELECT COUNT(*) AS n, COALESCE(SUM(c."OutstandingPrincipal"), 0) AS exposure,
                   COALESCE(SUM(c."OutstandingPrincipal") FILTER (WHERE c."DaysPastDue" >= 30), 0) AS at_risk
            {OpenContracts} {f.ContractWhere}
            """)).Single();
        var assets = (await sql.RowsAsync($"""
            SELECT COUNT(*) AS n, COALESCE(SUM(a."MarketValue"), 0) AS market, COALESCE(SUM(a."ForcedSaleValue"), 0) AS forced
            {ActiveAssets} {f.AssetWhere}
            """)).Single();
        var shortfall = await sql.ScalarAsync<decimal>($"""
            SELECT COALESCE(SUM(GREATEST(c."OutstandingPrincipal" - COALESCE((SELECT SUM(a."ForcedSaleValue") FROM "Assets" a
                WHERE a."ContractId" = c."Id" AND a."Status" <> 'Sold'), 0), 0)), 0)
            {OpenContracts} {f.ContractWhere}
            """);

        Assert.Equal(Convert.ToInt32(contracts["n"]), s.OpenContracts);
        Assert.Equal((decimal)contracts["exposure"]!, s.TotalExposure);
        Assert.Equal((decimal)contracts["at_risk"]!, s.ExposureAtRisk);
        Assert.Equal(Convert.ToInt32(assets["n"]), s.ActiveAssets);
        Assert.Equal((decimal)assets["market"]!, s.TotalMarketValue);
        Assert.Equal((decimal)assets["forced"]!, s.TotalForcedSaleValue);
        Assert.Equal(shortfall, s.ForcedSaleShortfall);
        var market = (decimal)assets["market"]!;
        Assert.Equal(market == 0 ? 0 : Math.Round((decimal)contracts["exposure"]! / market, 4), s.LoanToValue);
    }

    [Theory]
    [MemberData(nameof(Filters))]
    public async Task Snapshot_breakdowns_match_sql(FilterCase f)
    {
        var s = (await api.GetAsync<PortfolioSnapshot>($"/api/dashboard/snapshot?{f.Query}"))!;

        var buckets = await sql.RowsAsync($"""
            SELECT CASE WHEN c."DaysPastDue" = 0 THEN 0 WHEN c."DaysPastDue" < 30 THEN 1 WHEN c."DaysPastDue" < 60 THEN 2
                        WHEN c."DaysPastDue" < 90 THEN 3 ELSE 4 END AS bucket,
                   COUNT(*) AS n, SUM(c."OutstandingPrincipal") AS exposure
            {OpenContracts} {f.ContractWhere} GROUP BY 1
            """);
        for (var i = 0; i < 5; i++)
        {
            var row = buckets.FirstOrDefault(b => Convert.ToInt32(b["bucket"]) == i);
            Assert.Equal(row is null ? 0 : Convert.ToInt32(row["n"]), s.Delinquency[i].Contracts);
            Assert.Equal(row is null ? 0 : (decimal)row["exposure"]!, s.Delinquency[i].Exposure);
        }

        var classExposure = await sql.RowsAsync($"""
            SELECT c."AssetClass" AS cls, SUM(c."OutstandingPrincipal") AS exposure {OpenContracts} {f.ContractWhere} GROUP BY 1
            """);
        var classMarket = await sql.RowsAsync($"""
            SELECT a."AssetClass" AS cls, COUNT(*) AS n, SUM(a."MarketValue") AS market {ActiveAssets} {f.AssetWhere} GROUP BY 1
            """);
        Assert.Equal(classMarket.Count, s.ByAssetClass.Count);
        foreach (var c in s.ByAssetClass)
        {
            var m = classMarket.Single(r => (string)r["cls"]! == c.AssetClass.ToString());
            var e = classExposure.SingleOrDefault(r => (string)r["cls"]! == c.AssetClass.ToString());
            Assert.Equal(Convert.ToInt32(m["n"]), c.Assets);
            Assert.Equal((decimal)m["market"]!, c.MarketValue);
            Assert.Equal(e is null ? 0 : (decimal)e["exposure"]!, c.Exposure);
        }

        var countries = await sql.RowsAsync($"""
            SELECT a."Country" AS country, SUM(a."MarketValue") AS market {ActiveAssets} {f.AssetWhere} GROUP BY 1
            """);
        Assert.Equal(countries.Count, s.ByCountry.Count);
        Assert.All(s.ByCountry, c => Assert.Equal((decimal)countries.Single(r => (string)r["country"]! == c.Country)["market"]!, c.MarketValue));

        var topVendor = (await sql.RowsAsync($"""
            SELECT c."VendorId" AS id, SUM(c."OutstandingPrincipal") AS exposure
            {OpenContracts} {f.ContractWhere} GROUP BY 1 ORDER BY 2 DESC LIMIT 1
            """)).SingleOrDefault();
        if (topVendor is not null)
            Assert.Equal((decimal)topVendor["exposure"]!, s.TopVendors[0].Exposure);
    }

    [Theory]
    [MemberData(nameof(Filters))]
    public async Task Asset_list_total_and_ltv_match_sql(FilterCase f)
    {
        var page = (await api.GetAsync<PagedResult<AssetListItem>>($"/api/assets?{f.Query}&sort=Ltv&pageSize=5"))!;

        var total = await sql.ScalarAsync<long>($"""
            SELECT COUNT(*) FROM "Assets" a JOIN "Contracts" c ON c."Id" = a."ContractId" WHERE {f.AssetWhere}
            """);
        Assert.Equal(total, page.Total);

        foreach (var item in page.Items.Where(i => i.ContractLtv is not null))
        {
            var ltv = await sql.ScalarAsync<decimal>("""
                SELECT c."OutstandingPrincipal" / SUM(x."MarketValue")
                FROM "Assets" a JOIN "Contracts" c ON c."Id" = a."ContractId" JOIN "Assets" x ON x."ContractId" = c."Id"
                WHERE a."Id" = @id AND x."Status" <> 'Sold' GROUP BY c."OutstandingPrincipal"
                """, ("id", item.Id));
            Assert.Equal(Math.Round(ltv, 6), Math.Round(item.ContractLtv!.Value, 6));
        }
    }

    [Fact]
    public async Task Todays_history_point_matches_the_live_portfolio()
    {
        var history = (await api.GetAsync<List<HistoryPoint>>("/api/history?days=2"))!;
        var exposure = await sql.ScalarAsync<decimal>($"""SELECT SUM(c."OutstandingPrincipal") {OpenContracts} TRUE""");
        var market = await sql.ScalarAsync<decimal>($"""SELECT SUM(a."MarketValue") {ActiveAssets} TRUE""");

        var today = history[^1];
        Assert.False(today.IsBackfilled);
        Assert.Equal(exposure, today.Exposure);
        Assert.Equal(market, today.MarketValue);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("assetClass=Healthcare", "Healthcare")]
    public async Task Refinancing_summary_matches_sql(string query, string? cls)
    {
        var s = (await api.GetAsync<RefinancingSummary>($"/api/refinancing/summary?{query}"))!;
        var where = cls is null ? "TRUE" : $"c.\"AssetClass\" = '{cls}'";
        const string requests = """FROM "RefinancingRequests" r JOIN "Contracts" c ON c."Id" = r."ContractId" WHERE""";

        var statuses = await sql.RowsAsync($"""SELECT r."Status" AS status, COUNT(*) AS n {requests} {where} GROUP BY 1""");
        int Count(string status) => statuses.Where(r => (string)r["status"]! == status).Select(r => Convert.ToInt32(r["n"])).SingleOrDefault();
        Assert.Equal(Count("Submitted"), s.Submitted);
        Assert.Equal(Count("UnderReview"), s.UnderReview);
        Assert.Equal(Count("Approved"), s.Approved);
        Assert.Equal(Count("Declined"), s.Declined);
        Assert.Equal(Count("Withdrawn"), s.Withdrawn);

        var pending = await sql.ScalarAsync<decimal>($"""
            SELECT COALESCE(SUM(r."RequestedAmount"), 0) {requests} {where} AND r."Status" IN ('Submitted', 'UnderReview')
            """);
        Assert.Equal(pending, s.PendingAmount);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var wall = await sql.RowsAsync($"""
            SELECT EXTRACT(YEAR FROM c."MaturityDate")::int AS y, EXTRACT(QUARTER FROM c."MaturityDate")::int AS q,
                   COUNT(*) AS n, SUM(c."OutstandingPrincipal") AS exposure, SUM(c."ResidualValue") AS balloon
            FROM "Contracts" c WHERE c."Status" <> 'Closed' AND {where}
              AND c."MaturityDate" >= @from AND c."MaturityDate" < @to
            GROUP BY 1, 2
            """, ("from", today), ("to", today.AddMonths(24)));
        Assert.Equal(wall.Count, s.MaturityWall.Count);
        foreach (var q in s.MaturityWall)
        {
            var row = wall.Single(r => Convert.ToInt32(r["y"]) == q.Year && Convert.ToInt32(r["q"]) == q.Quarter);
            Assert.Equal(Convert.ToInt32(row["n"]), q.Contracts);
            Assert.Equal((decimal)row["exposure"]!, q.Exposure);
            Assert.Equal((decimal)row["balloon"]!, q.BalloonDue);
        }
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("assetClass=Agriculture", "Agriculture")]
    public async Task Remarketing_summary_matches_sql(string query, string? cls)
    {
        var s = (await api.GetAsync<RemarketingSummary>($"/api/remarketing/summary?{query}"))!;
        var where = cls is null ? "TRUE" : $"a.\"AssetClass\" = '{cls}'";
        const string cases = """FROM "RemarketingCases" rc JOIN "Assets" a ON a."Id" = rc."AssetId" WHERE""";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Assert.Equal(await sql.ScalarAsync<long>($"""SELECT COUNT(*) {cases} {where} AND rc."Status" = 'Repossessed'"""), s.Repossessed);
        Assert.Equal(await sql.ScalarAsync<long>($"""SELECT COUNT(*) {cases} {where} AND rc."Status" = 'Listed'"""), s.Listed);

        var ageing = await sql.RowsAsync($"""
            SELECT CASE WHEN @today - rc."RepossessedOn" <= 30 THEN 0 WHEN @today - rc."RepossessedOn" <= 60 THEN 1
                        WHEN @today - rc."RepossessedOn" <= 90 THEN 2 WHEN @today - rc."RepossessedOn" <= 180 THEN 3 ELSE 4 END AS bucket,
                   COUNT(*) AS n, SUM(a."ForcedSaleValue") AS fsv
            {cases} {where} AND rc."Status" <> 'Sold' GROUP BY 1
            """, ("today", today));
        for (var i = 0; i < 5; i++)
        {
            var row = ageing.FirstOrDefault(r => Convert.ToInt32(r["bucket"]) == i);
            Assert.Equal(row is null ? 0 : Convert.ToInt32(row["n"]), s.Ageing[i].Cases);
            Assert.Equal(row is null ? 0 : (decimal)row["fsv"]!, s.Ageing[i].ForcedSaleValue);
        }

        var channels = await sql.RowsAsync($"""
            SELECT rc."Channel" AS channel, COUNT(*) AS n, SUM(COALESCE(rc."SalePrice", 0)) AS proceeds,
                   SUM(rc."RecoveryCosts") AS costs, SUM(rc."ExposureAtDefault") AS ead
            {cases} {where} AND rc."Status" = 'Sold' AND rc."SoldOn" >= @since GROUP BY 1
            """, ("since", today.AddYears(-1)));
        Assert.Equal(channels.Count, s.ByChannel.Count);
        foreach (var c in s.ByChannel)
        {
            var row = channels.Single(r => (string)r["channel"]! == c.Channel.ToString());
            var ead = (decimal)row["ead"]!;
            Assert.Equal(Convert.ToInt32(row["n"]), c.Sold);
            Assert.Equal((decimal)row["proceeds"]!, c.Proceeds);
            Assert.Equal((decimal)row["costs"]!, c.Costs);
            Assert.Equal(ead == 0 ? 0 : Math.Round(((decimal)row["proceeds"]! - (decimal)row["costs"]!) / ead, 4), c.RecoveryRate);
        }
    }

    [Fact]
    public async Task Completed_auction_count_and_lots_match_sql()
    {
        var page = (await api.GetAsync<PagedResult<AuctionItem>>("/api/remarketing/auctions?status=Completed&pageSize=5"))!;

        Assert.Equal(await sql.ScalarAsync<long>("""SELECT COUNT(*) FROM "Auctions" WHERE "Status" = 'Completed'"""), page.Total);
        foreach (var a in page.Items)
        {
            var row = (await sql.RowsAsync("""
                SELECT COUNT(*) AS lots, COUNT(*) FILTER (WHERE "Status" = 'Sold') AS sold, COALESCE(SUM("SalePrice"), 0) AS hammer
                FROM "RemarketingCases" WHERE "AuctionId" = @id
                """, ("id", a.Id))).Single();
            Assert.Equal(Convert.ToInt32(row["lots"]), a.Lots);
            Assert.Equal(Convert.ToInt32(row["sold"]), a.LotsSold);
            Assert.Equal((decimal)row["hammer"]!, a.HammerTotal);
        }
    }

    [Fact]
    public async Task Vendor_figures_match_sql()
    {
        var vendors = (await api.GetAsync<PagedResult<VendorPerformance>>("/api/vendors?pageSize=5"))!;

        foreach (var v in vendors.Items)
        {
            var row = (await sql.RowsAsync("""
                SELECT COUNT(*) AS contracts,
                       COUNT(*) FILTER (WHERE c."Status" <> 'Closed') AS open,
                       COALESCE(SUM(c."OutstandingPrincipal") FILTER (WHERE c."Status" <> 'Closed'), 0) AS exposure,
                       COUNT(*) FILTER (WHERE c."Status" = 'Defaulted' OR EXISTS (
                           SELECT 1 FROM "Assets" a JOIN "RemarketingCases" rc ON rc."AssetId" = a."Id" WHERE a."ContractId" = c."Id")) AS defaulted
                FROM "Contracts" c WHERE c."VendorId" = @id
                """, ("id", v.Id))).Single();
            Assert.Equal(Convert.ToInt32(row["contracts"]), v.Contracts);
            Assert.Equal(Convert.ToInt32(row["open"]), v.OpenContracts);
            Assert.Equal((decimal)row["exposure"]!, v.Exposure);
            Assert.Equal(Math.Round((decimal)Convert.ToInt64(row["defaulted"]) / Convert.ToInt64(row["contracts"]), 4), v.DefaultRate);
        }
    }

    [Fact]
    public async Task Source_figures_match_sql()
    {
        var sources = (await api.GetAsync<List<SourceStatus>>("/api/sources"))!;
        var weekAgo = DateTime.UtcNow.AddDays(-7);

        foreach (var s in sources)
        {
            var row = (await sql.RowsAsync("""
                SELECT COUNT(*) AS runs, COUNT(*) FILTER (WHERE "Status" <> 'Failed') AS ok
                FROM "LoadRuns" WHERE "SourceSystemId" = @id AND "StartedAt" >= @since
                """, ("id", s.Id), ("since", weekAgo))).Single();
            // Rolling 7-day window: a run may cross the boundary between the two queries.
            Assert.InRange(s.Runs7Days, Convert.ToInt32(row["runs"]) - 1, Convert.ToInt32(row["runs"]) + 1);
            Assert.InRange(s.SuccessRate7Days, (decimal)Convert.ToInt64(row["ok"]) / Convert.ToInt64(row["runs"]) - 0.01m,
                (decimal)Convert.ToInt64(row["ok"]) / Convert.ToInt64(row["runs"]) + 0.01m);
        }
    }
}
