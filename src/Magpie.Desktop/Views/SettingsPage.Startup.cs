using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Interactivity;
using Magpie.Desktop.Core;
namespace Magpie.Desktop.Views;

public partial class SettingsPage
{
    public string StartupDescription => Localization.CurrentLanguage.StartsWith("zh",StringComparison.OrdinalIgnoreCase) ? "登录后启动 Magpie；启用托盘且可用时隐藏主窗口" : "Start Magpie after login; hide its window when the tray is enabled and available";
    private IStartupRegistration? _startup;
    private bool _refreshingStartup;
    internal void ConfigureStartup(IStartupRegistration registration)
    {
        _startup=registration;RefreshStartup();
    }
    private void RefreshStartup()
    {
        _refreshingStartup=true;
        try
        {
            // Preview sessions cannot read or modify real desktop registration.
            if(_startup is null&&!RuntimeSettings.PersistenceEnabled) { StartupToggle.IsEnabled=false;return; }
            _startup??=StartupRegistration.Create();
            StartupToggle.IsChecked=_startup.Enabled;StartupToggle.IsEnabled=true;StartupError.IsVisible=false;
        }
        catch(Exception error) { StartupToggle.IsEnabled=false;ShowStartupError(error); }
        finally { _refreshingStartup=false; }
    }
    private void ShowStartupError(Exception error)
    {
        StartupError.Text=(Localization.CurrentLanguage.StartsWith("zh",StringComparison.OrdinalIgnoreCase) ? "无法更新开机启动：" : "Could not update startup registration: ")+error.Message;
        StartupError.IsVisible=true;
        Dispatcher.UIThread.Post(()=>StartupError.BringIntoView(),DispatcherPriority.Background);
    }
    private void Startup_Changed(object? sender,RoutedEventArgs args)
    {
        if(!_initialized||_refreshingStartup||_startup is null)return;
        bool requested=StartupToggle.IsChecked==true;
        try { _startup.SetEnabled(requested);RefreshStartup(); }
        catch(Exception error)
        {
            // Let ToggleSwitch finish handling the original checked event before rollback.
            StartupToggle.IsEnabled=false;
            Dispatcher.UIThread.Post(()=> { RefreshStartup();ShowStartupError(error); });
        }
    }
    internal async Task VerifyStartupPreviewAsync(bool failure)
    {
        string root=Path.Combine(Path.GetTempPath(),"magpie-startup-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            string directory=Path.Combine(root,"autostart"),path=Path.Combine(directory,"test.desktop");
            string entry=StartupRegistration.DesktopEntry(["/test/Magpie"],root);
            ConfigureStartup(new FileStartupRegistration(path,entry,null,[],StartupRegistration.Marker));
            if(failure)File.WriteAllText(directory,"blocked");
            StartupToggle.IsChecked=true;
            await Dispatcher.UIThread.InvokeAsync(()=> { },DispatcherPriority.Background);
            if(failure)
            {
                if(StartupToggle.IsChecked!=false||!StartupError.IsVisible||File.Exists(path))throw new InvalidOperationException("Startup failure did not roll back the toggle.");
            }
            else
            {
                if(!File.Exists(path)||StartupToggle.IsChecked!=true||StartupError.IsVisible)throw new InvalidOperationException("Startup UI did not register.");
                StartupToggle.IsChecked=false;
                if(File.Exists(path)||StartupToggle.IsChecked!=false)throw new InvalidOperationException("Startup UI did not unregister.");
                StartupToggle.IsChecked=true;
            }
            if(failure)StartupError.BringIntoView();else StartupToggle.BringIntoView();
        }
        finally { Directory.Delete(root,true); }
    }
}
