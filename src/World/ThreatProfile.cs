namespace BeamQuest.World
{
    /// <summary>
    /// Which vehicle is hunting the player.  Selected at round start via the wrist menu.
    /// Each profile tweaks the physics feel, haptic character, and fog behaviour
    /// without touching BeamNG itself — the AI speed is broadcast as a target via UDP.
    /// </summary>
    public enum ThreatProfile
    {
        Truck,         // default: steady relentless approach, low frequency rumble
        Bus,           // wide and slow — harder to dodge in tight spaces, deep bass
        CursedForklift // erratic acceleration bursts, high-pitch whine haptic
    }

    public readonly struct ThreatProfileConfig
    {
        // How fast BeamNG's AI is allowed to move (m/s sent in the position packet)
        public float MaxSpeedMs      { get; init; }

        // How far the threat can detect the player (overrides ThreatTracker.TenseRadius)
        public float AggroRadiusM    { get; init; }

        // Amplitude modifier applied on top of the base haptic calculation
        public float HapticAmplitude { get; init; }

        // Frequency hint for XR haptic vibration (Hz, 0 = runtime decides)
        public float HapticFrequency { get; init; }

        // Fog density scalar added when the vehicle is in Danger/Critical zone
        // Positive = thicker fog (obscures the vehicle, more horror)
        // Negative = clearer (so the player can see it closing in — even scarier)
        public float FogBias         { get; init; }

        // Label shown in wrist menu
        public string DisplayName    { get; init; }

        // ── Presets ──────────────────────────────────────────────────────────

        public static readonly ThreatProfileConfig Truck = new()
        {
            MaxSpeedMs      = 14f,
            AggroRadiusM    = 45f,
            HapticAmplitude = 1.0f,
            HapticFrequency = 0f,   // low, runtime-chosen
            FogBias         = 0.005f,
            DisplayName     = "TRUCK",
        };

        public static readonly ThreatProfileConfig Bus = new()
        {
            MaxSpeedMs      = 10f,
            AggroRadiusM    = 55f,   // bigger sight cone — compensates for low speed
            HapticAmplitude = 1.3f,  // deep heavy rumble feels bigger
            HapticFrequency = 0f,
            FogBias         = 0.012f, // denser fog amplifies the slow dread
            DisplayName     = "BUS",
        };

        public static readonly ThreatProfileConfig CursedForklift = new()
        {
            MaxSpeedMs      = 18f,    // fast bursts
            AggroRadiusM    = 30f,
            HapticAmplitude = 0.85f,
            HapticFrequency = 150f,   // high-pitch whine
            FogBias         = -0.006f, // clears slightly so the player sees it coming
            DisplayName     = "CURSED FORKLIFT",
        };

        public static ThreatProfileConfig ForProfile(ThreatProfile p) => p switch
        {
            ThreatProfile.Bus           => Bus,
            ThreatProfile.CursedForklift => CursedForklift,
            _                           => Truck,
        };
    }
}
