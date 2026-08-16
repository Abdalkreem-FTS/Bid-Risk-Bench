using BidRisk.Auction.Domain;
using BidRisk.Contracts.Results;

namespace BidRisk.Auction.Tests;

public class BidRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private static LotSnapshot Lot(
        decimal reserve = 100m,
        decimal current = 120m,
        bool closed = false,
        int closesInHours = 24) =>
        new(reserve, current, closed, Now.AddHours(closesInHours));

    private static void AssertRefused(Error expected, Result<Success> actual)
    {
        Assert.True(actual.IsError);
        Assert.Equal(expected.Code, actual.TopError.Code);
        Assert.Equal(ErrorType.Conflict, actual.TopError.Type);
    }

    [Theory]
    [InlineData(0.001)]
    [InlineData(0.004)]
    [InlineData(1e11)]
    [InlineData(1e30)]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ParseAmount_AmountTheDatabaseCouldNotStore_ReturnsInvalidAmountError(double amount)
    {
        var parsed = BidRules.ParseAmount(amount);

        Assert.True(parsed.IsError);
        Assert.Equal(BidErrors.InvalidAmount.Code, parsed.TopError.Code);
        Assert.Equal(ErrorType.Validation, parsed.TopError.Type);
    }

    [Theory]
    [InlineData(0.005, 0.01)]
    [InlineData(130.0, 130.00)]
    [InlineData(130.005, 130.01)]
    [InlineData(9_999_999_999.99, 9_999_999_999.99)]
    public void ParseAmount_StorableAmount_RoundsToTwoDecimalPlaces(double amount, decimal expected)
    {
        var parsed = BidRules.ParseAmount(amount);

        Assert.True(parsed.IsSuccess);
        Assert.Equal(expected, parsed.Value);
    }

    [Fact]
    public void Validate_BidBeatsPriceAndClearsReserve_ReturnsSuccess()
    {
        Assert.True(BidRules.Validate(Lot(), 130m, Now).IsSuccess);
    }

    [Fact]
    public void Validate_LotIsClosed_ReturnsLotClosedError()
    {
        AssertRefused(BidErrors.LotClosed, BidRules.Validate(Lot(closed: true), 130m, Now));
    }

    [Fact]
    public void Validate_ClosingTimePassedButClosedFlagIsFalse_ReturnsLotClosedError()
    {
        AssertRefused(BidErrors.LotClosed, BidRules.Validate(Lot(closesInHours: -1), 130m, Now));
    }

    [Fact]
    public void Validate_BidArrivesExactlyAtClosingTime_ReturnsLotClosedError()
    {
        var lot = new LotSnapshot(100m, 120m, false, Now);
        AssertRefused(BidErrors.LotClosed, BidRules.Validate(lot, 130m, Now));
    }

    [Fact]
    public void Validate_BidBelowReserve_ReturnsBelowReserveError()
    {
        AssertRefused(
            BidErrors.BelowReserve,
            BidRules.Validate(Lot(reserve: 100m, current: 50m), 99.99m, Now));
    }

    [Fact]
    public void Validate_BidExactlyAtReserve_ReturnsSuccess()
    {
        Assert.True(BidRules.Validate(Lot(reserve: 100m, current: 50m), 100m, Now).IsSuccess);
    }

    [Fact]
    public void Validate_BidBelowCurrentPrice_ReturnsBelowCurrentPriceError()
    {
        AssertRefused(
            BidErrors.BelowCurrentPrice,
            BidRules.Validate(Lot(current: 120m), 119.99m, Now));
    }

    [Fact]
    public void Validate_BidEqualsCurrentPrice_ReturnsBelowCurrentPriceError()
    {
        AssertRefused(
            BidErrors.BelowCurrentPrice,
            BidRules.Validate(Lot(current: 120m), 120m, Now));
    }

    [Fact]
    public void Validate_BidExceedsCurrentPriceByOneCent_ReturnsSuccess()
    {
        Assert.True(BidRules.Validate(Lot(current: 120m), 120.01m, Now).IsSuccess);
    }

    [Fact]
    public void Validate_LotClosedAndAmountAlsoInvalid_ReturnsLotClosedError()
    {
        AssertRefused(BidErrors.LotClosed, BidRules.Validate(Lot(closed: true), 1m, Now));
    }

    [Fact]
    public void Validate_BidBelowBothReserveAndCurrentPrice_ReturnsBelowReserveError()
    {
        AssertRefused(
            BidErrors.BelowReserve,
            BidRules.Validate(Lot(reserve: 100m, current: 80m), 70m, Now));
    }

    [Fact]
    public void Validate_LotIsClosed_ErrorCarriesBidderFacingDescription()
    {
        var refused = BidRules.Validate(Lot(closed: true), 130m, Now);
        Assert.Equal("this lot has closed", refused.TopError.Description);
    }

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(0.69, false)]
    [InlineData(0.6999999, false)]
    [InlineData(0.70, true)]
    [InlineData(0.99, true)]
    [InlineData(1.0, true)]
    public void IsTooRisky_ScoreComparedWithThreshold_ReturnsTrueOnlyAtOrAboveIt(double score, bool expected)
    {
        Assert.Equal(expected, BidRules.IsTooRisky(score, threshold: 0.70));
    }
}
