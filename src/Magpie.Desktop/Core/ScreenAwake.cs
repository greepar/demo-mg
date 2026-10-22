using System.Diagnostics;
namespace Magpie.Desktop.Core;

internal interface IScreenAwakeLease : IAsyncDisposable
{
    bool Active { get; }
    event Action<string>? Lost;
}

internal static class ScreenAwake
{
    internal static Task<IScreenAwakeLease> AcquireAsync(CancellationToken stop)
    {
        stop.ThrowIfCancellationRequested();
        string reason="Magpie — "+Localization.Get("Home_Advanced_KeepScreenOn.Header");
        if(reason.Length>128)reason=reason[..128];
        if(OperatingSystem.IsLinux())return PortalScreenAwakeLease.AcquireAsync(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")??throw new NotSupportedException("The desktop has no session D-Bus connection."),reason,stop);
        if(OperatingSystem.IsWindows())return Task.FromResult<IScreenAwakeLease>(WindowsScreenAwakeLease.Acquire(new WindowsPowerApi(),reason));
        if(OperatingSystem.IsMacOS())return Task.FromResult<IScreenAwakeLease>(MacScreenAwakeLease.Acquire(new MacPowerApi(),reason));
        throw new PlatformNotSupportedException("Keeping the display awake is unavailable on this platform.");
    }
}

// Preserve ownership if an OS temporarily refuses release. The pending task
// roots its controller until cleanup succeeds; it never opens a new request.
internal static class ScreenAwakeCleanup
{
    private static readonly HashSet<Task> Pending=[];
    internal static void Schedule(IAsyncDisposable owner)
    {
        var task=RetryAsync(owner,()=>Task.Delay(TimeSpan.FromSeconds(5)));
        lock(Pending)Pending.Add(task);_=ForgetAsync(task);
    }
    private static async Task ForgetAsync(Task task) { await task;lock(Pending)Pending.Remove(task); }
    internal static async Task RetryAsync(IAsyncDisposable owner,Func<Task> delay)
    {
        while(true)
        {
            await delay();
            try { await owner.DisposeAsync();return; }
            catch(Exception error) { Console.Error.WriteLine("Retrying power request cleanup: "+error.Message); }
        }
    }
}

// Owned by a scaling session. Failed acquisition is nonfatal, as in the original.
// A lost portal is retried after one second; unavailable services are retried at
// most every thirty seconds. Disabling and stopping always release the lease.
internal sealed class ScreenAwakeController(Func<CancellationToken,Task<IScreenAwakeLease>> acquire,Func<long>? clock=null) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate=new(1,1);
    private readonly Func<long> _clock=clock??Stopwatch.GetTimestamp;
    private IScreenAwakeLease? _lease;
    private string? _warning;
    private long _nextAttempt;
    private bool _disposed;
    internal bool Active=>_lease?.Active==true;
    internal string? Warning=>Volatile.Read(ref _warning);
    internal event Action<string?>? Changed;
    private void Report(string? warning) { if(Interlocked.Exchange(ref _warning,warning)!=warning)Changed?.Invoke(warning); }
    private void Lost(string reason) { Interlocked.Exchange(ref _nextAttempt,_clock()+Stopwatch.Frequency);Report(reason); }
    internal async ValueTask UpdateAsync(bool enabled,CancellationToken stop)
    {
        await _gate.WaitAsync(stop);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed,this);stop.ThrowIfCancellationRequested();
            if(!enabled) { await ReleaseAsync();Interlocked.Exchange(ref _nextAttempt,0);Report(null);return; }
            if(_lease?.Active==true)return;
            if(_clock()<Interlocked.Read(ref _nextAttempt))return;
            await ReleaseAsync();
            IScreenAwakeLease? candidate=null;
            try
            {
                candidate=await acquire(stop);stop.ThrowIfCancellationRequested();
                if(!candidate.Active)throw new InvalidOperationException("The desktop closed the display-awake request.");
                _lease=candidate;candidate=null;_lease.Lost+=Lost;
                if(!_lease.Active)Lost("The desktop closed the display-awake request.");else Report(null);
            }
            catch(OperationCanceledException) when(stop.IsCancellationRequested) { throw; }
            catch(Exception error) { Interlocked.Exchange(ref _nextAttempt,_clock()+30*Stopwatch.Frequency);Report(error.Message); }
            finally { if(candidate is not null)await candidate.DisposeAsync(); }
        }
        finally { _gate.Release(); }
    }
    private async ValueTask ReleaseAsync()
    {
        if(_lease is not {} lease)return;
        lease.Lost-=Lost;
        try { await lease.DisposeAsync();_lease=null; }
        catch(Exception error) { Report(error.Message);throw; }
    }
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try { _disposed=true;await ReleaseAsync();Report(null); }
        finally { _gate.Release(); }
    }
}
