using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BeamQuest.Core;
using Microsoft.Extensions.Logging;

namespace BeamQuest.Protocol
{
    /// <summary>
    /// Receives WorldFrame JSON datagrams from the BeamNG Lua mod (BeamQuestBridge.lua).
    /// The mod broadcasts one UDP packet per update tick containing all vehicle states.
    ///
    /// Default port 37420 — must match BeamQuestBridge.lua config.
    /// </summary>
    public sealed class BeamNGUDPSource : IVehicleDataSource
    {
        private readonly int                    _port;
        private readonly ILogger<BeamNGUDPSource> _log;
        private UdpClient?                      _udp;
        private bool                            _connected;

        public string Name         => $"BeamNG UDP :{_port}";
        public bool   IsConnected  => _connected;

        public BeamNGUDPSource(int port, ILogger<BeamNGUDPSource> log)
        {
            _port = port;
            _log  = log;
        }

        public Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            try
            {
                _udp = new UdpClient(new IPEndPoint(IPAddress.Any, _port));
                _udp.Client.ReceiveTimeout = 5000;
                _connected = true;
                _log.LogInformation("UDP source listening on :{Port}", _port);
                EventBus.Publish(new SourceConnectedEvent(Name));
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to bind UDP :{Port}", _port);
                return Task.FromResult(false);
            }
        }

        public void Disconnect()
        {
            _connected = false;
            _udp?.Close();
        }

        public async IAsyncEnumerable<WorldFrame> ReadFramesAsync(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            if (_udp == null) yield break;

            while (!ct.IsCancellationRequested && _connected)
            {
                WorldFrame? frame = null;
                try
                {
                    var result = await _udp.ReceiveAsync(ct);
                    frame = JsonSerializer.Deserialize<WorldFrame>(result.Buffer);
                }
                catch (OperationCanceledException) { break; }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut) { continue; }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "UDP receive error");
                    continue;
                }

                if (frame != null) yield return frame;
            }

            _connected = false;
            EventBus.Publish(new SourceDisconnectedEvent(Name, "stream ended"));
        }

        public ValueTask DisposeAsync()
        {
            Disconnect();
            return ValueTask.CompletedTask;
        }
    }
}
