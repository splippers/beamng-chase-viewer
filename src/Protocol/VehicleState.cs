using System.Text.Json.Serialization;

namespace BeamQuest.Protocol
{
    /// <summary>
    /// Snapshot of one vehicle at a point in time.
    /// All values use BeamNG's Y-up coordinate system.
    /// </summary>
    public sealed class VehicleState
    {
        [JsonPropertyName("id")]    public string VehicleId  { get; set; } = "";
        [JsonPropertyName("name")]  public string PlayerName { get; set; } = "";
        [JsonPropertyName("model")] public string ModelName  { get; set; } = "";

        // Transform
        [JsonPropertyName("px")]  public float PosX { get; set; }
        [JsonPropertyName("py")]  public float PosY { get; set; }
        [JsonPropertyName("pz")]  public float PosZ { get; set; }
        [JsonPropertyName("rx")]  public float RotX { get; set; }
        [JsonPropertyName("ry")]  public float RotY { get; set; }
        [JsonPropertyName("rz")]  public float RotZ { get; set; }
        [JsonPropertyName("rw")]  public float RotW { get; set; } = 1f;

        // Velocity
        [JsonPropertyName("vx")]  public float VelX { get; set; }
        [JsonPropertyName("vy")]  public float VelY { get; set; }
        [JsonPropertyName("vz")]  public float VelZ { get; set; }

        // Telemetry
        [JsonPropertyName("spd")]  public float SpeedMs { get; set; }
        [JsonPropertyName("rpm")]  public float RPM     { get; set; }
        [JsonPropertyName("gear")] public int   Gear    { get; set; }
        [JsonPropertyName("fuel")] public float Fuel    { get; set; }
        [JsonPropertyName("thr")]  public float Throttle{ get; set; }
        [JsonPropertyName("brk")]  public float Brake   { get; set; }
        [JsonPropertyName("str")]  public float Steering{ get; set; }

        // Wheel positions (4 wheels: FL, FR, RL, RR)
        [JsonPropertyName("wh")]   public float[]? WheelHeights { get; set; }

        // Damage 0-1
        [JsonPropertyName("dmg")]  public float Damage { get; set; }

        public Vector3    Position => new(PosX, PosY, PosZ);
        public Quaternion Rotation => new(RotX, RotY, RotZ, RotW);
        public Vector3    Velocity => new(VelX, VelY, VelZ);
        public float      SpeedKph => SpeedMs * 3.6f;
    }

    /// <summary>One frame of the world: timestamp + all vehicle states.</summary>
    public sealed class WorldFrame
    {
        [JsonPropertyName("t")]  public double Timestamp { get; set; }
        [JsonPropertyName("vs")] public List<VehicleState> Vehicles { get; set; } = new();
    }

    /// <summary>
    /// OutGauge packet layout (from BeamNG's built-in UDP telemetry).
    /// See: https://wiki.beamng.com/OutGauge
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
    public struct OutGaugePacket
    {
        public uint   Time;         // ms since start
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 4)]
        public string Car;          // 4-char model tag
        public ushort Flags;
        public byte   Gear;         // 0=R, 1=N, 2=1st ...
        public byte   SpareB;
        public float  Speed;        // m/s
        public float  RPM;
        public float  Turbo;
        public float  EngTemp;
        public float  Fuel;
        public float  OilPressure;
        public float  OilTemp;
        public uint   DashLights;
        public uint   ShowLights;
        public float  Throttle;
        public float  Brake;
        public float  Clutch;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 16)]
        public string Display1;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 16)]
        public string Display2;
        public int    Id;
    }
}
