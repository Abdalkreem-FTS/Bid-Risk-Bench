using System.Diagnostics;
using System.Text.Json;

namespace BidRisk.Contracts.Tests;

public class GoldenContractTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string GoldenDir = Path.Combine(RepoRoot, "tests", "contract", "golden");

    private static readonly JsonElement Expected = JsonDocument
        .Parse(File.ReadAllText(Path.Combine(GoldenDir, "expected.json")))
        .RootElement;

    private static PlaceBidResponse LoadGolden(string name)
    {
        var path = Path.Combine(GoldenDir, name);
        Assert.True(File.Exists(path), $"missing {path} — run `make golden`");

        return PlaceBidResponse.Parser.ParseFrom(File.ReadAllBytes(path));
    }

    [Fact]
    public void AcceptedGolden_DecodedByCSharp_MatchesValuesPythonWrote()
    {
        var response = LoadGolden("accepted.bin");
        var expected = Expected.GetProperty("accepted");

        Assert.Equal(
            expected.GetProperty("outcome_case").GetString(),
            response.OutcomeCase.ToString().ToLowerInvariant());

        var bid = response.Accepted.Bid;
        var want = expected.GetProperty("bid");
        Assert.Equal(want.GetProperty("id").GetInt64(), bid.Id);
        Assert.Equal(want.GetProperty("lot_id").GetInt64(), bid.LotId);
        Assert.Equal(want.GetProperty("bidder_id").GetInt64(), bid.BidderId);
        Assert.Equal(want.GetProperty("amount").GetDouble(), bid.Amount);
        Assert.Equal(want.GetProperty("placed_at_seconds").GetInt64(), bid.PlacedAt.Seconds);
        Assert.Equal(want.GetProperty("placed_at_nanos").GetInt32(), bid.PlacedAt.Nanos);
        Assert.Equal(want.GetProperty("risk_score").GetDouble(), bid.RiskScore);
        Assert.Equal(want.GetProperty("risk_level").GetString(), ProtoName(bid.RiskLevel));
        Assert.Equal(want.GetProperty("accepted").GetBoolean(), bid.Accepted);
        Assert.Equal(want.GetProperty("reject_reason").GetString(), ProtoName(bid.RejectReason));
    }

    [Fact]
    public void RejectedGolden_DecodedByCSharp_MatchesValuesPythonWrote()
    {
        var response = LoadGolden("rejected.bin");
        var expected = Expected.GetProperty("rejected");

        Assert.Equal(
            expected.GetProperty("outcome_case").GetString(),
            response.OutcomeCase.ToString().ToLowerInvariant());

        Assert.Equal(expected.GetProperty("reason").GetString(), ProtoName(response.Rejected.Reason));
        Assert.Equal(expected.GetProperty("reason_number").GetInt32(), (int)response.Rejected.Reason);

        Assert.Equal(expected.GetProperty("message").GetString(), response.Rejected.Message);
    }

    [Theory]
    [InlineData("accepted.bin")]
    [InlineData("rejected.bin")]
    public void Golden_EitherArmOfTheOneof_CarriesTheRiskAssessment(string golden)
    {
        var response = LoadGolden(golden);
        var risk = response.OutcomeCase == PlaceBidResponse.OutcomeOneofCase.Accepted
            ? response.Accepted.Risk
            : response.Rejected.Risk;

        var want = Expected.GetProperty("risk");
        Assert.Equal(want.GetProperty("score").GetDouble(), risk.Score);
        Assert.Equal(want.GetProperty("level").GetString(), ProtoName(risk.Level));
        Assert.Equal(want.GetProperty("level_number").GetInt32(), (int)risk.Level);
        Assert.Equal(
            want.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()).ToArray(),
            risk.Reasons.ToArray());
        Assert.Equal(want.GetProperty("inference_ms").GetDouble(), risk.InferenceMs);
        Assert.Equal(want.GetProperty("model_version").GetString(), risk.ModelVersion);
    }

    [Fact]
    public void PlaceBidResponse_NoOutcomeSet_HasNeitherOneofArm()
    {
        Assert.Equal(PlaceBidResponse.OutcomeOneofCase.None, new PlaceBidResponse().OutcomeCase);
    }

    [Fact]
    public void GeneratedCode_ComparedWithCurrentProto_ProtoHashMatches()
    {
        var stored = Path.Combine(RepoRoot, "services", "contracts", "Generated", ".proto_hash");
        Assert.True(File.Exists(stored), $"missing {stored} — run `make proto`");

        var current = RunProtoHashScript();

        Assert.True(
            File.ReadAllText(stored).Trim() == current,
            "the .proto has changed since the C# code was generated — run `make proto`");
    }

    private static string RunProtoHashScript()
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "bash",
            ArgumentList = { Path.Combine(RepoRoot, "scripts", "proto-hash.sh") },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("could not run scripts/proto-hash.sh");

        var output = process.StandardOutput.ReadToEnd().Trim();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"scripts/proto-hash.sh failed: {error}");

        return output;
    }

    private static string ProtoName<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        typeof(TEnum)
            .GetField(value.ToString())!
            .GetCustomAttributes(typeof(Google.Protobuf.Reflection.OriginalNameAttribute), false)
            .Cast<Google.Protobuf.Reflection.OriginalNameAttribute>()
            .Single()
            .Name;

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "proto", "bidrisk", "bidrisk.proto")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("could not locate the repository root from the test binaries");
    }
}
