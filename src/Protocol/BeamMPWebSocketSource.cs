using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using BeamQuest.Core;
using Microsoft.Extensions.Logging;

namespace BeamQuest.Protocol
{
    /// <summary>
    /// Connects to the BeamQuestBridge.lua server plugin via WebSocket.
    /// The plugin runs on the BeamMP server and broadcasts a WorldFrame JSON
    /// message at ~20 Hz.
    ///
    /// URL: ws://&lt;host&gt;:&lt;port&gt;/beamquest
    /// </summary>
    public sealed class BeamMPWebSocketSource : IVehicleDataSource
    {
        private readonly Uri                          _uri;
        private readonly ILogger<BeamMPWebSocketSource> _log;
        private ClientWebSocket?                      _ws;
        private bool                                  _connected;

        public string Name        => $"BeamMP WS {_uri.Host}:{_uri.Port}";
        public bool   IsConnected => _connected;

        public BeamMPWebSocketSource(string host, int port, ILogger<BeamMPWebSocketSource> log)
        {
            _uri = new Uri($"ws://{host}:{port}/beamquest");
            _log = log;
        }

        public async Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            _ws = new ClientWebSocket();
            try
            {
                await _ws.ConnectAsync(_uri, ct);
                _connected = true;
                _log.LogInformation("Connected to BeamMP plugin at {Uri}", _uri);
                EventBus.Publish(new SourceConnectedEvent(Name));
                return true;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "WebSocket connect failed: {Uri}", _uri);
                return false;
            }
        }

        public void Disconnect()
        {
            _connected = false;
            try { _ws?.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).GetAwaiter().GetResult(); }
            catch { /* best effort */ }
        }

        public async IAsyncEnumerable<WorldFrame> ReadFramesAsync(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            if (_ws == null) yield break;

            var buf = new byte[65536];
            var sb  = new StringBuilder();

            while (!ct.IsCancellationRequested && _connected &&
                   _ws.State == WebSocketState.Open)
            {
                WorldFrame? frame = null;
                try
                {
                    sb.Clear();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _ws.ReceiveAsync(buf, ct);
                        if (result.MessageType == WebSocketMessageType.Close) goto done;
                        sb.Append(Encoding.UTF8.GetString(buf, 0, result.Count));
                    }
                    while (!result.EndOfMessage);

                    frame = JsonSerializer.Deserialize<WorldFrame>(sb.ToString());
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "WebSocket read error");
                    break;
                }

                if (frame != null) yield return frame;
            }

            done:
            _connected = false;
            EventBus.Publish(new SourceDisconnectedEvent(Name, "connection closed"));
        }

        public async ValueTask DisposeAsync()
        {
            _connected = false;
            if (_ws != null)
            {
                try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
                catch { /* ignore */ }
                _ws.Dispose();
            }
        }
    }
}
