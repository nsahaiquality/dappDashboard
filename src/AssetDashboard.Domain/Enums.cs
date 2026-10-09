namespace AssetDashboard.Domain;

/// <summary>Broad equipment segments typical for a vendor-finance portfolio.</summary>
public enum AssetClass
{
    Agriculture,
    Construction,
    Healthcare,
    Technology,
    Transportation,
    MaterialHandling,
    CleanTech,
}

/// <summary>Financial product under which the asset was financed.</summary>
public enum ProductType
{
    FinanceLease,
    OperatingLease,
    Loan,
    HirePurchase,
}

public enum ContractStatus
{
    /// <summary>Performing, payments up to date (or less than 30 days late).</summary>
    Active,
    /// <summary>30–89 days past due.</summary>
    Delinquent,
    /// <summary>90+ days past due; recovery process started.</summary>
    Defaulted,
    /// <summary>Terms renegotiated / refinanced.</summary>
    Restructured,
    /// <summary>Fully repaid or settled after asset sale.</summary>
    Closed,
}

public enum AssetStatus
{
    /// <summary>With the customer, generating revenue.</summary>
    InUse,
    /// <summary>Taken back from a defaulted customer, awaiting remarketing.</summary>
    Repossessed,
    /// <summary>Listed for sale (auction or private).</summary>
    InRemarketing,
    /// <summary>Sold; proceeds applied to the defaulted exposure.</summary>
    Sold,
    /// <summary>Returned at end of an operating lease.</summary>
    Returned,
}

public enum AssetCondition
{
    Excellent,
    Good,
    Fair,
    Poor,
}

public enum ValuationMethod
{
    /// <summary>Origination value (invoice price).</summary>
    Invoice,
    /// <summary>Desktop valuation using depreciation curves and market indices.</summary>
    IndexBased,
    /// <summary>On-site inspection by an appraiser.</summary>
    PhysicalInspection,
    /// <summary>Derived from comparable auction results.</summary>
    AuctionComparable,
}

public enum RemarketingChannel
{
    LiveAuction,
    OnlineAuction,
    PrivateSale,
    VendorBuyBack,
}

public enum RemarketingStatus
{
    Repossessed,
    Listed,
    Sold,
}

public enum RefinancingReason
{
    /// <summary>Customer struggles with the current installment and asks for lower payments.</summary>
    CashFlowStress,
    /// <summary>Customer wants additional funds against the same collateral.</summary>
    Expansion,
    /// <summary>End-of-term balloon / residual value that the customer wants to refinance.</summary>
    BalloonPayment,
    /// <summary>Customer asks for a lower rate after an improved credit profile.</summary>
    RateReduction,
}

public enum RefinancingStatus
{
    Submitted,
    UnderReview,
    Approved,
    Declined,
    Withdrawn,
}

public enum AuctionStatus
{
    Scheduled,
    Completed,
}

public enum VendorProgramType
{
    /// <summary>Manufacturer programme (captive-style), usually with buy-back support.</summary>
    Manufacturer,
    Dealer,
    Distributor,
}

/// <summary>What the vendor guarantees if the customer defaults.</summary>
public enum RecourseType
{
    None,
    /// <summary>Vendor covers part of the loss, capped.</summary>
    PartialRecourse,
    /// <summary>Vendor covers the full outstanding balance.</summary>
    FullRecourse,
    /// <summary>Vendor buys the repossessed asset back at an agreed price.</summary>
    BuyBack,
}
