using Dapr.Actors.Runtime;
using DaprIoT.Processor.Models;

namespace DaprIoT.Processor.Actors;

public class DeviceActor : Actor, IDeviceActor
{
    private const string HistoryKey = "readingHistory";
    private const int MaxHistory = 10;

    public DeviceActor(ActorHost host) : base(host) { }

    public async Task<ActorResult> RecordReadingAsync(ActorInput input)
    {
        var history = await StateManager.GetOrAddStateAsync(HistoryKey, new List<SensorReading>());

        history.Add(input.Reading);
        if (history.Count > MaxHistory)
            history.RemoveAt(0);

        await StateManager.SetStateAsync(HistoryKey, history);

        var anomaly = DetectAnomaly(input.Reading, input.Thresholds);
        return new ActorResult(anomaly, new List<SensorReading>(history));
    }

    private static bool DetectAnomaly(SensorReading reading, Dictionary<string, string> thresholds)
    {
        if (reading.Unit.Equals("celsius", StringComparison.OrdinalIgnoreCase)
            && thresholds.TryGetValue("maxTemperature", out var maxTempStr)
            && float.TryParse(maxTempStr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var maxTemp)
            && reading.Value > maxTemp)
            return true;

        if (reading.Unit.Equals("bar", StringComparison.OrdinalIgnoreCase)
            && thresholds.TryGetValue("minPressure", out var minPressStr)
            && float.TryParse(minPressStr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var minPress)
            && reading.Value < minPress)
            return true;

        return false;
    }
}
