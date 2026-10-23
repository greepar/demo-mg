namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    internal static ScalingSession CountdownFixture(RuntimeSettings settings,Action entered)
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Countdown fixtures require isolated preview mode.");
        var session=new ScalingSession(settings);
        session._previewLoop=async stop=> { entered();await Task.Delay(Timeout.Infinite,stop); };
        return session;
    }
}
