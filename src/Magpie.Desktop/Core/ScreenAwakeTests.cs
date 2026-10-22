using System.Diagnostics;
using System.Runtime.InteropServices;
namespace Magpie.Desktop.Core;

internal sealed class FixtureAwakeLease : IScreenAwakeLease
{
    private int _active=1;
    internal int Disposals;
    internal bool FailReleaseOnce;
    internal int FailReleaseCount;
    public bool Active=>Volatile.Read(ref _active)!=0;
    public event Action<string>? Lost;
    internal void Revoke() { _active=0;Lost?.Invoke("fixture service restarted"); }
    public ValueTask DisposeAsync()
    {
        Disposals++;
        if(FailReleaseCount>0) { FailReleaseCount--;throw new InvalidOperationException("fixture release failed"); }
        if(FailReleaseOnce) { FailReleaseOnce=false;throw new InvalidOperationException("fixture release failed"); }
        _active=0;return ValueTask.CompletedTask;
    }
}
internal static class ScreenAwakeTests
{
    private static void Require(bool value,string message) { if(!value)throw new InvalidOperationException(message); }
    internal static void Verify()=>VerifyAsync().GetAwaiter().GetResult();
    private static async Task VerifyAsync()
    {
        int acquisitions=0;long clock=Stopwatch.Frequency;var leases=new List<FixtureAwakeLease>();
        var controller=new ScreenAwakeController(_=> { acquisitions++;var lease=new FixtureAwakeLease();leases.Add(lease);return Task.FromResult<IScreenAwakeLease>(lease); },()=>clock);
        await controller.UpdateAsync(false,default);Require(acquisitions==0,"Disabled screen-awake preference contacted a system backend.");
        await controller.UpdateAsync(true,default);await controller.UpdateAsync(true,default);Require(acquisitions==1&&controller.Active,"Repeated frames duplicated the power assertion.");
        leases[0].Revoke();Require(!controller.Active&&controller.Warning is not null,"Power service loss remained marked active.");
        await controller.UpdateAsync(true,default);Require(acquisitions==1,"Lost-service retry was not throttled.");clock+=Stopwatch.Frequency;
        await controller.UpdateAsync(true,default);Require(acquisitions==2&&leases[0].Disposals==1&&controller.Active&&controller.Warning is null,"Power service did not recover/close its old request.");
        await controller.UpdateAsync(false,default);await controller.UpdateAsync(false,default);Require(leases[1].Disposals==1&&!controller.Active,"Disabling did not release exactly once.");
        await controller.DisposeAsync();await controller.DisposeAsync();
        try { await controller.UpdateAsync(true,default);throw new InvalidOperationException("Disposed controller accepted an update."); }catch(ObjectDisposedException) { }
        acquisitions=0;
        await using(var unavailable=new ScreenAwakeController(_=> { acquisitions++;return Task.FromException<IScreenAwakeLease>(new NotSupportedException("fixture unavailable")); },()=>clock))
        {
            await unavailable.UpdateAsync(true,default);await unavailable.UpdateAsync(true,default);Require(acquisitions==1&&unavailable.Warning=="fixture unavailable","Unavailable service was not reported/backed off.");
            clock+=31*Stopwatch.Frequency;await unavailable.UpdateAsync(true,default);Require(acquisitions==2,"Unavailable service was never retried.");
            await unavailable.UpdateAsync(false,default);Require(unavailable.Warning is null,"Disabling retained a power warning.");
        }
        var completion=new TaskCompletionSource<IScreenAwakeLease>(TaskCreationOptions.RunContinuationsAsynchronously);var late=new FixtureAwakeLease();using var stop=new CancellationTokenSource();
        await using(var pending=new ScreenAwakeController(_=>completion.Task))
        {
            var update=pending.UpdateAsync(true,stop.Token).AsTask();Require(!update.IsCompleted,"Power acquisition unexpectedly blocked synchronously.");stop.Cancel();completion.SetResult(late);
            try { await update;throw new InvalidOperationException("Cancelled power acquisition completed."); }catch(OperationCanceledException) { }
            Require(!pending.Active&&late.Disposals==1,"A late successful acquisition leaked after cancellation.");
        }
        var retryLease=new FixtureAwakeLease { FailReleaseOnce=true };
        var retry=new ScreenAwakeController(_=>Task.FromResult<IScreenAwakeLease>(retryLease));await retry.UpdateAsync(true,default);
        try { await retry.DisposeAsync();throw new InvalidOperationException("Release failure was hidden."); }catch(InvalidOperationException error) when(error.Message=="fixture release failed") { }
        Require(retry.Active&&retry.Warning is not null,"Failed native release lost ownership.");await retry.DisposeAsync();Require(!retry.Active&&retryLease.Disposals==2,"Failed native cleanup was not retried.");
        var delayedLease=new FixtureAwakeLease { FailReleaseCount=3 };var delayed=new ScreenAwakeController(_=>Task.FromResult<IScreenAwakeLease>(delayedLease));await delayed.UpdateAsync(true,default);
        int retryDelays=0;await ScreenAwakeCleanup.RetryAsync(delayed,()=> { retryDelays++;return Task.CompletedTask; });
        Require(retryDelays==4&&delayedLease.Disposals==4&&!delayed.Active,"Deferred release retry lost ownership or acquired a replacement lease.");
        VerifyNative();VerifyPersistence();
        Console.WriteLine("SCREEN AWAKE MANAGED PASS: disabled/no system calls, acquisition/reuse/revocation/retry/backoff, disable/stop/idempotence, cancellation/late cleanup, release recovery, Windows/macOS ownership transactions and AOT setting restoration.");
    }
    private sealed class WindowsFixture : IWindowsPowerApi
    {
        internal readonly List<string> Calls=[];internal int FailSet=-1,FailClear=-1,CloseFailures;internal bool Live;
        public nint Create(string reason) { Require(reason=="常亮😀","Native Unicode reason was lost.");Calls.Add("create");Live=true;return 17; }
        public void Set(nint request,int kind) { Require(request==17,"Power request handle changed.");Calls.Add("set"+kind);if(kind==FailSet)throw new InvalidOperationException("fixture set"); }
        public void Clear(nint request,int kind) { Calls.Add("clear"+kind);if(kind==FailClear)throw new InvalidOperationException("fixture clear"); }
        public void Close(nint request) { Calls.Add("close");if(CloseFailures>0) { CloseFailures--;throw new InvalidOperationException("fixture close failed"); }Live=false; }
    }
    private sealed class MacFixture : IMacPowerApi
    {
        internal int Released;internal bool FailOnce;
        public uint Create(string reason) { Require(reason=="常亮😀","macOS Unicode reason changed.");return 97; }
        public void Release(uint id) { Require(id==97,"macOS assertion identity changed.");Released++;if(FailOnce) { FailOnce=false;throw new InvalidOperationException("fixture mac release"); } }
    }
    private static void VerifyNative()
    {
        Require(Marshal.SizeOf<WindowsPowerReasonContext>()==32&&Marshal.OffsetOf<WindowsPowerReasonContext>(nameof(WindowsPowerReasonContext.SimpleReasonString))==(nint)8,"Windows REASON_CONTEXT union layout differs from the 64-bit SDK.");
        foreach(int fail in new[]{-1,0,1})
        {
            var api=new WindowsFixture { FailSet=fail };
            if(fail>=0) { try { WindowsScreenAwakeLease.Acquire(api,"常亮😀");throw new InvalidOperationException("Partial Windows acquisition succeeded."); }catch(InvalidOperationException error) when(error.Message=="fixture set") { }Require(!api.Live&&api.Calls[^1]=="close","Windows partial power request leaked."); }
            else { var lease=WindowsScreenAwakeLease.Acquire(api,"常亮😀");lease.DisposeAsync().GetAwaiter().GetResult();lease.DisposeAsync().GetAwaiter().GetResult();Require(api.Calls.SequenceEqual(new[]{"create","set0","set1","clear1","clear0","close"})&&!lease.Active,"Windows power lifetime/clear order changed."); }
        }
        var failed=new WindowsFixture { FailClear=1 };var failedLease=WindowsScreenAwakeLease.Acquire(failed,"常亮😀");
        try { failedLease.DisposeAsync().GetAwaiter().GetResult();throw new InvalidOperationException("Windows cleanup error was hidden."); }catch(AggregateException) { }
        Require(!failed.Live&&failed.Calls.TakeLast(3).SequenceEqual(new[]{"clear1","clear0","close"}),"Windows cleanup failure skipped the other request or CloseHandle.");
        var closeRetry=new WindowsFixture { CloseFailures=1 };var retained=WindowsScreenAwakeLease.Acquire(closeRetry,"常亮😀");
        try { retained.DisposeAsync().GetAwaiter().GetResult();throw new InvalidOperationException("Failed CloseHandle was hidden."); }catch(AggregateException) { }
        Require(retained.Active&&closeRetry.Live,"Failed CloseHandle lost request ownership.");retained.DisposeAsync().GetAwaiter().GetResult();
        Require(!retained.Active&&!closeRetry.Live&&closeRetry.Calls.SequenceEqual(new[]{"create","set0","set1","clear1","clear0","close","close"}),"CloseHandle retry repeated already cleared requests or leaked the handle.");
        var mac=new MacFixture { FailOnce=true };var macLease=MacScreenAwakeLease.Acquire(mac,"常亮😀");
        try { macLease.DisposeAsync().GetAwaiter().GetResult();throw new InvalidOperationException("macOS cleanup error was hidden."); }catch(InvalidOperationException error) when(error.Message=="fixture mac release") { }
        Require(macLease.Active,"macOS release failure discarded its assertion ID.");macLease.DisposeAsync().GetAwaiter().GetResult();macLease.DisposeAsync().GetAwaiter().GetResult();Require(mac.Released==2&&!macLease.Active,"macOS assertion retry/idempotence changed.");
    }
    private static void VerifyPersistence()
    {
        string directory=Path.Combine(Path.GetTempPath(),"magpie-awake-settings-"+Guid.NewGuid().ToString("N"));
        try
        {
            Require(!new RuntimeSettings().KeepScreenOn,"Original keep-screen-on default is false.");
            string path=Path.Combine(directory,"settings.json");new RuntimeSettings { KeepScreenOn=true }.WriteFile(path);Require(RuntimeSettings.ReadFile(path).KeepScreenOn,"Keep screen on was not persisted by AOT JSON.");
            File.WriteAllText(path,"{}");Require(!RuntimeSettings.ReadFile(path).KeepScreenOn,"Old settings unexpectedly enabled screen inhibition.");
        }
        finally { if(Directory.Exists(directory))Directory.Delete(directory,true); }
    }
}
