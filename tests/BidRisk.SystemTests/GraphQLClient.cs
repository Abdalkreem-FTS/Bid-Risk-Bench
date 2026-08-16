using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BidRisk.SystemTests;

internal sealed class GraphQLClient(HttpClient http) : IDisposable
{
    public static GraphQLClient Create() => new(new HttpClient
    {
        BaseAddress = new Uri(Endpoints.GatewayHttp),
        Timeout = TimeSpan.FromSeconds(60),
    });

    public void Dispose() => http.Dispose();

    public async Task<JsonNode> QueryAsync(string query, object? variables = null)
    {
        var response = await http.PostAsJsonAsync("/graphql", new
        {
            query,
            variables = variables ?? new { },
        });

        response.EnsureSuccessStatusCode();

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())
            ?? throw new JsonException("the gateway returned an empty body");

        Assert.True(body["errors"] is null, $"GraphQL returned errors: {body["errors"]}");

        return body["data"] ?? throw new JsonException("the response carried no data");
    }
}
