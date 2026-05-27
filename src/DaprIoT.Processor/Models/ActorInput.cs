namespace DaprIoT.Processor.Models;

public record ActorInput(SensorReading Reading, Dictionary<string, string> Thresholds);
