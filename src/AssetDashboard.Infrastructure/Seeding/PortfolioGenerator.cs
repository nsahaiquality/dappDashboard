using AssetDashboard.Domain;

namespace AssetDashboard.Infrastructure.Seeding;

/// <summary>
/// Generates a deterministic, reasonably realistic synthetic vendor-finance portfolio:
/// vendors, customers, contracts with amortising balances, collateral assets with
/// valuation history, delinquencies, defaults and remarketing (auction) outcomes.
/// </summary>
public sealed class PortfolioGenerator(int seed = 42)
{
    private readonly Random _rng = new(seed);
    private int _serial;
    private readonly Dictionary<(AssetClass, int), double> _marketIndex = [];
    private readonly Dictionary<(RemarketingChannel, string, DateOnly), Auction> _auctions = [];

    public sealed record Portfolio(List<Vendor> Vendors, List<Customer> Customers, List<Contract> Contracts, List<Auction> Auctions);

    public Portfolio Generate(int contractCount, DateTime asOfUtc)
    {
        var asOf = DateOnly.FromDateTime(asOfUtc);

        var vendorsByClass = AssetCatalog.Classes.ToDictionary(
            c => c.AssetClass,
            c => Enumerable.Range(0, Math.Max(3, (int)(contractCount / 120.0 * c.PortfolioWeight)))
                .Select(_ => NewVendor(c)).ToList());

        var customersByClass = AssetCatalog.Classes.ToDictionary(
            c => c.AssetClass,
            c => Enumerable.Range(0, Math.Max(5, (int)(contractCount * 0.6 * c.PortfolioWeight)))
                .Select(_ => NewCustomer(c)).ToList());

        var contracts = new List<Contract>(contractCount);
        for (var i = 1; i <= contractCount; i++)
        {
            var info = Weighted(AssetCatalog.Classes.Select(c => (c, c.PortfolioWeight)));
            // Vendor volumes are skewed: a few large partners write most of the business.
            var vendors = vendorsByClass[info.AssetClass];
            var vendor = vendors[(int)(vendors.Count * Math.Pow(_rng.NextDouble(), 2.2))];
            var customer = Pick(customersByClass[info.AssetClass]);
            contracts.Add(NewContract(i, info, vendor, customer, asOf));
        }

        return new Portfolio(
            vendorsByClass.Values.SelectMany(v => v).ToList(),
            customersByClass.Values.SelectMany(c => c).ToList(),
            contracts,
            _auctions.Values.OrderBy(a => a.Date).ToList());
    }

    private Vendor NewVendor(AssetCatalog.ClassInfo info)
    {
        var country = WeightedCountry();
        var prefix = _rng.NextDouble() < 0.5 ? Pick(country.Surnames) : Pick(country.Cities);
        return new Vendor
        {
            Name = $"{prefix} {info.VendorNoun} {country.LegalForm}",
            PrimaryAssetClass = info.AssetClass,
            Country = country.Code,
        };
    }

    private Customer NewCustomer(AssetCatalog.ClassInfo info)
    {
        var country = WeightedCountry();
        return new Customer
        {
            Name = $"{Pick(country.Surnames)} {info.CustomerNoun} {country.LegalForm}",
            Country = country.Code,
            City = Pick(country.Cities),
            RiskGrade = Math.Clamp((int)Math.Round(Normal(5, 2)), 1, 10),
        };
    }

