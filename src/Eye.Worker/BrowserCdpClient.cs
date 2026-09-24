using System.Net.WebSockets;
using System.Text.Json;

namespace StealthEye.Worker;

internal sealed class BrowserCdpClient : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private int _nextId;

    internal static async Task<BrowserCdpClient> ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!uri.IsLoopback)
            throw new InvalidOperationException("CDP WebSocket endpoint must be loopback-bound.");
        var client = new BrowserCdpClient();
        try
        {
            await client._socket.ConnectAsync(uri, cancellationToken);
            return client;
        }
        catch
        {
            client._socket.Dispose();
            throw;
        }
    }

    internal async Task<JsonElement> CallAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var request = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters ?? new { }
        };
        var payload = JsonSerializer.SerializeToUtf8Bytes(request);
        await _socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);

        while (true)
        {
            var message = await ReceiveAsync(cancellationToken);
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var responseId) || responseId.GetInt32() != id)
                continue;
            if (root.TryGetProperty("error", out var error))
            {
                var code = error.TryGetProperty("code", out var codeElement) ? codeElement.GetInt32() : 0;
                var text = error.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : "CDP command failed.";
                throw new InvalidOperationException($"CDP {method} failed ({code}): {text}");
            }
            return root.TryGetProperty("result", out var result) ? result.Clone() : default;
        }
    }

    internal async Task<JsonElement> WaitForEventAsync(
        string method,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(method))
            throw new ArgumentException("CDP event method is required.", nameof(method));

        while (true)
        {
            var message = await ReceiveAsync(cancellationToken);
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (!root.TryGetProperty("method", out var methodElement) ||
                !string.Equals(methodElement.GetString(), method, StringComparison.Ordinal))
                continue;

            return root.TryGetProperty("params", out var parameters)
                ? parameters.Clone()
                : default;
        }
    }
    private async Task<byte[]> ReceiveAsync(CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var result = await _socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new InvalidOperationException("CDP WebSocket closed unexpectedly.");
            memory.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                return memory.ToArray();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try { await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None); }
            catch { }
        }
        _socket.Dispose();
    }
}