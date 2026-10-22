using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    private static unsafe bool ReplayFrame(ScalingFrameProcessor processor,EngineHandle engine,byte[] source,NativeEffectStep[] plan,bool fresh,long time,double minimum=0,bool gpu=false)
    {
        fixed(byte* pixels=source)return processor.Process(engine,(nint)pixels,3,2,fresh,plan,DuplicateFrameMode.Always,false,minimum,0,time,gpu);
    }
    internal async Task VerifyProcessingOutputAsync(Window owner)
    {
        string directory=Path.Combine(Path.GetTempPath(),"magpie-processing-output-"+Guid.NewGuid().ToString("N"));
        string previousDirectory=_settings.ScreenshotDirectory,previousTemplate=_settings.ScreenshotTemplate;
        bool previousGpu=RuntimeSettings.Current.GpuProfilingEnabled;
        uint previousEffect=_settings.Effect;string? previousMode=_settings.SelectedModeId;
        try
        {
            _settings.ScreenshotDirectory=directory;_settings.ScreenshotTemplate="processing";_settings.Effect=0;_settings.SelectedModeId=null;
            using var engine=NativeCore.CreateForDevice(_settings.GraphicsDeviceId);if(engine.IsInvalid)throw NativeCore.Error();
            var processor=new ScalingFrameProcessor(0);
            byte[] source=[17,31,47,255,61,79,97,255,113,127,139,255,151,167,181,255,193,211,223,255,229,239,251,255];
            NativeEffectStep[] plan=[new(){Effect=0,Width=6,Height=4}];
            OpenOutput(owner,false);await Task.Delay(100);
            if(!ReplayFrame(processor,engine,source,plan,true,0) || !Present(processor.Pixels,6,4))throw new InvalidOperationException("Initial replay frame failed.");
            VerifyPng(SaveScreenshot() ?? throw new InvalidOperationException("Initial replay PNG failed."),processor.Pixels,new PixelSize(6,4));
            if(ReplayFrame(processor,engine,source,plan,true,Stopwatch.Frequency/100))throw new InvalidOperationException("Duplicate replay invoked Vulkan again.");
            // Use the same queued screenshot path as the real button/F12 while no frame is presented.
            Interlocked.Exchange(ref _screenshotPending,1);DrainScreenshot();
            var screenshots=Directory.GetFiles(directory,"*.png");
            if(screenshots.Length!=2 || processor.Performance.TotalScaled!=1)throw new InvalidOperationException("Idle screenshot required another GPU frame or did not save.");
            foreach(string path in screenshots)VerifyPng(path,processor.Pixels,new PixelSize(6,4));
            plan=[new(){Effect=0,Width=9,Height=6}];
            if(!ReplayFrame(processor,engine,source,plan,false,Stopwatch.Frequency/20))throw new InvalidOperationException("Idle replay did not invalidate its size.");
            Present(processor.Pixels,9,6);
            byte[] expected=new byte[9*6*4];
            for(int y=0;y<6;y++)for(int x=0;x<9;x++)for(int c=0;c<4;c++)expected[(y*9+x)*4+c]=source[((y/3)*3+x/3)*4+c];
            VerifyPng(SaveScreenshot() ?? throw new InvalidOperationException("Resized replay PNG failed."),expected,new PixelSize(9,6));
            if(!ReplayFrame(processor,engine,source,plan,false,Stopwatch.Frequency/5,10))throw new InvalidOperationException("Minimum frame rate did not refresh cached source.");
            Present(processor.Pixels,9,6);
            if(!processor.Performance.TrySnapshot(Stopwatch.Frequency,out var metrics) || metrics.Received!=2 || metrics.Scaled!=3 || metrics.Skipped!=1 || metrics.Forced!=1)throw new InvalidOperationException("Replay counts do not match displayed statistics.");
            if(_statistics is not null)_statistics.Text=FramePerformance.Describe(metrics,DuplicateFrameMode.Always,false);
            _output!.Width=480;_output.Height=320;await Task.Delay(150);
            if(Math.Abs(_output.ClientSize.Width-480)>1 || Math.Abs(_output.ClientSize.Height-320)>1)throw new InvalidOperationException("The compact output fixture did not receive its requested client size: "+_output.ClientSize);
            var toggle=_output.GetVisualDescendants().OfType<CheckBox>().Single(c=>c.Name=="GpuProfilingToggle");
            toggle.IsChecked=true;
            if(!RuntimeSettings.Current.GpuProfilingEnabled || _gpuPanel?.IsVisible!=true)throw new InvalidOperationException("Profiler UI toggle did not enable settings/panel.");
            ReplayFrame(processor,engine,source,plan,false,Stopwatch.Frequency+1,gpu:true);UpdateGpuProfile(processor.GpuProfile);
            if(_gpuDetails is null || string.IsNullOrWhiteSpace(_gpuDetails.Text))throw new InvalidOperationException("GPU panel is empty.");
            var switchView=_output.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="GpuTimingView");
            switchView.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            if(!_gpuPassView || (processor.GpuProfile.Valid && !_gpuDetails.Text.Contains("pass 1")))throw new InvalidOperationException("GPU pass view did not switch.");
            await Task.Delay(100);
            var actions=_toolbarActions!;
            if(_toolbar!.Bounds.Height>_output.ClientSize.Height+1 || actions.Children.Any(action=>action.Bounds.Right>actions.Bounds.Width+1))throw new InvalidOperationException("Performance toolbar actions exceed the compact output window.");
            if(!processor.Performance.TrySnapshot(Stopwatch.Frequency*2,out var gpuMetrics) || gpuMetrics.GpuSamples!=(processor.GpuProfile.Valid?1:0))throw new InvalidOperationException("Output GPU samples do not match real dispatches.");
            if(_statistics is not null)_statistics.Text=FramePerformance.Describe(gpuMetrics,DuplicateFrameMode.Always,false,true);
            await Task.Delay(50);
            var arguments=Environment.GetCommandLineArgs();int captureIndex=Array.IndexOf(arguments,"--capture");
            if(captureIndex>=0 && captureIndex+1<arguments.Length)
            {
                using var image=new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(720,480),new Vector(144,144));
                image.Render((Control)_output.Content!);image.Save(Path.ChangeExtension(arguments[captureIndex+1],"output.png"),Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
            toggle.IsChecked=false;if(RuntimeSettings.Current.GpuProfilingEnabled || _gpuPanel!.IsVisible)throw new InvalidOperationException("Profiler UI did not disable.");
            Stop();Stop();
            Console.WriteLine("PROCESS OUTPUT PASS: actual Vulkan replay, duplicate skipped, queued screenshot without render, lossless PNG, idle resize/minimum-rate refresh/counters, GPU toggle, real timestamp/pass display and compact scrolling.");
        }
        finally
        {
            RuntimeSettings.Current.GpuProfilingEnabled=previousGpu;
            _settings.ScreenshotDirectory=previousDirectory;_settings.ScreenshotTemplate=previousTemplate;_settings.Effect=previousEffect;_settings.SelectedModeId=previousMode;
            _output?.Close();_output=null;_bitmap?.Dispose();_bitmap=null;_finished=true;_stop.Dispose();
            if(Directory.Exists(directory))Directory.Delete(directory,true);
        }
    }
}