    private Contract NewContract(int seq, AssetCatalog.ClassInfo info, Vendor vendor, Customer customer, DateOnly asOf)
    {
        var term = Pick(info.TermsMonths);
        var product = Weighted(info.Products);
        var monthsElapsed = _rng.Next(0, term);
        var start = asOf.AddMonths(-monthsElapsed).AddDays(-_rng.Next(0, 28));

        // Credit state, driven by the customer's risk grade.
        var pd = 0.0025 * Math.Pow(customer.RiskGrade, 1.6);
        var roll = _rng.NextDouble();
        var (status, dpd) = roll switch
        {
            _ when roll < pd => (ContractStatus.Defaulted, 90 + _rng.Next(0, 270)),
            _ when roll < pd * 2.2 => (ContractStatus.Delinquent, 30 + _rng.Next(0, 60)),
            _ when roll < pd * 3.5 => (ContractStatus.Active, 1 + _rng.Next(0, 29)),
            _ when _rng.NextDouble() < 0.025 => (ContractStatus.Restructured, 0),
            _ => (ContractStatus.Active, 0),
        };

        // Assets (a contract finances one or more units of the same category).
        var category = Pick(info.Categories);
        var fleet = category.MaxFleetSize == 1 || _rng.NextDouble() < 0.6 ? 1 : _rng.Next(2, category.MaxFleetSize + 1);
        var manufacturer = Pick(info.Manufacturers);
        var model = $"{Initials(category.Name)} {_rng.Next(10, 99) * 10}";
        var newPrice = RoundTo(LogUniform((double)category.MinCost, (double)category.MaxCost), 100);
        var used = _rng.NextDouble() < 0.2;
        var yearOfManufacture = start.Year - (used ? _rng.Next(1, 6) : 0);
        var purchasePrice = used
            ? ValuationModel.MarketValue(newPrice, info.AssetClass, start.Year - yearOfManufacture + 0.5, AssetCondition.Good)
            : newPrice;
        var purchaseTotal = purchasePrice * fleet;

        // Financing terms.
        var isLease = product is ProductType.FinanceLease or ProductType.OperatingLease;
        var downPayment = isLease ? Uniform(0, 0.10) : Uniform(0.10, 0.25);
        var financed = Math.Round(purchaseTotal * (decimal)(1 - downPayment), 2);
        var residual = product switch
        {
            ProductType.OperatingLease => Math.Round(purchaseTotal * (decimal)Uniform(0.20, 0.35), 2),
            ProductType.FinanceLease => Math.Round(purchaseTotal * (decimal)Uniform(0.05, 0.15), 2),
            _ => 0m,
        };
        var rate = Math.Round((decimal)(2.5 + customer.RiskGrade * 0.45 + Uniform(-0.4, 0.6)
                                        + (info.AssetClass == AssetClass.Technology ? 0.5 : 0)), 3);
        var installment = Amortization.Installment(financed, rate, term, residual);
        var monthsPaid = Math.Max(0, monthsElapsed - dpd / 30);
        var outstanding = Amortization.OutstandingAfter(financed, rate, installment, monthsPaid);

        var contract = new Contract
        {
            ContractNumber = $"VF-{start.Year}-{seq:D7}",
            Vendor = vendor,
            Customer = customer,
            ProductType = product,
            AssetClass = info.AssetClass,
            Currency = "EUR",
            StartDate = start,
            TermMonths = term,
            MaturityDate = start.AddMonths(term),
            FinancedAmount = financed,
            InterestRate = rate,
            MonthlyInstallment = installment,
            OutstandingPrincipal = outstanding,
            ResidualValue = residual,
            DaysPastDue = dpd,
            Status = status,
            UpdatedAt = asOf.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        };

        for (var u = 0; u < fleet; u++)
        {
            var asset = NewAsset(info.AssetClass, category.Name, manufacturer, model, yearOfManufacture,
                newPrice, purchasePrice, start, asOf, customer, distressed: status == ContractStatus.Defaulted);
            contract.Assets.Add(asset);
        }

        if (status == ContractStatus.Defaulted && dpd > 150)
            Recover(contract, info, start, monthsPaid, purchasePrice / purchaseTotal, asOf);
        else if (status == ContractStatus.Restructured || _rng.NextDouble() < 0.04)
            AddRefinancingRequest(contract, customer, monthsElapsed, asOf);

        return contract;
    }

    private Asset NewAsset(AssetClass assetClass, string category, string manufacturer, string model,
        int yearOfManufacture, decimal newPrice, decimal purchasePrice, DateOnly start, DateOnly asOf,
        Customer customer, bool distressed)
    {
        var ageNow = YearsBetween(new DateOnly(yearOfManufacture, 7, 1), asOf);
        var condition = PickCondition(ageNow, distressed);

        var asset = new Asset
        {
            SerialNumber = $"{manufacturer[..3].ToUpperInvariant()}-{yearOfManufacture}-{++_serial:D7}",
            AssetClass = assetClass,
            Category = category,
            Manufacturer = manufacturer,
            Model = model,
            YearOfManufacture = yearOfManufacture,
            OriginalCost = newPrice,
            Condition = condition,
            Status = AssetStatus.InUse,
            Country = customer.Country,
            City = customer.City,
        };

        AddValuation(asset, start, purchasePrice, ValuationMethod.Invoice);
        // Annual desktop revaluations, plus a recent one.
        var lastValued = start;
        for (var d = start.AddMonths(12); d < asOf; d = d.AddMonths(12))
        {
            AddValuation(asset, d, method: _rng.NextDouble() < 0.15 ? ValuationMethod.PhysicalInspection : ValuationMethod.IndexBased);
            lastValued = d;
        }
        var latest = asOf.AddDays(-_rng.Next(0, 90));
        if (latest > lastValued) AddValuation(asset, latest, method: ValuationMethod.IndexBased);

        return asset;
    }

