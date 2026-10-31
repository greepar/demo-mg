namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    private Action<bool>? _previewStartedMode;
    internal static ScalingSession CountdownFixture(RuntimeSettings settings,Action entered,WindowsCaptureSelection? source=null,Action<bool>? mode=null,Task? finishGate=null)
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Countdown fixtures require isolated preview mode.");
        var session=new ScalingSession(settings,selectedSource:source);session._previewStartedMode=mode;
        session._previewLoop=async stop=> {entered();try {await Task.Delay(Timeout.Infinite,stop);}finally {if(finishGate is not null)await finishGate;} };
        return session;
    }
}
