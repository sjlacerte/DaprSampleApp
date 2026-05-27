using DaprIoT.Processor.Models;

namespace DaprIoT.Processor.Workflows;

public record AnomalyWorkflowInput(
    string DeviceId,
    SensorReading Reading,
    float Threshold,
    List<SensorReading> History);
