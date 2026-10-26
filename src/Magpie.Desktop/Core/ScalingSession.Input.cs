using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Threading;
namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    private uint _captureFrameWidth,_captureFrameHeight;
    private TextBlock? _inputStatus;
    private IDisposable? _inputPoll;
    private int? _inputState;

    private void ConfigureMacInput()
    {
        if(!OperatingSystem.IsMacOS() || _output is null || _image is null || _bitmap is null ||
            _captureFrameWidth==0 || _captureFrameHeight==0 || _stop.IsCancellationRequested)return;
        var handle=_output.TryGetPlatformHandle();
        if(handle is null || handle.HandleDescriptor!="NSWindow")return;
        var bounds=_image.Bounds;
        if(bounds.Width<=0 || bounds.Height<=0)return;
        var crop=SourceRegion.Resolve(_settings,_captureFrameWidth,_captureFrameHeight);
        var binding=RuntimeSettings.Current.ToolbarShortcut;
        var (symbol,modifiers)=NativeShortcutMapping.Parse(binding.Trigger);
        string character=NativeShortcutMapping.Character(symbol)?.ToString() ?? "";
        int keyCode=-1;
        if(character.Length==0)
            try { keyCode=(int)NativeShortcutMapping.MacSpecial(symbol); }catch(InvalidDataException) { }
        ulong flags=((modifiers&4)!=0?1ul<<17:0)|((modifiers&2)!=0?1ul<<18:0)|
            ((modifiers&1)!=0?1ul<<19:0)|((modifiers&8)!=0?1ul<<20:0);
        var (screenshotSymbol,screenshotModifiers)=NativeShortcutMapping.Parse(RuntimeSettings.Current.ScreenshotShortcut.Trigger);
        string screenshotCharacter=NativeShortcutMapping.Character(screenshotSymbol)?.ToString() ?? "";
        int screenshotCode=-1;
        if(screenshotCharacter.Length==0)
            try { screenshotCode=(int)NativeShortcutMapping.MacSpecial(screenshotSymbol); }catch(InvalidDataException) { }
        ulong screenshotFlags=((screenshotModifiers&4)!=0?1ul<<17:0)|((screenshotModifiers&2)!=0?1ul<<18:0)|
            ((screenshotModifiers&1)!=0?1ul<<19:0)|((screenshotModifiers&8)!=0?1ul<<20:0);
        int state;
        lock(_captureLock)
        {
            if(_capture is not { IsClosed:false,IsInvalid:false } capture)return;
            Rect region=default;
            if(_profilerWindow is { IsVisible:true } profiler&&profiler.TranslatePoint(default,(Visual)_output.Content!) is {} origin)
                region=new Rect(origin,profiler.Bounds.Size);
            if(_profilerDragStart is not null)region=new Rect(_output.ClientSize);
            bool regionReady=NativeCore.CaptureOverlayRegion(capture,region.X,region.Y,region.Width,region.Height)==1;
            state=NativeCore.CaptureConfigureInput(capture,handle.Handle,bounds.X,bounds.Y,bounds.Width,bounds.Height,
                (double)crop.X/_captureFrameWidth,(double)crop.Y/_captureFrameHeight,
                (double)crop.Width/_captureFrameWidth,(double)crop.Height/_captureFrameHeight,
                !regionReady?_output.ClientSize.Height:_toolbar is { IsVisible:true,IsHitTestVisible:true }?_toolbar.Bounds.Bottom:0,character,keyCode,flags,regionReady?_lastSourceInfo.WindowId:0,regionReady?_lastSourceInfo.ProcessId:0,_lastSourceSequence,screenshotCharacter,screenshotCode,screenshotFlags);
        }
        UpdateInputStatus(state);
    }
    private void UpdateInputStatus(int state)
    {
        if(_inputState==state)return;
        _inputState=state;
        if(_inputStatus is null)return;
        bool chinese=Localization.CurrentLanguage.StartsWith("zh",StringComparison.Ordinal);
        _inputStatus.Text=state switch
        {
            3=>chinese?"正在更新捕获画面，等待匹配的帧后恢复操作。":"Waiting for a matching capture frame before enabling input.",
            2=>chinese?"要操作源窗口，请在“系统设置 → 隐私与安全性 → 辅助功能”中允许 Magpie。首次点击缩放画面会请求权限。":
                "To control the source window, allow Magpie in System Settings → Privacy & Security → Accessibility. Click the scaled image to request access.",
            0=>chinese?"当前源仅支持查看。操作源窗口需要 macOS 15.2 或更高版本，并在系统选源中选择其他应用的单个窗口。":
                "This source is view-only. Control requires macOS 15.2 or later and a single window from another app selected in the system picker.",
            _=>""
        };
        _inputStatus.IsVisible=state is 0 or 2 or 3;
    }

    private void StartMacInputPolling()
    {
        if(!OperatingSystem.IsMacOS() || _captureFrameWidth==0)return;
        _inputPoll=DispatcherTimer.Run(()=>
        {
            if(_output is null || _stop.IsCancellationRequested)return false;
            ConfigureMacInput();return true;
        },TimeSpan.FromSeconds(1));
    }
}
