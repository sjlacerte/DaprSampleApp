using System.Runtime.Serialization;

namespace DaprIoT.Processor.Models;

[DataContract]
public class ActorResult
{
    public ActorResult() { }
    public ActorResult(bool anomalyDetected, List<SensorReading> history)
    {
        AnomalyDetected = anomalyDetected;
        History = history;
    }

    [DataMember] public bool AnomalyDetected { get; set; }
    [DataMember] public List<SensorReading> History { get; set; } = new();
}
