using AssetDashboard.Domain;
using AssetDashboard.Infrastructure.Dashboard;
using AssetDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace AssetDashboard.Infrastructure.Simulation;

public enum SimulationStep
{
    Revalue,
    ReceivePayment,
    MissPayment,
    Repossess,
    ListForSale,
    Sell,
    MarketShock,
}

public sealed class SimulatorOptions
{
    public bool Enabled { get; set; } = true;
    public int EventsPerSecond { get; set; } = 4;
}

/// <summary>
/// Stand-in for the client's live systems: continuously mutates the database with
/// revaluations, payments, defaults, repossessions, auction sales and market shocks.
/// </summary>
public sealed class MarketSimulator(
    IServiceScopeFactory scopeFactory,
    IPortfolioEventSink sink,
    IOptions<SimulatorOptions> options,
    ILogger<MarketSimulator> logger) : BackgroundService
{
    private readonly Random _rng = new();
    private volatile bool _paused = !options.Value.Enabled;

    public bool IsRunning => !_paused;
    public void Pause() => _paused = true;
    public void Resume() => _paused = false;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, options.Value.EventsPerSecond));
        using var timer = new PeriodicTimer(delay);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (_paused) continue;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AssetDbContext>();
                var evt = await NextEventAsync(db, stoppingToken);
                if (evt is not null) await sink.PublishAsync(evt, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Simulation step failed");
            }
        }
    }

    private Task<PortfolioEvent?> NextEventAsync(AssetDbContext db, CancellationToken ct) => StepAsync(db, _rng.NextDouble() switch
    {
        < 0.40 => SimulationStep.Revalue,
        < 0.65 => SimulationStep.ReceivePayment,
        < 0.82 => SimulationStep.MissPayment,
        < 0.90 => SimulationStep.Repossess,
        < 0.95 => SimulationStep.ListForSale,
        < 0.99 => SimulationStep.Sell,
        _ => SimulationStep.MarketShock,
    }, ct);

    /// <summary>Runs one specific kind of change; used by the loop above and by integration tests.</summary>
    public Task<PortfolioEvent?> StepAsync(AssetDbContext db, SimulationStep step, CancellationToken ct = default) => step switch
    {
        SimulationStep.Revalue => RevalueAsync(db, ct),
        SimulationStep.ReceivePayment => ReceivePaymentAsync(db, ct),
        SimulationStep.MissPayment => MissPaymentAsync(db, ct),
        SimulationStep.Repossess => RepossessAsync(db, ct),
        SimulationStep.ListForSale => ListForSaleAsync(db, ct),
        SimulationStep.Sell => SellAsync(db, ct),
        SimulationStep.MarketShock => MarketShockAsync(db, ct),
        _ => throw new ArgumentOutOfRangeException(nameof(step)),
    };

    private async Task<PortfolioEvent?> RevalueAsync(AssetDbContext db, CancellationToken ct)
    {
        var asset = await RandomAsync(db.Assets.Where(a => a.Status == AssetStatus.InUse), a => a.Id, ct);
        if (asset is null) return null;

        var before = asset.MarketValue;
        var drift = Math.Clamp(Normal(-0.004, 0.025), -0.12, 0.08);
        asset.MarketValue = Math.Min(asset.OriginalCost, Math.Round(asset.MarketValue * (decimal)(1 + drift), 2));
        asset.ForcedSaleValue = ValuationModel.ForcedSaleValue(asset.MarketValue, asset.AssetClass);
        asset.LastValuedAt = DateTime.UtcNow;
        var method = _rng.NextDouble() < 0.1 ? ValuationMethod.PhysicalInspection : ValuationMethod.IndexBased;
        db.AssetValuations.Add(new AssetValuation
        {
            AssetId = asset.Id, ValuedAt = asset.LastValuedAt, MarketValue = asset.MarketValue,
            ForcedSaleValue = asset.ForcedSaleValue, Method = method,
        });
        await db.SaveChangesAsync(ct);

        return new PortfolioEvent(DateTime.UtcNow, PortfolioEventType.Revaluation,
            $"{asset.Category} {asset.SerialNumber} revalued {Pct(drift)} to {Eur(asset.MarketValue)}",
            asset.AssetClass, asset.ContractId, asset.Id, asset.MarketValue - before);
    }

    private async Task<PortfolioEvent?> ReceivePaymentAsync(AssetDbContext db, CancellationToken ct)
    {
        var contract = await RandomAsync(db.Contracts.Where(c =>
            c.OutstandingPrincipal > 0 && (c.Status == ContractStatus.Active || c.Status == ContractStatus.Delinquent
                                           || c.Status == ContractStatus.Restructured)), c => c.Id, ct);
        if (contract is null) return null;

        var principalPart = Math.Max(0, contract.MonthlyInstallment
                                        - Amortization.InterestPortion(contract.OutstandingPrincipal, contract.InterestRate));
        // A delinquent customer catching up pays all missed installments.
        var installments = Math.Max(1, contract.DaysPastDue / 30);
        contract.OutstandingPrincipal = Math.Max(0, contract.OutstandingPrincipal - principalPart * installments);
        contract.DaysPastDue = 0;
        if (contract.Status == ContractStatus.Delinquent) contract.Status = ContractStatus.Active;
        if (contract.OutstandingPrincipal == 0) contract.Status = ContractStatus.Closed;
        contract.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return new PortfolioEvent(DateTime.UtcNow, PortfolioEventType.PaymentReceived,
            $"{contract.ContractNumber}: {installments} installment(s) received ({Eur(contract.MonthlyInstallment * installments)})",
            contract.AssetClass, contract.Id, Amount: contract.MonthlyInstallment * installments);
    }

    private async Task<PortfolioEvent?> MissPaymentAsync(AssetDbContext db, CancellationToken ct)
    {
        // Weaker customers are more likely to miss: sample among higher risk grades.
        var minGrade = _rng.Next(4, 9);
        var contract = await RandomAsync(db.Contracts.Where(c =>
            (c.Status == ContractStatus.Active || c.Status == ContractStatus.Delinquent) && c.Customer.RiskGrade >= minGrade),
            c => c.Id, ct);
        if (contract is null) return null;

        contract.DaysPastDue += 30;
        contract.UpdatedAt = DateTime.UtcNow;
        var defaulted = contract.DaysPastDue >= 90;
        contract.Status = defaulted ? ContractStatus.Defaulted
            : contract.DaysPastDue >= 30 ? ContractStatus.Delinquent : ContractStatus.Active;
        await db.SaveChangesAsync(ct);

        return defaulted
            ? new PortfolioEvent(DateTime.UtcNow, PortfolioEventType.Default,
                $"{contract.ContractNumber} defaulted at {contract.DaysPastDue} dpd, exposure {Eur(contract.OutstandingPrincipal)}",
                contract.AssetClass, contract.Id, Amount: contract.OutstandingPrincipal)
            : new PortfolioEvent(DateTime.UtcNow, PortfolioEventType.PaymentMissed,
                $"{contract.ContractNumber} missed a payment, now {contract.DaysPastDue} dpd",
                contract.AssetClass, contract.Id, Amount: contract.MonthlyInstallment);
    }

    private async Task<PortfolioEvent?> RepossessAsync(AssetDbContext db, CancellationToken ct)
    {
        var contract = await RandomAsync(db.Contracts.Include(c => c.Assets).Where(c =>
            c.Status == ContractStatus.Defaulted && c.Assets.Any(a => a.Status == AssetStatus.InUse)), c => c.Id, ct);
        if (contract is null) return null;

        var assets = contract.Assets.Where(a => a.Status == AssetStatus.InUse).ToList();
        var totalCost = contract.Assets.Sum(a => a.OriginalCost);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var asset in assets)
        {
            asset.Status = AssetStatus.Repossessed;
            db.RemarketingCases.Add(new RemarketingCase
            {
                AssetId = asset.Id,
                Status = RemarketingStatus.Repossessed,
                Channel = PickChannel(asset.AssetClass),
                RepossessedOn = today,
                ExposureAtDefault = totalCost == 0 ? 0 : Math.Round(contract.OutstandingPrincipal * asset.OriginalCost / totalCost, 2),
                ReservePrice = Math.Round(asset.ForcedSaleValue * 0.9m, 2),
                RecoveryCosts = Math.Round(asset.MarketValue * (decimal)(0.03 + _rng.NextDouble() * 0.05) + 2_500, 2),
            });
        }
        await db.SaveChangesAsync(ct);

        return new PortfolioEvent(DateTime.UtcNow, PortfolioEventType.Repossession,
            $"{assets.Count} × {assets[0].Category} repossessed under {contract.ContractNumber}",
            contract.AssetClass, contract.Id, assets[0].Id, assets.Sum(a => a.ForcedSaleValue));
    }

    private async Task<PortfolioEvent?> ListForSaleAsync(AssetDbContext db, CancellationToken ct)
    {
        var @case = await RandomAsync(db.RemarketingCases.Include(c => c.Asset)
            .Where(c => c.Status == RemarketingStatus.Repossessed), c => c.Id, ct);
        if (@case is null) return null;

        @case.Status = RemarketingStatus.Listed;
        @case.ListedOn = DateOnly.FromDateTime(DateTime.UtcNow);
        @case.Asset.Status = AssetStatus.InRemarketing;
        await db.SaveChangesAsync(ct);

        return new PortfolioEvent(DateTime.UtcNow, PortfolioEventType.Listed,
            $"{@case.Asset.Category} {@case.Asset.SerialNumber} listed via {@case.Channel}, reserve {Eur(@case.ReservePrice)}",
            @case.Asset.AssetClass, @case.Asset.ContractId, @case.AssetId, @case.ReservePrice);
    }

    private async Task<PortfolioEvent?> SellAsync(AssetDbContext db, CancellationToken ct)
    {
        var @case = await RandomAsync(db.RemarketingCases.Include(c => c.Asset).ThenInclude(a => a.Contract)
            .Where(c => c.Status == RemarketingStatus.Listed), c => c.Id, ct);
        if (@case is null) return null;

        var asset = @case.Asset;
        var price = Math.Round(asset.ForcedSaleValue * (decimal)Math.Clamp(Normal(1.0, 0.12), 0.5, 1.4), 2);
        @case.Status = RemarketingStatus.Sold;
        @case.SoldOn = DateOnly.FromDateTime(DateTime.UtcNow);
        @case.SalePrice = price;
        asset.Status = AssetStatus.Sold;
        db.AssetValuations.Add(new AssetValuation
        {
            AssetId = asset.Id, ValuedAt = DateTime.UtcNow, MarketValue = price, ForcedSaleValue = price,
            Method = ValuationMethod.AuctionComparable,
        });

        // Apply proceeds to the contract; once every asset is sold the remainder is written off.
        var contract = asset.Contract;
        contract.OutstandingPrincipal = Math.Max(0, contract.OutstandingPrincipal - (price - @case.RecoveryCosts));
        var anyLeft = await db.Assets.AnyAsync(a => a.ContractId == contract.Id && a.Id != asset.Id && a.Status != AssetStatus.Sold, ct);
        if (!anyLeft)
        {
            contract.OutstandingPrincipal = 0;
            contract.Status = ContractStatus.Closed;
        }
        contract.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var recovery = @case.ExposureAtDefault == 0 ? 0 : (price - @case.RecoveryCosts) / @case.ExposureAtDefault;
        return new PortfolioEvent(DateTime.UtcNow, PortfolioEventType.Sold,
            $"{asset.Category} {asset.SerialNumber} sold via {@case.Channel} for {Eur(price)} (recovery {recovery.ToString("P0", Culture)})",
            asset.AssetClass, contract.Id, asset.Id, price);
    }

    /// <summary>Moves the value of a whole asset class at once, e.g. a drop in used-truck prices.</summary>
    private async Task<PortfolioEvent?> MarketShockAsync(AssetDbContext db, CancellationToken ct)
    {
        var assetClass = (AssetClass)_rng.Next(Enum.GetValues<AssetClass>().Length);
        var change = (_rng.NextDouble() < 0.6 ? -1 : 1) * (0.02 + _rng.NextDouble() * 0.04);
        var factor = (decimal)(1 + change);
        var now = DateTime.UtcNow;

        var affected = await db.Assets
            .Where(a => a.AssetClass == assetClass && a.Status != AssetStatus.Sold)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.MarketValue, a => Math.Min(a.OriginalCost, Math.Round(a.MarketValue * factor, 2)))
                .SetProperty(a => a.ForcedSaleValue, a => Math.Min(a.OriginalCost, Math.Round(a.ForcedSaleValue * factor, 2)))
                .SetProperty(a => a.LastValuedAt, now), ct);

        return new PortfolioEvent(now, PortfolioEventType.MarketShock,
            $"Market index for {assetClass} moved {Pct(change)}: {affected.ToString("N0", Culture)} assets revalued",
            assetClass);
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>
    /// Picks a random row cheaply on large tables: jump to a random key and take the next match,
    /// instead of ORDER BY random().
    /// </summary>
    private async Task<T?> RandomAsync<T>(IQueryable<T> query, System.Linq.Expressions.Expression<Func<T, long>> key,
        CancellationToken ct) where T : class
    {
        var max = await query.OrderByDescending(key).Select(key).FirstOrDefaultAsync(ct);
        if (max == 0) return null;
        var pivot = _rng.NextInt64(1, max + 1);
        var param = key.Parameters[0];
        var predicate = System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(
            System.Linq.Expressions.Expression.GreaterThanOrEqual(key.Body, System.Linq.Expressions.Expression.Constant(pivot)), param);
        return await query.Where(predicate).OrderBy(key).FirstOrDefaultAsync(ct)
               ?? await query.OrderBy(key).FirstOrDefaultAsync(ct);
    }

    private RemarketingChannel PickChannel(AssetClass assetClass) => assetClass switch
    {
        AssetClass.Healthcare or AssetClass.Technology => _rng.NextDouble() < 0.5 ? RemarketingChannel.VendorBuyBack : RemarketingChannel.PrivateSale,
        AssetClass.CleanTech => RemarketingChannel.PrivateSale,
        _ => _rng.NextDouble() < 0.6 ? RemarketingChannel.LiveAuction : RemarketingChannel.OnlineAuction,
    };

    private double Normal(double mean, double stdDev)
    {
        var u1 = 1.0 - _rng.NextDouble();
        var u2 = _rng.NextDouble();
        return mean + stdDev * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }

    // Event messages use a fixed culture so they read the same regardless of server locale.
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-IE");
    private static string Eur(decimal amount) => "€" + amount.ToString("N0", Culture);
    private static string Pct(double change) => (change >= 0 ? "+" : "") + change.ToString("P1", Culture);
}
