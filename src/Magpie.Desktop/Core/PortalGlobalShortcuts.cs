using Tmds.DBus.Protocol;
namespace Magpie.Desktop.Core;

// The low-level protocol API is NativeAOT compatible; no runtime proxy generation.
internal sealed class PortalGlobalShortcuts : IGlobalShortcutSession
{
    internal const string Service="org.freedesktop.portal.Desktop",Path="/org/freedesktop/portal/desktop",Interface="org.freedesktop.portal.GlobalShortcuts";
    private readonly DBusConnection _connection;
    private readonly List<IDisposable> _subscriptions=[];
    private readonly HashSet<string> _active=[];
    private string? _owner,_session;
    private bool _disposed,_closed;
    private readonly CancellationTokenSource _lifetime=new();
    public IReadOnlyList<BoundShortcut> Bound { get; private set; }=[];
    public event Action<string>? Activated;
    public event Action? Changed;
    public event Action<string>? Closed;
    internal PortalGlobalShortcuts(string address)=>_connection=new DBusConnection(address);
    public async Task BindAsync(PortalShortcut[] definitions,string parent,CancellationToken stop)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(stop,_lifetime.Token);stop=linked.Token;
        if(_disposed)throw new ObjectDisposedException(nameof(PortalGlobalShortcuts));
        if(_session is not null)throw new InvalidOperationException("Shortcuts are already bound for this session.");
        if(definitions.Length==0||definitions.Select(s=>s.Id).Distinct().Count()!=definitions.Length)throw new InvalidDataException("Invalid shortcut actions.");
        await _connection.ConnectAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5),stop);
        _owner=await OwnerAsync().WaitAsync(TimeSpan.FromSeconds(5),stop);
        _subscriptions.Add(await _connection.WatchSignalAsync("org.freedesktop.DBus","/org/freedesktop/DBus","org.freedesktop.DBus","NameOwnerChanged",static(message,_)=>
        {
            var reader=message.GetBodyReader();return (name:reader.ReadString(),oldOwner:reader.ReadString(),newOwner:reader.ReadString());
        },(Notification<(string name,string oldOwner,string newOwner)> notification)=>
        {
            if(notification.HasValue&&notification.Value.name==Service&&notification.Value.oldOwner==_owner&&notification.Value.newOwner!=_owner)FailSession("The desktop shortcut service was restarted.");
        },ObserverFlags.EmitOnConnectionClosed,emitOnCapturedContext:true));
        uint version=(await LinuxTrayReadiness.PropertyAsync(_connection,_owner,Path,Interface,"version").WaitAsync(TimeSpan.FromSeconds(5),stop)).GetUInt32();
        if(version<1)throw new NotSupportedException("The desktop does not provide GlobalShortcuts portal version 1.");
        var created=await RequestAsync("CreateSession",(token)=>CreateMessage(token),stop);
        _session=created.TryGetValue("session_handle",out var session) ? session.GetString() : throw new InvalidDataException("The portal omitted its shortcut session.");
        if(!_session.StartsWith("/org/freedesktop/portal/desktop/session/",StringComparison.Ordinal))throw new InvalidDataException("Invalid portal shortcut session path.");
        _subscriptions.Add(await WatchAsync("org.freedesktop.portal.Session","Closed",_session,static (message,_)=>true,(error,value)=>
        {
            if(!_disposed)FailSession(error?.Message??"The desktop closed the shortcut session.");
        }));
        foreach(string signal in new[]{"Activated","Deactivated"})
        {
            bool activate=signal=="Activated";
            _subscriptions.Add(await WatchAsync(Interface,signal,Path,static(message,_)=>
            {
                var reader=message.GetBodyReader();string session=reader.ReadObjectPath().ToString();string id=reader.ReadString();_ = reader.ReadUInt64();return (session,id);
            },(error,value)=>
            {
                if(error is not null||_disposed||_closed||value.session!=_session||!Bound.Any(s=>s.Id==value.id))return;
                if(activate) { if(_active.Add(value.id))Activated?.Invoke(value.id); }
                else _active.Remove(value.id);
            }));
        }
        _subscriptions.Add(await WatchAsync(Interface,"ShortcutsChanged",Path,static(message,_)=>
        {
            var reader=message.GetBodyReader();string session=reader.ReadObjectPath().ToString();return (session,shortcuts:ReadShortcuts(ref reader));
        },(error,value)=>
        {
            if(error is null&&!_disposed&&!_closed&&value.session==_session) { Bound=Filter(value.shortcuts,definitions);_active.Clear();Changed?.Invoke(); }
        }));
        var bound=await RequestAsync("BindShortcuts",token=>BindMessage(token,definitions,parent),stop);
        if(!bound.TryGetValue("shortcuts",out var shortcuts))throw new InvalidDataException("The portal omitted the registered shortcuts.");
        stop.ThrowIfCancellationRequested();Bound=Filter(ParseShortcuts(shortcuts),definitions);Changed?.Invoke();
        _=WatchDisconnectAsync();
    }
    private async Task WatchDisconnectAsync()
    {
        var failure=await _connection.DisconnectedAsync();
        if(!_disposed)FailSession(failure?.Message??"The desktop shortcut connection was closed.");
    }
    private void FailSession(string message)
    {
        if(_disposed||_closed)return;_closed=true;Bound=[];_active.Clear();_lifetime.Cancel();Changed?.Invoke();Closed?.Invoke(message);
    }
    private static BoundShortcut[] Filter(BoundShortcut[] shortcuts,PortalShortcut[] requested)
    {
        if(shortcuts.Select(s=>s.Id).Distinct().Count()!=shortcuts.Length||shortcuts.Any(s=>!requested.Any(r=>r.Id==s.Id)))throw new InvalidDataException("Portal returned unexpected shortcut actions.");
        return shortcuts;
    }
    private Task<string> OwnerAsync()
    {
        using var writer=_connection.GetMessageWriter();writer.WriteMethodCallHeader(destination:"org.freedesktop.DBus",path:"/org/freedesktop/DBus",@interface:"org.freedesktop.DBus",member:"GetNameOwner",signature:"s");writer.WriteString(Service);
        return _connection.CallMethodAsync(writer.CreateMessage(),static(message,_)=>message.GetBodyReader().ReadString());
    }
    private MessageWriter Writer(string member,string signature)
    {
        var writer=_connection.GetMessageWriter();writer.WriteMethodCallHeader(destination:_owner!,path:Path,@interface:Interface,member:member,signature:signature);return writer;
    }
    private MessageBuffer CreateMessage(string token)
    {
        var writer=Writer("CreateSession","a{sv}");try { Options(ref writer,new Dictionary<string,string>{{"handle_token",token},{"session_handle_token","magpie_"+Guid.NewGuid().ToString("N")}});return writer.CreateMessage(); }finally { writer.Dispose(); }
    }
    private MessageBuffer BindMessage(string token,PortalShortcut[] definitions,string parent)
    {
        var writer=Writer("BindShortcuts","oa(sa{sv})sa{sv}");try { writer.WriteObjectPath(_session!);
        var array=writer.WriteArrayStart(DBusType.Struct);
        foreach(var definition in definitions)
        {
            writer.WriteStructureStart();writer.WriteString(definition.Id);Options(ref writer,new Dictionary<string,string>{{"description",definition.Description},{"preferred_trigger",definition.PreferredTrigger}});
        }
        writer.WriteArrayEnd(array);writer.WriteString(parent);Options(ref writer,new Dictionary<string,string>{{"handle_token",token}});return writer.CreateMessage(); }finally { writer.Dispose(); }
    }
    private static void Options(ref MessageWriter writer,Dictionary<string,string> values)
    {
        var dict=writer.WriteDictionaryStart();foreach(var (key,value) in values) { writer.WriteDictionaryEntryStart();writer.WriteString(key);writer.WriteVariantString(value); }writer.WriteDictionaryEnd(dict);
    }
    private async Task<Dictionary<string,VariantValue>> RequestAsync(string member,Func<string,MessageBuffer> message,CancellationToken stop)
    {
        string token="magpie_"+Guid.NewGuid().ToString("N"),path="/org/freedesktop/portal/desktop/request/"+_connection.UniqueName!.TrimStart(':').Replace('.','_')+"/"+token;
        var completion=new TaskCompletionSource<(uint code,Dictionary<string,VariantValue> values)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription=await WatchAsync("org.freedesktop.portal.Request","Response",path,static(message,_)=>
        {
            var reader=message.GetBodyReader();uint code=reader.ReadUInt32();return (code,values:ReadDictionary(ref reader));
        },(error,value)=> { if(error is not null)completion.TrySetException(error);else completion.TrySetResult(value); });
        bool completed=false;
        try
        {
            string actual=await _connection.CallMethodAsync(message(token),static(reply,_)=>reply.GetBodyReader().ReadObjectPath().ToString()).WaitAsync(TimeSpan.FromSeconds(10),stop);
            if(actual!=path)throw new InvalidDataException("The portal returned an unexpected request handle.");
            var response=await completion.Task.WaitAsync(TimeSpan.FromMinutes(2),stop);
            completed=true;
            if(response.code==1)throw new OperationCanceledException("Global shortcut setup was cancelled by the user.");
            if(response.code!=0)throw new IOException("The desktop rejected global shortcut setup ("+response.code+").");
            return response.values;
        }
        finally { if(!completed)await CloseAsync(path,"org.freedesktop.portal.Request"); }
    }
    private Task<IDisposable> WatchAsync<T>(string iface,string signal,string path,MessageValueReader<T> reader,Action<Exception?,T> handler)=>
        _connection.WatchSignalAsync(_owner!,path,iface,signal,reader,(Notification<T> notification)=>
        {
            try
            {
                if(notification.HasValue)handler(null,notification.Value);
                else if(notification.Exception is not null) { handler(notification.Exception,default!);FailSession(notification.Exception.Message); }
            }
            catch(Exception error) { FailSession(error.Message); }
        },ObserverFlags.EmitOnConnectionClosed|ObserverFlags.EmitOnReaderFailed,emitOnCapturedContext:true).AsTask();
    private static Dictionary<string,VariantValue> ReadDictionary(ref Reader reader)
    {
        var result=new Dictionary<string,VariantValue>();var end=reader.ReadDictionaryStart();
        while(reader.HasNext(end)) { reader.AlignStruct();string key=reader.ReadString();result.Add(key,reader.ReadVariantValue()); }return result;
    }
    private static BoundShortcut[] ReadShortcuts(ref Reader reader)
    {
        var result=new List<BoundShortcut>();var end=reader.ReadArrayStart(DBusType.Struct);
        while(reader.HasNext(end))
        {
            reader.AlignStruct();string id=reader.ReadString();var values=ReadDictionary(ref reader);
            result.Add(new(id,values.TryGetValue("description",out var description)?description.GetString():id,values.TryGetValue("trigger_description",out var trigger)?trigger.GetString():""));
        }
        return result.ToArray();
    }
    private static BoundShortcut[] ParseShortcuts(VariantValue value)
    {
        if(value.Type!=VariantValueType.Array||value.ItemType!=VariantValueType.Struct)throw new InvalidDataException("The portal returned a malformed shortcut list.");
        var result=new List<BoundShortcut>();for(int i=0;i<value.Count;i++)
        {
            var item=value.GetItem(i);if(item.Count!=2)throw new InvalidDataException("The portal returned a malformed shortcut entry.");string id=item.GetItem(0).GetString();var values=item.GetItem(1).GetDictionary<string,VariantValue>();
            result.Add(new(id,values.TryGetValue("description",out var description)?description.GetString():id,values.TryGetValue("trigger_description",out var trigger)?trigger.GetString():""));
        }
        return result.ToArray();
    }
    private async Task CloseAsync(string path,string iface)
    {
        try
        {
            Task task;
            { using var writer=_connection.GetMessageWriter();writer.WriteMethodCallHeader(destination:_owner!,path:path,@interface:iface,member:"Close");task=_connection.CallMethodAsync(writer.CreateMessage()); }
            await task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch(Exception error) { Console.Error.WriteLine("Shortcut session close: "+error.Message); }
    }
    public async ValueTask DisposeAsync()
    {
        if(_disposed)return;_disposed=true;_lifetime.Cancel();
        foreach(var subscription in _subscriptions)subscription.Dispose();_subscriptions.Clear();
        if(_session is not null)await CloseAsync(_session,"org.freedesktop.portal.Session");
        Bound=[];_active.Clear();_connection.Dispose();_lifetime.Dispose();
    }
}
