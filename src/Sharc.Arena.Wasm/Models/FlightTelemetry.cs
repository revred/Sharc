namespace Sharc.Arena.Wasm.Models;

public enum SensorType
{
    Altimeter,
    Airspeed,
    EngineTempLeft,
    EngineTempRight,
    Pitch,
    Roll,
    Yaw,
    Warning,
    Heading,
    VerticalSpeed,
    Throttle,
    FlapPosition,
    TurnRate,
    GpsAltitude
}

public enum AutoPilotMode
{
    Suggestive,
    AutoPilot
}

public class SensorReading
{
    public SensorType Type { get; set; }
    public double Value { get; set; }
    public string Unit { get; set; } = "";
    public string Message { get; set; } = "";
    public long TimestampMs { get; set; }

    public static SensorReading Create(SensorType type, double value, string unit = "")
    {
        return new SensorReading
        {
            Type = type,
            Value = value,
            Unit = unit,
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    public static SensorReading CreateWarning(string message)
    {
        return new SensorReading
        {
            Type = SensorType.Warning,
            Message = message,
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(SensorReading))]
public partial class FlightSimulatorTelemetryContext : System.Text.Json.Serialization.JsonSerializerContext
{
}
