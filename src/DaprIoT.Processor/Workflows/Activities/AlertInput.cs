using DaprIoT.Processor.Models;

namespace DaprIoT.Processor.Workflows.Activities;

public record AlertInput(
    string DeviceId,
    SensorReading Reading,
    float Threshold,
    string Analysis);
