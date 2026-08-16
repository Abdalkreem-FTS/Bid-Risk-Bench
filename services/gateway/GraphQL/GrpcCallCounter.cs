using System.Collections.Concurrent;

namespace BidRisk.Gateway.GraphQL;

public sealed class GrpcCallCounter
{
    private readonly ConcurrentDictionary<string, long> _calls = new();

    public void Record(string method) => _calls.AddOrUpdate(method, 1, (_, existing) => existing + 1);

    public IReadOnlyDictionary<string, long> Snapshot()
    {
        var snapshot = _calls.ToDictionary(entry => entry.Key, entry => entry.Value);
        snapshot["total"] = snapshot.Values.Sum();

        return snapshot;
    }

    public void Reset() => _calls.Clear();
}
