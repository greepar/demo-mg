using Avalonia.Controls;
using Magpie.Desktop.Core;
namespace Magpie.Desktop.Views;

public partial class ProfilePage
{
    private void RefreshCaptureMethod()
    {
        bool initialized=_initialized;_initialized=false;
        try
        {
            var available=CaptureBackendPolicy.Available;bool chinese=Localization.CurrentLanguage.StartsWith("zh",StringComparison.OrdinalIgnoreCase);
            CaptureMethod.Items.Clear();
            CaptureMethod.Items.Add(new ComboBoxItem { Tag=CaptureBackend.Native,Content=CaptureBackendPolicy.Name(available) });
            // Preserve unavailable preferences from another OS; never silently
            // replace their backend while displaying or saving the profile.
            var choices=new[]{CaptureBackend.GraphicsCapture,CaptureBackend.DesktopDuplication,CaptureBackend.Gdi,CaptureBackend.DwmSharedSurface,CaptureBackend.Portal,CaptureBackend.ScreenCaptureKit};
            foreach(var backend in choices)
            {
                if(backend==available || (backend is CaptureBackend.Portal or CaptureBackend.ScreenCaptureKit) && Settings.CaptureBackend!=backend)continue;
                bool supported=CaptureBackendPolicy.Supported(backend);
                CaptureMethod.Items.Add(new ComboBoxItem { Tag=backend,Content=CaptureBackendPolicy.Name(backend)+(supported?"":chinese?"（当前系统不可用）":" (unavailable)"),IsEnabled=supported });
            }
            // An explicit selection matching the native implementation shares
            // the same visible row, while keeping its stored enum value.
            var selected=Settings.CaptureBackend;
            CaptureMethod.SelectedIndex=selected==available||selected==CaptureBackend.Native?0:CaptureMethod.Items.Cast<ComboBoxItem>().ToList().FindIndex(i=>(CaptureBackend)i.Tag! ==selected);
            RefreshCaptureStatus();
            CaptureTitleBarToggle.IsChecked=Settings.CaptureTitleBar;
        }
        finally { _initialized=initialized; }
    }
    private void RefreshCaptureStatus()
    {
        var available=CaptureBackendPolicy.Available;
        bool chinese=Localization.CurrentLanguage.StartsWith("zh",StringComparison.OrdinalIgnoreCase);
        bool unsupported=!CaptureBackendPolicy.Supported(Settings.CaptureBackend);
        CaptureWarning.Severity=unsupported?"Warning":"Informational";
        CaptureWarning.Title=unsupported?(chinese?"已保存的捕获方式在此版本不可用，请选择本机后端。":"The saved capture backend is unavailable in this build. Select the native backend."):
            (chinese?"当前捕获后端：":"Current capture backend: ")+CaptureBackendPolicy.Name(Settings.CaptureBackend==CaptureBackend.Native?available:Settings.CaptureBackend);
        CaptureWarning.IsOpen=true;
        CaptureTitleBarCard.IsEnabled=CaptureBackendPolicy.CanCaptureTitleBar(Settings.CaptureBackend);
        ToolTip.SetTip(CaptureTitleBarCard,CaptureTitleBarCard.IsEnabled?null:chinese?"此捕获后端不支持独立切换标题栏；偏好会保留，切回 Graphics Capture 后可继续使用。":"This backend cannot toggle the title bar. The preference is retained for Graphics Capture.");
    }
    private void CaptureTitleBar_Changed(object? sender,Avalonia.Interactivity.RoutedEventArgs args)
    {
        if(!_initialized||!CaptureTitleBarCard.IsEnabled)return;
        var previous=Settings.CaptureTitleBar;
        try {Settings.CaptureTitleBar=CaptureTitleBarToggle.IsChecked==true;RuntimeSettings.Current.Save();}
        catch(Exception error) {
            Settings.CaptureTitleBar=previous;_initialized=false;CaptureTitleBarToggle.IsChecked=previous;_initialized=true;
            (TopLevel.GetTopLevel(this) as MainWindow)?.ShowToast(Localization.Get("Profile_SourceWindow_CaptureTitleBar.Header"),error.Message);
        }
    }
}
