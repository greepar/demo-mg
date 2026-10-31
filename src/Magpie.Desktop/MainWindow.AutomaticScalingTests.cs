using System.Text.Json;
using Avalonia.Threading;
using Magpie.Desktop.Core;
namespace Magpie.Desktop;

public partial class MainWindow
{
    private sealed class AutomaticFixtureSource : IWindowsAutomaticSource
    {
        internal WindowsCaptureSelection? Selected;
        internal ulong Window;
        internal bool IsReady=true,Popup;
        internal int Reads;
        internal TaskCompletionSource? ReadGate,ReadEntered;
        public ulong Foreground=>Window;
        public WindowsCaptureSelection? Read()
        {
            if(Dispatcher.UIThread.CheckAccess())throw new InvalidOperationException("Foreground metadata blocked the UI thread.");
            Interlocked.Increment(ref Reads);ReadEntered?.TrySetResult();ReadGate?.Task.GetAwaiter().GetResult();return Selected;
        }
        public bool Ready(WindowsCaptureSelection source)
        {
            if(Dispatcher.UIThread.CheckAccess())throw new InvalidOperationException("Window responsiveness blocked the UI thread.");
            return IsReady;
        }
        public bool IsPopup(WindowsCaptureSelection source,ulong owner)=>Popup;
        internal void Set(WindowsCaptureSelection? source,ulong other=99) {Selected=source;Window=source?.Source.WindowId??other;}
    }
    internal async Task VerifyAutomaticScalingAsync()
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Automatic lifecycle fixture requires isolated settings.");
        var root=RuntimeSettings.Current;var previous=root.Profiles.ToList();int entered=0,created=0;
        var modes=new List<bool>();var windows=new List<ulong>();
        var secondGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thirdGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ApplicationProfile Profile(string name,string path,string windowClass,AutoScaleMode mode)
        {
            using var rule=JsonDocument.Parse("{\"packaged\":false,\"pathRule\":\""+JsonEncodedText.Encode(path)+"\",\"classNameRule\":\""+JsonEncodedText.Encode(windowClass)+"\"}");
            return new() {Name=name,OriginalProfile=rule.RootElement.Clone(),Settings=new RuntimeSettings {AutoScale=mode,Modes=root.Modes}};
        }
        var a=Profile("Automatic A","C:\\Games\\A.exe","GameA",AutoScaleMode.Windowed);
        var b=Profile("Automatic B","C:\\Games\\B.exe","Chrome_WidgetWin_1",AutoScaleMode.Fullscreen);
        WindowsCaptureSelection Source(ulong window,uint pid,string path,string name)=>new(new("Test","Test",path,pid,window,1),pid+1,name);
        var first=Source(11,111,"C:\\Games\\A.exe","GameA");
        var next=Source(12,112,"C:\\Games\\B.exe","Chrome_WidgetWin_77");
        var probe=new AutomaticFixtureSource();_automaticSource=probe;
        _previewAutomaticSession=(settings,selected)=>
        {
            int index=++created;
            return ScalingSession.CountdownFixture(settings,()=>Interlocked.Increment(ref entered),selected,
                fullscreen=> {modes.Add(fullscreen);windows.Add(selected.Source.WindowId);},index==2?secondGate.Task:index==3?thirdGate.Task:null);
        };
        async Task Wait(Func<bool> condition)
        {
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while(!condition())await Task.Delay(15,deadline.Token);
        }
        void Require(bool condition,string message) {if(!condition)throw new InvalidOperationException("Automatic scaling UI: "+message);}
        try
        {
            root.Profiles.Clear();root.Profiles.AddRange([a,b]);
            probe.Set(first);probe.IsReady=false;await TickAutomaticScalingAsync();
            Require(entered==0&&_automaticChecked!=first.Source.WindowId,"Unready window was consumed instead of retried.");
            probe.IsReady=true;await TickAutomaticScalingAsync();await Wait(()=>Volatile.Read(ref entered)==1);
            int reads=probe.Reads;await TickAutomaticScalingAsync();
            Require(probe.Reads==reads&&created==1&&!modes[0]&&windows[0]==11,"Same source restarted or wrong initial mode/identity started.");
            // Direct session Stop is the toolbar path, not only MainWindow's
            // global stop entry. It must suppress immediate automatic restart.
            _scalingSession!.Stop();await Wait(()=>_scalingSession is null);await TickAutomaticScalingAsync();
            Require(entered==1,"User stop immediately restarted the same foreground source.");
            probe.Set(null);await TickAutomaticScalingAsync();probe.Set(first);await TickAutomaticScalingAsync();await Wait(()=>Volatile.Read(ref entered)==2);
            probe.Set(next);probe.Popup=true;await TickAutomaticScalingAsync();
            Require(_pendingAutomatic is null&&_scalingSession?.SelectedWindowsWindow==11,"Source popup replaced the running capture.");
            probe.Popup=false;probe.Set(first);await TickAutomaticScalingAsync();probe.Set(next);probe.IsReady=false;await TickAutomaticScalingAsync();
            Require(_pendingAutomatic is null,"A loading second application stopped the first capture.");
            probe.IsReady=true;await TickAutomaticScalingAsync();
            Require(_pendingAutomatic?.Source.WindowId==12&&_scalingSession?.SelectedWindowsWindow==11&&created==2,"Handoff overlapped two native lifetimes.");
            _scalingSession!.Stop();Require(_pendingAutomatic is null,"Toolbar stop left a queued automatic request.");
            secondGate.SetResult();await Wait(()=>_scalingSession is null);await TickAutomaticScalingAsync();
            Require(entered==2,"Cancelled queued request started after old cleanup.");
            probe.Set(null);await TickAutomaticScalingAsync();probe.Set(next);await TickAutomaticScalingAsync();await Wait(()=>Volatile.Read(ref entered)==3);
            Require(modes[2]&&windows[2]==12,"Second application's fullscreen preference or pinned identity was ignored.");
            probe.Set(first);await TickAutomaticScalingAsync();Require(_pendingAutomatic is not null,"Enabled source did not queue a replacement.");
            a.Settings.AutoScale=AutoScaleMode.Disabled;thirdGate.SetResult();await Wait(()=>_scalingSession is null);await Task.Delay(100);
            Require(entered==3,"Rule disabled during cleanup still started.");
            a.Settings.AutoScale=AutoScaleMode.Windowed;probe.Set(null);await TickAutomaticScalingAsync();probe.Set(first);await TickAutomaticScalingAsync();await Wait(()=>Volatile.Read(ref entered)==4);
            Require(windows.SequenceEqual(new ulong[]{11,11,12,11})&&modes.SequenceEqual(new[]{false,false,true,false}),"Automatic ordering/mode selection changed.");
            probe.Set(next);await TickAutomaticScalingAsync();await Wait(()=>Volatile.Read(ref entered)==5);
            Require(_scalingSession?.SelectedWindowsWindow==12&&windows[4]==12&&modes[4],"Valid queued handoff did not start after old cleanup.");
            _scalingSession!.Stop();await Wait(()=>_scalingSession is null);
            probe.Set(null);await TickAutomaticScalingAsync();probe.Set(first);
            probe.ReadGate=new(TaskCreationOptions.RunContinuationsAsynchronously);probe.ReadEntered=new(TaskCreationOptions.RunContinuationsAsynchronously);
            var inFlight=TickAutomaticScalingAsync();await probe.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            StopScaling();probe.ReadGate.SetResult();await inFlight;
            Require(entered==5&&_scalingSession is null,"User stop during asynchronous identity lookup still started a capture.");
            Console.WriteLine("AUTOMATIC SCALING LIFECYCLE PASS: actual MainWindow/ScalingSession start/stop/cleanup, unready retry, class matching, source suppression/rearm, popup exclusion, serial handoff, toolbar cancellation, disabled queued rule and user stop during lookup; foreground/readiness/capture are synthetic, no OS windows or input queried.");
        }
        finally
        {
            _automaticClosed=true;CancelAutomaticRequest();StopScaling();secondGate.TrySetResult();thirdGate.TrySetResult();probe.ReadGate?.TrySetResult();
            await Wait(()=>_scalingSession is null);_previewAutomaticSession=null;_automaticSource=new WindowsAutomaticSource();
            root.Profiles.Clear();root.Profiles.AddRange(previous);
        }
    }
}
