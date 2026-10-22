using Avalonia.Controls;
using Avalonia.Threading;
namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    internal static async Task VerifyPowerLifecycleAsync(Window owner,string scenario)
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Power UI fixtures require isolated preview mode.");
        int acquired=0,frames=0;var leases=new List<FixtureAwakeLease>();bool pending=scenario=="AwakePendingOutput",fail=scenario=="AwakeUnavailableOutput",captureFailure=scenario=="AwakeFailureOutput";
        var session=new ScalingSession(new RuntimeSettings(),async stop=>
        {
            acquired++;
            if(fail)throw new NotSupportedException("Test desktop power service is unavailable.");
            if(pending)await Task.Delay(Timeout.Infinite,stop);
            var lease=new FixtureAwakeLease();leases.Add(lease);return lease;
        });
        string? finishedError=null;session.Finished+=error=>finishedError=error;
        session._previewLoop=async stop=>
        {
            await Dispatcher.UIThread.InvokeAsync(()=>session.OpenOutput(owner,false));
            while(!stop.IsCancellationRequested)
            {
                session.UpdateKeepAwake(RuntimeSettings.Current.KeepScreenOn);Interlocked.Increment(ref frames);
                if(captureFailure&&session._awake.Active)throw new InvalidOperationException("synthetic capture failure");
                await Task.Delay(10,stop);
            }
        };
        bool previous=RuntimeSettings.Current.KeepScreenOn;RuntimeSettings.Current.KeepScreenOn=true;
        var run=session.RunAsync(owner,false,0);
        async Task Wait(Func<bool> condition)
        {
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(8));while(!condition()) { deadline.Token.ThrowIfCancellationRequested();if(run.IsCompleted)throw new InvalidOperationException("Synthetic scaling stopped too early: "+finishedError);await Task.Delay(20,deadline.Token); }
        }
        try
        {
            if(captureFailure)
            {
                await run.WaitAsync(TimeSpan.FromSeconds(8));
                if(finishedError!="synthetic capture failure"||leases.Single().Disposals!=1||session._output is not null)throw new InvalidOperationException("Capture failure did not release its power lease/output.");
            }
            else
            {
                await Wait(()=>pending?frames>=8&&acquired==1:fail?session._powerStatus?.IsVisible==true:session._awake.Active);
                if(pending&&(!session._output!.IsVisible||frames<8))throw new InvalidOperationException("Pending power service blocked the scaling frame worker.");
                if(!pending&&!fail)
                {
                    RuntimeSettings.Current.KeepScreenOn=false;await Wait(()=>!session._awake.Active&&leases.Single().Disposals==1);
                    RuntimeSettings.Current.KeepScreenOn=true;await Wait(()=>session._awake.Active&&leases.Count==2);
                }
                // Render a deterministic synthetic frame while the real lifecycle owns power cleanup.
                await Dispatcher.UIThread.InvokeAsync(()=>
                {
                    byte[] pixels=new byte[32*24*4];for(int i=0;i<pixels.Length;i++)pixels[i]=i%4==3?(byte)255:(byte)(i*17%251);session.Present(pixels,32,24);
                });
                await Task.Delay(100);session.Stop();await run.WaitAsync(TimeSpan.FromSeconds(8));
                if(session._output is not null||leases.Any(l=>l.Active||l.Disposals!=1)||finishedError is not null)throw new InvalidOperationException("Scaling stop leaked power/output state or surfaced normal cancellation.");
            }
            if(acquired!=(pending||fail||captureFailure?1:2))throw new InvalidOperationException("Unexpected power acquisitions while frames were running.");
            Console.WriteLine("SCREEN AWAKE OUTPUT PASS: "+scenario+", actual session Stop/failure cleanup, live preference changes and nonblocking frame worker; system power backend is a test fixture.");
        }
        finally { session.Stop();try { await run.WaitAsync(TimeSpan.FromSeconds(8)); }finally { RuntimeSettings.Current.KeepScreenOn=previous; } }
    }
}
