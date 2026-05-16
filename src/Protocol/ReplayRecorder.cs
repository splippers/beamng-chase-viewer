using System.Text.Json;

namespace BeamQuest.Protocol
{
    /// <summary>
    /// Wraps any IVehicleDataSource and tees frames to a JSON-Lines replay file
    /// (.beamrec) that ReplaySource can play back later.
    /// </summary>
    public sealed class ReplayRecorder : IVehicleDataSource
    {
        private readonly IVehicleDataSource _inner;
        private readonly string             _path;
        private StreamWriter?               _writer;

        public string Name        => $"Rec→{_inner.Name}";
        public bool   IsConnected => _inner.IsConnected;

        public ReplayRecorder(IVehicleDataSource inner, string path)
        {
            _inner = inner;
            _path  = path;
        }

        public async Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            bool ok = await _inner.ConnectAsync(ct);
            if (ok)
                _writer = new StreamWriter(_path, append: false);
            return ok;
        }

        public void Disconnect() => _inner.Disconnect();

        public async IAsyncEnumerable<WorldFrame> ReadFramesAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var frame in _inner.ReadFramesAsync(ct))
            {
                if (_writer != null)
                {
                    await _writer.WriteLineAsync(JsonSerializer.Serialize(frame));
                    await _writer.FlushAsync();
                }
                yield return frame;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_writer != null) await _writer.DisposeAsync();
            await _inner.DisposeAsync();
        }
    }
}
