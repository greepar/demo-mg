using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Tmds.DBus.Protocol;

namespace Magpie.Desktop.Core;

internal interface ITrayPresence : IDisposable
{
    bool IsReady { get; }
    event Action? Changed;
    void SetVisible(bool visible);
}

internal sealed class TrayPresence(NativeMenu menu,Action activate) : ITrayPresence
{
    private TrayIcon? _icon;
    private CancellationTokenSource? _stop;
    private bool _ready;
    public bool IsReady=>_ready;
    public event Action? Changed;
    private void Ready(bool value)
    {
        if(_ready==value)return;_ready=value;Changed?.Invoke();
    }
    public void SetVisible(bool visible)
    {
        SetHidden();
        if(!visible)return;
        // Avoid any implicit D-Bus autolaunch when a Wayland session has no bus.
        if(OperatingSystem.IsLinux()&&string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")))return;
        if(_icon is null)
        {
            _icon=new TrayIcon { Menu=menu,ToolTipText="Magpie",IsVisible=false };
            using(var stream=AssetLoader.Open(new Uri("avares://Magpie.Desktop/Assets/Magpie.ico")))_icon.Icon=new WindowIcon(stream);
            _icon.Clicked+=(_,_)=>activate();
        }
        _icon.IsVisible=true;
        if(OperatingSystem.IsLinux())
        {
            _stop=new CancellationTokenSource();_ = MonitorLinuxAsync(_stop.Token);
        }
        else Ready(_icon.NativeMenuExporter is not null);
    }
    private void SetHidden()
    {
        _stop?.Cancel();_stop?.Dispose();_stop=null;
        if(_icon is not null)_icon.IsVisible=false;Ready(false);
    }
    private async Task MonitorLinuxAsync(CancellationToken stop)
    {
        // Avalonia registers asynchronously. Only a host that lists our actual item
        // provides a usable route back to a hidden window.
        using var connection=new DBusConnection(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")!);
        try
        {
            await connection.ConnectAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3),stop);
            while(!stop.IsCancellationRequested)
            {
                bool ready=false;
                try { ready=await LinuxTrayReadiness.IsReadyAsync(connection).WaitAsync(TimeSpan.FromSeconds(2),stop); }
                catch(DBusErrorReplyException) { }
                stop.ThrowIfCancellationRequested();await Dispatcher.UIThread.InvokeAsync(()=> { if(!stop.IsCancellationRequested)Ready(ready); });
                await Task.Delay(1000,stop);
            }
        }
        catch(OperationCanceledException) when(stop.IsCancellationRequested) { }
        catch(Exception error)
        {
            if(!stop.IsCancellationRequested) { Dispatcher.UIThread.Post(()=> { if(!stop.IsCancellationRequested)Ready(false); });Console.Error.WriteLine("Tray monitor: "+error.Message); }
        }
    }
    public void Dispose()
    {
        SetHidden();
        // Avalonia 12.1.1's DBusTrayIconImpl.Dispose cancels async-void WatchAsync,
        // which posts an unhandled TaskCanceledException. Keep the single Linux
        // exporter for the process lifetime; hiding still releases its SNI name.
        // Other native backends support explicit disposal.
        if(!OperatingSystem.IsLinux())_icon?.Dispose();
        _icon=null;
    }
}

internal static class LinuxTrayReadiness
{
    private const string Watcher="org.kde.StatusNotifierWatcher";
    internal static async Task<bool> IsReadyAsync(DBusConnection connection)
    {
        if(!(await PropertyAsync(connection,Watcher,"/StatusNotifierWatcher",Watcher,"IsStatusNotifierHostRegistered")).GetBool())return false;
        string[] items=(await PropertyAsync(connection,Watcher,"/StatusNotifierWatcher",Watcher,"RegisteredStatusNotifierItems")).GetArray<string>();
        foreach(string item in items)
        {
            int slash=item.IndexOf('/');
            string service=slash<0 ? item : item[..slash],path=slash<0 ? "/StatusNotifierItem" : item[slash..];
            if(service.Length==0)continue;
            try
            {
                if(await ProcessIdAsync(connection,service)!=Environment.ProcessId)continue;
                if((await PropertyAsync(connection,service,path,"org.kde.StatusNotifierItem","Id")).GetString()=="Magpie")return true;
            }
            catch(DBusErrorReplyException) { }
        }
        return false;
    }
    private static Task<uint> ProcessIdAsync(DBusConnection connection,string service)
    {
        using var writer=connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination:"org.freedesktop.DBus",path:"/org/freedesktop/DBus",@interface:"org.freedesktop.DBus",member:"GetConnectionUnixProcessID",signature:"s");
        writer.WriteString(service);
        return connection.CallMethodAsync(writer.CreateMessage(),static (message,_)=>message.GetBodyReader().ReadUInt32());
    }
    internal static Task<VariantValue> PropertyAsync(DBusConnection connection,string service,string path,string iface,string property)
    {
        using var writer=connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination:service,path:path,@interface:"org.freedesktop.DBus.Properties",member:"Get",signature:"ss");
        writer.WriteString(iface);writer.WriteString(property);
        return connection.CallMethodAsync(writer.CreateMessage(),static (message,_)=>message.GetBodyReader().ReadVariantValue());
    }
}
