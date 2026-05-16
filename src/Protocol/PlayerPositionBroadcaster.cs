using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace BeamQuest.Protocol
{
    /// <summary>
    /// Sends the player's world position back to BeamNG every frame so the
    /// AI driver can target it.
    ///
    /// Packet format: JSON  { "px": x, "py": y, "pz": z, "t": timestamp }
    /// Destination: BeamNG Lua mod listening on UDP port 37421 (loopback or LAN).
    ///
    /// Send rate is capped to avoid flooding — 20 Hz is plenty for AI targeting.
    /// </summary>
    public sealed class PlayerPositionBroadcaster : IAsyncDisposable
    {
        private readonly IPEndPoint               _endpoint;
        private readonly ILogger<PlayerPositionBroadcaster> _log;
        private readonly UdpClient                _udp = new();
        private readonly float                    _intervalSeconds;

        private float _accumulator;
        private bool  _disposed;

        public PlayerPositionBroadcaster(
            string host, int port,
            ILogger<PlayerPositionBroadcaster> log,
            float sendHz = 20f)
        {
            _log             = log;
            _endpoint        = new IPEndPoint(IPAddress.Parse(host), port);
            _intervalSeconds = 1f / sendHz;
        }

        public void Update(float dt, Vector3 playerPos)
        {
            if (_disposed) return;

            _accumulator += dt;
            if (_accumulator < _intervalSeconds) return;
            _accumulator -= _intervalSeconds;

            var payload = JsonSerializer.Serialize(new
            {
                px = playerPos.X,
                py = playerPos.Y,
                pz = playerPos.Z,
                t  = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });

            var bytes = Encoding.UTF8.GetBytes(payload);
            try { _udp.Send(bytes, bytes.Length, _endpoint); }
            catch (Exception ex) { _log.LogWarning(ex, "Position broadcast failed"); }
        }

        public ValueTask DisposeAsync()
        {
            _disposed = true;
            _udp.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
