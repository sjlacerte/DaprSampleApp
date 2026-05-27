namespace DaprIoT.Processor.Models;

public record ProcessRequest(
    string DeviceId,
    SensorReading Reading,
    Dictionary<string, string> Thresholds);
