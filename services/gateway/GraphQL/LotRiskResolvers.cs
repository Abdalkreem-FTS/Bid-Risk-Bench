using GreenDonut;

namespace BidRisk.Gateway.GraphQL;

// Two ways to get a lot's risk level: one call per lot, or one call for the whole batch.
// This is the N+1 before and after.
public interface ILotRiskResolver
{
    Task<RiskLevel> ResolveAsync(long lotId, CancellationToken cancellationToken);
}

public sealed class NaiveLotRiskResolver(
    RiskService.RiskServiceClient client,
    GrpcCallCounter counter) : ILotRiskResolver
{
    public async Task<RiskLevel> ResolveAsync(long lotId, CancellationToken cancellationToken)
    {
        counter.Record("AssessLotRisk");

        var response = await client.AssessLotRiskAsync(
            new AssessLotRiskRequest { LotId = lotId },
            cancellationToken: cancellationToken);

        return response.Risk.Level.ToGraphQL();
    }
}

public sealed class BatchedLotRiskResolver(LotRiskDataLoader loader) : ILotRiskResolver
{
    public async Task<RiskLevel> ResolveAsync(long lotId, CancellationToken cancellationToken)
    {
        // LoadAsync does not call the server. It queues the id and waits for the batch below.
        var risk = await loader.LoadAsync(lotId, cancellationToken);

        // A lot missing from the batch comes back null, so show Unknown instead of failing.
        return risk?.Level.ToGraphQL() ?? RiskLevel.Unknown;
    }
}

public sealed class LotRiskDataLoader(
    RiskService.RiskServiceClient client,
    GrpcCallCounter counter,
    IBatchScheduler batchScheduler,
    DataLoaderOptions options) : BatchDataLoader<long, LotRisk>(batchScheduler, options)
{
    // GreenDonut calls this once per batch, with every id the resolvers asked for.
    protected override async Task<IReadOnlyDictionary<long, LotRisk>> LoadBatchAsync(
        IReadOnlyList<long> keys,
        CancellationToken cancellationToken)
    {
        counter.Record("AssessLotRiskBatch");

        var request = new AssessLotRiskBatchRequest();
        request.LotIds.AddRange(keys);

        var response = await client.AssessLotRiskBatchAsync(request, cancellationToken: cancellationToken);

        return response.Risks.ToDictionary(risk => risk.LotId);
    }
}
