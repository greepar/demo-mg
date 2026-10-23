namespace Magpie.Desktop.Core;

internal enum CaptureBackend { Native,Portal,ScreenCaptureKit,Gdi,GraphicsCapture,DesktopDuplication,DwmSharedSurface }
internal static class CaptureBackendPolicy
{
    internal static string Name(CaptureBackend backend)=>backend switch
    {
        CaptureBackend.Native=>"Native",CaptureBackend.Portal=>"Portal / PipeWire (Wayland)",CaptureBackend.ScreenCaptureKit=>"ScreenCaptureKit",
        CaptureBackend.Gdi=>"GDI",CaptureBackend.GraphicsCapture=>"Graphics Capture",CaptureBackend.DesktopDuplication=>"Desktop Duplication",CaptureBackend.DwmSharedSurface=>"DwmSharedSurface",
        _=>throw new InvalidDataException("Invalid capture backend.")
    };
    internal static CaptureBackend Available
    {
        get
        {
            var backend=(CaptureBackend)NativeCore.CaptureBackend();
            if(backend is not (CaptureBackend.Portal or CaptureBackend.ScreenCaptureKit or CaptureBackend.Gdi))throw new InvalidDataException("The native capture backend is unknown.");
            return backend;
        }
    }
    internal static bool Supported(CaptureBackend requested,CaptureBackend available)=>requested==CaptureBackend.Native||requested==available;
    internal static CaptureBackend Resolve(CaptureBackend requested,CaptureBackend available)
    {
        if(available is not (CaptureBackend.Portal or CaptureBackend.ScreenCaptureKit or CaptureBackend.Gdi))throw new InvalidDataException("Invalid native capture backend.");
        if(!Enum.IsDefined(requested))throw new InvalidDataException("Invalid capture backend.");
        if(!Supported(requested,available))throw new NotSupportedException(Localization.CurrentLanguage.StartsWith("zh",StringComparison.OrdinalIgnoreCase)
            ? $"当前版本无法使用 {Name(requested)} 捕获。请在配置中选择 {Name(available)}。"
            : $"This build cannot capture with {Name(requested)}. Select {Name(available)} in the profile.");
        return available;
    }
}
