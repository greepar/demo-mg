using Avalonia.Controls;
using Magpie.Desktop.Core;
namespace Magpie.Desktop;

public partial class MainWindow
{
    private IGlobalShortcutSession? _globalShortcuts;
    private Func<IGlobalShortcutSession>? _shortcutSessionFactory;
    private CancellationTokenSource? _shortcutSetup;
    internal bool GlobalShortcutsActive=>_globalShortcuts?.Bound.Count>0;
    internal bool GlobalShortcutsBusy=>_shortcutSetup is not null;
    internal string GlobalShortcutStatus { get; private set; }="";
    internal event Action? GlobalShortcutsChanged;
    internal async Task EnableGlobalShortcutsAsync(bool probe=false)
    {
        if(_shortcutSetup is not null)return;
        if(!Core.RuntimeSettings.PersistenceEnabled&&!probe)throw new InvalidOperationException("System shortcut setup is disabled in preview mode.");
        IGlobalShortcutSession session;
        if(_shortcutSessionFactory is not null)session=_shortcutSessionFactory();
        else if(OperatingSystem.IsLinux())
            session=new PortalGlobalShortcuts(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")??throw new NotSupportedException("The Wayland session has no D-Bus connection."));
        else if(OperatingSystem.IsWindows())session=new NativeGlobalShortcuts(new WindowsShortcutApi(this));
        else if(OperatingSystem.IsMacOS())session=new NativeGlobalShortcuts(new MacShortcutApi());
        else throw new PlatformNotSupportedException("Global shortcut registration is unavailable on this operating system.");
        await DisableGlobalShortcutsAsync();
        var stop=new CancellationTokenSource();_shortcutSetup=stop;
        var portal=session;_globalShortcuts=portal;
        GlobalShortcutStatus=OperatingSystem.IsLinux() ? (Chinese ? "请在系统中确认快捷键；系统可能调整按键。" : "Confirm shortcuts in the system dialog; the desktop may change the suggested keys.") : (Chinese ? "正在注册系统快捷键……" : "Registering native shortcuts…");GlobalShortcutsChanged?.Invoke();
        portal.Activated+=id=>
        {
            if(probe) { ShortcutProbeActivations.Add(id);return; }
            switch(id)
            {
                case "fullscreen":StartScaling(true,0);break;
                case "windowed":StartScaling(false,0);break;
                case "toolbar":_scalingSession?.ToggleToolbar();break;
                case "screenshot":_scalingSession?.RequestScreenshot();break;
            }
        };
        portal.Changed+=()=> { if(ReferenceEquals(_globalShortcuts,portal)) { RefreshBoundShortcutStatus();GlobalShortcutsChanged?.Invoke(); } };
        portal.Closed+=message=> { GlobalShortcutStatus=(Chinese ? "全局快捷键已断开：" : "Global shortcuts disconnected: ")+message;GlobalShortcutsChanged?.Invoke(); };
        try
        {
            await portal.BindAsync(GlobalShortcutSettings.Definitions(RuntimeSettings.Current),"",stop.Token);
            if(portal is NativeGlobalShortcuts native)native.MonitorLayout();
            RefreshBoundShortcutStatus();
        }
        catch(Exception error)
        {
            if(ReferenceEquals(_globalShortcuts,portal))_globalShortcuts=null;
            try { await portal.DisposeAsync(); }
            catch(Exception cleanup) { GlobalShortcutStatus=cleanup.Message;GlobalShortcutsChanged?.Invoke();return; }
            GlobalShortcutStatus=(Chinese ? "未注册全局快捷键：" : "Global shortcuts were not registered: ")+error.Message;
        }
        finally
        {
            if(ReferenceEquals(_shortcutSetup,stop))_shortcutSetup=null;stop.Dispose();GlobalShortcutsChanged?.Invoke();
        }
    }
    private static bool Chinese=>Localization.CurrentLanguage.StartsWith("zh",StringComparison.OrdinalIgnoreCase);
    private void RefreshBoundShortcutStatus()
    {
        GlobalShortcutStatus=_globalShortcuts is { Bound.Count:>0 } portal
            ? (Chinese ? "系统已绑定：" : "System bindings: ")+string.Join("；",portal.Bound.Select(b=>b.Description+": "+(b.TriggerDescription.Length>0?b.TriggerDescription:(Chinese?"未提供触发按键说明":"trigger description unavailable"))))
            : Chinese ? "系统没有绑定任何快捷键。" : "The system did not bind any shortcuts.";
    }
    internal async Task DisableGlobalShortcutsAsync()
    {
        _shortcutSetup?.Cancel();
        var current=_globalShortcuts;_globalShortcuts=null;
        if(current is not null)
        {
            try { await current.DisposeAsync(); }
            catch(Exception cleanup) { GlobalShortcutStatus=cleanup.Message;GlobalShortcutsChanged?.Invoke();throw; }
        }
        GlobalShortcutStatus=Chinese ? "全局快捷键已停用。" : "Global shortcuts disabled.";GlobalShortcutsChanged?.Invoke();
    }
    internal readonly List<string> ShortcutProbeActivations=[];
    internal async Task SaveShortcutAsync(Controls.ShortcutControl control,string key,string modifiers)
    {
        string id=control.Name switch { "FullscreenShortcut"=>"fullscreen","WindowedShortcut"=>"windowed","ToolbarShortcut"=>"toolbar","ScreenshotShortcut"=>"screenshot",_=>throw new InvalidDataException("Unknown shortcut action.") };
        var binding=GlobalShortcutSettings.Binding(RuntimeSettings.Current,id);string oldKey=binding.Key,oldModifiers=binding.Modifiers;
        binding.Key=key;binding.Modifiers=modifiers;
        try { RuntimeSettings.Current.Save(); }
        catch { binding.Key=oldKey;binding.Modifiers=oldModifiers;throw; }
        control.Key=key;control.Modifiers=modifiers;
        // BindShortcuts is allowed once per session. A changed preference invalidates
        // the current registration; applying again is an explicit user action.
        if(_globalShortcuts is not null)await DisableGlobalShortcutsAsync();
        GlobalShortcutStatus=Chinese ? "按键已保存，点击启用以向系统申请绑定。" : "Keys saved. Enable global shortcuts to request system bindings.";GlobalShortcutsChanged?.Invoke();
    }
}
