using Dapr.Actors;
using DaprIoT.Processor.Models;

namespace DaprIoT.Processor.Actors;

public interface IDeviceActor : IActor
{
    Task<ActorResult> RecordReadingAsync(ActorInput input);
}
