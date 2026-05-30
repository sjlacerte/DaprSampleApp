using System.Runtime.Serialization;

namespace DaprIoT.Processor.Models;

[DataContract]
public class ActorInput
{
    public ActorInput() { }
    public ActorInput(SensorReading reading, Dictionary<string, string> thresholds)
    {
        Reading = reading;
        Thresholds = thresholds;
    }

    [DataMember] public SensorReading Reading { get; set; } = new();
    [DataMember] public Dictionary<string, string> Thresholds { get; set; } = new();
}
