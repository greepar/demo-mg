using Tmds.DBus.Protocol;
namespace Magpie.Desktop.Core;

internal sealed class PortalScreenAwakeLease : IScreenAwakeLease
{
    internal const string Service="org.freedesktop.portal.Desktop",Path="/org/freedesktop/portal/desktop",Interface="org.freedesktop.portal.Inhibit";
    private readonly DBusConnection _connection;
    private readonly List<IDisposable> _subscriptions=[];
    private readonly CancellationTokenSource _lifetime=new();
    private string? _owner,_request,_failure;
    private int _active,_disposed;
    public bool Active=>Volatile.Read(ref _active)!=0;
    public event Action<string>? Lost;
    private PortalScreenAwakeLease(string address)=>_connection=new(address);
    internal static async Task<IScreenAwakeLease> AcquireAsync(string address,string reason,CancellationToken stop)
    {
        var lease=new PortalScreenAwakeLease(address);
        try { await lease.OpenAsync(reason,stop);return lease; }
        catch { await lease.DisposeAsync();throw; }
    }
    private void Fail(string reason)
    {
        if(Volatile.Read(ref _disposed)!=0)return;
        Interlocked.Exchange(ref _failure,reason);Interlocked.Exchange(ref _active,0);
        try { _lifetime.Cancel(); }catch(ObjectDisposedException) { return; }
        Lost?.Invoke(reason);
    }
    private async Task OpenAsync(string reason,CancellationToken stop)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(stop,_lifetime.Token);
        var lifetime=linked.Token;
        try
        {
            await _connection.ConnectAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5),lifetime);
            Task<string> owner;
            { using var writer=_connection.GetMessageWriter();writer.WriteMethodCallHeader(destination:"org.freedesktop.DBus",path:"/org/freedesktop/DBus",@interface:"org.freedesktop.DBus",member:"GetNameOwner",signature:"s");writer.WriteString(Service);owner=_connection.CallMethodAsync(writer.CreateMessage(),static(message,_)=>message.GetBodyReader().ReadString()); }
            _owner=await owner.WaitAsync(TimeSpan.FromSeconds(5),lifetime);
            _subscriptions.Add(await _connection.WatchSignalAsync("org.freedesktop.DBus","/org/freedesktop/DBus","org.freedesktop.DBus","NameOwnerChanged",static(message,_)=>
            {
                var reader=message.GetBodyReader();return (reader.ReadString(),reader.ReadString(),reader.ReadString());
            },(Notification<(string,string,string)> notification)=>
            {
                if(notification.HasValue)
                {
                    if(notification.Value.Item1==Service&&notification.Value.Item2==_owner&&notification.Value.Item3!=_owner)Fail("The desktop power service was restarted.");
                }
                else if(notification.IsCompletion)Fail(notification.Exception?.Message??"The desktop session bus closed.");
            },ObserverFlags.EmitOnConnectionClosed|ObserverFlags.EmitOnReaderFailed,emitOnCapturedContext:false));
            uint version=(await LinuxTrayReadiness.PropertyAsync(_connection,_owner,Path,Interface,"version").WaitAsync(TimeSpan.FromSeconds(5),lifetime)).GetUInt32();
            if(version<1)throw new NotSupportedException("The desktop does not provide the Inhibit portal.");
            string token="magpie_power_"+Guid.NewGuid().ToString("N");
            _request="/org/freedesktop/portal/desktop/request/"+_connection.UniqueName!.TrimStart(':').Replace('.','_')+"/"+token;
            // Inhibit has no success Response to await: the returned Request is
            // held for the entire inhibition. Observe errors if a backend emits one.
            _subscriptions.Add(await _connection.WatchSignalAsync(_owner,_request,"org.freedesktop.portal.Request","Response",static(message,_)=>message.GetBodyReader().ReadUInt32(),(Notification<uint> notification)=>
            {
                if(notification.HasValue) { if(notification.Value!=0)Fail("The desktop refused the display-awake request."); }
                else if(notification.IsCompletion)Fail(notification.Exception?.Message??"The desktop power service closed its connection.");
            },ObserverFlags.EmitOnConnectionClosed|ObserverFlags.EmitOnReaderFailed,emitOnCapturedContext:false));
            Task<string> response;
            {
                using var writer=_connection.GetMessageWriter();writer.WriteMethodCallHeader(destination:_owner,path:Path,@interface:Interface,member:"Inhibit",signature:"sua{sv}");
                writer.WriteString("");writer.WriteUInt32(12);var dict=writer.WriteDictionaryStart();
                foreach(var (key,value) in new[]{("handle_token",token),("reason",reason)}) { writer.WriteDictionaryEntryStart();writer.WriteString(key);writer.WriteVariantString(value); }
                writer.WriteDictionaryEnd(dict);response=_connection.CallMethodAsync(writer.CreateMessage(),static(message,_)=>message.GetBodyReader().ReadObjectPath().ToString());
            }
            string actual=await response.WaitAsync(TimeSpan.FromSeconds(5),lifetime);
            if(actual!=_request)throw new InvalidDataException("The desktop returned an unexpected power request path.");
            lifetime.ThrowIfCancellationRequested();Interlocked.Exchange(ref _active,1);
            if(_failure is not null) { Interlocked.Exchange(ref _active,0);throw new InvalidOperationException(_failure); }
        }
        catch(OperationCanceledException) when(!stop.IsCancellationRequested&&_failure is not null) { throw new InvalidOperationException(_failure); }
    }
    public async ValueTask DisposeAsync()
    {
        if(Interlocked.Exchange(ref _disposed,1)!=0)return;
        Interlocked.Exchange(ref _active,0);_lifetime.Cancel();
        foreach(var subscription in _subscriptions)subscription.Dispose();_subscriptions.Clear();
        try
        {
            if(_request is not null&&_owner is not null)
            {
                Task close;
                { using var writer=_connection.GetMessageWriter();writer.WriteMethodCallHeader(destination:_owner,path:_request,@interface:"org.freedesktop.portal.Request",member:"Close");close=_connection.CallMethodAsync(writer.CreateMessage()); }
                await close.WaitAsync(TimeSpan.FromSeconds(2));
            }
        }
        catch(Exception error) { Console.Error.WriteLine("Power request close; disconnecting its owning client: "+error.Message); }
        finally { _connection.Dispose();_lifetime.Dispose(); }
    }
}
