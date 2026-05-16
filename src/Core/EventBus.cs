using System.Collections.Concurrent;

namespace BeamQuest.Core
{
    // ── Domain events ────────────────────────────────────────────────────────

    public readonly record struct SourceConnectedEvent(string SourceName);
    public readonly record struct SourceDisconnectedEvent(string SourceName, string Reason);

    public readonly record struct VehicleSpawnedEvent(string VehicleId, string PlayerName);
    public readonly record struct VehicleRemovedEvent(string VehicleId);
    public readonly record struct VehicleResetEvent(string VehicleId, Vector3 Position);

    public readonly record struct TerrainLoadedEvent(string MapName, int GridSize);
    public readonly record struct TeleportEvent(Vector3 Position);

    // Replay events
    public readonly record struct ReplayStartedEvent(string FilePath, float TotalSeconds);
    public readonly record struct ReplayPositionChangedEvent(float CurrentSeconds);
    public readonly record struct ReplayEndedEvent;

    // UI
    public readonly record struct ViewerModeChangedEvent(string Mode);
    public readonly record struct NotificationEvent(string Message, float Duration);
    public readonly record struct FollowTargetChangedEvent(string? VehicleId);

    // Chase mode — threat proximity
    public enum ThreatZone { Safe, Tense, Danger, Critical, Impact }
    public readonly record struct ThreatZoneChangedEvent(ThreatZone Zone, float Distance);
    public readonly record struct ThreatImpactEvent(string VehicleId, float SpeedMs);
    public readonly record struct PlayerCaughtEvent(float SurvivalSeconds);
    public readonly record struct ChaseStartedEvent;
    public readonly record struct ChaseRestartedEvent;
    public readonly record struct ChaseEscapedEvent(float SurvivalSeconds);

    // ── Bus ───────────────────────────────────────────────────────────────────

    public static class EventBus
    {
        private static readonly ConcurrentQueue<Action> _pending = new();
        private static readonly Dictionary<Type, List<Delegate>> _handlers = new();

        public static void Subscribe<T>(Action<T> h)
        {
            lock (_handlers)
            {
                if (!_handlers.TryGetValue(typeof(T), out var list))
                    _handlers[typeof(T)] = list = new();
                list.Add(h);
            }
        }

        public static void Unsubscribe<T>(Action<T> h)
        {
            lock (_handlers)
            {
                if (_handlers.TryGetValue(typeof(T), out var list))
                    list.Remove(h);
            }
        }

        public static void Publish<T>(T evt)
        {
            _pending.Enqueue(() =>
            {
                lock (_handlers)
                {
                    if (!_handlers.TryGetValue(typeof(T), out var list)) return;
                    foreach (var d in list) ((Action<T>)d)(evt);
                }
            });
        }

        public static void Flush()
        {
            while (_pending.TryDequeue(out var action))
                try { action(); } catch (Exception ex) { Console.Error.WriteLine(ex); }
        }

        public static void Clear()
        {
            while (_pending.TryDequeue(out _)) { }
            lock (_handlers) _handlers.Clear();
        }
    }
}
