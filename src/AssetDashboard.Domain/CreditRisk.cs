namespace AssetDashboard.Domain;

/// <summary>
/// Simplified expected-loss model: EL = PD × LGD × EAD.
/// PD comes from the customer's risk grade, raised for arrears; LGD from how much of the exposure
/// a forced sale of the collateral (net of remarketing costs) would not cover.
/// </summary>
public static class CreditRisk
{
    /// <summary>Remarketing costs as a share of forced sale proceeds.</summary>
    public const decimal RecoveryCostRate = 0.06m;

    /// <summary>One-year probability of default per risk grade 1 (best) to 10 (worst).</summary>
    public static readonly decimal[] GradePd = [0.003m, 0.005m, 0.008m, 0.012m, 0.020m, 0.032m, 0.050m, 0.080m, 0.120m, 0.200m];

    public static decimal ProbabilityOfDefault(int riskGrade, int daysPastDue)
    {
        if (daysPastDue >= 90) return 1m;
        var pd = GradePd[Math.Clamp(riskGrade, 1, 10) - 1];
        var uplift = daysPastDue >= 60 ? 5m : daysPastDue >= 30 ? 3m : daysPastDue > 0 ? 1.5m : 1m;
        return Math.Min(1m, pd * uplift);
    }

    public static decimal LossGivenDefault(decimal exposure, decimal forcedSaleValue)
    {
        if (exposure <= 0) return 0;
        var netRecovery = forcedSaleValue * (1 - RecoveryCostRate);
        return Math.Round(Math.Clamp((exposure - netRecovery) / exposure, 0, 1), 4);
    }

    public static decimal ExpectedLoss(decimal exposure, decimal forcedSaleValue, int riskGrade, int daysPastDue) =>
        Math.Round(ProbabilityOfDefault(riskGrade, daysPastDue) * LossGivenDefault(exposure, forcedSaleValue) * exposure, 2);
}

/// <summary>Credit policy applied to refinancing requests.</summary>
public static class RefinancingPolicy
{
    public sealed record Decision(RefinancingStatus Status, decimal? ProposedRate, string Note);

    public static Decision Decide(RefinancingReason reason, decimal requestedLtv, int riskGrade, int daysPastDue, decimal currentRate)
    {
        if (daysPastDue >= 90)
            return new(RefinancingStatus.Declined, null, "Contract in default; handled by recovery, not refinancing.");
        if (requestedLtv > 1.10m)
            return new(RefinancingStatus.Declined, null, $"Requested LTV {requestedLtv:P0} above the 110% limit.");
        if (riskGrade >= 9)
            return new(RefinancingStatus.Declined, null, $"Risk grade {riskGrade} outside appetite.");
        if (reason == RefinancingReason.RateReduction && riskGrade > 4)
            return new(RefinancingStatus.Declined, null, "Rate reduction only for risk grades 1–4.");

        // Price for risk: add-on grows with LTV, grade and arrears; rate reductions get a discount.
        var addOn = 0.25m * Math.Max(0, riskGrade - 3) / 2
                    + (requestedLtv > 0.85m ? 0.75m : 0)
                    + (daysPastDue >= 30 ? 1.00m : 0)
                    - (reason == RefinancingReason.RateReduction ? 0.75m : 0);
        var rate = Math.Round(Math.Max(1.5m, currentRate + addOn), 3);
        var note = reason == RefinancingReason.CashFlowStress
            ? "Approved as restructuring: longer term, lower installment."
            : requestedLtv > 0.85m ? "Approved with risk add-on for LTV above 85%." : "Approved within policy.";
        return new(RefinancingStatus.Approved, rate, note);
    }
}
