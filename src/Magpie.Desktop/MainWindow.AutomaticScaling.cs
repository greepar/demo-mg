using Avalonia.Threading;
using Magpie.Desktop.Core;
namespace Magpie.Desktop;

public partial class MainWindow
{
    private DispatcherTimer? _automaticTimer;
    private IWindowsAutomaticSource _automaticSource=new WindowsAutomaticSource();
    private ulong _automaticChecked;
    private long _automaticUserRevision;
    private bool _automaticBusy,_automaticClosed;
    private WindowsCaptureSelection? _pendingAutomatic;
    private Func<RuntimeSettings,WindowsCaptureSelection,ScalingSession>? _previewAutomaticSession;

    private void StartAutomaticScaling()
    {
        if(!OperatingSystem.IsWindows()||!RuntimeSettings.PersistenceEnabled||_automaticTimer is not null)return;
        _automaticTimer=new DispatcherTimer(TimeSpan.FromMilliseconds(50),DispatcherPriority.Background,async (_,_)=>await TickAutomaticScalingAsync());
        _automaticTimer.Start();
        _=TickAutomaticScalingAsync();
    }
    private void CancelAutomaticRequest()
    {
        _pendingAutomatic=null;_automaticUserRevision++;
    }
    private async Task TickAutomaticScalingAsync()
    {
        if(_automaticBusy||_automaticClosed)return;
        var root=RuntimeSettings.Current;
        if(!root.Profiles.Any(p=>p.OriginalProfile.HasValue&&p.Settings.AutoScale!=AutoScaleMode.Disabled))return;
        _automaticBusy=true;
        try
        {
            ulong foreground=_automaticSource.Foreground,current=_scalingSession?.SelectedWindowsWindow??0;
            if(foreground==0||foreground==_automaticChecked)return;
            if(foreground==current){_automaticChecked=foreground;return;}
            long revision=_automaticUserRevision;
            var source=await Task.Run(_automaticSource.Read);
            if(_automaticClosed||revision!=_automaticUserRevision||_automaticSource.Foreground!=foreground)return;
            if(source is null) {_automaticChecked=foreground;return;}
            if(source.Source.WindowId!=foreground)return;
            var profile=WindowsProfileMatching.Find(root.Profiles,source.Identity,true);
            if(profile is null) {_automaticChecked=foreground;return;}
            var state=await Task.Run(()=>
            {
                bool popup=current!=0&&_automaticSource.IsPopup(source,current);
                return (Popup:popup,Ready:!popup&&_automaticSource.Ready(source));
            });
            if(_automaticClosed||revision!=_automaticUserRevision||_automaticSource.Foreground!=foreground||
                (_scalingSession?.SelectedWindowsWindow??0)!=current)return;
            if(state.Popup) {_automaticChecked=foreground;return;}
            if(!state.Ready)return; // Retry a loading/off-screen window without a foreground change.
            if(!ReferenceEquals(WindowsProfileMatching.Find(root.Profiles,source.Identity,true),profile))return;
            _automaticChecked=foreground;
            if(_scalingSession is {} active)
            {
                _pendingAutomatic=source;active.StopForAutomaticReplacement();
            }
            else StartAutomaticSession(source,profile);
        }
        catch(Exception error) {Console.Error.WriteLine("Automatic scaling: "+error.Message);}
        finally {_automaticBusy=false;}
    }
    private void StartAutomaticSession(WindowsCaptureSelection source,ApplicationProfile profile)
    {
        if(_automaticClosed||_scalingSession is not null)return;
        CancelCountdown();
        ScalingSession session;
        if(_hostedAutomaticSession is {} hosted)
        {
            HostedWindowsLifecycle.Require();session=hosted(profile.Settings,source);
        }
        else if(_previewAutomaticSession is {} preview)
        {
            if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Automatic scaling fixtures require isolated settings.");
            session=preview(profile.Settings,source);
        }
        else session=new ScalingSession(profile.Settings,selectedSource:source);
        StartScalingSession(session,profile.Settings.AutoScale==AutoScaleMode.Fullscreen);
    }
    private void AutomaticSessionStopped(ScalingSession session)
    {
        if(_automaticClosed)return;
        ulong foreground=OperatingSystem.IsWindows()||_previewAutomaticSession is not null?_automaticSource.Foreground:0;
        if(foreground!=0&&foreground==session.SelectedWindowsWindow)_automaticChecked=foreground;
        if(_pendingAutomatic is not {} pending)return;
        _pendingAutomatic=null;
        // Recheck a queued handoff after the old native capture is destroyed;
        // deleted/disabled/reordered rules and a newer foreground win.
        if(foreground!=pending.Source.WindowId)return;
        _=ResumeAutomaticRequestAsync(pending,_automaticUserRevision);
    }
    private async Task ResumeAutomaticRequestAsync(WindowsCaptureSelection pending,long revision)
    {
        try
        {
            var current=await Task.Run(_automaticSource.Read);
            if(_automaticClosed||revision!=_automaticUserRevision||_scalingSession is not null||current is null||
                current.Source.WindowId!=pending.Source.WindowId||current.Source.ProcessId!=pending.Source.ProcessId||current.ThreadId!=pending.ThreadId||
                _automaticSource.Foreground!=pending.Source.WindowId)return;
            bool ready=await Task.Run(()=>_automaticSource.Ready(current));
            if(_automaticClosed||revision!=_automaticUserRevision||_scalingSession is not null||_automaticSource.Foreground!=pending.Source.WindowId)return;
            if(!ready) {_automaticChecked=0;return;}
            if(WindowsProfileMatching.Find(RuntimeSettings.Current.Profiles,current.Identity,true) is {} profile)
                StartAutomaticSession(current,profile);
        }
        catch(Exception error) {Console.Error.WriteLine("Automatic scaling handoff: "+error.Message);}
    }
}
