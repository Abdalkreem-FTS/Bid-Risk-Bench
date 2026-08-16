namespace BidRisk.SystemTests;

[Trait("Category", "Smoke")]
public sealed class GatewaySmokeTests : IDisposable
{
    private static readonly string[] ScoredLevels = ["LOW", "MEDIUM", "HIGH"];

    private readonly GraphQLClient _gateway = GraphQLClient.Create();

    public void Dispose() => _gateway.Dispose();

    [Fact]
    public async Task LotQuery_ExistingLot_ReturnsRiskLevelStitchedFromMlService()
    {
        var lot = (await _gateway.QueryAsync(
            "{ lot(id: 1) { id title currentPrice bidCount closed riskLevel } }"))["lot"]!;

        Assert.Equal(1, lot["id"]!.GetValue<long>());
        Assert.Contains(lot["riskLevel"]!.GetValue<string>(), ScoredLevels);
    }

    [Fact]
    public async Task LotsQuery_TwentyLotsRequested_AllCarryARiskLevel()
    {
        var lots = (await _gateway.QueryAsync("{ lots(first: 20) { id riskLevel } }"))["lots"]!
            .AsArray();

        Assert.Equal(20, lots.Count);
        Assert.All(lots, lot => Assert.False(string.IsNullOrEmpty(lot!["riskLevel"]?.GetValue<string>())));
    }

    [Fact]
    public async Task PlaceBidMutation_AbsurdAmount_RefusedAsHighRiskWithReasons()
    {
        var result = (await _gateway.QueryAsync(
            """
            mutation { placeBid(lotId: 1, bidderId: 12, amount: 99999) {
              accepted rejectReason riskScore riskLevel reasons } }
            """))["placeBid"]!;

        Assert.False(result["accepted"]!.GetValue<bool>(), "an absurd bid was accepted");
        Assert.Equal("HIGH_RISK", result["rejectReason"]!.GetValue<string>());
        Assert.NotEmpty(result["reasons"]!.AsArray());
    }
}
