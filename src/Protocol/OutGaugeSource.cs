using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BeamQuest.Core;
using Microsoft.Extensions.Logging;

namespace BeamQuest.Protocol
{
    /// <summary>
    /// Reads BeamNG's built-in OutGauge UDP stream (single local vehicle only).
    /// Enable in BeamNG: Options → Other → OutGauge → port 4444.
    ///
    /// Wraps each packet as a single-vehicle WorldFrame so it's compatible with
    /// the same consumer pipeline as multi-vehicle sources.
    /// </summary>
    public sealed class OutGaugeSource : IVehicleDataSource
    {
        private readonly int                  _port;
        private readonly ILogger<OutGaugeSource> _log;
        private UdpClient?                    _udp;
        private bool                          _connected;

        private static readonly int PacketSize = Marshal.SizeOf<OutGaugePacket>();

        public string Name        => $"OutGauge UDP :{_port}";
        public bool   IsConnected => _connected;

        public OutGaugeSource(int port, ILogger<OutGaugeSource> log)
        {
            _port = port;
            _log  = log;
        }

        public Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            try
            {
                _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, _port));
                _connected = true;
                _log.LogInformation("OutGauge listening on 127.0.0.1:{Port}", _port);
                EventBus.Publish(new SourceConnectedEvent(Name));
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to bind OutGauge :{Port}", _port);
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
                    if (result.Buffer.Length < PacketSize) continue;

                    OutGaugePacket pkt;
                    unsafe
                    {
                        fixed (byte* p = result.Buffer)
                            pkt = Marshal.PtrToStructure<OutGaugePacket>((nint)p);
                    }

                    var vs = new VehicleState
                    {
                        VehicleId  = "local",
                        PlayerName = "Local",
                        ModelName  = pkt.Car.TrimEnd('\0'),
                        SpeedMs    = pkt.Speed,
                        RPM        = pkt.RPM,
                        Gear       = pkt.Gear > 1 ? pkt.Gear - 1 : (pkt.Gear == 0 ? -1 : 0),
                        Fuel       = pkt.Fuel,
                        Throttle   = pkt.Throttle,
                        Brake      = pkt.Brake,
                    };

                    frame = new WorldFrame
                    {
                        Timestamp = pkt.Time / 1000.0,
                        Vehicles  = new List<VehicleState> { vs },
                    };
                }
                catch (OperationCanceledException) { break; }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut) { continue; }
                catch (Exception ex) { _log.LogWarning(ex, "OutGauge read error"); continue; }

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
