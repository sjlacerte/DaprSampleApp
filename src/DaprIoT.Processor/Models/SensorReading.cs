using System.Runtime.Serialization;

namespace DaprIoT.Processor.Models;

[DataContract]
public class SensorReading
{
    public SensorReading() { }
    public SensorReading(float value, string unit, DateTimeOffset timestamp)
    {
        Value = value;
        Unit = unit;
        Timestamp = timestamp;
    }

    [DataMember] public float Value { get; set; }
    [DataMember] public string Unit { get; set; } = string.Empty;
    [DataMember] public DateTimeOffset Timestamp { get; set; }
}