    /// <summary>Repossess and (possibly) sell the assets of a defaulted contract.</summary>
    private void Recover(Contract contract, AssetCatalog.ClassInfo info, DateOnly start, int monthsPaid,
        decimal assetShare, DateOnly asOf)
    {
        var repossessedOn = Min(start.AddMonths(monthsPaid + 4).AddDays(_rng.Next(0, 60)), asOf.AddDays(-5));
        var resolved = _rng.NextDouble() < 0.35;
        var channel = Weighted(info.Channels);
        var country = AssetCatalog.Countries.Single(c => c.Code == contract.Assets[0].Country);

        foreach (var asset in contract.Assets)
        {
            // Valuation history stops at repossession, where an inspection sets the recovery value.
            asset.Valuations.RemoveAll(v => v.ValuedAt >= Utc(repossessedOn));
            AddValuation(asset, repossessedOn, method: ValuationMethod.PhysicalInspection);
            var fsv = asset.ForcedSaleValue;
            var @case = new RemarketingCase
            {
                Channel = channel,
                RepossessedOn = repossessedOn,
                ExposureAtDefault = Math.Round(contract.OutstandingPrincipal * assetShare, 2),
                ReservePrice = Math.Round(fsv * 0.9m, 2),
                Status = RemarketingStatus.Repossessed,
                Yard = $"{Pick(country.Cities)} yard",
                DailyStorageRate = RemarketingRules.DailyStorageRate(asset.AssetClass),
                TransportCost = Math.Round(asset.MarketValue * (decimal)Uniform(0.01, 0.03) + (decimal)Uniform(500, 2_500), 2),
                RefurbishmentCost = _rng.NextDouble() < 0.5 ? Math.Round(asset.MarketValue * (decimal)Uniform(0.01, 0.05), 2) : 0,
            };
            asset.Status = AssetStatus.Repossessed;

            if (resolved || _rng.NextDouble() < 0.6)
            {
                @case.Status = RemarketingStatus.Listed;
                @case.ListedOn = Min(repossessedOn.AddDays(_rng.Next(10, 40)), asOf);
                asset.Status = AssetStatus.InRemarketing;
                if (!RemarketingRules.IsAuction(channel))
                {
                    @case.PriceReductions = resolved ? _rng.Next(0, 2) : _rng.Next(0, 4);
                    @case.AskingPrice = Math.Round(asset.MarketValue * 0.95m * (decimal)Math.Pow(0.95, @case.PriceReductions), 2);
                }
            }

            if (resolved)
            {
                DateOnly soldOn;
                decimal price;
                if (RemarketingRules.IsAuction(channel))
                {
                    var auction = AuctionFor(channel, country, Min(@case.ListedOn!.Value.AddDays(_rng.Next(7, 45)), asOf), asOf, upcoming: false);
                    soldOn = auction.Date;
                    @case.Auction = auction;
                    @case.Bids = _rng.Next(2, 21);
                    @case.TimesPassedIn = _rng.NextDouble() < 0.15 ? 1 : 0;
                    if (_rng.NextDouble() < 0.18) auction.LotsPassedIn++;
                    price = Math.Round(fsv * RemarketingRules.HammerFactor(@case.Bids.Value, Normal(0, 0.06)), 2);
                }
                else
                {
                    soldOn = Min(@case.ListedOn!.Value.AddDays(_rng.Next(14, 90)), asOf);
                    price = Math.Round(fsv * (decimal)Math.Clamp(Normal(1.0, 0.10), 0.6, 1.3), 2);
                }
                @case.Status = RemarketingStatus.Sold;
                @case.SoldOn = soldOn;
                @case.SalePrice = price;
                RemarketingRules.ApplySaleCosts(@case, price, soldOn);
                asset.Status = AssetStatus.Sold;
                AddValuation(asset, soldOn, price, ValuationMethod.AuctionComparable);
                // The realised price is the asset's final value, whatever the class haircut would say.
                asset.ForcedSaleValue = asset.Valuations[^1].ForcedSaleValue = price;
            }
            else
            {
                if (@case.Status == RemarketingStatus.Listed && RemarketingRules.IsAuction(channel))
                {
                    @case.Auction = AuctionFor(channel, country, asOf.AddDays(_rng.Next(1, 29)), asOf, upcoming: true);
                    @case.TimesPassedIn = _rng.NextDouble() < 0.2 ? _rng.Next(1, 3) : 0;
                }
                // Costs accrued so far; selling fees only arise on sale.
                @case.StorageCost = Math.Round((asOf.DayNumber - repossessedOn.DayNumber) * @case.DailyStorageRate, 2);
                @case.RecoveryCosts = @case.TransportCost + @case.StorageCost + @case.RefurbishmentCost;
            }

            asset.RemarketingCase = @case;
        }

        if (resolved)
        {
            // Sale proceeds applied; any shortfall written off.
            contract.Status = ContractStatus.Closed;
            contract.OutstandingPrincipal = 0;
        }
    }

