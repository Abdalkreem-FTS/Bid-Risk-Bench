using BidRisk.Contracts.Results;

namespace BidRisk.Auction.Domain;

public readonly record struct LotSnapshot(
    decimal ReservePrice,
    decimal CurrentPrice,
    bool Closed,
    DateTimeOffset ClosesAt);

public static class BidRules
{

    // The amount column is numeric(12, 2), so this is the biggest bid it can hold.
    public const decimal MaxAmount = 9_999_999_999.99m;

    public static Result<decimal> ParseAmount(double amount)
    {
        if (double.IsNaN(amount) || double.IsInfinity(amount)
            || amount <= 0 || amount > (double)MaxAmount)
        {
            return BidErrors.InvalidAmount;
        }

        var money = Math.Round((decimal)amount, 2, MidpointRounding.AwayFromZero);

        // Checked after rounding: 0.001 is positive but stores as 0.00, which the column refuses.
        return money > 0m && money <= MaxAmount ? money : BidErrors.InvalidAmount;
    }

    public static Result<Success> Validate(LotSnapshot lot, decimal amount, DateTimeOffset now)
    {
        if (lot.Closed || now >= lot.ClosesAt)
        {
            return BidErrors.LotClosed;
        }

        if (amount < lot.ReservePrice)
        {
            return BidErrors.BelowReserve;
        }

        if (amount <= lot.CurrentPrice)
        {
            return BidErrors.BelowCurrentPrice;
        }

        return Result.Success;
    }

    public static bool IsTooRisky(double score, double threshold) => score >= threshold;
}
