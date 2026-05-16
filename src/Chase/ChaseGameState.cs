using BeamQuest.Core;
using BeamQuest.World;

namespace BeamQuest.Chase
{
    public enum ChasePhase { WaitingToStart, Running, Caught, Escaped }

    /// <summary>
    /// Tracks the game loop for Chase mode.
    /// Caught = vehicle within ImpactRadius.
    /// Escaped = player physically reaches BeaconPosition OR survives EscapeGoalSeconds.
    /// </summary>
    public sealed class ChaseGameState
    {
        private readonly ThreatTracker _threat;

        public ChasePhase Phase           { get; private set; } = ChasePhase.WaitingToStart;
        public float      SurvivalSeconds { get; private set; }
        public float      BestSeconds     { get; private set; }
        public int        AttemptNumber   { get; private set; }

        // Time-based escape fallback (still active as a secondary win condition)
        public float EscapeGoalSeconds { get; set; } = 180f;

        // Physical escape zone — 4m radius around the beacon
        private const float EscapeZoneRadius = 4f;

        // Last 5 round durations (survival seconds per round)
        private readonly Queue<float> _roundHistory = new();
        private const int HistoryMax = 5;
        public IReadOnlyCollection<float> RoundHistory => _roundHistory;

        // Impact grace period — prevent instant re-catch after restart
        private float _gracePeriod;
        private const float GraceDuration = 2f;

        public ChaseGameState(ThreatTracker threat)
        {
            _threat = threat;
            EventBus.Subscribe<ThreatImpactEvent>(OnImpact);
        }

        public void StartRound()
        {
            Phase           = ChasePhase.Running;
            SurvivalSeconds = 0f;
            _gracePeriod    = GraceDuration;
            AttemptNumber++;
            EventBus.Publish(new ChaseStartedEvent());
        }

        public void Update(float dt, Vector3 playerPos)
        {
            if (Phase != ChasePhase.Running) return;

            SurvivalSeconds += dt;
            _gracePeriod    -= dt;

            // Physical escape: player reached the beacon
            var toBeacon = playerPos - ProceduralEnvironment.BeaconPosition;
            if (toBeacon.LengthSquared() <= EscapeZoneRadius * EscapeZoneRadius)
            {
                FinishRound(escaped: true);
                return;
            }

            // Time-based escape fallback
            if (SurvivalSeconds >= EscapeGoalSeconds)
                FinishRound(escaped: true);
        }

        private void FinishRound(bool escaped)
        {
            Phase = escaped ? ChasePhase.Escaped : ChasePhase.Caught;
            if (SurvivalSeconds > BestSeconds) BestSeconds = SurvivalSeconds;
            RecordHistory(SurvivalSeconds);

            if (escaped)
                EventBus.Publish(new ChaseEscapedEvent(SurvivalSeconds));
        }

        private void RecordHistory(float seconds)
        {
            _roundHistory.Enqueue(seconds);
            while (_roundHistory.Count > HistoryMax)
                _roundHistory.Dequeue();
        }

        private void OnImpact(ThreatImpactEvent e)
        {
            if (Phase != ChasePhase.Running || _gracePeriod > 0) return;
            FinishRound(escaped: false);
            EventBus.Publish(new PlayerCaughtEvent(SurvivalSeconds));
        }

        public void Restart()
        {
            Phase           = ChasePhase.WaitingToStart;
            SurvivalSeconds = 0f;
            EventBus.Publish(new ChaseRestartedEvent());
        }
    }
}
