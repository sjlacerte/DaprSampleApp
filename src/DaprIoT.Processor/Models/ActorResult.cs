namespace DaprIoT.Processor.Models;

public record ActorResult(bool AnomalyDetected, List<SensorReading> History);
