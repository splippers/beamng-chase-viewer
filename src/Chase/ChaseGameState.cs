using BeamQuest.Core;
using BeamQuest.World;

namespace BeamQuest.Chase
{
    public enum ChasePhase { WaitingToStart, Running, Caught, Escaped }

    /// <summary>
    /// Tracks the game loop for Chase mode.
    /// Caught = vehicle within ImpactRadius.
    /// Escaped = surviving BeyondSeconds without impact (optional win condition).
    /// </summary>
    public sealed class ChaseGameState
    {
        private readonly ThreatTracker _threat;

        public ChasePhase Phase           { get; private set; } = ChasePhase.WaitingToStart;
        public float      SurvivalSeconds { get; private set; }
        public float      BestSeconds     { get; private set; }
        public int        AttemptNumber   { get; private set; }

        // Optional: survive this long to "win" a round
        public float EscapeGoalSeconds { get; set; } = 180f;

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

        public void Update(float dt)
        {
            if (Phase != ChasePhase.Running) return;

            SurvivalSeconds += dt;
            _gracePeriod    -= dt;

            if (SurvivalSeconds >= EscapeGoalSeconds)
            {
                Phase = ChasePhase.Escaped;
                if (SurvivalSeconds > BestSeconds) BestSeconds = SurvivalSeconds;
            }
        }

        private void OnImpact(ThreatImpactEvent e)
        {
            if (Phase != ChasePhase.Running || _gracePeriod > 0) return;
            Phase = ChasePhase.Caught;
            if (SurvivalSeconds > BestSeconds) BestSeconds = SurvivalSeconds;
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
