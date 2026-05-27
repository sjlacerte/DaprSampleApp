namespace DaprIoT.Ingestor.Models;

public record SensorReading(float Value, string Unit, DateTimeOffset Timestamp);
