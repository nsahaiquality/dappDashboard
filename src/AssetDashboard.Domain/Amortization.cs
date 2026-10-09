namespace AssetDashboard.Domain;

/// <summary>Annuity maths for leases and loans with an optional balloon / residual value.</summary>
public static class Amortization
{
    /// <summary>Fixed monthly installment that amortises <paramref name="principal"/> down to <paramref name="residual"/>.</summary>
    public static decimal Installment(decimal principal, decimal annualRatePct, int termMonths, decimal residual = 0)
    {
        if (termMonths <= 0) throw new ArgumentOutOfRangeException(nameof(termMonths));
        var r = (double)annualRatePct / 100 / 12;
        if (r == 0) return Math.Round((principal - residual) / termMonths, 2);

        var growth = Math.Pow(1 + r, termMonths);
        var pvResidual = (double)residual / growth;
        var pmt = ((double)principal - pvResidual) * r / (1 - 1 / growth);
        return Math.Round((decimal)pmt, 2);
    }

    /// <summary>Remaining balance after <paramref name="monthsPaid"/> installments.</summary>
    public static decimal OutstandingAfter(decimal principal, decimal annualRatePct, decimal installment, int monthsPaid)
    {
        if (monthsPaid <= 0) return principal;
        var r = (double)annualRatePct / 100 / 12;
        if (r == 0) return Math.Max(0, principal - installment * monthsPaid);

        var growth = Math.Pow(1 + r, monthsPaid);
        var balance = (double)principal * growth - (double)installment * (growth - 1) / r;
        return Math.Round((decimal)Math.Max(0, balance), 2);
    }

    /// <summary>Interest part of the next installment.</summary>
    public static decimal InterestPortion(decimal outstanding, decimal annualRatePct) =>
        Math.Round(outstanding * annualRatePct / 100 / 12, 2);
}
