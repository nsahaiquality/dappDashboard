# Domain model

All data is synthetic. The model follows the general mechanics of **vendor finance** (equipment
finance offered through manufacturers and dealers). It's simplified for the exercise.

## Business flow

1. A **vendor** (equipment manufacturer or dealer) sells equipment to a business **customer**.
2. The finance company pays the vendor and enters into a **contract** (lease or loan) with the
   customer, who repays in monthly installments.
3. The equipment is the **asset**. It stays on the finance company's books as collateral (leases)
   or is pledged (loans). It is **revalued** periodically as it depreciates and as used-equipment
   markets move.
4. If the customer falls behind (**days past due**) and eventually **defaults** (90+ dpd), the asset
   is **repossessed** and **remarketed**: sold at live or online auction, privately, or back to the
   vendor. Sale proceeds minus costs are set against the outstanding exposure. The ratio is the
   **recovery rate**.
5. For **refinancing or restructuring**, the current asset value (and its forced sale value)
   determines how much security remains, and therefore the risk and price of new terms.

## Entities

```mermaid
erDiagram
    VENDOR ||--o{ CONTRACT : originates
    CUSTOMER ||--o{ CONTRACT : "is obligor of"
    CONTRACT ||--|{ ASSET : finances
    ASSET ||--o{ ASSET_VALUATION : "valued by"
    ASSET ||--o| REMARKETING_CASE : "recovered via"

    VENDOR {
        int Id
        string Name
        enum PrimaryAssetClass
        string Country
    }
    CUSTOMER {
        int Id
        string Name
        string Country
        string City
        int RiskGrade
    }
    CONTRACT {
        long Id
        string ContractNumber
        enum ProductType
        enum AssetClass
        date StartDate
        int TermMonths
        decimal FinancedAmount
        decimal InterestRate
        decimal MonthlyInstallment
        decimal OutstandingPrincipal
        decimal ResidualValue
        int DaysPastDue
        enum Status
    }
    ASSET {
        long Id
        string SerialNumber
        enum AssetClass
        string Category
        string Manufacturer
        string Model
        int YearOfManufacture
        decimal OriginalCost
        decimal MarketValue
        decimal ForcedSaleValue
        enum Condition
        enum Status
        string Country
    }
    ASSET_VALUATION {
        long Id
        datetime ValuedAt
        decimal MarketValue
        decimal ForcedSaleValue
        enum Method
    }
    REMARKETING_CASE {
        long Id
        enum Status
        enum Channel
        date RepossessedOn
        date ListedOn
        date SoldOn
        decimal ExposureAtDefault
        decimal ReservePrice
        decimal SalePrice
        decimal RecoveryCosts
    }
```

### Key terms

| Term | Meaning |
|---|---|
| Exposure | Outstanding principal still owed on open contracts |
| Market value (fair market value) | What the asset would fetch in a normal sale |
| Forced sale value (FSV) | Expected proceeds from a quick sale (auction after repossession) |
| LTV | Exposure ÷ collateral market value. Above 100% means "underwater" |
| Forced sale shortfall | Σ max(0, exposure − FSV) per contract: loss if all were liquidated now |
| DPD | Days past due; buckets: current, 1–29, 30–59, 60–89, 90+ |
| Recovery rate | (sale proceeds − recovery costs) ÷ exposure at default |

### State machines

**Contract:** `Active ⇄ Delinquent (30–89 dpd) → Defaulted (90+) → Closed`, plus `Restructured`.

**Asset:** `InUse → Repossessed → InRemarketing → Sold`, plus `Returned` (end of an operating lease).

## Synthetic data assumptions

The generator (`PortfolioGenerator`, seeded and deterministic) uses these assumptions.

**Portfolio mix:** Agriculture 25%, Construction 20%, Transportation 18%, Technology 12%,
Healthcare 10%, Material handling 10%, CleanTech 5%. Countries: eurozone, with NL and DE the
largest.

**Vendors:** about one per 120 contracts, with skewed volumes (a few large partners write most of
the business).

**Contracts:**
- Term depends on the asset class: 24–48 months for tech, up to 120 for CleanTech.
- Product mix depends on the class: tech mostly operating lease; transport mostly hire purchase.
- Down payment: 0–10% for leases, 10–25% for loans.
- Residual value: 20–35% for operating leases, 5–15% for finance leases.
- Interest is about 2.5% + 0.45% × risk grade, with an extra margin for tech.
- Installments use the standard annuity formula with a balloon.

**Credit:**
- Customer risk grade is roughly normally distributed around 5 (on a 1–10 scale).
- Probability of default is 0.25% × grade^1.6: about 0.25% for grade 1 and about 10% for grade 10.
- Delinquent customers have unpaid installments added back to their balance.

**Valuation:** declining-balance depreciation per class, plus a first-year drop, a condition factor
and a per-class market index (random walk), with a salvage floor.

| Class | 1st-year drop | Annual depreciation | Floor | FSV / market value |
|---|---|---|---|---|
| Agriculture | 10% | 11% | 15% | 75% |
| Construction | 12% | 13% | 12% | 70% |
| Healthcare | 15% | 17% | 5% | 50% |
| Technology | 20% | 30% | 2% | 35% |
| Transportation | 12% | 16% | 8% | 70% |
| Material handling | 10% | 12% | 10% | 65% |
| CleanTech | 8% | 7% | 10% | 55% |

**Remarketing:**
- About 35% of older defaults are fully resolved (sold, contract closed).
- Sale price follows a normal distribution around the forced sale value (σ 12%).
- Recovery costs are 3–8% of value plus a fixed amount.
- Channel depends on the asset class: auctions for agriculture and construction, vendor buy-back
  for healthcare and tech.

**Resulting portfolio (20k contracts):**
- About 31k assets and 113k valuations.
- Loan-to-value about 69%; healthcare and tech above 100%, because that equipment depreciates
  fast.
- About 3.5% defaulted and about 4% delinquent.
- Recovery rate about 60%.

## Simulator

`MarketSimulator` makes about 4 changes per second:

| Change | Share of events |
|---|---|
| Revaluations | 40% |
| Payments received | 25% |
| Missed payments (skewed to weaker customers) | 17% |
| Repossessions of defaulted contracts | 8% |
| Listings | 5% |
| Sales | 4% |
| Market shocks (a whole asset class moves 2–6%, applied with one bulk `UPDATE`) | 1% |
