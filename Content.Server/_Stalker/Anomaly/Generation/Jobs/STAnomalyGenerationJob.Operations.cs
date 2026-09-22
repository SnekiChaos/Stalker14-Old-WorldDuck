using System.Threading.Tasks;

namespace Content.Server._Stalker.Anomaly.Generation.Jobs;

public sealed partial class STAnomalyGenerationJob
{
    // ST:OW begin
    private ValueTask MakeOperation()
    {
        Cancellation.ThrowIfCancellationRequested();
        return SuspendIfOutOfTime();
    }
}
// ST:OW end