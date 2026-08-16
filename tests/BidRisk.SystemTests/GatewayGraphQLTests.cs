using System.Text.Json.Nodes;

namespace BidRisk.SystemTests;

[Trait("Category", "Integration")]
public sealed class GatewayGraphQLTests : IDisposable
{
    private const long LotOrdinary = 1;
    private const long LotClosed = 40;
    private const long Bidder = 5;

    private const long YoungBidder = 12;

    private static readonly string[] ScoredLevels = ["LOW", "MEDIUM", "HIGH"];
    private static readonly string[] AnyLevel = ["LOW", "MEDIUM", "HIGH", "UNKNOWN"];

    private readonly GraphQLClient _gateway = GraphQLClient.Create();

    public void Dispose() => _gateway.Dispose();

    private async Task<JsonNode> GetLotAsync(long lotId)
    {
        var data = await _gateway.QueryAsync(
            "query($id: Long!) { lot(id: $id) { id currentPrice bidCount closed riskLevel } }",
            new { id = lotId });

        return data["lot"]!;
    }

    private async Task<JsonNode> PlaceBidAsync(long lotId, double amount, long bidder = Bidder)
    {
        var data = await _gateway.QueryAsync(
            """
            mutation($lot: Long!, $bidder: Long!, $amount: Float!) {
              placeBid(lotId: $lot, bidderId: $bidder, amount: $amount) {
                accepted rejectReason message riskScore riskLevel reasons bidId } }
            """,
            new { lot = lotId, bidder, amount });

        return data["placeBid"]!;
    }

    [Fact]
    public async Task PlaceBidMutation_ValidBid_IsScoredAndReadableThroughADifferentQuery()
    {
        var before = await GetLotAsync(LotOrdinary);
        var amount = Math.Round(before["currentPrice"]!.GetValue<double>() * 1.05, 2);

        var result = await PlaceBidAsync(LotOrdinary, amount);

        Assert.True(result["accepted"]!.GetValue<bool>(), result.ToJsonString());
        Assert.InRange(result["riskScore"]!.GetValue<double>(), 0.0, 1.0);
        Assert.Contains(result["riskLevel"]!.GetValue<string>(), ScoredLevels);
        Assert.NotEmpty(result["reasons"]!.AsArray());
        Assert.NotNull(result["bidId"]);

        var after = await GetLotAsync(LotOrdinary);
        Assert.Equal(amount, after["currentPrice"]!.GetValue<double>(), 2);
        Assert.Equal(
            before["bidCount"]!.GetValue<int>() + 1,
            after["bidCount"]!.GetValue<int>());

        var bids = (await _gateway.QueryAsync(
            "query($lot: Long!) { bids(lotId: $lot, limit: 1) { id amount accepted riskScore } }",
            new { lot = LotOrdinary }))["bids"]!.AsArray();

        Assert.Equal(result["bidId"]!.GetValue<long>(), bids[0]!["id"]!.GetValue<long>());
        Assert.True(bids[0]!["accepted"]!.GetValue<bool>());
        Assert.Equal(amount, bids[0]!["amount"]!.GetValue<double>(), 2);
    }

    [Fact]
    public async Task PlaceBidMutation_SuspiciousBidFromYoungAccount_RejectsAsHighRiskAndLeavesPriceUnchanged()
    {
        var before = await GetLotAsync(LotOrdinary);
        var price = before["currentPrice"]!.GetValue<double>();

        var result = await PlaceBidAsync(LotOrdinary, Math.Round(price * 9, 2), YoungBidder);

        Assert.False(result["accepted"]!.GetValue<bool>());
        Assert.Equal("HIGH_RISK", result["rejectReason"]!.GetValue<string>());
        Assert.True(result["riskScore"]!.GetValue<double>() >= 0.70);
        Assert.True(result["reasons"]!.AsArray().Count > 0, "a refusal must be explainable");

        var after = await GetLotAsync(LotOrdinary);
        Assert.Equal(price, after["currentPrice"]!.GetValue<double>(), 2);
    }

    [Fact]
    public async Task PlaceBidMutation_AmountBelowCurrentPrice_RejectsAsBelowCurrentPrice()
    {
        var lot = await GetLotAsync(LotOrdinary);
        var result = await PlaceBidAsync(
            LotOrdinary, Math.Round(lot["currentPrice"]!.GetValue<double>() * 0.5, 2));

        Assert.False(result["accepted"]!.GetValue<bool>());
        Assert.Equal("BELOW_CURRENT_PRICE", result["rejectReason"]!.GetValue<string>());
    }

    [Fact]
    public async Task PlaceBidMutation_LotIsClosed_RejectsAsLotClosed()
    {
        var lot = await GetLotAsync(LotClosed);
        Assert.True(lot["closed"]!.GetValue<bool>());

        var result = await PlaceBidAsync(
            LotClosed, lot["currentPrice"]!.GetValue<double>() * 2);

        Assert.False(result["accepted"]!.GetValue<bool>());
        Assert.Equal("LOT_CLOSED", result["rejectReason"]!.GetValue<string>());
    }

    [Fact]
    public async Task LotQuery_LotDoesNotExist_ReturnsNullRatherThanError()
    {
        var data = await _gateway.QueryAsync("{ lot(id: 999999) { id } }");
        Assert.Null(data["lot"]);
    }

    [Fact]
    public async Task LotsQuery_TwentyLotsRequested_EveryLotCarriesRiskLevelFromMlService()
    {
        var lots = (await _gateway.QueryAsync("{ lots(first: 20) { id riskLevel } }"))["lots"]!
            .AsArray();

        Assert.Equal(20, lots.Count);
        Assert.All(lots, lot => Assert.Contains(
            lot!["riskLevel"]!.GetValue<string>(), AnyLevel));
    }
}