    /// <summary>
    /// Auctions run on Thursdays per channel and country. Past ones are completed; upcoming ones
    /// (after <paramref name="asOf"/>) are scheduled and collect the currently listed lots.
    /// </summary>
    private Auction AuctionFor(RemarketingChannel channel, AssetCatalog.CountryInfo country, DateOnly around, DateOnly asOf, bool upcoming)
    {
        var date = around.AddDays(((int)DayOfWeek.Thursday - (int)around.DayOfWeek + 7) % 7);
        if (!upcoming && date > asOf) date = date.AddDays(-7);
        if (upcoming && date <= asOf) date = date.AddDays(7);
        var key = (channel, country.Code, date);
        if (_auctions.TryGetValue(key, out var auction)) return auction;

        var city = country.Cities[0];
        auction = new Auction
        {
            Name = $"{city} {(channel == RemarketingChannel.LiveAuction ? "Live" : "Online")} Equipment Auction {date:d MMM yyyy}",
            Channel = channel,
            Date = date,
            Location = channel == RemarketingChannel.LiveAuction ? $"{city}, {country.Code}" : $"Online ({country.Code})",
            Status = date <= asOf ? AuctionStatus.Completed : AuctionStatus.Scheduled,
        };
        return _auctions[key] = auction;
    }

    /// <summary>A refinancing request in the last year, decided by the credit policy unless still open.</summary>
    private void AddRefinancingRequest(Contract contract, Customer customer, int monthsElapsed, DateOnly asOf)
    {
        if (contract.Status is ContractStatus.Closed or ContractStatus.Defaulted || monthsElapsed < 3) return;

        var monthsLeft = contract.TermMonths - monthsElapsed;
        var reason = contract.Status == ContractStatus.Restructured || contract.DaysPastDue > 0 ? RefinancingReason.CashFlowStress
            : contract.ResidualValue > 0 && monthsLeft <= 9 ? RefinancingReason.BalloonPayment
            : customer.RiskGrade <= 4 && _rng.NextDouble() < 0.4 ? RefinancingReason.RateReduction
            : RefinancingReason.Expansion;
        var (amount, term) = reason switch
        {
            RefinancingReason.CashFlowStress => (contract.OutstandingPrincipal, monthsLeft + _rng.Next(12, 25)),
            RefinancingReason.Expansion => (Math.Round(contract.OutstandingPrincipal * (decimal)Uniform(1.10, 1.30), 2), _rng.Next(36, 61)),
            RefinancingReason.BalloonPayment => (contract.ResidualValue, _rng.Next(12, 37)),
            _ => (contract.OutstandingPrincipal, Math.Max(6, monthsLeft)),
        };

        var market = contract.Assets.Sum(a => a.MarketValue);
        var forced = contract.Assets.Sum(a => a.ForcedSaleValue);
        var ltv = market == 0 ? 9.99m : Math.Round(amount / market, 4);
        var requestedOn = asOf.AddDays(-_rng.Next(0, Math.Min(365, monthsElapsed * 30)));
        var request = new RefinancingRequest
        {
            RequestedAt = Utc(requestedOn).AddHours(_rng.Next(8, 18)),
            Reason = reason,
            RequestedAmount = amount,
            RequestedTermMonths = term,
            CurrentRate = contract.InterestRate,
            LtvAtRequest = Math.Min(ltv, 9.99m),
            ForcedSaleCoverAtRequest = amount == 0 ? 0 : Math.Min(9.99m, Math.Round(forced / amount, 4)),
            RiskGradeAtRequest = customer.RiskGrade,
            ExpectedLossAtRequest = CreditRisk.ExpectedLoss(amount, forced, customer.RiskGrade, contract.DaysPastDue),
            Status = RefinancingStatus.Submitted,
        };

        var age = asOf.DayNumber - requestedOn.DayNumber;
        if (contract.Status == ContractStatus.Restructured)
        {
            Decide(request, RefinancingStatus.Approved, contract.InterestRate, "Approved as restructuring: longer term, lower installment.", Math.Min(7, age));
        }
        else if (age < 10)
        {
            request.Status = _rng.NextDouble() < 0.5 ? RefinancingStatus.Submitted : RefinancingStatus.UnderReview;
        }
        else if (_rng.NextDouble() < 0.06)
        {
            Decide(request, RefinancingStatus.Withdrawn, null, "Withdrawn by customer.", _rng.Next(3, Math.Min(20, age)));
        }
        else
        {
            var d = RefinancingPolicy.Decide(reason, request.LtvAtRequest, customer.RiskGrade, contract.DaysPastDue, contract.InterestRate);
            Decide(request, d.Status, d.ProposedRate, d.Note, _rng.Next(2, Math.Min(15, age)));
        }
        contract.RefinancingRequests.Add(request);
    }

