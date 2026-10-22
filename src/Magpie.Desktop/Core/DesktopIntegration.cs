using Avalonia.Controls;
using Avalonia.Threading;
namespace Magpie.Desktop.Core;

internal sealed class DesktopIntegration : IDisposable
{
    private readonly MainWindow _window;
    private readonly Action _shutdown;
    private readonly Action<bool,int> _start;
    private readonly ITrayPresence _tray;
    private readonly NativeMenuItem _fullscreen,_windowed;
    private bool _exiting,_disposed,_hidden;
    internal bool IsReady=>RuntimeSettings.Current.ShowTrayIcon&&_tray.IsReady;
    internal bool IsHidden=>_hidden;
    internal event Action? Changed;
    internal NativeMenu Menu { get; }
    internal DesktopIntegration(MainWindow window,Action shutdown,Func<NativeMenu,Action,ITrayPresence>? factory=null,Action<bool,int>? start=null)
    {
        _window=window;_shutdown=shutdown;_start=start??window.StartScaling;
        var show=new NativeMenuItem(Localization.Get("NotifyIcon_MainWindow"));show.Click+=(_,_)=>Show();
        _fullscreen=new NativeMenuItem();_fullscreen.Click+=(_,_)=>Start(true);
        _windowed=new NativeMenuItem();_windowed.Click+=(_,_)=>Start(false);
        var exit=new NativeMenuItem(Localization.Get("NotifyIcon_Exit"));exit.Click+=(_,_)=>Exit();
        Menu=new NativeMenu { Items={show,_fullscreen,_windowed,new NativeMenuItemSeparator(),exit} };
        Menu.NeedsUpdate+=(_,_)=>RefreshMenu();RefreshMenu();
        _tray=(factory??((menu,activate)=>new TrayPresence(menu,activate)))(Menu,Show);
        _tray.Changed+=PresenceChanged;window.DesktopIntegration=this;
        try { _tray.SetVisible(RuntimeSettings.Current.ShowTrayIcon); }
        catch(Exception error) { Console.Error.WriteLine("Tray registration: "+error.Message); }
    }
    internal void RefreshMenu()
    {
        string seconds=RuntimeSettings.Current.CountdownSeconds.ToString(System.Globalization.CultureInfo.CurrentCulture);
        _fullscreen.Header=Localization.Get("NotifyIcon_Timer_Fullscreen").Replace("{}",seconds);
        _windowed.Header=Localization.Get("NotifyIcon_Timer_Windowed").Replace("{}",seconds);
    }
    private void Start(bool fullscreen)=>_start(fullscreen,RuntimeSettings.Current.CountdownSeconds);
    private void PresenceChanged()
    {
        if(_disposed)return;
        if(!IsReady&&_hidden)Show();Changed?.Invoke();
    }
    internal void SetEnabled(bool enabled)
    {
        bool old=RuntimeSettings.Current.ShowTrayIcon;RuntimeSettings.Current.ShowTrayIcon=enabled;
        try { RuntimeSettings.Current.Save(); }
        catch { RuntimeSettings.Current.ShowTrayIcon=old;throw; }
        if(!enabled&&_hidden)Show();
        try { _tray.SetVisible(enabled); }
        catch(Exception error) { Console.Error.WriteLine("Tray registration: "+error.Message); }
        PresenceChanged();
    }
    internal bool TryHide(WindowClosingEventArgs args)
    {
        if(_disposed||_exiting||args.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown||!IsReady)return false;
        args.Cancel=true;_hidden=true;_window.Hide();Changed?.Invoke();return true;
    }
    internal void Show()
    {
        if(_disposed||_exiting)return;
        _hidden=false;_window.Show();
        if(_window.WindowState==WindowState.Minimized)_window.WindowState=WindowState.Normal;
        _window.Activate();Changed?.Invoke();
    }
    internal async Task<bool> PrepareHiddenStartupAsync(TimeSpan timeout)
    {
        var deadline=DateTime.UtcNow+timeout;
        while(!IsReady&&RuntimeSettings.Current.ShowTrayIcon&&DateTime.UtcNow<deadline)await Task.Delay(50);
        if(IsReady) { _hidden=true;return true; }
        return false;
    }
    internal void Exit()
    {
        if(_exiting)return;_exiting=true;_window.StopScaling();_shutdown();
    }
    public void Dispose()
    {
        if(_disposed)return;_disposed=true;_tray.Changed-=PresenceChanged;_tray.Dispose();
        if(ReferenceEquals(_window.DesktopIntegration,this))_window.DesktopIntegration=null;
    }
}
