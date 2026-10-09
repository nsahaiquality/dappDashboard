namespace AssetDashboard.Domain;

/// <summary>Equipment manufacturer or dealer that partners with the finance company.</summary>
public class Vendor
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public AssetClass PrimaryAssetClass { get; set; }
    public required string Country { get; set; }

    public List<Contract> Contracts { get; set; } = [];
}

/// <summary>The business (obligor) that uses the equipment and repays the financing.</summary>
public class Customer
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Country { get; set; }
    public required string City { get; set; }

    /// <summary>Internal risk grade, 1 (best) to 10 (worst).</summary>
    public int RiskGrade { get; set; }

    public List<Contract> Contracts { get; set; } = [];
}

/// <summary>A lease or loan that finances one or more assets of the same class.</summary>
public class Contract
{
    public long Id { get; set; }
    public required string ContractNumber { get; set; }

    public int VendorId { get; set; }
    public Vendor Vendor { get; set; } = null!;
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public ProductType ProductType { get; set; }
    public AssetClass AssetClass { get; set; }
    public required string Currency { get; set; }

    public DateOnly StartDate { get; set; }
    public int TermMonths { get; set; }
    /// <summary>Date of the last installment (start + term); the balloon / residual falls due then.</summary>
    public DateOnly MaturityDate { get; set; }

    public decimal FinancedAmount { get; set; }
    /// <summary>Annual nominal interest rate in percent, e.g. 5.25.</summary>
    public decimal InterestRate { get; set; }
    public decimal MonthlyInstallment { get; set; }
    /// <summary>Balance still owed: the exposure.</summary>
    public decimal OutstandingPrincipal { get; set; }
    /// <summary>Guaranteed residual value at end of term (leases only).</summary>
    public decimal ResidualValue { get; set; }

    public int DaysPastDue { get; set; }
    public ContractStatus Status { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<Asset> Assets { get; set; } = [];
    public List<RefinancingRequest> RefinancingRequests { get; set; } = [];
}

/// <summary>A physical piece of equipment serving as collateral.</summary>
public class Asset
{
    public long Id { get; set; }
    public long ContractId { get; set; }
    public Contract Contract { get; set; } = null!;

    public required string SerialNumber { get; set; }
    public AssetClass AssetClass { get; set; }
    public required string Category { get; set; }
    public required string Manufacturer { get; set; }
    public required string Model { get; set; }
    public int YearOfManufacture { get; set; }

    /// <summary>List price when new; the basis for depreciation.</summary>
    public decimal OriginalCost { get; set; }
    /// <summary>Fair market value from the latest valuation.</summary>
    public decimal MarketValue { get; set; }
    /// <summary>Expected proceeds in a quick, forced sale (e.g. auction after repossession).</summary>
    public decimal ForcedSaleValue { get; set; }
    public DateTime LastValuedAt { get; set; }

    public AssetCondition Condition { get; set; }
    public AssetStatus Status { get; set; }
    public required string Country { get; set; }
    public required string City { get; set; }

    public List<AssetValuation> Valuations { get; set; } = [];
    public RemarketingCase? RemarketingCase { get; set; }
}

/// <summary>Point-in-time valuation of an asset (history is kept for trend analysis).</summary>
public class AssetValuation
{
    public long Id { get; set; }
    public long AssetId { get; set; }
    public Asset Asset { get; set; } = null!;

    public DateTime ValuedAt { get; set; }
    public decimal MarketValue { get; set; }
    public decimal ForcedSaleValue { get; set; }
    public ValuationMethod Method { get; set; }
}

/// <summary>Recovery of a repossessed asset through sale.</summary>
public class RemarketingCase
{
    public long Id { get; set; }
    public long AssetId { get; set; }
    public Asset Asset { get; set; } = null!;

    public RemarketingStatus Status { get; set; }
    public RemarketingChannel Channel { get; set; }
    public DateOnly RepossessedOn { get; set; }
    public DateOnly? ListedOn { get; set; }
    public DateOnly? SoldOn { get; set; }

    /// <summary>Share of the contract exposure attributed to this asset at default.</summary>
    public decimal ExposureAtDefault { get; set; }
    public decimal ReservePrice { get; set; }
    public decimal? SalePrice { get; set; }
    /// <summary>Repossession, transport, storage, refurbishment and auction fees.</summary>
    public decimal RecoveryCosts { get; set; }
}

/// <summary>
/// One day of portfolio figures, for the whole portfolio (<see cref="AssetClass"/> null) or one asset class.
/// Older rows are a synthetic backfill; from the first run onwards a background job captures today's row.
/// </summary>
public class PortfolioHistoryPoint
{
    public long Id { get; set; }
    public DateOnly Date { get; set; }
    public AssetClass? AssetClass { get; set; }

    public int OpenContracts { get; set; }
    public int ActiveAssets { get; set; }
    public decimal Exposure { get; set; }
    public decimal MarketValue { get; set; }
    public decimal ForcedSaleValue { get; set; }
    /// <summary>Exposure on contracts 30+ days past due.</summary>
    public decimal ExposureAtRisk { get; set; }
    /// <summary>Exposure on contracts 90+ days past due (defaulted).</summary>
    public decimal DefaultedExposure { get; set; }
    /// <summary>Net remarketing proceeds (sale price − costs) booked that day.</summary>
    public decimal NetRecoveries { get; set; }

    /// <summary>True for generated history, false for figures captured from the live portfolio.</summary>
    public bool IsBackfilled { get; set; }
}

/// <summary>A customer's request to change the terms of a contract, and the credit decision on it.</summary>
public class RefinancingRequest
{
    public long Id { get; set; }
    public long ContractId { get; set; }
    public Contract Contract { get; set; } = null!;

    public DateTime RequestedAt { get; set; }
    public RefinancingReason Reason { get; set; }
    /// <summary>New financed amount the customer asks for.</summary>
    public decimal RequestedAmount { get; set; }
    public int RequestedTermMonths { get; set; }
    public decimal CurrentRate { get; set; }
    public decimal? ProposedRate { get; set; }

    // Risk picture when the request came in.
    public decimal LtvAtRequest { get; set; }
    public decimal ForcedSaleCoverAtRequest { get; set; }
    public int RiskGradeAtRequest { get; set; }
    public decimal ExpectedLossAtRequest { get; set; }

    public RefinancingStatus Status { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
}
