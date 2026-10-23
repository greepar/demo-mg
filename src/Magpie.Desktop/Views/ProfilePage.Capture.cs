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
                CaptureMethod.Items.Add(new ComboBoxItem { Tag=backend,Content=CaptureBackendPolicy.Name(backend)+(chinese?"（此版本不可用）":" (unavailable in this build)"),IsEnabled=false });
            }
            // An explicit selection matching the native implementation shares
            // the same visible row, while keeping its stored enum value.
            var selected=Settings.CaptureBackend;
            CaptureMethod.SelectedIndex=selected==available||selected==CaptureBackend.Native?0:CaptureMethod.Items.Cast<ComboBoxItem>().ToList().FindIndex(i=>(CaptureBackend)i.Tag! ==selected);
            RefreshCaptureStatus();
            CaptureTitleBarCard.IsEnabled=false;
            ToolTip.SetTip(CaptureTitleBarCard,chinese?"当前捕获后端不支持独立切换标题栏。GDI 保持原版仅捕获客户区；Portal / ScreenCaptureKit 由系统确定边界。":"This backend cannot toggle the title bar separately. GDI keeps the original client-area capture; Portal / ScreenCaptureKit use system-defined bounds.");
        }
        finally { _initialized=initialized; }
    }
    private void RefreshCaptureStatus()
    {
        var available=CaptureBackendPolicy.Available;
        bool chinese=Localization.CurrentLanguage.StartsWith("zh",StringComparison.OrdinalIgnoreCase);
        bool unsupported=!CaptureBackendPolicy.Supported(Settings.CaptureBackend,available);
        CaptureWarning.Severity=unsupported?"Warning":"Informational";
        CaptureWarning.Title=unsupported?(chinese?"已保存的捕获方式在此版本不可用，请选择本机后端。":"The saved capture backend is unavailable in this build. Select the native backend."):
            (chinese?"当前捕获后端：":"Current capture backend: ")+CaptureBackendPolicy.Name(available);
        CaptureWarning.IsOpen=true;
    }
}
