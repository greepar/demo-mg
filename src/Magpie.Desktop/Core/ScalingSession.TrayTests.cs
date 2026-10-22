using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
namespace Magpie.Desktop.Core;

internal sealed class PreviewTrayPresence : ITrayPresence
{
    public bool IsReady { get; private set; }
    public event Action? Changed;
    public void SetVisible(bool visible) { IsReady=visible;Changed?.Invoke(); }
    public void Dispose()=>SetVisible(false);
}

internal sealed partial class ScalingSession
{
    internal async Task VerifyTrayOutputAsync(MainWindow owner,DesktopIntegration integration)
    {
        string root=Path.Combine(Path.GetTempPath(),"magpie-tray-output-"+Guid.NewGuid().ToString("N"));
        string previousDirectory=_settings.ScreenshotDirectory;
        try
        {
            _settings.ScreenshotDirectory=root;
            using var engine=NativeCore.CreateForDevice(_settings.GraphicsDeviceId);
            if(engine.IsInvalid)throw NativeCore.Error();
            var processor=new ScalingFrameProcessor(0);
            byte[] source=Enumerable.Range(0,24).Select(i=>(byte)(i*7)).ToArray();
            for(int i=3;i<source.Length;i+=4)source[i]=255;
            NativeEffectStep[] graph=[new() { Effect=0,Width=6,Height=4 }];
            OpenOutput(owner,false,3,2);await Task.Delay(120);
            var output=_output ?? throw new InvalidOperationException("Output missing.");
            if(!ReplayFrame(processor,engine,source,graph,true,0))throw new InvalidOperationException("Initial tray GPU frame failed.");
            Present(processor.Pixels,6,4);owner.SetPreviewScalingSession(this);
            owner.Close();await Task.Delay(150);
            if(owner.IsVisible||!integration.IsHidden||!output.IsVisible||output.Owner is not null||_stop.IsCancellationRequested)
                throw new InvalidOperationException("Closing to tray hid/stopped the independent scaling output.");
            source[0]=231;
            if(!ReplayFrame(processor,engine,source,graph,true,Stopwatch.Frequency))throw new InvalidOperationException("Hidden main window stopped Vulkan updates.");
            Present(processor.Pixels,6,4);
            VerifyPng(SaveScreenshot() ?? throw new InvalidOperationException("Background output PNG failed."),processor.Pixels,new PixelSize(6,4));
            if(processor.Pixels[0]!=231||processor.Performance.TotalScaled!=2||!output.IsVisible)throw new InvalidOperationException("Background GPU image did not update.");
            integration.Show();await Task.Delay(100);
            if(!owner.IsVisible||integration.IsHidden||!output.IsVisible)throw new InvalidOperationException("Restoring the main window hid the output.");
            Console.WriteLine("TRAY OUTPUT PASS: close cancels without capture cancellation, independent output remains visible, two real Vulkan frames and exact PNG while hidden, restore retains output.");
        }
        finally
        {
            owner.SetPreviewScalingSession(null);integration.Show();
            _finished=true;_output?.Close();_output=null;_bitmap?.Dispose();_bitmap=null;_stop.Dispose();
            _settings.ScreenshotDirectory=previousDirectory;
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }
}
