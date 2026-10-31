using System.Text.Json;
using Magpie.Desktop.Core;
namespace Magpie.Desktop;

public partial class MainWindow
{
    private Func<RuntimeSettings,WindowsCaptureSelection,ScalingSession>? _hostedAutomaticSession;
    private ApplicationProfile? _hostedAutomaticProfile;
    internal async Task<(ScalingSession Session,Task Run)> BeginHostedAutomaticCase(RuntimeSettings settings,bool fullscreen,ulong window,uint process,
        Func<RuntimeSettings,WindowsCaptureSelection,ScalingSession> factory)
    {
        HostedWindowsLifecycle.Require();
        if(_scalingSession is not null||RuntimeSettings.Current.Profiles.Count!=0||_previewAutomaticSession is not null)
            throw new InvalidOperationException("Hosted automatic case requires an idle isolated window.");
        settings.AutoScale=fullscreen?AutoScaleMode.Fullscreen:AutoScaleMode.Windowed;
        using var rule=JsonDocument.Parse("{\"packaged\":false,\"pathRule\":\""+JsonEncodedText.Encode(Path.GetFullPath(HostedWindowsLifecycle.Fixture))+"\",\"classNameRule\":\"MagpieGdiDisposableSource\"}");
        _hostedAutomaticProfile=new ApplicationProfile {Name="Hosted automatic source",OriginalProfile=rule.RootElement.Clone(),Settings=settings};
        RuntimeSettings.Current.Profiles.Add(_hostedAutomaticProfile);_hostedAutomaticSession=factory;
        _automaticSource=new WindowsAutomaticSource();_automaticChecked=0;
        await TickAutomaticScalingAsync();
        if(_scalingSession is not {} session||_scalingRun is not {} run||session.SelectedWindowsWindow!=window)
            throw new InvalidOperationException("Actual foreground/rule/readiness did not automatically start the owned source.");
        // Require the independently created process, not merely a matching
        // window class in another process on the hosted desktop.
        var selected=WindowsCaptureSelection.Read();
        if(selected is null||selected.Source.WindowId!=window||selected.Source.ProcessId!=process)
            throw new InvalidOperationException("Automatic source was not the independently launched process.");
        return (session,run);
    }
    internal async Task VerifyHostedAutomaticStable(ScalingSession session,bool fullscreen)
    {
        HostedWindowsLifecycle.Require();
        await TickAutomaticScalingAsync();await TickAutomaticScalingAsync();
        if(!ReferenceEquals(_scalingSession,session)||!session.HostedAutomaticMode(fullscreen))
            throw new InvalidOperationException("Automatic polling replaced the active source or chose a different output mode.");
    }
    internal void EndHostedAutomaticCase()
    {
        HostedWindowsLifecycle.Require();
        _hostedAutomaticSession=null;CancelAutomaticRequest();
        if(_hostedAutomaticProfile is {} profile)RuntimeSettings.Current.Profiles.Remove(profile);
        _hostedAutomaticProfile=null;
    }
}
