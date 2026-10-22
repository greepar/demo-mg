namespace Magpie.Desktop;
public partial class MainWindow
{
    internal void SetPreviewScalingSession(Core.ScalingSession? session)
    {
        if(Core.RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Preview session injection requires disabled persistence.");
        if(session is not null&&_scalingSession is not null)throw new InvalidOperationException("A scaling session is already active.");
        _scalingSession=session;
    }
}
