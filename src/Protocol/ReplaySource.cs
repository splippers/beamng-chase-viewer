using System.Runtime.CompilerServices;
using System.Text.Json;
using BeamQuest.Core;
using Microsoft.Extensions.Logging;

namespace BeamQuest.Protocol
{
    /// <summary>
    /// Plays back a .beamrec (JSON-Lines) recording written by ReplayRecorder.
    /// Supports speed control and seeking via <see cref="SeekAsync"/>.
    /// </summary>
    public sealed class ReplaySource : IVehicleDataSource
    {
        private readonly string               _path;
        private readonly ILogger<ReplaySource> _log;

        private List<WorldFrame>? _frames;
        private int               _cursor;
        private bool              _playing;
        private float             _speed = 1f;

        public string Name        => $"Replay {System.IO.Path.GetFileName(_path)}";
        public bool   IsConnected => _playing;

        public float Speed
        {
            get => _speed;
            set => _speed = Math.Clamp(value, 0.1f, 8f);
        }

        public float TotalSeconds  => _frames != null && _frames.Count > 1
            ? (float)(_frames[^1].Timestamp - _frames[0].Timestamp)
            : 0f;

        public float CurrentSeconds => _frames != null && _cursor > 0
            ? (float)(_frames[Math.Min(_cursor, _frames.Count - 1)].Timestamp - _frames[0].Timestamp)
            : 0f;

        public ReplaySource(string path, ILogger<ReplaySource> log)
        {
            _path = path;
            _log  = log;
        }

        public async Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            try
            {
                _frames = new List<WorldFrame>();
                await foreach (var line in System.IO.File.ReadLinesAsync(_path, ct))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var frame = JsonSerializer.Deserialize<WorldFrame>(line);
                    if (frame != null) _frames.Add(frame);
                }

                if (_frames.Count == 0)
                {
                    _log.LogWarning("Replay file is empty: {Path}", _path);
                    return false;
                }

                _cursor  = 0;
                _playing = true;
                _log.LogInformation("Loaded {Count} frames ({Dur:F1}s) from {Path}",
                    _frames.Count, TotalSeconds, _path);
                EventBus.Publish(new ReplayStartedEvent(_path, TotalSeconds));
                EventBus.Publish(new SourceConnectedEvent(Name));
                return true;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to load replay: {Path}", _path);
                return false;
            }
        }

        public void Disconnect() => _playing = false;

        /// <summary>Seek to a timestamp offset in seconds from the start.</summary>
        public void Seek(float seconds)
        {
            if (_frames == null) return;
            double target = _frames[0].Timestamp + seconds;
            _cursor = _frames.FindIndex(f => f.Timestamp >= target);
            if (_cursor < 0) _cursor = _frames.Count - 1;
            EventBus.Publish(new ReplayPositionChangedEvent(CurrentSeconds));
        }

        public async IAsyncEnumerable<WorldFrame> ReadFramesAsync(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            if (_frames == null || _frames.Count == 0) yield break;

            var startReal  = DateTime.UtcNow;
            var startSim   = _frames[_cursor].Timestamp;

            while (!ct.IsCancellationRequested && _playing && _cursor < _frames.Count)
            {
                var frame = _frames[_cursor];

                // Wall-clock pacing
                double simElapsed  = (frame.Timestamp - startSim) / _speed;
                double realElapsed = (DateTime.UtcNow - startReal).TotalSeconds;
                double waitMs      = (simElapsed - realElapsed) * 1000.0;
                if (waitMs > 1) await Task.Delay(TimeSpan.FromMilliseconds(waitMs), ct);

                yield return frame;
                EventBus.Publish(new ReplayPositionChangedEvent(CurrentSeconds));

                _cursor++;
            }

            if (_cursor >= (_frames?.Count ?? 0))
                EventBus.Publish(new ReplayEndedEvent());

            _playing = false;
        }

        public ValueTask DisposeAsync()
        {
            _playing = false;
            return ValueTask.CompletedTask;
        }
    }
}
