using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Magpie.Desktop.Core;
namespace Magpie.Desktop.Views;

public partial class SettingsPage
{
    private DesktopIntegration? _trayIntegration;
    private bool _refreshingTray;
    private void RefreshTray()
    {
        if(TopLevel.GetTopLevel(this) is MainWindow main && !ReferenceEquals(_trayIntegration,main.DesktopIntegration))
        {
            if(_trayIntegration is not null)_trayIntegration.Changed-=RefreshTray;
            _trayIntegration=main.DesktopIntegration;
            if(_trayIntegration is not null)_trayIntegration.Changed+=RefreshTray;
        }
        _refreshingTray=true;
        TrayToggle.IsChecked=RuntimeSettings.Current.ShowTrayIcon;
        TrayToggle.IsEnabled=_trayIntegration is not null;
        bool unavailable=RuntimeSettings.Current.ShowTrayIcon&&_trayIntegration is { IsReady:false };
        TrayStatus.IsVisible=unavailable;
        TrayStatus.Text=Localization.CurrentLanguage.StartsWith("zh",StringComparison.OrdinalIgnoreCase)
            ? "托盘暂不可用；关闭主窗口会退出程序。托盘宿主恢复后会重新登记。"
            : "The tray is unavailable; closing the main window exits Magpie. Registration resumes when the tray host returns.";
        _refreshingTray=false;
    }
    private void Tray_Changed(object? sender,RoutedEventArgs args)
    {
        if(!_initialized||_refreshingTray||_trayIntegration is null)return;
        try { _trayIntegration.SetEnabled(TrayToggle.IsChecked==true);RefreshTray(); }
        catch(Exception error)
        {
            Dispatcher.UIThread.Post(()=> { RefreshTray();TrayStatus.Text=error.Message;TrayStatus.IsVisible=true;TrayStatus.BringIntoView(); });
        }
    }
}
