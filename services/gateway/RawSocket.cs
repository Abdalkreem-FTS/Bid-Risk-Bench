using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BidRisk.Gateway.Http;

using Error = BidRisk.Contracts.Results.Error;

namespace BidRisk.Gateway;

// Shared by both raw sockets, so the baseline the report measures cannot drift between them.
internal static class RawSocket
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly Error NotAWebSocketRequest = Error.Validation("Socket.UpgradeRequired", "this endpoint speaks WebSocket only");

    public static async Task<WebSocket?> AcceptAsync(HttpContext context)
    {
        if (context.WebSockets.IsWebSocketRequest)
        {
            return await context.WebSockets.AcceptWebSocketAsync();
        }

        await NotAWebSocketRequest.ToProblem().ExecuteAsync(context);

        return null;

    }

    public static Task SendAsync<T>(WebSocket socket, T frame, CancellationToken cancellationToken) =>
        socket.SendAsync(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(frame, Json)),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);
}
