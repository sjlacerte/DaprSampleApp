namespace DaprIoT.Processor.Models;

public record SensorReading(float Value, string Unit, DateTimeOffset Timestamp);