    private static void Decide(RefinancingRequest request, RefinancingStatus status, decimal? rate, string note, int daysLater)
    {
        request.Status = status;
        request.ProposedRate = rate;
        request.DecisionNote = note;
        request.DecidedAt = request.RequestedAt.AddDays(daysLater);
    }

    private void AddValuation(Asset asset, DateOnly on, decimal? fixedValue = null, ValuationMethod method = ValuationMethod.IndexBased)
    {
        var age = YearsBetween(new DateOnly(asset.YearOfManufacture, 7, 1), on);
        var market = fixedValue ?? ValuationModel.MarketValue(asset.OriginalCost, asset.AssetClass, age, asset.Condition,
            MarketIndex(asset.AssetClass, on.Year) * Math.Clamp(Normal(1, 0.02), 0.95, 1.05));
        var valuation = new AssetValuation
        {
            ValuedAt = Utc(on),
            MarketValue = market,
            ForcedSaleValue = ValuationModel.ForcedSaleValue(market, asset.AssetClass),
            Method = method,
        };
        asset.Valuations.Add(valuation);
        asset.MarketValue = valuation.MarketValue;
        asset.ForcedSaleValue = valuation.ForcedSaleValue;
        asset.LastValuedAt = valuation.ValuedAt;
    }

    /// <summary>Per class, per year used-equipment price index (random walk around 1.0).</summary>
    private double MarketIndex(AssetClass assetClass, int year)
    {
        if (_marketIndex.TryGetValue((assetClass, year), out var idx)) return idx;
        var previous = year > 2010 ? MarketIndex(assetClass, year - 1) : 1.0;
        idx = Math.Clamp(previous * Normal(1.0, 0.03), 0.85, 1.15);
        return _marketIndex[(assetClass, year)] = idx;
    }

    private AssetCondition PickCondition(double ageYears, bool distressed)
    {
        (AssetCondition, double)[] weights = ageYears < 2
            ? [(AssetCondition.Excellent, 0.5), (AssetCondition.Good, 0.45), (AssetCondition.Fair, 0.05)]
            : [(AssetCondition.Excellent, 0.05), (AssetCondition.Good, 0.5), (AssetCondition.Fair, 0.35), (AssetCondition.Poor, 0.10)];
        if (distressed)
            weights = [(AssetCondition.Good, 0.3), (AssetCondition.Fair, 0.45), (AssetCondition.Poor, 0.25)];
        return Weighted(weights);
    }

    private AssetCatalog.CountryInfo WeightedCountry() => Weighted(AssetCatalog.Countries.Select(c => (c, c.Weight)));

    // ---- random helpers -------------------------------------------------

    private T Pick<T>(IReadOnlyList<T> items) => items[_rng.Next(items.Count)];

    private T Weighted<T>(IEnumerable<(T Item, double Weight)> items)
    {
        var list = items as IList<(T, double)> ?? items.ToList();
        var roll = _rng.NextDouble() * list.Sum(x => x.Item2);
        foreach (var (item, weight) in list)
        {
            if ((roll -= weight) <= 0) return item;
        }
        return list[^1].Item1;
    }

    private double Uniform(double min, double max) => min + _rng.NextDouble() * (max - min);

    private double LogUniform(double min, double max) => Math.Exp(Uniform(Math.Log(min), Math.Log(max)));

    private double Normal(double mean, double stdDev)
    {
        // Box–Muller transform.
        var u1 = 1.0 - _rng.NextDouble();
        var u2 = _rng.NextDouble();
        return mean + stdDev * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }

    private static decimal RoundTo(double value, int step) => Math.Round((decimal)value / step) * step;

    private static string Initials(string name) =>
        string.Concat(name.Split(' ', '-').Where(w => w.Length > 0).Select(w => char.ToUpperInvariant(w[0])));

    private static double YearsBetween(DateOnly from, DateOnly to) => (to.DayNumber - from.DayNumber) / 365.25;

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    private static DateTime Utc(DateOnly d) => d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
}
